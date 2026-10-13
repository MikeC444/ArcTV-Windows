# Working notes for Claude Code

- This is the native **Windows** app for Arc TV (C# on .NET 8, WPF, an unsigned installer and zip). It follows the Fire TV app (`MikeC444/ArcTV-AndroidTV`,
  which also holds the backend in `server/`) for what it does, the Mac app (`MikeC444/ArcTV-Mac`, Swift) for how a native desktop port is organised, and the
  web app (`MikeC444/ArcTV-Web`, see `docs/PARITY.md` there) for how it looks. A change made on web is recorded in the web repo's `docs/PARITY.md`; say
  what is still open for the Windows app.
- `src/ArcTV.Core` is everything that is not on screen (addon protocol, account API, Home rows, recommendations): it is ported from the Mac app's
  `ArcTVCore` (Swift) and its tests from `ArcTVCoreTests`, so behaviour matches. `src/ArcTV.App` is the WPF app. `tests/ArcTV.Core.Tests` is xUnit.
- Add each user-visible change as a bullet under `## Unreleased` in `RELEASE_NOTES.md` (plain, short, upbeat, no file names: it is what the update
  pop-up shows). Log engineering work in `CHANGELOG.md` as `## <N> -- <title>` with Status, Context, Changes, Tests performed, Issues discovered,
  Issues fixed. Update an existing unreleased bullet/entry in place when refining the same feature. Cutting a release is a separate, explicit request.
- This sandbox is Linux, but Microsoft's .NET 8 SDK (installed with `dotnet-install.sh --channel 8.0 --install-dir /opt/dotnet-ms`; the Ubuntu package lacks
  the WPF parts) builds the whole solution, WPF included, and runs the xUnit tests here (`dotnet build ArcTV.sln`, `dotnet test ArcTV.sln`). Nothing can be
  *run* here: the app is never seen on screen from this sandbox. The **Build and test (Arc TV Windows)** workflow runs on a Windows machine. Say plainly
  what was built, tested and run, and never imply a screen was seen when it was not.

## Where the work stands (for a new session)

- Done and on `main`, CI green: foundations, sign-in (log in, sign up, phone QR, encrypted saved session), Home + floating top bar (banner, addon rows, `ArcTV.App.Tests` opens the real screens on the Windows build server).
- Next, in order: Movies / TV Shows (browse by genre), Search, My List, title details + sources; the player (libmpv); Continue Watching, settings, addons, profiles,
  Plus, recommendations; torrents (MonoTorrent) and downloads; installer, updater, first release. Port each from the Mac app's `Sources/ArcTV*` (clone `MikeC444/ArcTV-Mac`).
- Not done on Home yet: hide-bar-on-scroll, Continue Watching, Picked for you, banner Play/Trailer/My List/Info buttons, saved-rows cache.
- The owner tests with Visual Studio on their PC (pull, F5); they report what they see. Always build onto `main`. Commit trailers per the session's attribution reminder.
