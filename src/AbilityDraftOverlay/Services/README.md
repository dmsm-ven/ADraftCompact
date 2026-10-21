# Services

- `DotaAssetsDownloader` downloads hero and ability images plus OpenDota JSON reference data.
- Assets are stored in the `Assets` folder next to the executable (see `AppPaths`).
- Existing files are skipped, so the downloader can be rerun to fetch missing assets.
- Network sources: OpenDota constants API and the public Steam CDN. If a network request fails, retry the download later.
- `IconIndex` builds and caches the hero and ability icon descriptor index (`icons.index` next to the exe).
- `DraftLayoutProvider` loads slot coordinates from `Layouts\ability_draft_1920x1080.json` or the user layout `layout.1920x1080.json` (next to the executable).
- `ScreenshotRecognitionService` recognizes slots using the layout and saves a debug image with slot outlines.
- `HeroNameReader` reads hero-name text using built-in Windows OCR; `HeroNameMatcher` fuzzy-matches it against the hero catalog.
- `GameWindowCapture` captures only the game window (process `dota2`, any monitor) and skips minimized or covered windows. It does not need the game to be the foreground window, but the window must not be covered.
- `DraftSessionTracker` tracks sessions and each player's pick order in memory (sessions are not saved to disk).
- `OverlayAutoHide` hides the overlay when the game process closes.
- `HotkeyService` is the system-wide hotkey for "Recognize abilities" (`RegisterHotKey`, no keyboard hook).
- `AppOptions` reads command-line switches (`--debug-mode` shows the event log).
- `AppPaths` is the single place that defines where files live.
- `ManualRecognitionService` does one recognition on demand: capture, recognize, update the draft session. There are no timers and no automatic screenshots.
- `EnvironmentCheckService` runs the startup checks (English in Windows, OCR, Dota 2 language via `SteamGameLanguage`, screen resolution).
- `TrayIconService` is the tray icon (Show / Exit menu, native `Shell_NotifyIcon`, no WinForms); `AppLifecycle` tells the main window whether closing hides it or exits.
- `DraftReportFormatter` formats a text report for players and picks.
