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

Everything below is on `main` and was green on CI when written (Oct 2026). CI is the only compiler and test runner; the app has never been seen on screen from the sandbox.

**Built:** foundations; sign-in (log in, sign up, phone QR, encrypted saved session); Home (banner with Play / Trailer / More info / My List, addon rows, Continue Watching, Picked for you, saved-rows cache, top bar that hides on scroll, right-click card menu); Movies / TV Shows; Search; My List; title details; sources list; the player (libmpv: controls, resize handling, 45 s start watchdog, `player.log` and `mpv.log` in `%LOCALAPPDATA%\ArcTV`); playback behaviour (last source, smart pick, next episode, episode ticks); profiles ("Who's watching", manage, PIN, kids; styled like the Mac); Settings (account, Plus panes, addons, home rows, blocked genres, player incl. torrent buffer and storage limit, subtitles, audio, updates); torrent playback; Downloads.

**Torrents** (`src/ArcTV.App/Player/TorrentStreamer.cs`, MonoTorrent 3.0.2): the player opens at once and gets the torrent ready behind its loading cover (`PlayingItem.Prepare`). Things learned the hard way, do not undo them without reading why:
- MonoTorrent stops its DHT, listeners and port mapping when its last torrent stops, so one idle made-up torrent is kept running (`KeepEngineRunningAsync`), and opening a source list warms it (`Warm`).
- The library's prebuffer (first and last piece) is off; the start of the file is read through the stream's own local address (a ranged HTTP request for as many bytes as the Torrent buffer setting says). Without that read nothing asks for the start and pieces arrive from all over the file.
- A torrent whose file list was kept from earlier is in state Hashing first; reading then fails ("Could not read the requested data"), so the streamer waits for the check.
- Leaving the player cancels the start and stops the torrent at once; the read that is waiting then fails with its own error, which is treated as leaving, not as a failure.
- `player.log` gets a line every 5 s while a torrent loads (state, DHT state and nodes, connections, speed). Ask the owner to paste it.
- MonoTorrent's docs and source: the nupkg (api.nuget.org flat container) has the XML docs; `git clone https://github.com/alanmcgovern/monotorrent` has the source (master is newer than 3.0.2: it has uTP; 3.0.2 does not). Check a name against them before using it: several were wrong from memory.

**Downloads** (Arc TV Plus, as on the Mac): `src/ArcTV.Core/Downloads` (choices, file names, `DownloadManager` writing `name.part` then renaming) and `Views/DownloadPanelView.cs` (fixed size, drawn up to 1.3x, "Save to" folder picker kept in `LocalSettings`, files scroll inside; its scrolling is marked handled so the top bar does not react). Only direct web links (a debrid service) can be saved; torrents cannot.

**Open, in the order I would do them:**
1. Loading placeholders (grey shapes while Home, rows and title pages load).
2. Installer, updater and first release: only when the owner explicitly asks for a release.
3. Downloads extras: a Download button on each episode (the Mac has one), debrid cached / not cached labels, a place to manage downloads outside the panel, resuming after the app closes.
4. Torrent peer finding is slow on the owner's PC: the DHT stays "Initialising / NotReady with 0 nodes" (trackers still give peers). Windows Firewall for `ArcTV` (private and public) and a VPN were not yet ruled out. The MonoTorrent 3.9 alpha (adds uTP) was offered and not tried: it is an alpha with a different settings API and nothing here can compile locally.
5. The remembered-source hang from Continue Watching was never root-caused: Continue Watching shows the source list instead. `player.log` / `mpv.log` and the watchdog exist to catch it.
6. Visual Studio stops on a cancelled web request in `ApiHttp` ("User-Unhandled") when a page that was loading is left: it is a debugger setting (untick it), and the app already ignores cancelled and network errors. Which caller starts it was not traced (a call stack would show it).
7. Check the web repo's `docs/PARITY.md` for changes not yet matched here.

**How the owner works:** they test with Visual Studio on their PC (pull, F5) and paste what they see, screenshots and `player.log`. Push every change straight to `main` (and to the session's branch if the hook asks), then read the CI result and report failures plainly; do not hold work back waiting for CI, and do not push over a red build you have not read. WPF here has no implicit `System.IO` or `System.Net.Http` usings (add them), and warnings are errors. Commit trailers per the session's attribution reminder.
