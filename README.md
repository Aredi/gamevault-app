[![logo](https://gamevau.lt/img/logo-text-and-image-sbs.png)](https://gamevau.lt)

> [!NOTE]
> **This is an unofficial, non-commercial fork of [Phalcode/gamevault-app](https://github.com/Phalcode/gamevault-app)**, distributed under the same [CC BY-NC-SA 4.0](http://creativecommons.org/licenses/by-nc-sa/4.0/) license. It is not affiliated with or endorsed by Phalcode.
>
> **Changes from upstream:**
> - All features previously gated behind a GameVault+ subscription are available without a Phalcode account: premium themes, community themes, animated (GIF) avatars, multiple server profiles, cloud saves, Steam shortcut sync, Discord Rich Presence, and `gamevault://` install/uninstall links.
> - The Phalcode account login (OIDC + embedded WebView2), the subscription lookup and the in-app upsell UI were removed. The *GameVault+* settings tab is now called *Integrations*.
> - The license status is no longer included in analytics payloads.
> - The client was ported from WPF to **Avalonia**: one code base for **Windows and Linux**.
> - Windows installer with automatic updates (Velopack), Linux AppImage.
> - Linux: Proton / Wine version per game (like on the Steam Deck), automatic protonfixes (umu database) and winetricks components.
> - Download queue with a schedule, full offline mode, collections, played / never played filters, disk cleanup, notifications for new games.
> - "Install & Play" (download, extract, install silently and start in one click) and a "Publish a Game" assistant in the admin console.
>
> If you enjoy GameVault, please consider supporting the original developers (see below).

## Download ⬇️

Get the [latest release](https://github.com/Aredi/gamevault-app/releases/latest):

| System | File | |
|---|---|---|
| Windows | `GameVault-win-Setup.exe` | Installer, updates itself |
| Windows | `GameVault-win-Portable.zip` | No installation |
| Linux | `GameVault.AppImage` | `chmod +x GameVault.AppImage`, updates itself |
| Linux | `GameVault-linux-x64.tar.gz` | Portable |

The Windows installer is not code-signed: if SmartScreen warns about it, choose *More info* → *Run anyway*.

On Linux, Windows games need Wine, or [umu-launcher](https://github.com/Open-Wine-Components/umu-launcher) for Proton. Builds of GE-Proton and Wine can also be downloaded in *Settings → Linux*. Extraction uses 7-Zip (`sudo apt install 7zip`).

# GameVault Application

## Introduction

**What is GameVault?**  
GameVault is an innovative gaming platform providing a self-hosted, source-available alternative to popular gaming platforms. It lets you and your friends enjoy DRM-free games stored on your file server in an organized way. Think of it as a self-hosted Steam. The project you are looking at right now is the client application.
  
**Learn More & Get Started**  
[You can learn more about the project and find useful guides and information on the official Website.](https://gamevau.lt)
  
## Support 🤝

We're working hard in our free time to provide you, your friends, and families with the best self-hosted gaming experience.  
It would mean a lot to us if you could support us developers by [getting GameVault+](https://gamevau.lt/gamevault-plus).

Alternatively, you can support us by donating us some spare dollars on any of these platforms:

- [Ko-Fi](https://ko-fi.com/phalcode)
- [Liberapay](https://liberapay.com/Phalcode)
- [GitHub Sponsors](https://github.com/sponsors/Phalcode)
- [PayPal](https://paypal.me/phalcode)

**TIP FOR DONATORS:**  
If you connect your Discord account to Ko-Fi, you'll automatically receive the "@Supporters" role and permanently stand out in our Discord members list. If you donate through a different platform and want to obtain the role, simply send us a message with your receipt as proof that you're truly a Supporter. 🌟

## License 📜

**[CC BY-NC-SA 4.0](http://creativecommons.org/licenses/by-nc-sa/4.0/)**

This work is licensed under a [Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International License](http://creativecommons.org/licenses/by-nc-sa/4.0/).

This project is not and never was open-source. [Click here to learn why.](https://gamevau.lt/blog/2023/07/13/)

## Legal Disclaimer ⚖️

GameVault manages DRM-free games and is solely a tool to address this need. We are not responsible for the content or files users store or share.

When we say DRM-free games, we only mean games obtained legally. While GameVault can theoretically be used with illegally obtained games, we do not endorse or support piracy.

Users must be aware of and comply with copyright laws in their respective jurisdictions. We encourage responsible and legal use of GameVault. Unlawful use is strictly improper and unauthorized.
