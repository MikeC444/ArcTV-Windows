#include <chrono>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

#include <libtorrent/add_torrent_params.hpp>
#include <libtorrent/create_torrent.hpp>
#include <libtorrent/hex.hpp>
#include <libtorrent/session.hpp>
#include <libtorrent/settings_pack.hpp>
#include <libtorrent/torrent_info.hpp>

// A test helper: seeds one made-up 12 MB video on the loopback address, prints "MAGNET <link>" and "FILE <path of the original>" and keeps seeding until its input ends. The C# tests
// start it, give the link to LibTorrentSession and compare what arrives with the original.
namespace fs = std::filesystem;

int main(int argc, char **argv) {
    const fs::path root = argc > 1 ? fs::path(argv[1]) : fs::temp_directory_path() / "ctorrent-seeder";
    fs::remove_all(root);
    fs::create_directories(root / "seed" / "Show.S01E02.1080p");
    std::vector<char> data(12 * 1024 * 1024);
    unsigned x = 777;
    for (auto &b : data) { x = x * 1664525u + 1013904223u; b = static_cast<char>(x >> 24); }
    const fs::path original = root / "seed" / "Show.S01E02.1080p" / "show.s01e02.mkv";
    { std::ofstream(original, std::ios::binary).write(data.data(), static_cast<std::streamsize>(data.size())); }
    { std::ofstream(root / "seed" / "Show.S01E02.1080p" / "info.nfo") << "nfo"; }

    lt::file_storage storage;
    lt::add_files(storage, (root / "seed" / "Show.S01E02.1080p").string());
    lt::create_torrent creator(storage, 256 * 1024);
    lt::error_code ec;
    lt::set_piece_hashes(creator, (root / "seed").string(), ec);
    std::vector<char> buffer;
    lt::bencode(std::back_inserter(buffer), creator.generate());
    auto info = std::make_shared<lt::torrent_info>(buffer.data(), static_cast<int>(buffer.size()), ec);
    if (ec) return 1;

    lt::settings_pack pack;
    pack.set_str(lt::settings_pack::listen_interfaces, "127.0.0.1:0");
    pack.set_bool(lt::settings_pack::enable_dht, false);
    pack.set_bool(lt::settings_pack::enable_lsd, false);
    pack.set_bool(lt::settings_pack::enable_upnp, false);
    pack.set_bool(lt::settings_pack::enable_natpmp, false);
    pack.set_int(lt::settings_pack::alert_mask, 0);
    lt::session session(pack);
    lt::add_torrent_params params;
    params.ti = info;
    params.save_path = (root / "seed").string();
    params.flags |= lt::torrent_flags::seed_mode;
    session.add_torrent(params);
    std::this_thread::sleep_for(std::chrono::milliseconds(500));

    std::printf("MAGNET magnet:?xt=urn:btih:%s&dn=Show&x.pe=127.0.0.1:%d\n", lt::aux::to_hex(info->info_hashes().get_best()).c_str(), session.listen_port());
    std::printf("FILE %s\n", original.string().c_str());
    std::fflush(stdout);
    std::string line;
    while (std::getline(std::cin, line)) {}
    return 0;
}
