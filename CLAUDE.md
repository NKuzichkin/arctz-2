# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

See also `AI_AGENT_README.md` for a Russian-language overview of the tech stack and folder layout.

## Commands

Build the full solution:
```
dotnet build ArctZ.slnx
```

Build/run a single platform head:
```
dotnet build ArctZ.Desktop/ArctZ.Desktop.csproj
dotnet run --project ArctZ.Desktop/ArctZ.Desktop.csproj

dotnet build ArctZ.Browser/ArctZ.Browser.csproj
dotnet run --project ArctZ.Browser/ArctZ.Browser.csproj
```

Android/iOS build requirements (already set up on this machine):
- `android` and `ios` .NET workloads installed (`dotnet workload install android ios`).
- Android build additionally needs a JDK and the Android SDK (platform-tools, `platforms;android-36`, `build-tools;36.0.0`) — installed at `%LOCALAPPDATA%\Android\Jdk` and `%LOCALAPPDATA%\Android\Sdk` (the default locations the tooling auto-detects). `JAVA_HOME` is set as a user environment variable pointing at the JDK.
- On Windows, `ArctZ.iOS` builds (targets `iossimulator-x64`) but cannot produce a device-signed app without a paired Mac/Xcode — that limitation is inherent to iOS tooling, not fixable from Windows.

There are no test projects in this solution yet.

## Architecture

Avalonia UI cross-platform solution (.NET 10, MVVM via `CommunityToolkit.Mvvm`, compiled bindings). One shared core project plus four thin platform heads, all referencing the core:

- `ArctZ/` — core project: all Views, ViewModels, Components, Themes, Assets. This is where nearly all application logic lives.
- `ArctZ.Desktop/`, `ArctZ.Android/`, `ArctZ.iOS/`, `ArctZ.Browser/` — platform entry points only (bootstrap + platform manifest/config), no app logic.

Inside `ArctZ/`:
- `ViewModels/ViewModelBase.cs` — base class for all ViewModels, extends `ObservableObject` (MVVM Toolkit). New ViewModels should derive from this and use `[ObservableProperty]` / `[RelayCommand]` code-gen attributes rather than hand-written properties/commands.
- `Views/MainView.axaml` — the shared root view rendered on every platform (Desktop wraps it in `MainWindow.axaml`; mobile/browser heads host it directly).
- `Components/VirtualJoystick/` — custom `TemplatedControl` for touch-based joystick input (game/character control), styled via `Themes/VirtualJoystick.axaml`. `Components/VirtualJoystick/virtual-joystick.md` has the full design spec for this control (pointer handling, direction/force math, `Fixed`/`Semi`/`Dynamic` modes) — read it before modifying joystick behavior.
- Package versions are centrally managed in `Directory.Packages.props` (`ManagePackageVersionsCentrally`); add new package versions there, not in individual `.csproj` files. Keep all `Avalonia.*` package versions in sync.

Key conventions called out in `AI_AGENT_README.md`:
- Compiled bindings are enabled by default (`AvaloniaUseCompiledBindingsByDefault=true`), so XAML bindings require `x:DataType` for strict typing.
- Custom control styling lives in `Themes/*.axaml` (`ControlTheme`) and must be registered in `App.axaml` to take effect.
