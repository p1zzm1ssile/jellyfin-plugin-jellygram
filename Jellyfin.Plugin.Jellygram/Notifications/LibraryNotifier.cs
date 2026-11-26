using System.Collections.Concurrent;
using Jellyfin.Plugin.Jellygram.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Jellygram.Notifications;

public sealed class LibraryNotifier(
    ILibraryManager libraryManager,
    TelegramSender sender,
    ILogger<LibraryNotifier> logger) : BackgroundService
{
    private const int MaxMetadataRetries = 10;
    private const int MaxSendAttempts = 5;
    private static readonly char[] ListSeparators = [',', ';', '\n', '\r'];
    private readonly ConcurrentDictionary<Guid, byte> _queue = new();
    private long _lastAddedTicks;
    private int _metadataRetryCount;
    private int _sendFailures;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        libraryManager.ItemAdded += ItemAdded;
        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        libraryManager.ItemAdded -= ItemAdded;
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            if (_queue.IsEmpty)
            {
                continue;
            }

            var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
            var batchSeconds = Math.Clamp(configuration.BatchSeconds, 5, 3600);
            var lastAdded = new DateTime(Interlocked.Read(ref _lastAddedTicks), DateTimeKind.Utc);
            if (DateTime.UtcNow - lastAdded < TimeSpan.FromSeconds(batchSeconds))
            {
                continue;
            }

            await FlushAsync(configuration, stoppingToken).ConfigureAwait(false);
        }
    }

    private void ItemAdded(object? sender, ItemChangeEventArgs eventArgs)
    {
        if (eventArgs.Item.IsVirtualItem || eventArgs.Item is not (Movie or Series or Season or Episode))
        {
            return;
        }

        var configuration = Plugin.Instance?.Configuration;
        if (configuration is not null && IsIgnored(eventArgs.Item, configuration))
        {
            logger.LogDebug("Not queuing {ItemName} for Telegram: it is in an ignored library", eventArgs.Item.Name);
            return;
        }

        _queue.TryAdd(eventArgs.Item.Id, 0);
        Interlocked.Exchange(ref _lastAddedTicks, DateTime.UtcNow.Ticks);
        logger.LogDebug("Queued {ItemType} {ItemName} for Telegram", eventArgs.Item.GetType().Name, eventArgs.Item.Name);
    }

    private async Task FlushAsync(PluginConfiguration configuration, CancellationToken cancellationToken)
    {
        var ids = _queue.Keys.ToArray();
        try
        {
            var libraryItems = ids
                .Where(id => id != Guid.Empty)
                .Select(libraryManager.GetItemById)
                .Where(item => item is not null)
                .Cast<BaseItem>()
                .Where(item => !IsIgnored(item, configuration))
                .ToList();
            var incompleteItems = libraryItems.Where(IsMetadataIncomplete).ToList();
            if (incompleteItems.Count > 0 && _metadataRetryCount < MaxMetadataRetries)
            {
                _metadataRetryCount++;
                logger.LogInformation(
                    "Waiting for hierarchy metadata on {ItemCount} TV items before sending Jellygram batch (attempt {Attempt}/{MaxAttempts})",
                    incompleteItems.Count,
                    _metadataRetryCount,
                    MaxMetadataRetries);
                ScheduleRetry(configuration, 60);
                return;
            }

            if (incompleteItems.Count > 0)
            {
                logger.LogWarning(
                    "Sending Jellygram batch with {ItemCount} TV items still missing hierarchy metadata after {AttemptCount} retries",
                    incompleteItems.Count,
                    MaxMetadataRetries);
            }

            // One item that can't be read must not block the whole batch: skip it and send the rest.
            var items = new List<MediaItem>();
            foreach (var libraryItem in libraryItems)
            {
                try
                {
                    if (Convert(libraryItem) is { } converted)
                    {
                        items.Add(converted);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "Leaving {ItemName} ({ItemId}) out of the Telegram batch: its details could not be read", libraryItem.Name, libraryItem.Id);
                }
            }

            var notifications = NotificationFormatter.Build(items, configuration.IncludeDescriptions);
            foreach (var notification in notifications)
            {
                await sender.SendAsync(notification, cancellationToken).ConfigureAwait(false);
            }

            foreach (var id in ids)
            {
                _queue.TryRemove(id, out _);
            }

            _metadataRetryCount = 0;
            _sendFailures = 0;
            logger.LogInformation("Sent {NotificationCount} Telegram notifications for {ItemCount} library items", notifications.Count, items.Count);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Retrying for ever would jam every later notification behind this batch, so give up after a few tries.
            if (++_sendFailures >= MaxSendAttempts)
            {
                foreach (var id in ids)
                {
                    _queue.TryRemove(id, out _);
                }

                _sendFailures = 0;
                _metadataRetryCount = 0;
                logger.LogError(exception, "Giving up on a Jellygram batch of {ItemCount} items after {AttemptCount} failed attempts; newly added items will be sent as normal", ids.Length, MaxSendAttempts);
                return;
            }

            logger.LogError(exception, "Could not send Jellygram batch (attempt {Attempt}/{MaxAttempts}); it will be retried", _sendFailures, MaxSendAttempts);
            ScheduleRetry(configuration, 30);
        }
    }

    private void ScheduleRetry(PluginConfiguration configuration, int delaySeconds)
    {
        var batchSeconds = Math.Clamp(configuration.BatchSeconds, 5, 3600);
        Interlocked.Exchange(ref _lastAddedTicks, DateTime.UtcNow.AddSeconds(delaySeconds - batchSeconds).Ticks);
    }

    private static bool IsMetadataIncomplete(BaseItem item) => item switch
    {
        Season season => season.SeriesId == Guid.Empty || season.IndexNumber is null,
        Episode episode => episode.SeriesId == Guid.Empty
            || episode.SeasonId == Guid.Empty
            || episode.ParentIndexNumber is null
            || episode.IndexNumber is null,
        _ => false
    };

    private MediaItem? Convert(BaseItem item)
    {
        var imagePath = item.HasImage(ImageType.Primary) ? item.PrimaryImagePath : null;
        return item switch
        {
            Movie movie => New(movie, "Movie", null, null, null, null, null, imagePath),
            Series series => New(series, "Series", null, null, null, null, null, imagePath),
            Season season => New(
                season,
                "Season",
                NullIfEmpty(season.SeriesId),
                season.Series?.Name ?? Lookup(season.SeriesId)?.Name ?? NullIfBlank(season.SeriesName),
                null,
                season.IndexNumber,
                null,
                imagePath),
            Episode episode => New(
                episode,
                "Episode",
                NullIfEmpty(episode.SeriesId),
                episode.Series?.Name ?? Lookup(episode.SeriesId)?.Name ?? NullIfBlank(episode.SeriesName),
                NullIfEmpty(episode.SeasonId),
                episode.ParentIndexNumber ?? (Lookup(episode.SeasonId) as Season)?.IndexNumber,
                episode.IndexNumber,
                imagePath),
            _ => null
        };
    }

    // Jellyfin throws on a blank ID, and items that haven't been linked to their show or season yet have one.
    private BaseItem? Lookup(Guid id) => id == Guid.Empty ? null : libraryManager.GetItemById(id);

    private static Guid? NullIfEmpty(Guid id) => id == Guid.Empty ? null : id;

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    // Ignored libraries are matched by library name, or by folder when the entry starts with "/".
    private bool IsIgnored(BaseItem item, PluginConfiguration configuration)
    {
        var entries = (configuration.IgnoredLibraries ?? string.Empty)
            .Split(ListSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (entries.Length == 0)
        {
            return false;
        }

        var path = item.Path ?? string.Empty;
        foreach (var folder in entries.Where(entry => entry.StartsWith('/')).Select(entry => entry.TrimEnd('/')))
        {
            if (path.Equals(folder, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var names = entries.Where(entry => !entry.StartsWith('/')).ToList();
        if (names.Count == 0)
        {
            return false;
        }

        try
        {
            return libraryManager.GetCollectionFolders(item).Any(library => names.Contains(library.Name, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Could not work out which library {ItemName} belongs to", item.Name);
            return false;
        }
    }

    private static MediaItem New(
        BaseItem item,
        string type,
        Guid? seriesId,
        string? seriesName,
        Guid? seasonId,
        int? seasonNumber,
        int? episodeNumber,
        string? imagePath) =>
        new(item.Id, type, item.Name, item.Overview ?? string.Empty, item.ProductionYear, seriesId, seriesName, seasonId, seasonNumber, episodeNumber, imagePath);
}
