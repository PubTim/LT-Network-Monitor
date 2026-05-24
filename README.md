# NetworkTrayApp
Simple EXE to read your network speed and total usage. If you think its malicious read build the app yourself XD.

## Features

- Live download and upload speeds
- Current network name
- Per-network totals that resume when you reconnect to the same network
- Program uptime per network session
- Dark and light mode

## Requirements

- Just run the exe or build it youself
- .NET 10 SDK for local builds
- A Windows machine with network access

## How to build

```powershell
dotnet build .\NetworkTrayApp.csproj
```

## How to publish

```powershell
dotnet publish .\NetworkTrayApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None
```

The published app will be created under `bin\Release\net10.0-windows\win-x64\publish\`.
