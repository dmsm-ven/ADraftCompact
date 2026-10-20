# Ability Draft Overlay

A lightweight Windows overlay for **Dota 2 Ability Draft**. On demand it recognizes the draft board, reads the heroes and abilities of all 10 players, and displays them in a transparent overlay.

## Features

- .NET 10 / WPF desktop app.
- One-click recognition: press **Recognize abilities** (or the hotkey, `Ctrl+Shift+O` by default) once every player has picked everything.
- Transparent overlay with configurable position, icon size, spacing, background opacity, and borders.
- Event log of detected picks (only when started with `--debug-mode`).
- Incremental download of hero and ability artwork.
- Closing the window hides the app into the tray (tray menu: Show, Exit); the hotkey and the overlay keep working.
- The overlay is shown after a successful recognition and hidden when the game closes (`OverlayAutoHide` in `settings.json`).

## Requirements

- Windows and the .NET 10 SDK.
- Dota 2 set to **Borderless Window** or **Windowed** mode.
- English (United States) Windows OCR language for hero-name recognition.
- Dota 2 language set to English, and a 1920x1080 primary display.

These requirements are checked at startup. If one fails, a warning is shown, the window is disabled (title-bar buttons still work) and closing it exits instead of hiding to the tray. To bypass the checks set `"SkipEnvironmentChecks": true` in `%AppData%\AbilityDraftOverlay\settings.json`; the expected resolution is `TargetScreenWidth` / `TargetScreenHeight` in the same file.

## Run

Open `AbilityDraftOverlay.slnx` in Visual Studio, restore packages, and run the `AbilityDraftOverlay` project. Use **Download assets** (Overlay settings, bottom left) on first launch. When all players have picked, click **Recognize abilities** or press the hotkey: the game window is captured (it must be visible, not covered) and the overlay is filled in. The hotkey is configured in the overlay settings.

## Local data

Next to the executable:

- `app.log` - the single application log (started afresh when it exceeds 5 MB).
- `last_recognition.png` - debug image of the latest recognition.
- `Assets` - downloaded images and reference data.
- `layout.1920x1080.json` - optional layout override.

`%LocalAppData%\AbilityDraftOverlay\` contains only two files, directly in its root: `overlay-settings.json` and `icons.index` (generated icon index).

The application folder must therefore be writable.

## Overlay layout

The overlay displays odd-numbered players (1, 3, 5, 7, 9) in the upper block and even-numbered players (2, 4, 6, 8, 10) in the lower block. Each player row contains the hero, three abilities, and the ultimate. Use the gear button to adjust appearance, hotkey, and position (**Move overlay**).

## Sources and Notes

- [OpenDota constants](https://github.com/odota/dotaconstants) — JSON metadata for heroes and abilities.
- [Steam CDN](https://cdn.cloudflare.steamstatic.com/apps/dota2/images/) — public Dota 2 images.
- [Dota 2 Assets Downloader](https://github.com/CaptchaQ/dota2-assets-downloader) — an alternative PowerShell downloader if additional asset categories are needed later.

Images and game assets belong to their respective copyright holders. The downloaded icons should be checked against real Ability Draft screenshots to confirm that they match the artwork shown in-game.
