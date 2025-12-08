# macOS Support Implementation Plan

## Overview

This document outlines the plan to add macOS support to XIVTCLauncher, inspired by [XIV-on-Mac-in-TC](https://github.com/PlusoneChiang/XIV-on-Mac-in-TC/tree/tc_region).

## Architecture Decision: Avalonia UI

**Chosen Framework**: Avalonia UI (instead of .NET MAUI)

**Rationale**:
- Very similar to WPF syntax (minimal migration effort)
- Mature cross-platform support (Windows, macOS, Linux)
- Stays within C# and .NET ecosystem
- Active community and good documentation
- Better desktop application support than MAUI

## Implementation Phases

### Phase 1: Project Setup & Migration to Avalonia UI

**Tasks**:
1. Create new Avalonia project structure
2. Migrate XAML views from WPF to Avalonia
   - MainWindow.xaml → MainWindow.axaml
   - SettingsWindow.xaml → SettingsWindow.axaml
   - OtpDialog.xaml → OtpDialog.axaml
   - WebLoginWindow.xaml → WebLoginWindow.axaml (replace WebView2 with Avalonia WebView)
3. Adapt ViewModels (minimal changes needed)
4. Replace MaterialDesignThemes with Avalonia.Themes.Fluent or Material.Avalonia
5. Update project file to target multiple platforms

**Dependencies to Replace**:
- `MaterialDesignThemes` → `Material.Avalonia` or `Avalonia.Themes.Fluent`
- `Microsoft.Web.WebView2` → `Avalonia.WebView` or `WebViewControl`

### Phase 2: Platform Abstraction Layer

Create interfaces and platform-specific implementations for:

#### 2.1 Credential Storage
```csharp
public interface ICredentialService
{
    void SavePassword(string username, string password);
    string? GetPassword(string username);
    void DeletePassword(string username);
}

// Implementations:
// - WindowsCredentialService (existing, using advapi32.dll)
// - MacOSCredentialService (using Security.framework via P/Invoke or package)
```

**macOS Implementation Options**:
- Use `Security.framework` via P/Invoke
- Use NuGet package like `KeychainAccess` or implement wrapper

#### 2.2 Game Launcher
```csharp
public interface IGameLauncher
{
    Process LaunchGame(string gamePath, string sessionId);
    string GetGameExecutablePath(string gamePath);
}

// Implementations:
// - WindowsGameLauncher (direct execution of ffxiv_dx11.exe)
// - MacOSGameLauncher (launch through Wine)
```

**macOS Game Launcher**:
- Requires Wine installation (check for marzent/winecx or standard Wine)
- Launch command: `wine64 "{gamePath}/game/ffxiv_dx11.exe" {args}`
- Need to set Wine environment variables (WINEPREFIX, etc.)

#### 2.3 Platform Paths
```csharp
public interface IPlatformPaths
{
    string GetApplicationDataPath();
    string GetConfigPath();
    string GetDefaultGamePath();
}

// Implementations:
// - WindowsPlatformPaths (uses Environment.SpecialFolder.ApplicationData)
// - MacOSPlatformPaths (uses ~/Library/Application Support/)
```

**macOS Paths**:
- Application Data: `~/Library/Application Support/FFXIVSimpleLauncher/`
- Config: Same as above
- Default Game Path: Depends on Wine prefix (e.g., `~/.wine/drive_c/Program Files/...`)

### Phase 3: Wine Integration for macOS

**Requirements**:
1. Detect Wine installation (winecx preferred, fallback to standard Wine)
2. Set up Wine prefix if needed
3. Configure Wine environment for FFXIV
4. Launch game through Wine with correct arguments

**Wine Detection**:
```csharp
public class WineService
{
    public bool IsWineInstalled();
    public string? GetWinePath();
    public string GetWinePrefix();
    public void LaunchThroughWine(string exePath, string arguments);
}
```

**Wine Prefix Setup**:
- Default location: `~/Library/Application Support/FFXIVSimpleLauncher/wineprefix/`
- Or use existing XIV-on-Mac wine prefix if detected
- Need to set WINEPREFIX environment variable

### Phase 4: Dalamud Integration for macOS

**Challenges**:
- Dalamud.Injector.exe needs to run through Wine
- .NET Runtime detection needs to work with Wine
- Injection timing may differ on macOS

**Approach**:
1. Run Dalamud.Injector.exe through Wine
2. Ensure .NET Runtime is accessible within Wine prefix
3. May need to copy/symlink runtime files into Wine prefix
4. Test injection timing and adjust delays if needed

**Modified DalamudService**:
- Detect platform in `LaunchGameWithDalamud()`
- On macOS: launch both game and injector through Wine
- Adjust paths to Windows-style paths for Wine (C:\...)

### Phase 5: Platform-Specific Features

#### macOS-Specific:
- Wine configuration UI in Settings
- Wine prefix management
- DXVK/MoltenVK version display
- Performance optimization settings (from XIV-on-Mac)

#### Windows-Specific (keep existing):
- Direct game launch
- Windows Credential Manager
- WebView2 for web login

### Phase 6: Testing & Validation

**Test Cases**:
1. UI rendering on macOS
2. Credential storage/retrieval on macOS
3. Game launch through Wine
4. Login flow (API calls work cross-platform)
5. Dalamud injection on macOS
6. OTP generation (cross-platform)
7. Settings persistence on macOS

## Project Structure

```
XIVTCLauncher/
├── XIVTCLauncher.csproj (updated for multi-platform)
├── Views/ (migrated to .axaml)
├── ViewModels/ (minimal changes)
├── Services/
│   ├── Platform/ (new)
│   │   ├── ICredentialService.cs
│   │   ├── IGameLauncher.cs
│   │   ├── IPlatformPaths.cs
│   │   ├── Windows/
│   │   │   ├── WindowsCredentialService.cs
│   │   │   ├── WindowsGameLauncher.cs
│   │   │   └── WindowsPlatformPaths.cs
│   │   └── MacOS/
│   │       ├── MacOSCredentialService.cs
│   │       ├── MacOSGameLauncher.cs
│   │       ├── MacOSPlatformPaths.cs
│   │       └── WineService.cs
│   ├── LoginService.cs (update to use IGameLauncher)
│   ├── DalamudService.cs (update for Wine support)
│   └── ... (other services)
└── ...
```

## Dependencies

**New NuGet Packages**:
- `Avalonia` (11.0+)
- `Avalonia.Desktop`
- `Avalonia.Themes.Fluent` or `Material.Avalonia`
- `Avalonia.ReactiveUI`
- Platform-specific packages as needed

**Runtime Requirements**:
- **Windows**: .NET 8.0 Desktop Runtime
- **macOS**: .NET 8.0 Runtime + Wine (winecx or standard)

## Wine Installation Guide (for users)

Users will need to install Wine on macOS:

**Option 1: Homebrew (easiest)**:
```bash
brew tap gcenx/wine
brew install --cask --no-quarantine wine-crossover
```

**Option 2: Manual Install**:
- Download winecx from https://github.com/marzent/winecx/releases
- Install to /Applications or ~/Applications

**Wine Prefix Setup**:
- Launcher will automatically create Wine prefix on first run
- Or detect existing XIV-on-Mac installation

## Configuration

**New Settings for macOS**:
```json
{
  "Platform": "macOS",
  "WinePath": "/usr/local/bin/wine64",
  "WinePrefix": "~/Library/Application Support/FFXIVSimpleLauncher/wineprefix",
  "GamePath": "C:\\Program Files\\USERJOY GAMES\\FINAL FANTASY XIV TC",
  // ... existing settings
}
```

## Migration Path

1. **Incremental Migration**: Keep Windows version working while adding macOS support
2. **Conditional Compilation**: Use `#if OSX` where needed
3. **Dependency Injection**: Use DI container to inject platform-specific services
4. **Testing**: Test on both platforms before each release

## Notes

- The login API calls are platform-agnostic (HTTP requests work everywhere)
- OTP generation is cross-platform (no changes needed)
- Settings JSON format remains compatible
- Dalamud downloads work cross-platform (just HTTP downloads)
- Main challenges are UI framework, credential storage, and Wine integration

## References

- [XIV-on-Mac-in-TC](https://github.com/PlusoneChiang/XIV-on-Mac-in-TC/tree/tc_region) - macOS launcher reference
- [Avalonia UI Documentation](https://docs.avaloniaui.net/)
- [marzent/winecx](https://github.com/marzent/winecx) - Wine Crossover fork for macOS
- [Avalonia XAML Migration Guide](https://docs.avaloniaui.net/docs/guides/migration-from-wpf)
