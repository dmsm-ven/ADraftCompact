# Ability Draft Overlay

A lightweight Windows overlay for **Dota 2 Ability Draft**. It recognizes the draft board, tracks hero and ability picks for all 10 players, saves sessions locally, and displays the current picks in a transparent overlay.

## Features

- .NET 10 / WPF desktop app.
- Automatic draft-board capture and recognition.
- Transparent overlay with configurable position, icon size, spacing, background opacity, and borders.
- Local session history and event log.
- Incremental download of hero and ability artwork.

## Requirements

- Windows and the .NET 10 SDK.
- Dota 2 set to **Borderless Window** or **Windowed** mode.
- English (United States) Windows OCR language for hero-name recognition.

## Run

Open `AbilityDraftOverlay.slnx` in Visual Studio, restore packages, and run the `AbilityDraftOverlay` project. Use **Download Assets** on first launch, then select **Start Tracking**. Captures are performed only while the configured game process is the foreground window.

## Local data

All app data is stored under `%LocalAppData%\AbilityDraftOverlay\`:

- `Assets` — downloaded images and reference data.
- `Sessions` — saved draft sessions (`.json`).
- `Logs` — application logs.
- `Cache` — generated icon index.
- `Debug` — latest recognition debug image.

## Overlay layout

The overlay displays odd-numbered players (1, 3, 5, 7, 9) in the upper block and even-numbered players (2, 4, 6, 8, 10) in the lower block. Each player row contains the hero, three abilities, and the ultimate. Use **Overlay** to adjust appearance and **Move Overlay** to reposition it.

## Sources and Notes

- [OpenDota constants](https://github.com/odota/dotaconstants) — JSON metadata for heroes and abilities.
- [Steam CDN](https://cdn.cloudflare.steamstatic.com/apps/dota2/images/) — public Dota 2 images.
- [Dota 2 Assets Downloader](https://github.com/CaptchaQ/dota2-assets-downloader) — an alternative PowerShell downloader if additional asset categories are needed later.

Images and game assets belong to their respective copyright holders. The downloaded icons should be checked against real Ability Draft screenshots to confirm that they match the artwork shown in-game.
