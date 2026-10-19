# Services

- `DotaAssetsDownloader` downloads hero and ability images plus OpenDota JSON reference data.
- Assets are stored under `%LocalAppData%\AbilityDraftOverlay\Assets`.
- Existing files are skipped, so the downloader can be rerun to fetch missing assets.
- Network sources: OpenDota constants API and the public Steam CDN. If a network request fails, retry the download later.
- `IconIndex` builds and caches the hero and ability icon descriptor index (`Cache\icons.index`).
- `DraftLayoutProvider` loads slot coordinates from `Layouts\ability_draft_1920x1080.json` or the user layout `layout.1920x1080.json`.
- `ScreenshotRecognitionService` recognizes slots using the layout and saves a debug image with slot outlines.
- `HeroNameReader` reads hero-name text using built-in Windows OCR; `HeroNameMatcher` fuzzy-matches it against the hero catalog.
- `GameWindowCapture` captures only the game window (process `dota2`, any monitor) and skips minimized or covered windows. It does not capture unless the game is the foreground window.
- `DraftSessionTracker` tracks sessions and each player's pick order; `SessionStore` saves sessions to `Sessions\*.json`.
- `AutoTrackingService` runs the capture → recognition → tracking pipeline on a timer.
- `DraftReportFormatter` formats a text report for players and picks.
