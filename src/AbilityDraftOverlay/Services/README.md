# Services

- `DotaAssetsDownloader` downloads hero and ability images plus OpenDota JSON reference data.
- Assets are stored in the `Assets` folder next to the executable (see `AppPaths`).
- Existing files are skipped, so the downloader can be rerun to fetch missing assets.
- Network sources: OpenDota constants API and the public Steam CDN. If a network request fails, retry the download later.
- `IconIndex` builds and caches the hero and ability icon descriptor index (`icons.index` in `%LocalAppData%\AbilityDraftOverlay`).
- `DraftLayoutProvider` loads slot coordinates from `Layouts\ability_draft_1920x1080.json` or the user layout `layout.1920x1080.json` (next to the executable).
- `ScreenshotRecognitionService` recognizes slots using the layout and saves a debug image with slot outlines.
- `HeroNameReader` reads hero-name text using built-in Windows OCR; `HeroNameMatcher` fuzzy-matches it against the hero catalog.
- `GameWindowCapture` captures only the game window (process `dota2`, any monitor) and skips minimized or covered windows. It does not capture unless the game is the foreground window.
- `DraftSessionTracker` tracks sessions and each player's pick order in memory (sessions are not saved to disk).
- `OverlayAutoVisibility` hides the overlay when the game closes and shows it 45 s after the draft board is detected.
- `AppPaths` is the single place that defines where files live.
- `AutoTrackingService` runs the capture → recognition → tracking pipeline on a timer and exposes `Schedule` (time of the next capture) for the status-bar countdown.
- `EnvironmentCheckService` runs the startup checks (English in Windows, OCR, Dota 2 language via `SteamGameLanguage`, screen resolution).
- `TrayIconService` is the tray icon (Show / Exit menu, native `Shell_NotifyIcon`, no WinForms); `AppLifecycle` tells the main window whether closing hides it or exits.
- `DraftReportFormatter` formats a text report for players and picks.
