# Ability Draft Overlay

Overlay for Dota 2 Ability Draft mode: recognizes picked heroes/abilities and shows them over the game

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
3. Move the overlay and adjust spacing, border, etc.

> **Note:** put the exe into its own folder (e.g. `D:\Games\AbilityDraftOverlay`), not the Desktop. `Assets`, settings and `app.log` are created next to it.

## Requirements

- Windows 10/11
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (x64).
- 1920x1080 screen resolution.
- Dota 2 in **Borderless Window** or **Windowed** mode, game language English.
- Windows text recognition for **English** used to read hero names. It is usually already there if English is among your Windows languages.

## External resources

- [OpenDota constants](https://github.com/odota/dotaconstants)
- [Steam CDN images](https://cdn.cloudflare.steamstatic.com/apps/dota2/images/)
