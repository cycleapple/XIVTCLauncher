# XIVTCLauncher (FFXIVSimpleLauncher)

[English](README.md) | [繁體中文](README_zh-TW.md)

A faster launcher for Final Fantasy XIV Taiwan version, inspired by [FFXIVQuickLauncher](https://github.com/goatcorp/FFXIVQuickLauncher).

## Features

- **Fast Login** - Streamlined login process with saved credentials
- **OTP Support** - One-Time Password authentication support
- **Web Login** - Integrated WebView2 browser for web-based authentication
- **Dalamud Integration** - Optional plugin framework support for enhanced gameplay
- **Modern UI** - Clean Material Design interface
- **Settings Management** - Customizable game path and launch options

## Requirements

- Windows 10/11
- [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
- Final Fantasy XIV Taiwan version installed

## Installation

1. Download the latest release
2. Extract to a folder of your choice
3. Run `FFXIVSimpleLauncher.exe`
4. Configure your game path in Settings

## Building from Source

```bash
# Clone the repository
git clone https://github.com/your-repo/XIVTCLauncher.git
cd XIVTCLauncher

# Build the project
dotnet build

# Run the application
dotnet run
```

## Configuration

Settings are stored in `%APPDATA%/FFXIVSimpleLauncher/settings.json`

### Game Path

Default installation path for FFXIV Taiwan version:

```
C:\Program Files\USERJOY GAMES\FINAL FANTASY XIV TC
```

### Dalamud Plugin Support

The launcher supports [Dalamud](https://github.com/goatcorp/Dalamud) plugin injection with Taiwan client compatibility.

#### Taiwan Client Forks

Since the Taiwan client has different memory signatures from the global client, we maintain forked versions:

- **Dalamud TW**: [cycleapple/Dalamud (tw-client branch)](https://github.com/cycleapple/Dalamud/tree/tw-client)
- **FFXIVClientStructs TW**: [cycleapple/FFXIVClientStructs (tw-client branch)](https://github.com/cycleapple/FFXIVClientStructs/tree/tw-client)

#### Dependency Structure

```
cycleapple/Dalamud (tw-client)
    │
    ├── Taiwan client language support
    ├── Updated DalamudStartInfo, DataManager, NetworkHandlers
    │
    └── lib/FFXIVClientStructs (submodule)
            │
            └── cycleapple/FFXIVClientStructs (tw-client)
                    │
                    └── Taiwan client signature fixes:
                        - GameMain, LayoutWorld, PacketDispatcher
                        - RaptureLogModule, UIModule, AtkUnitBase
                        - AddonContextMenu, AgentActionDetail
                        - ShellCommands, etc.
```

#### Building Dalamud TW from Source

```bash
# Clone with submodules
git clone --recursive -b tw-client https://github.com/cycleapple/Dalamud.git

# Or if already cloned, initialize submodules
git submodule update --init --recursive

# Build
dotnet build
```

#### Usage

1. Build Dalamud TW from source (see above) or download a pre-built release
2. In Settings, set the **Local Dalamud Path** to your Dalamud build directory (e.g., `E:\FFXIV\XIVLauncher\Dalamud-TW\bin\Release`)
3. Enable Dalamud in Settings
4. Configure injection delay if needed (default works for most users)
5. Launch the game - Assets will be automatically downloaded from goatcorp, then Dalamud will be injected

> **Note**: Only Dalamud assets are downloaded automatically. The Dalamud TW build must be provided locally.

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
- [goatcorp/FFXIVClientStructs](https://github.com/goatcorp/FFXIVClientStructs) - Game client structures
- [MaterialDesignInXAML](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) - UI toolkit

## Legal

XIVTCLauncher is not affiliated with or endorsed by SQUARE ENIX CO., LTD. or Gameflier International Corp.

FINAL FANTASY is a registered trademark of Square Enix Holdings Co., Ltd.

All game assets and trademarks are the property of their respective owners.

## License

This project is provided as-is for educational and personal use.
