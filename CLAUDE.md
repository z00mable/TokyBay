# TokyBay

C# .NET 9 Console App zum Scrapen und Konvertieren von Audiobüchern (M4B/MP3) von mehreren Websites.

## Stack

- **.NET 9**, C# mit Nullable-Referenztypen und Primary Constructors
- **Spectre.Console** — alle Konsolenausgaben (Markup mit `[green]...[/]` etc., Status-Spinner)
- **Xabe.FFmpeg** — Audio-Konvertierung und Segment-Merging; Binaries via `Xabe.FFmpeg.Downloader`
- **Newtonsoft.Json** — JSON-Parsing der API-Antworten
- **Microsoft.Extensions.DependencyInjection** — DI-Container

## Architektur

### Strategy Pattern (Scraper)

```
IScraperStrategy                          (Scraper/Abstractions/)
    └── BaseScraperStrategy               (Scraper/Base/)
            ├── TokybookStrategy          (Scraper/Strategies/) — tokybook.com
            ├── ZAudiobooksStrategy       (Scraper/Strategies/) — zaudiobooks / freeaudiobooks.top
            ├── GoldenAudiobookStrategy   (Scraper/Strategies/) — alle Sites mit `<source type="audio/mpeg">` Struktur:
            │                             goldenaudiobook.net, fulllengthaudiobooks.net, bigaudiobooks.net,
            │                             findaudiobook.com, bookaudiobook.net, hotaudiobooks.com, audiozaic.com
            └── PlaylistAudiobookStrategy (Scraper/Strategies/) — alle Sites mit `data-playlist` JSON-Attribut:
                                          hdaudiobooks.net
```

- `ScraperFactory` wählt per `CanHandle(url)` die passende Strategie aus
- `ScraperConfig` steuert Parallelismus-Parameter (Defaults: 3 parallele Downloads, 2 Konvertierungen, 5 Segmente/Track)

### Download-Pipeline

Downloads und Konvertierungen laufen entkoppelt über `Channel<T>` + `SemaphoreSlim`:
1. Download-Tasks schreiben fertige Tracks in einen bounded Channel
2. Konvertierungs-Tasks lesen aus dem Channel und rufen FFmpeg auf
3. Channel wird nach `Task.WhenAll(downloadTasks)` geschlossen

### Track-Typen

- `SegmentedTrackData` — für HLS-Streams (`.m3u8` → `.ts`-Segmente → merge via FFmpeg concat)
- `DirectFileTrackData` — für direkte MP3/Audio-Downloads (zaudiobooks, goldenaudiobook); Konvertierung wird übersprungen wenn Quelldatei bereits im Zielformat vorliegt

## Neue Website hinzufügen

1. Neue Klasse in `TokyBay/Scraper/Strategies/` anlegen, die von `BaseScraperStrategy` erbt
2. `CanHandle(string url)` implementieren — URL-basierte Erkennung
3. `DownloadBookAsync(string url)` implementieren — Metadata fetch, dann `ProcessTracksInParallelAsync`
4. In `ScraperServiceExtensions.cs` registrieren:
   ```csharp
   services.AddTransient<IScraperStrategy, NeueStrategy>();
   ```

## Build & Run

```sh
dotnet build
dotnet run --project TokyBay -- -d "C:\Pfad\zum\Download"
```

### Publish (Cross-Platform)

```sh
dotnet publish -c Release -r win-x64 --self-contained
dotnet publish -c Release -r linux-x64 --self-contained
dotnet publish -c Release -r linux-arm64 --self-contained
dotnet publish -c Release -r osx-x64 --self-contained
dotnet publish -c Release -r osx-arm64 --self-contained
```

## Konventionen

- Konsolenausgaben immer via `_console.MarkupLine(...)` (Spectre), nie `Console.WriteLine`
- Fehlermeldungen in `[red]`, Erfolg in `[green]`, Info in `[blue]`, Konvertierungen in `[cyan]`
- Dateinamen werden via `SanitizeName()` bereinigt (`[^A-Za-z0-9]+` → `_`)
- Retry-Logik: `RetryAsync<T>()` aus `BaseScraperStrategy` verwenden (exponentielles Delay)
- Temp-Verzeichnisse immer via `SafeDeleteDirectory()` aufräumen
