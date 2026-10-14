# ctorrent: libtorrent for Arc TV

A small C face on [libtorrent](https://libtorrent.org) (the torrent engine the Mac app uses; its `Sources/CTorrent` is where `ctorrent.cpp` came from). One session holds one torrent at a time, the one being
watched. `ctorrent.h` is the whole interface. The Windows app loads it as `ctorrent.dll` (`src/ArcTV.Core/Torrents/LibTorrentSession.cs` is the C# side) and serves the video to the player itself
(`LocalVideoServer`). MonoTorrent stays in the app as the fallback: Settings > Player > Torrent engine, or automatically when this DLL can't load.

Additions over the Mac's copy (all at the end of the structure, so the Mac's fields still match): `CT_API` export macro, `ct_piece_length`, and `num_connections`, `list_peers`, `dht_nodes` in the status.

## Building

- **On Windows (the real build):** `.github/workflows/native.yml` installs libtorrent with vcpkg (`libtorrent:x64-windows`), builds with CMake, runs the tests below and, on `main`, publishes
  `libtorrent-win-x64.zip` (ctorrent.dll, libtorrent, OpenSSL, zlib and the C++ runtime) as the `native-libtorrent` pre-release. `scripts/get-libtorrent.ps1` downloads it into `src/ArcTV.App/libtorrent/`
  (the app's build does that by itself the first time).
- **On Linux (to try it here):** `sudo apt install libtorrent-rasterbar-dev libboost-dev`, then `cmake -S native -B /tmp/ctbuild && cmake --build /tmp/ctbuild`.

## Tests

- `ctorrent_smoke`: loads, adds a made-up torrent, asks about it, stops it. No network.
- `ctorrent_integration`: a second libtorrent session seeds a 12 MB file on the loopback address; ctorrent gets the file list from it through a magnet link that names it as a peer, takes one file,
  streams the start, jumps to the middle, and the bytes on disk must match.
- C# (`tests/ArcTV.Core.Tests/LibTorrentTests.cs`): the same through the wrapper, `LocalVideoServer` and an HTTP range read, with `ctorrent_seeder` as the seeder. They run when
  `ARCTV_CTORRENT_PATH` (the library) and `ARCTV_CT_SEEDER` (the helper) are set, and pass without doing anything otherwise.
