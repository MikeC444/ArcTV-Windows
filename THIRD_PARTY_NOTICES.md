# Third-party software

- **mpv / libmpv** (https://mpv.io), the video player's engine, used through `libmpv-2.dll`, loaded as a separate file next to `ArcTV.exe` (not compiled into the app), so it can be replaced by another build of the same library.
  Arc TV uses the **LGPL** build (mpv built with `-Dgpl=false`, so none of its GPL-only parts are in it; the library lists no `gpl` feature), published by https://github.com/zhongfly/mpv-winbuild ("mpv-dev-lgpl-x86_64"; the
  recipe and source it was built from are in that project and at https://github.com/mpv-player/mpv). `scripts/get-libmpv.ps1` fetches it, and the release workflow refuses to ship anything else (`-RequireLgpl`). Under the
  LGPL anyone may replace `libmpv-2.dll` with their own build of the same library, which works here because it is a separate file. Checked on 2026-10-14 (the build's own configuration line and feature list); the licences of the
  libraries bundled inside it (FFmpeg, libplacebo, libass and others, built LGPL or more permissive) were not checked one by one: do that before a wide release.
- **QRCoder** (MIT), for the sign-in QR code.
- **MonoTorrent** (MIT, https://github.com/alanmcgovern/monotorrent), the torrent engine: it finds the people sharing a torrent, downloads its video in the order it is watched and serves it to the player over a local address.

