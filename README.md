<p align="center">
  <img src="assets/banner.png" alt="Jellygram" width="800">
</p>

<h1 align="center">Jellygram</h1>

<p align="center">
  Rich, grouped Telegram notifications for newly added Jellyfin movies and TV.
</p>

<p align="center">
  <a href="https://github.com/p1zzm1ssile/jellyfin-plugin-jellygram/actions/workflows/ci.yml"><img src="https://github.com/p1zzm1ssile/jellyfin-plugin-jellygram/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue.svg" alt="MIT License"></a>
  <img src="https://img.shields.io/badge/Jellyfin-12.0.0--rc2-00A4DC" alt="Jellyfin 12.0.0-rc2">
</p>

Jellygram runs inside Jellyfin and listens directly for library additions. It waits for bulk imports to settle, resolves their hierarchy, and sends concise Telegram updates without requiring a webhook, sidecar service, or public endpoint.

## About this fork

This is a fork of [TheCommishDeuce/jellyfin-plugin-jellygram](https://github.com/TheCommishDeuce/jellyfin-plugin-jellygram). Version **1.2.0.0** adds:

- **Fix for stuck notifications.** Episodes or seasons that Jellyfin had not yet linked to their show made every batch fail with `Guid can't be empty`, retried every 30 seconds for ever, so no further notifications were sent. They are now handled, and the show name falls back to the episode's own details.
- **One bad item no longer blocks a batch.** An item whose details can't be read is left out and the rest is sent.
- **No endless retries.** A batch that still fails after 5 attempts is dropped, with an error in the log.
- **Libraries to ignore.** New items in the libraries you name are never announced, which keeps large IPTV or STRM libraries out of your chat.

Install it from this fork's plugin repository (see [Installation](#installation)). Full details are in the [changelog](CHANGELOG.md).

## Features

- Notifications for movies, series, seasons, and episodes
- Posters and descriptions from Jellyfin metadata
- Hierarchical grouping that avoids one message per episode
- Sliding batch window for slow and staggered imports
- Metadata-readiness retries for episodes still being identified
- Telegram groups, channels, and forum topics
- Optional silent delivery
- Libraries to ignore, by name or folder
- Persistent configuration through the Jellyfin dashboard
- HTML escaping, and delivery retries that give up after 5 attempts instead of blocking later notifications

## Notification hierarchy

| Priority | Jellyfin addition | Telegram result |
|---:|---|---|
| 1 | Entire new series | One show digest that absorbs its seasons and episodes |
| 2 | New season on an existing series | One season digest that absorbs its episodes |
| 3 | Multiple episodes on an existing series | One grouped digest with episode ranges |
| 4 | One episode on an existing series | One detailed episode notification |
| — | Movie | One detailed movie notification |

Higher-level additions take precedence. Importing a new series does not also produce separate season and episode notifications. Posters are attached when available; the text examples below omit them.

### Movie

```text
🎬 New movie
Cars (2006)

Description
A hotshot rookie race car gets stranded in a rundown town and learns that winning isn't everything.
```

### New show

If the first content Jellyfin sees is a later season, Jellygram identifies it instead of merely saying “1 season”:

```text
📺 New show
The Bear (2022)
Season 05 · 8 episodes

Description
A young chef from the fine dining world returns to Chicago to run his family's sandwich shop.
```

A multi-season import uses a compact range such as `Seasons 01–03 · 28 episodes`.

### New season on an existing show

```text
📚 New season
For All Mankind — Season 03
10 episodes included

Description
The Red Planet becomes the new frontier in the Space Race.
```

The season notification absorbs all episodes from that season in the same batch.

### Multiple episodes on an existing show

```text
🍿 New episodes
For All Mankind · 3 episodes
• S03: E08–E10 — The Sands of Ares, Coming Home, Stranger in a Strange Land
```

Discontinuous episode numbers remain explicit, for example `S04: E01–E03, E05`.

### Single episode on an existing show

```text
🍿 New episode
For All Mankind
S03E08 — The Sands of Ares

Description
Helios and the Soviet mission make an unexpected discovery.
```

## Compatibility

The `1.2.0.0` release targets:

- Jellyfin `12.x` (built against the `12.0.0-rc2` packages, target ABI `12.0.0.0`)
- .NET `10.0`

Jellyfin plugins are ABI-specific. Do not install this build on Jellyfin 10.10 or 10.11.

## Installation

### Plugin repository

Add this URL under **Dashboard → Plugins → Repositories**:

```text
https://raw.githubusercontent.com/p1zzm1ssile/jellyfin-plugin-jellygram/main/manifest.json
```

Then install **Jellygram** from the plugin catalog and restart Jellyfin.

**Moving over from the original plugin:** in **Dashboard → Plugins → Repositories**, delete the entry for `TheCommishDeuce/jellyfin-plugin-jellygram` and add the URL above instead. Then install **1.2.0.0** from the catalog and restart. It's the same plugin, so it upgrades in place and keeps your bot token, chat ID and other settings. If the catalog still offers only 1.1.0.0, the old repository is still listed, or GitHub's copy hasn't refreshed yet (allow up to 5 minutes).

### Manual Docker installation

1. Download `Jellygram_1.2.0.0.zip` from this fork's [GitHub releases](https://github.com/p1zzm1ssile/jellyfin-plugin-jellygram/releases).
2. Extract `Jellyfin.Plugin.Jellygram.dll` into the Jellyfin configuration volume:

   ```text
   /config/plugins/Jellygram/Jellyfin.Plugin.Jellygram.dll
   ```

3. Restart the Jellyfin container.

For development deployments from this repository:

```bash
./scripts/deploy-docker.sh root@your-jellyfin-host
```

The script builds locally, finds the running Jellyfin container, copies the DLL, and restarts the container.

## Telegram setup

1. Message [@BotFather](https://t.me/BotFather), run `/newbot`, and copy the bot token.
2. Add the bot to the destination group or channel. For a channel, grant permission to post messages.
3. Send a message in the destination chat.
4. Open `https://api.telegram.org/bot<TOKEN>/getUpdates` and find `message.chat.id` or `channel_post.chat.id`.
5. For forum topics, use the `message_thread_id` from an update posted inside the topic.

Treat the bot token like a password. If it is exposed, rotate it with BotFather.

Tips:

- **Channel IDs start with `-100`.** Include the minus sign, or Telegram replies `chat not found`.
- **The bot must be an admin of a channel**, with permission to post messages.
- **Sending to your own private chat?** Open the bot in Telegram and tap **Start** first. A bot can't message someone who hasn't started it (`bot can't initiate conversation with a user`).
- The digits before the `:` in the bot token are the bot's own ID, not a chat ID.

## Configuration

Open **Dashboard → Plugins → My Plugins → Jellygram**.

| Setting | Description |
|---|---|
| Telegram bot token | Token issued by BotFather |
| Chat ID | User, group, supergroup, or channel ID; group IDs are commonly negative |
| Topic/thread ID | Optional Telegram forum topic |
| Batch window | Seconds of inactivity before sending; every new item resets the timer |
| Include descriptions | Uses Jellyfin's `Overview` metadata |
| Include posters | Uploads Jellyfin's primary image when Telegram's caption limit permits |
| Send silently | Delivers without a notification sound |
| Libraries to ignore | Library names, separated by commas, whose new items are never announced, for example `IPTV Movies, IPTV Shows`. An entry starting with `/` matches a folder instead, for example `/config/m3u-vod` |

A window of `90` seconds is suitable for normal imports. For large 4K seasons copied sequentially, `300`–`600` seconds is safer.

## How batching works

Jellygram starts an inactivity timer when Jellyfin emits `ItemAdded`. Additional additions reset the timer. Once the window closes, Jellygram reloads the items from Jellyfin and builds the smallest useful set of notifications.

Items in an ignored library are never queued.

If an episode still lacks a series ID, season ID, season number, or episode number, the whole batch waits and retries metadata resolution every minute. After ten retries it sends what it can and logs a warning. Items still missing their show or season link are sent with the show name taken from the episode itself, and an item whose details can't be read at all is left out of the batch.

If Telegram rejects a batch (wrong chat ID, network error and so on), it is retried every 30 seconds. After 5 failed attempts the batch is dropped and an error is logged, so a problem with one batch never stops later notifications.

Jellygram currently tracks newly created Jellyfin items. Adding a 4K alternate version to an episode that already exists may produce `ItemUpdated` rather than `ItemAdded` and is intentionally not notified.

## Logs

Jellygram uses Jellyfin's normal logging system. For Docker:

```bash
docker logs --follow jellyfin 2>&1 | grep --line-buffered -iE 'jellygram|telegram'
```

Successful batches look like:

```text
Jellyfin.Plugin.Jellygram.Notifications.LibraryNotifier: Sent 1 Telegram notifications for 14 library items
```

Common errors:

| Log message | Meaning |
|---|---|
| `Guid can't be empty (Parameter 'id')` | The stuck-batch bug in 1.1.0.0 and earlier. Update to 1.2.0.0 |
| `Telegram returned 400 … chat not found` | Wrong chat ID, a channel ID without its leading `-`, or the bot isn't in the chat |
| `Telegram returned 403 … bot can't initiate conversation with a user` | Using a personal chat ID before tapping **Start** on the bot |
| `Jellygram bot token or chat ID is empty` | The settings haven't been saved yet |
| `Giving up on a Jellygram batch … after 5 failed attempts` | That batch was dropped; fix the cause above and new items will be sent as normal |

## Build and test

Requirements: .NET 10 SDK.

```bash
dotnet restore Jellyfin.Plugin.Jellygram.sln
dotnet test Jellyfin.Plugin.Jellygram.sln -c Release
dotnet build Jellyfin.Plugin.Jellygram.sln -c Release --no-restore
```

Create the release archive and MD5 checksum:

```bash
./scripts/package.sh
```

### Releasing (this fork)

1. Raise `<Version>`, `<AssemblyVersion>` and `<FileVersion>` in `Jellyfin.Plugin.Jellygram/Jellyfin.Plugin.Jellygram.csproj`.
2. Add a section for the new version to `CHANGELOG.md`, headed `## <version> — <date>`.
3. Merge to `main`, then open **Actions → Release → Run workflow**.

The workflow runs the tests, builds the plugin, publishes a GitHub release named `v<version>` with the zip, and adds the version to `manifest.json`, so it appears in Jellyfin's catalog within a few minutes.

## Privacy and security

Jellygram sends the configured notification text and selected artwork to Telegram's Bot API. It does not expose an inbound HTTP endpoint or contact metadata providers directly. The bot token is stored in Jellyfin's plugin configuration and is never intentionally logged.

See [SECURITY.md](SECURITY.md) for vulnerability reporting.

## License

[MIT](LICENSE). Original plugin by [TheCommishDeuce](https://github.com/TheCommishDeuce/jellyfin-plugin-jellygram).
