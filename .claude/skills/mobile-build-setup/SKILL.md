---
name: mobile-build-setup
description: Android/iOS build requirements for ArctZ (JDK/SDK setup, .NET workloads, iOS signing limitation on Windows). Use when building, running, or troubleshooting the ArctZ.Android or ArctZ.iOS project heads.
---

Android/iOS build requirements (already set up on this machine):
- `android` and `ios` .NET workloads installed (`dotnet workload install android ios`).
- Android build additionally needs a JDK and the Android SDK (platform-tools, `platforms;android-36`, `build-tools;36.0.0`) — installed at `%LOCALAPPDATA%\Android\Jdk` and `%LOCALAPPDATA%\Android\Sdk` (the default locations the tooling auto-detects). `JAVA_HOME` is set as a user environment variable pointing at the JDK.
- On Windows, `ArctZ.iOS` builds (targets `iossimulator-x64`) but cannot produce a device-signed app without a paired Mac/Xcode — that limitation is inherent to iOS tooling, not fixable from Windows.
