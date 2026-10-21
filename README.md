# Ability Draft Overlay

Windows overlay for Dota 2 Ability Draft: recognizes the heroes and abilities of all 10 players from a screenshot and shows them over the game.

<table width="100%">
  <tr>
    <th width="50%" align="center">Overlay</th>
    <th width="50%" align="center">Settings</th>
  </tr>
  <tr>
    <td width="50%">
      <img src="screenshot1.png"  />
    </td>
    <td width="50%">
      <img src="screenshot2.png" />
    </td>
  </tr>
</table>

## How it works

1. Download the icon resources (Overlay settings → **Download assets**).
2. At the end of the draft press **Recognize** (or `Ctrl+Shift+O`).
3. Move the overlay and adjust the spacing if needed.

> **Note:** put the exe into its own folder (e.g. `C:\Games\AbilityDraftOverlay`), not the Desktop. `Assets`, settings and `app.log` are created next to it.

## Requirements

- Windows 10 (build 19041) or newer, 64-bit.
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64).
- 1920x1080 screen resolution.
- Dota 2 in **Borderless Window** or **Windowed** mode, game language English.
- Windows text recognition for **English (United States)** (used to read hero names). It is usually already there if English is among your Windows languages. Otherwise add it once: *Settings → Time & language → Language & region → Add a language → English (United States)*, and keep the *Optical character recognition* option ticked. No restart of the app is needed except to re-run the startup check.

## External resources

- [OpenDota constants](https://github.com/odota/dotaconstants)
- [Steam CDN images](https://cdn.cloudflare.steamstatic.com/apps/dota2/images/)
