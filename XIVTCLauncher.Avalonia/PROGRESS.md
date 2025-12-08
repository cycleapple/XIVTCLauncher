# Avalonia UI Migration Progress

Last Updated: 2024-12-08

## Overview

This document tracks the progress of migrating XIVTCLauncher from WPF to Avalonia UI for cross-platform support.

## Completed ✅

### Project Setup
- [x] Create Avalonia project structure
- [x] Configure shared code linking (Models, Services, Platform layer)
- [x] Setup project dependencies
- [x] Configure multi-platform build (win-x64, osx-arm64, osx-x64)

### MainWindow
- [x] MainViewModel (WPF dependencies removed)
- [x] MainWindow.axaml (UI layout)
- [x] MainWindow.axaml.cs (dialog callbacks)
- [x] Integration with SettingsWindow

### SettingsWindow
- [x] SettingsViewModel (fully MVVM)
- [x] SettingsWindow.axaml (complete UI)
- [x] SettingsWindow.axaml.cs (folder pickers, dialogs)
- [x] OTP configuration UI
- [x] Dalamud configuration UI
- [x] Game path selection
- [x] Validation logic

## In Progress 🔨

### WebLoginWindow
- [ ] Need WebView alternative (WebView2 is Windows-only)
- [ ] Options:
  - Avalonia.WebView
  - CefGlue (Chromium Embedded Framework)
  - System browser launch with callback
- [ ] UI layout conversion
- [ ] Login flow integration

## Pending ⏳

### OtpDialog
- [ ] Simple dialog for manual OTP entry
- [ ] Low priority (auto OTP is preferred)

### Testing
- [ ] Windows platform testing
- [ ] macOS platform testing
- [ ] Linux platform testing (optional)

### Polish
- [ ] Custom themes/styling
- [ ] Animations and transitions
- [ ] Error handling improvements
- [ ] Loading indicators

## Technical Decisions

### Dialog Pattern
Using callback-based dialogs instead of direct window instantiation:
```csharp
// Instead of:
var dialog = new MessageBox(...);
dialog.ShowDialog();

// We use:
ShowConfirmDialog?.Invoke(message, title, result => {
    // Handle result
});
```

This allows the ViewModel to remain platform-agnostic.

### Folder Selection
Using `StorageProvider` API for cross-platform folder/file selection:
```csharp
var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(...);
```

### WebView Challenge
WebView2 is Windows-only. For cross-platform support, we need to either:
1. Use Avalonia.WebView (if available and mature)
2. Embed CefGlue/CefSharp
3. Launch system browser and handle callback
4. Create custom HTML/JS login UI

## File Structure

```
XIVTCLauncher.Avalonia/
├── Program.cs              ✅ Entry point
├── App.axaml              ✅ Application config
├── App.axaml.cs           ✅ Initialization
├── ViewModels/
│   ├── MainViewModel.cs    ✅ Main window logic
│   └── SettingsViewModel.cs ✅ Settings logic
├── Views/
│   ├── MainWindow.axaml     ✅ Main UI
│   ├── MainWindow.axaml.cs  ✅ Main callbacks
│   ├── SettingsWindow.axaml  ✅ Settings UI
│   └── SettingsWindow.axaml.cs ✅ Settings callbacks
└── [Shared from WPF project]
    ├── Models/             ✅ Linked
    ├── Services/           ✅ Linked
    └── Dalamud/           ✅ Linked
```

## Dependencies

### Current
- Avalonia 11.1.3
- Avalonia.Desktop
- Avalonia.Themes.Fluent
- MessageBox.Avalonia 3.1.6
- CommunityToolkit.Mvvm 8.4.0
- (Shared with WPF project)

### Needed for WebLoginWindow
- TBD: WebView solution

## Known Issues

1. **WebLoginWindow not implemented** - Blocking full functionality
2. **No custom styling yet** - Using default Fluent theme
3. **OtpDialog not created** - Manual OTP entry not available
4. **Untested** - No testing on any platform yet

## Next Steps

### Priority 1: WebLoginWindow
Must implement to enable login functionality.

### Priority 2: Testing
Test on Windows first, then macOS.

### Priority 3: Polish
Improve UI/UX, add custom styling.

## Metrics

### Code Statistics
- Lines of code (Avalonia-specific): ~1,500
- ViewModels: 2
- Views: 2
- Commits: 5

### Coverage
- UI Components: 40% (2/5 windows)
- ViewModels: 40% (2/5)
- Integration: 60%

## Timeline

- 2024-12-08: Project created
- 2024-12-08: MainWindow completed
- 2024-12-08: SettingsWindow completed
- TBD: WebLoginWindow
- TBD: Testing phase
- TBD: Release

## Notes

- Shared code with WPF project means backend is fully functional
- Only UI layer needs migration
- Platform abstraction layer already handles Windows/macOS differences
- Wine integration for macOS already implemented
