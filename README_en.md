# XIVTCLauncher (FFXIVSimpleLauncher)

[English](README_en.md) | [繁體中文](README.md)

A faster launcher for Final Fantasy XIV Taiwan version with **automatic Dalamud support**, inspired by [FFXIVQuickLauncher](https://github.com/goatcorp/FFXIVQuickLauncher).

## Features

- **Fast Login** - Streamlined login process with saved credentials
- **OTP Support** - Manual entry or **Auto OTP** (automatically generates codes, no phone app needed!)
- **Web Login** - Integrated WebView2 browser for web-based authentication
- **Auto Dalamud Download** - Automatically downloads and updates Dalamud from [yanmucorp/Dalamud](https://github.com/yanmucorp/Dalamud)
- **Auto .NET Runtime** - Automatically downloads .NET 9.0 Runtime from NuGet (same as XIVLauncherCN)
- **Modern UI** - Clean Material Design interface in Traditional Chinese

## Requirements

- Windows 10/11
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (for the launcher itself)
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
- Final Fantasy XIV Taiwan version installed

> Note: .NET 9.0 Runtime for Dalamud is downloaded automatically, you don't need to install it manually.

## Installation

1. Download the latest release from [Releases](https://github.com/cycleapple/XIVTCLauncher/releases)
2. Extract to a folder of your choice
3. Run `FFXIVSimpleLauncher.exe`
4. Configure your game path in Settings
5. Enable Dalamud (optional) - it will download automatically!

## Auto OTP (One-Time Password)

No more opening your phone app every time you log in! The launcher can automatically generate OTP codes for you.

### How to Setup

1. Open **Settings**
2. Check **Enable Auto OTP**
3. Enter your OTP secret key (Base32 format)
   - You can find this on the SE Account OTP setup page
   - It's usually shown below the QR code
4. Click **Save Secret**
5. Done! OTP will be auto-filled when you log in

### Features

- **Secure Storage** - Secret key is stored in Windows Credential Manager (not in plain text)
- **Auto Refresh** - Code updates every 30 seconds with countdown display
- **Auto Fill** - Automatically fills OTP field on the login page
- **Easy Clear** - Remove secret anytime from Settings

### Manual OTP

If you prefer not to store your secret, you can still enter OTP manually on the login page.

## Dalamud Support

### Automatic Mode (Recommended)

The launcher can automatically download and manage Dalamud for you:

1. Open **Settings**
2. Enable **Dalamud**
3. Select **Auto Download (Recommended)**
4. Launch the game - everything will be downloaded automatically!

**What gets downloaded:**
- Dalamud from [yanmucorp/Dalamud](https://github.com/yanmucorp/Dalamud/releases) (~31MB)
- .NET 9.0 Runtime from NuGet (~70MB)
- Assets from [ottercorp](https://aonyx.ffxiv.wang)

**Storage location:**
```
%AppData%\FFXIVSimpleLauncher\Dalamud\
├── Injector\     # Dalamud files
├── Runtime\      # .NET 9.0 Runtime
├── Assets\       # Dalamud assets
└── Config\       # Plugin configurations
```

### Manual Mode

If you prefer to use your own Dalamud build:

1. Build Dalamud from [yanmucorp/Dalamud](https://github.com/yanmucorp/Dalamud) or download from releases
2. Open **Settings**
3. Enable **Dalamud**
4. Select **Manual Path**
5. Browse to your Dalamud folder (containing `Dalamud.Injector.exe`)

## Plugin Repository

Since yanmucorp Dalamud uses API12, we provide a custom plugin repository with compatible versions:

**Repository URL:**
```
https://raw.githubusercontent.com/cycleapple/DalamudPlugins-TW/main/repo.json
```

**Available Plugins:**

| Plugin | Version | Description |
|--------|---------|-------------|
| Penumbra | 1.2.0.0 | Mod loader and manager |
| Simple Tweaks | 1.10.10.0 | Quality of life improvements |
| Brio | 0.5.1.2 | Enhanced GPose tools |
| Glamourer | 1.4.0.1 | Appearance modification |
| CustomizePlus | 2.0.7.22 | Character customization |

**How to add:**
1. Open Dalamud Settings in-game (`/xlsettings`)
2. Go to "Experimental" tab
3. Add the repository URL to "Custom Plugin Repositories"
4. Save and browse the plugin installer

For more plugins, visit: [DalamudPlugins-TW](https://github.com/cycleapple/DalamudPlugins-TW)

## Building from Source

```bash
# Clone the repository
git clone https://github.com/cycleapple/XIVTCLauncher.git
cd XIVTCLauncher

# Build the project
dotnet build

# Run the application
dotnet run

# Publish for release
dotnet publish -c Release -r win-x64 --self-contained false
```

## Configuration

Settings are stored in `%APPDATA%/FFXIVSimpleLauncher/settings.json`

### Game Path

Default installation path for FFXIV Taiwan version:
```
C:\Program Files\USERJOY GAMES\FINAL FANTASY XIV TC
```

## Disclaimer

XIVTCLauncher is not in-line with the game's Terms of Service. We are doing our best to make it safe to use for everyone, and to our knowledge, no one has gotten into trouble for using XIVTCLauncher, but please be aware that it is a possibility.

**Use at your own risk.**

### Plugin Guidelines

If you use Dalamud plugins, please follow these guidelines:

- Do not use plugins that automate gameplay in ways that provide unfair advantages
- Do not use plugins that interact with the game servers in unauthorized ways
- Do not use plugins that circumvent any game restrictions or paywalls

## Acknowledgments

- [goatcorp/FFXIVQuickLauncher](https://github.com/goatcorp/FFXIVQuickLauncher) - Original inspiration and Dalamud framework
- [goatcorp/Dalamud](https://github.com/goatcorp/Dalamud) - Plugin framework
- [yanmucorp/Dalamud](https://github.com/yanmucorp/Dalamud) - Chinese client Dalamud fork
- [ottercorp/FFXIVQuickLauncher](https://github.com/ottercorp/FFXIVQuickLauncher) - Chinese client launcher and asset server
- [cycleapple/DalamudPlugins-TW](https://github.com/cycleapple/DalamudPlugins-TW) - Plugin repository for Taiwan client
- [MaterialDesignInXAML](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) - UI toolkit

## Legal

XIVTCLauncher is not affiliated with or endorsed by SQUARE ENIX CO., LTD. or Gameflier International Corp.

FINAL FANTASY is a registered trademark of Square Enix Holdings Co., Ltd.

All game assets and trademarks are the property of their respective owners.

## License

This project is provided as-is for educational and personal use.
