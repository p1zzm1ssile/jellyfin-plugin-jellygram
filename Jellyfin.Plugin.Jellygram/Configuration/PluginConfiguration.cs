using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Jellygram.Configuration;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public string BotToken { get; set; } = string.Empty;

    public string ChatId { get; set; } = string.Empty;

    public string ThreadId { get; set; } = string.Empty;

    public int BatchSeconds { get; set; } = 90;

    public bool IncludeDescriptions { get; set; } = true;

    public bool IncludePosters { get; set; } = true;

    public bool Silent { get; set; }

    /// <summary>
    /// Gets or sets the libraries whose new items are never announced: library names, or folders starting with "/",
    /// separated by commas or new lines.
    /// </summary>
    public string IgnoredLibraries { get; set; } = string.Empty;
}
