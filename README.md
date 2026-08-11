# JitenMPC-BE

JitenMPC-BE brings JitenMPV-style Japanese subtitle parsing, dictionary popups, reviews, and mining to MPC-BE while leaving MPC-BE responsible for normal video and audio playback.

This is an unofficial project based on JitenMPV. 

## Requirements

- Windows 10 or 11, 64-bit
- [MPC-BE](https://github.com/Aleksoid1978/MPC-BE)
- A [Jiten](https://jiten.moe/) account and API key
- `ffmpeg` and `ffprobe` for embedded subtitle extraction and media mining

## Install

1. Download `JitenMPC-BE-Setup-vX.X.X.exe` from the latest GitHub Release.
2. Run the installer. 
3. Open JitenMPC-BE and set the MPC-BE path if it was not detected automatically.
4. Enter your Jiten API key.
5. Make sure `ffmpeg` and `ffprobe` are detected, or set their paths manually.
6. Use **Open MPC-BE** from JitenMPC-BE, then open your video normally in MPC-BE.

JitenMPC-BE must launch MPC-BE so it can establish MPC-BE's `/slave` connection.

Settings and logs are stored in `%LOCALAPPDATA%\JitenMPC-BE`.

## Updating

JitenMPC-BE checks this repository for new releases once per day when update checking is enabled. You can also use **Check now** under General → Updates. When an installer is available, **Install update** downloads it, upgrades the existing installation, and relaunches JitenMPC-BE.

## Building from source

Run `Build-and-Run.cmd` to build and launch, or `Build-Release.cmd` to create the self-contained Windows build. `installer/JitenMPC-BE.iss` builds the normal Windows installer with Inno Setup.

## License and attribution

See `THIRD-PARTY-NOTICES.md` and `LICENSE-JitenMPV.txt`.
