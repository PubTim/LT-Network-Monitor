# NetworkTrayApp

NetworkTrayApp is a small Windows desktop monitor that shows live total download/upload throughput, per-network history, and a simple dark/light theme toggle.

## Features

- Live download and upload speeds
- Current network name
- Per-network totals that resume when you reconnect to the same network
- Program uptime per network session
- Resizable Windows desktop window
- Tray icon with exit menu
- Dark and light modes

## Requirements

- Windows
- .NET 10 SDK for local builds
- A Windows machine with network access

## Build

```powershell
dotnet build .\NetworkTrayApp.csproj
```

## Publish a single-file executable

```powershell
dotnet publish .\NetworkTrayApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None
```

The published app will be created under `bin\Release\net10.0-windows\win-x64\publish\`.

## Versioning and releases

This project uses semantic versioning:

- `1.0.0` for the first stable public release
- `1.0.1`, `1.0.2`, etc. for fixes
- `1.1.0`, `1.2.0`, etc. for small feature additions
- `2.0.0` for breaking changes

Recommended release flow:

1. Update `VersionPrefix` in `NetworkTrayApp.csproj`.
2. Commit the change.
3. Tag the commit as `v1.0.0` or similar.
4. Push the tag to GitHub.
5. Let the release workflow build the Windows executable and attach it to the GitHub Release.

## Good release practice

- Keep build outputs out of the repository.
- Attach the published `.zip` or `.exe` to each GitHub Release.
- Write short release notes that describe what changed.
- Use tags that match the version, like `v1.0.0`.
- Bump the version only when you are ready to publish a new release.
