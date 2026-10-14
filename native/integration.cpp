#include "ctorrent.h"

#include <chrono>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <string>
#include <thread>
#include <vector>

#include <libtorrent/add_torrent_params.hpp>
#include <libtorrent/create_torrent.hpp>
#include <libtorrent/magnet_uri.hpp>
#include <libtorrent/session.hpp>
#include <libtorrent/settings_pack.hpp>
#include <libtorrent/torrent_handle.hpp>
#include <libtorrent/torrent_info.hpp>
#include <libtorrent/torrent_status.hpp>
#include <libtorrent/hex.hpp>
#include <filesystem>

// End to end on one machine, no internet: a second libtorrent session seeds a 12 MB file on the loopback address; ctorrent is given a magnet link that names that seeder as a peer.
// It must get the file list from the seeder, take one file, say when the start of it has arrived, and the bytes on disk must then match the original.
namespace fs = std::filesystem;

static bool wait_for(auto condition, int seconds) {
    for (int i = 0; i < seconds * 20; i++) {
        if (condition()) return true;
        std::this_thread::sleep_for(std::chrono::milliseconds(50));
    }
    return false;
}

int main() {
    const fs::path root = fs::temp_directory_path() / "ctorrent-integration";
    fs::remove_all(root);
    fs::create_directories(root / "seed" / "Movie");
    fs::create_directories(root / "client");

    // two files: a video-like one of 12 MB and a small one that must not be downloaded
    std::vector<char> data(12 * 1024 * 1024);
    unsigned x = 12345;
    for (auto &b : data) { x = x * 1664525u + 1013904223u; b = static_cast<char>(x >> 24); }
    { std::ofstream(root / "seed" / "Movie" / "film.mkv", std::ios::binary).write(data.data(), static_cast<std::streamsize>(data.size())); }
    { std::ofstream(root / "seed" / "Movie" / "readme.txt") << "hello"; }

    lt::file_storage storage;
    lt::add_files(storage, (root / "seed" / "Movie").string());
    lt::create_torrent creator(storage, 256 * 1024);
    lt::error_code ec;
    lt::set_piece_hashes(creator, (root / "seed").string(), ec);
    if (ec) { std::printf("hashing failed: %s\n", ec.message().c_str()); return 1; }
    lt::entry entry = creator.generate();
    std::vector<char> buffer;
    lt::bencode(std::back_inserter(buffer), entry);
    auto info = std::make_shared<lt::torrent_info>(buffer.data(), static_cast<int>(buffer.size()), ec);
    if (ec) { std::printf("torrent_info failed: %s\n", ec.message().c_str()); return 1; }

    lt::settings_pack pack;
    pack.set_str(lt::settings_pack::listen_interfaces, "127.0.0.1:0");
    pack.set_bool(lt::settings_pack::enable_dht, false);
    pack.set_bool(lt::settings_pack::enable_lsd, false);
    pack.set_bool(lt::settings_pack::enable_upnp, false);
    pack.set_bool(lt::settings_pack::enable_natpmp, false);
    pack.set_int(lt::settings_pack::alert_mask, 0);
    lt::session seeder(pack);
    lt::add_torrent_params seed_params;
    seed_params.ti = info;
    seed_params.save_path = (root / "seed").string();
    seed_params.flags |= lt::torrent_flags::seed_mode;
    seeder.add_torrent(seed_params);
    std::this_thread::sleep_for(std::chrono::milliseconds(500));
    int port = seeder.listen_port();
    if (port == 0) { std::puts("the seeder has no port"); return 2; }

    std::string magnet = "magnet:?xt=urn:btih:" + lt::aux::to_hex(info->info_hashes().get_best()) + "&dn=Movie&x.pe=127.0.0.1:" + std::to_string(port);

    ct_engine *engine = ct_create();
    if (!engine) { std::puts("ct_create failed"); return 3; }
    const std::string save = (root / "client").string();
    if (ct_start(engine, magnet.c_str(), save.c_str()) != 0) { std::puts("ct_start failed"); return 4; }

    ct_status status;
    if (!wait_for([&] { ct_get_status(engine, &status); return status.has_metadata != 0; }, 30)) { std::puts("no file list from the seeder"); return 5; }
    if (status.file_count < 2) { std::printf("file count %d, wanted at least 2 (libtorrent may add pad files)\n", status.file_count); return 6; }

    int video = -1;
    for (int i = 0; i < status.file_count; i++) {
        char name[512]; long long size = 0;
        if (ct_file_info(engine, i, name, sizeof name, &size) != 0) { std::puts("file info failed"); return 7; }
        std::printf("file %d: %s (%lld bytes)\n", i, name, size);
        if (std::strstr(name, "film.mkv")) { video = i; if (size != static_cast<long long>(data.size())) { std::puts("wrong size"); return 8; } }
    }
    if (video < 0) { std::puts("no video in the file list"); return 9; }
    if (ct_piece_length(engine) != 256 * 1024) { std::printf("piece length %d\n", ct_piece_length(engine)); return 10; }
    if (ct_select_file(engine, video) != 0) { std::puts("select failed"); return 11; }

    // the start arrives first
    ct_stream_from(engine, video, 0, 24);
    if (!wait_for([&] { return ct_have_range(engine, video, 0, 3 * 1024 * 1024) == 1; }, 30)) { std::puts("the start of the video never arrived"); return 12; }
    ct_get_status(engine, &status);
    std::printf("start arrived: %d peers, %d connections, %lld B/s\n", status.num_peers, status.num_connections, status.download_rate);

    // a jump into the middle is fetched on request
    const long long middle = 8LL * 1024 * 1024;
    ct_stream_from(engine, video, middle, 24);
    if (!wait_for([&] { return ct_have_range(engine, video, middle, 1024 * 1024) == 1; }, 30)) { std::puts("the middle never arrived"); return 13; }

    // the bytes on disk are the original's
    char path[2048];
    if (ct_file_path(engine, video, path, sizeof path) != 0) { std::puts("file path failed"); return 14; }
    std::ifstream file(path, std::ios::binary);
    if (!file) { std::printf("cannot open %s\n", path); return 15; }
    std::vector<char> got(1024 * 1024);
    file.seekg(middle);
    file.read(got.data(), static_cast<std::streamsize>(got.size()));
    if (std::memcmp(got.data(), data.data() + middle, got.size()) != 0) { std::puts("bytes in the middle differ"); return 16; }
    file.seekg(0);
    file.read(got.data(), static_cast<std::streamsize>(got.size()));
    if (std::memcmp(got.data(), data.data(), got.size()) != 0) { std::puts("bytes at the start differ"); return 17; }

    // the file that was not chosen was not fetched (libtorrent may not even have created it)
    const fs::path readme = root / "client" / "Movie" / "readme.txt";
    if (fs::exists(readme) && fs::file_size(readme) > 0) { std::puts("the unchosen file was downloaded"); return 18; }

    ct_stop(engine, 1);
    ct_destroy(engine);
    fs::remove_all(root);
    std::puts("ok");
    return 0;
}
