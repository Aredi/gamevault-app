# WPF → Avalonia port

The client (`gamevault/`) was a WPF + MahApps.Metro application that only ran on Windows.
On the `avalonia` branch it is converted in place to Avalonia 11 so it runs natively on
Linux and Windows. The WPF version stays available on the `unlock-plus` branch.

## Stack

| WPF                                   | Avalonia                                              |
|---------------------------------------|-------------------------------------------------------|
| MahApps.Metro `MetroWindow`           | `Window` with custom chrome (`ExtendClientAreaToDecorationsHint`) |
| MahApps `HamburgerMenu`               | Left navigation `ListBox` in `MainWindow`             |
| MahApps `ShowMessageAsync`            | `DialogService` (in-window overlay)                   |
| MahApps `Flyout` (app bar)            | Bottom toast in `MainWindow` / `LoginWindow`          |
| MahApps `ToggleSwitch` / `NumericUpDown` | Avalonia `ToggleSwitch` / `NumericUpDown`         |
| MahApps `ProgressRing` / `Badged`     | `ProgressRing` / `Badge` controls in `UserControls/GeneralControls` |
| MahApps.IconPacks                     | `StreamGeometry` resources in `Icons.axaml`           |
| WebView2 (SSO, YouTube trailers)      | `Avalonia.Controls.WebView` (WebView2 / WebKitGTK)    |
| Markdig.Wpf                           | Markdown.Avalonia                                     |
| VirtualizingWrapPanel                 | `ItemsRepeater` + `UniformGridLayout`                 |
| LiveCharts pie chart                  | `PieChart` control drawn with Avalonia geometry       |
| WinForms `NotifyIcon`, JumpList       | Avalonia `TrayIcon` (JumpList dropped)                |
| Windows toast notifications           | DesktopNotifications (FreeDesktop DBus / Windows)     |
| `Lib/Preferences.dll`                 | `GameVault.Core/Preferences.cs`                       |
| Registry: URI scheme, autostart       | `Helper/Platform/*` (registry on Windows, XDG files on Linux) |
| MS Store updater, analytics, crash reporter | removed                                         |

## Conventions used during the conversion

- Compiled bindings are off by default (`AvaloniaUseCompiledBindingsByDefault=false`) so the
  existing reflection bindings keep working.
- `Visibility` properties in view models became `bool` (`IsVisible`).
- WPF `DataTrigger`s are rewritten as bound style classes (`Classes.foo="{Binding ...}"`) or
  as direct property bindings with converters.
- Brushes keep their semantic names: `MahApps.Brushes.ThemeForeground` → `Brush.Foreground`,
  `MahApps.Brushes.ThemeBackground` → `Brush.Background`,
  `MahApps.Brushes.ThemeBackground2` → `Brush.Background2`, `MahApps.Brushes.Accent` → `Brush.Accent`,
  `MahApps.Brushes.IdealForeground` → `Brush.Foreground2`.
- Themes: the existing theme files (including community themes from
  `Phalcode/gamevault-community-themes`) are WPF resource dictionaries. They are not loaded as
  XAML; `ThemeManager` parses their `Color`/`String` entries, so the same files work unchanged.
- Paths are built with `Path.Combine`; no hard-coded `\`.

## Linux specifics

- Windows games (`.exe`, `.bat`, `.msi`) are launched through a compatibility tool:
  `umu-run` (latest GE-Proton) if installed, otherwise `wine`; the default is set in Settings → Linux.
- Compatibility tool manager (Settings → Linux), similar to ProtonUp-Qt / the Steam Deck:
  - lists the Proton and Wine builds of GameVault (`~/.local/share/GameVault/compatibilitytools.d`),
    Steam (`compatibilitytools.d`, `steamapps/common/Proton*`, also Flatpak) and Lutris;
  - downloads GE-Proton (GloriousEggroll/proton-ge-custom), Wine and Wine Staging
    (Kron4ek/Wine-Builds, WoW64) with checksum verification, and deletes the downloaded ones.
- Per game (Game Settings → Launch Options): the tool to use ("Force a specific compatibility tool")
  and a shared or separate Wine prefix (`~/.local/share/GameVault/prefixes/<game id>`), plus
  winecfg and prefix shortcuts. Stored in the game's `gamevault-exec` file; installers,
  uninstallers and cloud saves use the same tool and prefix.
- Proton builds run through `umu-run` (`PROTONPATH`) when it is installed, otherwise directly
  (`proton waitforexitandrun`, `STEAM_COMPAT_DATA_PATH`). Both use the umu prefix layout
  (`pfx -> .`), so a prefix can switch between them.
- 7-Zip and Ludusavi come from the system (`7zz`/`7z`, `ludusavi`) when the bundled Windows
  binaries can't be used.
- Play-time tracking reads `/proc` (exe, cmdline, cwd) instead of window handles, so games
  running under Wine/Proton are detected.
- `gamevault://` links are registered with a `.desktop` file + `xdg-mime`.
- Steam shortcuts point at the GameVault executable (`start --gameid=N`), since Steam on Linux
  cannot launch URL shortcuts.

## Status

See the checklist in the pull request / commit history. Files are converted one by one; each
commit must build (`dotnet build gamevault/gamevault.csproj`).
