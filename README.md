# JitenMPC-BE Avalonia 0.4.0-preview.2

## Build and run

This source package does not contain a precompiled executable. On Windows, double-click:

**`Build-and-Run.cmd`**

The script will:

1. use a suitable installed .NET 10 SDK if present;
2. otherwise reuse a local SDK from another JitenMPC-BE Avalonia preview when possible;
3. otherwise install .NET SDK 10.0.302 locally under `.dotnet` without admin rights or a system PATH change;
4. restore Avalonia NuGet packages;
5. publish a self-contained Windows x64 single-file build to `publish`;
6. launch `publish\JitenMPC-BE.exe`.

`Build-Release.cmd` performs the same publish without launching the application.

Settings and logs are stored under:

`%LOCALAPPDATA%\JitenMPC-BE`

## Project layout

- `src/JitenMPC-BE.App/Views/` — settings, subtitle overlay, popup and mining dialogs
- `src/JitenMPC-BE.App/Controls/OutlinedTokenControl.cs` — vector subtitle/token renderer
- `src/JitenMPC-BE.App/Services/AppRuntime.cs` — application orchestration and mining workflow
- `src/JitenMPC-BE.App/Services/MiningMediaService.cs` — ffmpeg screenshot/clip/audio capture
- `src/JitenMPC-BE.App/Services/MpcBeController.cs` — MPC-BE `/slave` controller
- `src/JitenMPC-BE.App/Services/SubtitleTrackService.cs` — ffprobe discovery + ffmpeg extraction
- `src/JitenMPC-BE.App/Services/JitenApiClient.cs` — parsing, state, review, deck and media APIs
- `src/JitenMPC-BE.App/Services/KeybindService.cs` — global reader/keybind handling
- `src/JitenMPC-BE.App/Native/` — Win32 controller/geometry/input helpers

## Third-party projects

See `THIRD-PARTY-NOTICES.md` and `LICENSE-JitenMPV.txt`.

JitenMPC-BE is an unofficial companion project and is not an official JitenMPV, Jiten, MPC-BE, or Avalonia release.
