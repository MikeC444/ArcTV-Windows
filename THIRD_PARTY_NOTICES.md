# Third-party software

- **mpv / libmpv** (https://mpv.io), the video player's engine, used through `libmpv-2.dll`, loaded as a separate file next to `ArcTV.exe` (not compiled into the app), so it can be replaced by another build of the same library.
  `scripts/get-libmpv.ps1` fetches a build from https://github.com/shinchiro/mpv-winbuild-cmake. **Licence to check before giving the app to other people:** mpv itself can be built as LGPL, but that project's recent
  releases (as of 2026-10-13) list no LGPL-only library, only the standard `mpv-dev-x86_64` one, which I believe includes GPL parts. I have not confirmed its licence. If it is GPL, shipping it has obligations (for example offering its source);
  the alternative is an LGPL build of libmpv from another source.
- **QRCoder** (MIT), for the sign-in QR code.
