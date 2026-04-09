# TokyBay

C# .NET 10 console app for scraping and converting audiobooks (M4B/MP3) from multiple websites.

## Stack

- **.NET 10**, C# with nullable reference types and primary constructors
- **Spectre.Console** — all console output (markup with `[green]...[/]` etc., status spinners, tables, figlet)
- **Xabe.FFmpeg** — audio conversion and segment merging; binaries via `Xabe.FFmpeg.Downloader`
- **Newtonsoft.Json** — JSON parsing of API responses
- **Microsoft.Extensions.DependencyInjection** — DI container
- **Microsoft.Extensions.Configuration** — configuration via `appsettings.json` with binder
- **Microsoft.Extensions.Http** — `IHttpClientFactory` for typed HttpClients

## Architecture

### Strategy Pattern (Scraper)

```
IScraperStrategy                          (Scraper/Abstractions/)
    └── BaseScraperStrategy               (Scraper/Base/)
            ├── TokybookStrategy          (Scraper/Strategies/) — tokybook.com
            ├── DropboxTracksStrategy     (Scraper/Strategies/) — sites with JS `tracks = [{ chapter_link_dropbox }]` structure:
            │                             zaudiobooks.com, freeaudiobooks.top
            ├── AudioSourceTagStrategy    (Scraper/Strategies/) — sites with `<source type="audio/mpeg">` or `<a href="*.mp3">` structure:
            │                             goldenaudiobook.net, fulllengthaudiobooks.net, bigaudiobooks.net,
            │                             findaudiobook.com, bookaudiobook.net, hotaudiobooks.com, audiozaic.com,
            │                             appaudiobooks.com
            ├── PlaylistAudiobookStrategy (Scraper/Strategies/) — all sites with `data-playlist` JSON attribute:
            │                             hdaudiobooks.net
            └── AudioAzStrategy           (Scraper/Strategies/) — Next.js site with tracks JSON in streaming data:
                                          audioaz.com
```

- `ScraperFactory` selects the matching strategy via `CanHandle(url)`
- `ScraperConfig` controls parallelism parameters (defaults: 3 parallel downloads, 2 conversions, 5 segments/track)
- Runtime overrides in `Program.cs`: MaxParallelDownloads=5, MaxParallelConversions=3, MaxSegmentsPerTrack=8

### Download Pipeline

Downloads and conversions run decoupled via `Channel<T>` + `SemaphoreSlim`:
1. Download tasks write completed tracks into a bounded channel
2. Conversion tasks read from the channel and invoke FFmpeg
3. Channel is closed after `Task.WhenAll(downloadTasks)`

### Track Types

- `SegmentedTrackData` — for HLS streams (`.m3u8` → `.ts` segments → merge via FFmpeg concat)
- `DirectFileTrackData` — for direct MP3/audio downloads (zaudiobooks, goldenaudiobook); when the source is already in the target format, a copy-conversion runs to embed metadata without re-encoding

### Metadata Pipeline

`BaseScraperStrategy` provides shared metadata infrastructure used by all strategies.

For all non-Tokybook strategies, metadata is collected in two stages before downloading:

**Stage 1 — MP3 tag enrichment** (`EnrichFromFirstTrackTagsAsync`):
- Runs `ffprobe` on the first chapter URL to read existing ID3 tags without downloading the file
- Maps: `artist` → `Author`, `date` → `Year`, `comment` → `Description` (skips chapter references like "Chapter 1")
- Only fills empty fields — never overwrites

**Stage 2 — HTML extraction** (`ExtractCommonMetadata`):
- Only fills fields still empty after Stage 1
- `og:image` → `CoverArtUrl`
- `og:description` → `Description`
- `<script type="application/ld+json">` with `@type:"Audiobook"` → all fields (AudioAZ)
- `ld+json` `headline` field → author via `ExtractAuthorFromHeadline()` (WordPress/Yoast sites)
- `<link rel="preload" as="image">` → `CoverArtUrl` fallback (fulllengthaudiobooks, appaudiobooks)
- H1 title → author as last resort

Other shared helpers:
- `DownloadCoverArtAsync(url, folder)` — downloads cover once to `_cover.{ext}`, returns temp path
- `BuildMetadataParams(bookMetadata, trackData, hasCoverArt)` — returns FFmpeg `-metadata` flags (title, album, artist, album_artist, track, genre, comment, publisher, date, cover art)
- Cover art is passed to FFmpeg as a second input (`-map 0:a -map 1:v -c:v copy -disposition:v attached_pic`)
- Cover art temp file is always cleaned up via `finally` after the conversion pipeline completes

**Tokybook** gets richer metadata directly from the `post-details` API response (`authors`, `narrators`, `coverImage`, `description`, `publisher`) — no HTML scraping or ffprobe needed.

### Data Model

