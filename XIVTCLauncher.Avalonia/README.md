# XIVTCLauncher.Avalonia

Cross-platform version of XIVTCLauncher using Avalonia UI.

## Status

🚧 **Work in Progress** - This is the cross-platform port of the original WPF launcher.

### Completed ✅

- [x] Project structure setup
- [x] Shared code linking (Models, Services, Platform layer)
- [x] MainViewModel ported (WPF dependencies removed)
- [x] MainWindow UI converted to Avalonia
- [x] Platform abstraction layer integrated

### In Progress 🔨

- [ ] SettingsWindow conversion
- [ ] WebLoginWindow conversion (needs WebView alternative)
- [ ] OtpDialog conversion
- [ ] Testing on Windows
- [ ] Testing on macOS

### Not Started ⏳

- [ ] Custom themes/styling
- [ ] macOS-specific optimizations
- [ ] Packaging and distribution

## Architecture

This project shares the business logic from the original WPF project:

```
XIVTCLauncher.Avalonia/
├── Views/              # Avalonia AXAML views (new)
├── ViewModels/         # Ported ViewModels (WPF-independent)
├── Models/             # Linked from WPF project
├── Services/           # Linked from WPF project
├── Dalamud/            # Linked from WPF project
└── Platform/           # Cross-platform abstraction layer
```

## Building

### Prerequisites

- .NET 8.0 SDK
- Windows, macOS, or Linux

### Build Commands

```bash
# Restore dependencies
dotnet restore

# Build
dotnet build

# Run
dotnet run

# Publish for Windows
dotnet publish -c Release -r win-x64 --self-contained

# Publish for macOS (Apple Silicon)
dotnet publish -c Release -r osx-arm64 --self-contained

# Publish for macOS (Intel)
dotnet publish -c Release -r osx-x64 --self-contained
```

## Dependencies

### Avalonia UI

- Avalonia 11.1.3 - Cross-platform UI framework
- Avalonia.Desktop - Desktop-specific features
- Avalonia.Themes.Fluent - Fluent design theme
- MessageBox.Avalonia - Cross-platform message boxes

### Shared Dependencies

- CommunityToolkit.Mvvm - MVVM helpers
- Newtonsoft.Json - JSON serialization
- SevenZipExtractor - Archive extraction

## Key Differences from WPF Version

### ViewModel Changes

- Removed `System.Windows` dependencies
- Replaced `MessageBox` with callback-based dialogs
- Replaced `Application.Current.Shutdown()` with `RequestClose` callback
- Dialog interactions now use delegates instead of direct window creation

### UI Changes

- MaterialDesign → Fluent Theme
- `.xaml` → `.axaml` file extensions
- WPF-specific controls replaced with Avalonia equivalents
- Different styling approach

### Platform Support

- **Windows**: Fully supported (same as WPF version)
- **macOS**: Supported via Wine integration (requires Wine installation)
- **Linux**: Theoretically supported (not yet tested)

## macOS Requirements

For macOS users:

1. **.NET 8.0 Runtime**
   ```bash
   brew install dotnet
   ```

2. **Wine (for running the game)**
   ```bash
   brew tap gcenx/wine
   brew install --cask --no-quarantine wine-crossover
   ```

3. **Game Installation**
   - Install game in Wine prefix
   - Configure game path in launcher settings

## Current Limitations

### Not Yet Implemented

1. **WebLoginWindow**
   - Original uses WebView2 (Windows only)
   - Need cross-platform WebView alternative
   - Considering: Avalonia.WebView or embedded browser

2. **SettingsWindow**
   - Needs conversion from WPF to Avalonia
   - Should be straightforward

3. **OtpDialog**
   - Needs conversion from WPF to Avalonia
   - Simple dialog, low priority

### Known Issues

- Message boxes are basic (no custom styling yet)
- Some UI elements may look different from WPF version
- macOS Wine integration needs testing

## Development Roadmap

### Phase 1: Core UI (Current)
- [x] Project setup
- [x] MainWindow
- [ ] SettingsWindow
- [ ] WebLoginWindow alternative
- [ ] Basic testing

### Phase 2: Polish
- [ ] Match WPF UI styling
- [ ] Improve user experience
- [ ] Add custom themes
- [ ] Comprehensive testing

### Phase 3: Platform-Specific
- [ ] macOS optimizations
- [ ] Windows installer
- [ ] macOS .app bundle
- [ ] Distribution setup

## Testing

### Windows Testing

```bash
dotnet run
# Should work similar to WPF version
```

### macOS Testing

```bash
# Ensure Wine is installed
wine64 --version

# Run launcher
dotnet run

# Check platform detection
# Should detect macOS and use Wine service
```

## Contributing

When working on this project:

1. **Don't modify shared code** - Models, Services, and Platform layer are shared with WPF project
2. **Keep UI platform-agnostic** - Use Avalonia controls, not platform-specific APIs
3. **Test on multiple platforms** - Ensure changes work on both Windows and macOS
4. **Follow Avalonia patterns** - Use MVVM, data binding, and reactive UI principles

## Related Documentation

- `../XIVTCLauncher/MACOS_SUPPORT.md` - Platform abstraction architecture
- `../XIVTCLauncher/NEXT_STEPS.md` - Migration guide
- `../XIVTCLauncher/PLATFORM_TEST.md` - Testing procedures

## License

Same as parent project.

## Acknowledgments

- Original WPF launcher: XIVTCLauncher
- Avalonia UI: https://avaloniaui.net/
- Wine integration inspired by XIV-on-Mac
