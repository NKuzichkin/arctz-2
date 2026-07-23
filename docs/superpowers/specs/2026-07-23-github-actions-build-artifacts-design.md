# GitHub Actions CI: build artifacts (Windows, Android)

## Purpose

Give every push/PR a downloadable build of the app for Windows and Android, without requiring anyone to build locally. Confirms the solution compiles on both platforms on every change.

## Scope

- Windows (Desktop head) and Android head only. iOS is explicitly out of scope for now (no Apple developer account available; can be added later as a separate `macos-latest` job).
- Artifacts are plain GitHub Actions workflow artifacts (90-day retention), not GitHub Releases — no versioning/tagging involved.
- No code signing: Windows build is self-contained but unsigned; Android build is the debug-signed APK.

## Workflow

File: `.github/workflows/build.yml`

Triggers: `push` and `pull_request` targeting `master`.

### Job `build-windows` (`windows-latest`)
1. `actions/checkout@v4`
2. `actions/setup-dotnet@v4`, `dotnet-version: '10.0.x'`
3. `dotnet publish ArctZ.Desktop/ArctZ.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64`
4. `actions/upload-artifact@v4`, name `ArctZ-Desktop-win-x64`, path `publish/win-x64`

### Job `build-android` (`ubuntu-latest`)
GitHub's `ubuntu-latest` image ships with a preinstalled Android SDK and JDK, so — unlike the local Windows dev machine, where JDK/Android SDK had to be downloaded and installed manually — no extra SDK provisioning step is needed here beyond the .NET workload.

1. `actions/checkout@v4`
2. `actions/setup-dotnet@v4`, `dotnet-version: '10.0.x'`
3. `dotnet workload install android`
4. `dotnet build ArctZ.Android/ArctZ.Android.csproj -c Debug` (Debug builds are automatically signed with a debug keystore, producing a runnable `.apk`)
5. `actions/upload-artifact@v4`, name `ArctZ-Android-debug-apk`, path `ArctZ.Android/bin/Debug/net10.0-android/*Signed.apk`

The two jobs run in parallel and are independent — a failure in one does not block the other's artifact from being produced.

## Out of scope / explicitly deferred

- iOS build job (needs a `macos-latest` runner and, for anything beyond simulator, an Apple developer certificate).
- Release/tag-triggered builds and GitHub Releases publishing.
- Code signing for either platform.
- Browser (WASM) head build — not requested.