```
AudiobookMetadata (abstract)
│   Title, FolderPath
│   Author, Narrator, CoverArtUrl, Description, Publisher, Year   ← populated by ffprobe tags, HTML, or API response
├── SimpleAudiobookMetadata       — ChapterUrls: List<string>
└── StreamingAudiobookMetadata    — Tracks: List<TrackInfo>, StreamToken, AudioBookId

TrackData (abstract)
│   TrackTitle, SanitizedTitle, TrackNumber, TotalTracks
├── SegmentedTrackData            — TempFolder, FolderPath, TsSegments: List<string>
└── DirectFileTrackData           — FilePath, FolderPath

TrackInfo                         — Src, TrackTitle
UserSettings                      — DownloadPath, FFmpegDirectory, ConvertToMp3, ConvertToM4b
```

### Services

- `HttpService` (`IHttpService`) — GET/POST via `HttpClient` (10 min timeout, DI via `IHttpClientFactory`)
- `DownloadService` — orchestrates strategy selection and execution, provides supported domains
- `SettingsService` (`ISettingsService`) — loads/saves `UserSettings` in `appsettings.json`, auto-downloads FFmpeg
- `IpifyService` (`IIpifyService`) — fetches IP address via `api.ipify.org` (required for Tokybook API)
- `PageService` (`IPageService`) — Spectre.Console UI wrapper with figlet banner (`Bulbhead.flf`), ESC cancellation

### Pages (UI Flow)

```
Application.RunAsync(args)
  → InitializeAsync() — load settings, ensure FFmpeg
  → Parse CLI args (-d / --directory)
  → MainPage.ShowAsync() — main menu loop
      ├── "Search book on Tokybook.com" → SearchTokybookPage (API: /api/v1/search, pagination)
      ├── "Download from URL"           → DownloadPage (shows supported domains, prompts for URL)
      ├── "Settings"                    → SettingsPage (download path, FFmpeg path, MP3/M4B toggles)
      └── "Exit"
```

### Dependency Injection

Registration in `Program.cs` → `ConfigureServices()` and `ScraperServiceExtensions.AddScraperServices()`:

- Singleton: `IConfiguration`, `IAnsiConsole`, `EscapeCancellableConsole`, `IIpifyService`, `IPageService`, `ISettingsService`, `ScraperFactory`, `DownloadService`
- Typed HttpClient: `IHttpService` → `HttpService`
- Transient: all `IScraperStrategy` implementations

## Configuration

### appsettings.json

```json
{
  "UserSettings": {
    "DownloadPath": "",
    "FFmpegDirectory": "",
    "ConvertToMp3": true,
    "ConvertToM4b": false
  }
}
```

### CLI Arguments

- `-d` / `--directory` `<path>` — sets download directory

## Adding a New Website

1. Create a new class in `TokyBay/Scraper/Strategies/` extending `BaseScraperStrategy`
2. Implement `CanHandle(string url)` — URL-based detection
3. Implement `DownloadBookAsync(string url)` — fetch metadata, then call `ProcessTracksInParallelAsync` or `ProcessDirectFilesInParallelAsync`
4. In the metadata fetch method, call `ExtractCommonMetadata(html, metadata)` after building the `SimpleAudiobookMetadata` object — this fills cover art, author, description automatically from og-tags and ld+json
5. Register in `ScraperServiceExtensions.cs`:
   ```csharp
   services.AddTransient<IScraperStrategy, NewStrategy>();
   ```

## Build & Run

```sh
dotnet build
dotnet run --project TokyBay -- -d "C:\Path\To\Downloads"
```

### Publish (Cross-Platform)

```sh
dotnet publish -c Release -r win-x64 --self-contained
dotnet publish -c Release -r linux-x64 --self-contained
dotnet publish -c Release -r linux-arm64 --self-contained
dotnet publish -c Release -r osx-x64 --self-contained
dotnet publish -c Release -r osx-arm64 --self-contained
```

Publish profiles are in `TokyBay/Properties/PublishProfiles/` (WinX64, LinuxX64, LinuxArm64).
Flags: `PublishSingleFile=true`, `PublishTrimmed=false`, `SelfContained=true`.

## CI/CD (GitHub Actions)

Workflow: `.github/workflows/release.yml`

- **Trigger:** Pull request merged into `master`
- **Constraint:** Org `z00mable` only allows actions owned by the org — no third-party actions like `actions/checkout`, `actions/setup-dotnet`, `softprops/action-gh-release`. Use `git clone`/`run`-based alternatives instead (e.g. `dotnet-install.sh` for SDK setup, `gh release create` instead of `softprops/action-gh-release`).
- **Steps:** git clone → install .NET 10 SDK (via `dotnet-install.sh`) → extract version from `.csproj` → publish for 5 platforms (win-x64, linux-x64, linux-arm64, osx-x64, osx-arm64) → ZIP → git tag `v{version}` → GitHub Release via `gh release create`
- **Release notes:** From PR body

## Conventions

- Console output always via `_console.MarkupLine(...)` (Spectre), never `Console.WriteLine`
- Error messages in `[red]`, success in `[green]`, info in `[blue]`, conversions in `[cyan]`, warnings in `[yellow]`, secondary in `[grey]`
- Filenames are sanitized via `SanitizeName()` (`[^A-Za-z0-9]+` → `_`)
- Retry logic: use `RetryAsync<T>()` from `BaseScraperStrategy` (exponential delay)
- Temp directories always cleaned up via `SafeDeleteDirectory()`
- ESC cancellation in UI prompts via `EscapeCancellableConsole`
