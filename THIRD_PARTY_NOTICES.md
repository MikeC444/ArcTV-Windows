# Third-party software

- **mpv / libmpv** (https://mpv.io), the video player's engine, used through `libmpv-2.dll`. Arc TV uses an **LGPL** build and loads it as a separate file next to `ArcTV.exe` (it is not
  compiled into the app), so it can be replaced by another build of the same library. The build is fetched by `scripts/get-libmpv.ps1` from https://github.com/shinchiro/mpv-winbuild-cmake. mpv's licence
  (LGPL 2.1 or later when built this way) and source are at the links above.
- **QRCoder** (MIT), for the sign-in QR code.
