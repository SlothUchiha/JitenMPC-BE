# Third-party notices

## JitenMPV

JitenMPC-BE is behaviorally and structurally derived in part from **JitenMPV** by Sirush. The Avalonia migration intentionally follows JitenMPV's application/settings concepts and reuses/adapts Jiten-facing behavior where appropriate while replacing the MPV backend with an MPC-BE companion backend.

JitenMPV is licensed under the Apache License, Version 2.0. A copy of the upstream license is included as `LICENSE-JitenMPV.txt`.

Upstream: https://github.com/Sirush/JitenMPV

JitenMPC-BE is not an official JitenMPV release.

## Jiten

The remote parsing/dictionary service is Jiten. No Jiten server code is bundled in this source package.

Upstream: https://github.com/Sirush/Jiten

## MPC-BE

MPC-BE is licensed under GNU GPL v3 or later. JitenMPC-BE does **not** bundle MPC-BE source code, binaries, DLLs, or its subtitle renderer. It communicates with an independently installed MPC-BE process through MPC-BE's `/slave` WM_COPYDATA controller API and standard Windows APIs.

Upstream: https://github.com/Aleksoid1978/MPC-BE

## Avalonia

The Avalonia framework is licensed under the MIT License. This project references Avalonia NuGet packages at build time.

Upstream: https://github.com/AvaloniaUI/Avalonia

## Inter

The `Avalonia.Fonts.Inter` package supplies the Inter UI font. Inter is licensed under the SIL Open Font License, Version 1.1.

Upstream: https://github.com/rsms/inter

## FFmpeg

JitenMPC-BE does not bundle FFmpeg in this preview. It detects and invokes the user's separately installed `ffmpeg.exe` and `ffprobe.exe` for subtitle probing/extraction.
