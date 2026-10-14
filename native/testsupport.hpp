#ifndef CTORRENT_TESTSUPPORT_H
#define CTORRENT_TESTSUPPORT_H

#include <filesystem>
#include <stdexcept>
#include <string>
#include <vector>

#include <libtorrent/add_torrent_params.hpp>
#include <libtorrent/create_torrent.hpp>
#include <libtorrent/load_torrent.hpp>
#include <libtorrent/version.hpp>

// For the test programs: makes a torrent out of a folder, the same on libtorrent 2.0 (apt) and 2.1 (vcpkg), whose torrent-making calls differ. The folder's own name starts the names inside the torrent
// ("Movie/film.mkv"), and its parent is where the files are read from when hashing.
inline lt::add_torrent_params make_test_torrent(const std::filesystem::path &folder, int piece_length) {
    lt::error_code ec;
#if LIBTORRENT_VERSION_NUM >= 20100
    lt::create_torrent creator(lt::list_files(folder.string()), piece_length);
#else
    lt::file_storage storage;
    lt::add_files(storage, folder.string());
    lt::create_torrent creator(storage, piece_length);
#endif
    lt::set_piece_hashes(creator, folder.parent_path().string(), ec);
    if (ec) throw std::runtime_error("hashing failed: " + ec.message());
    std::vector<char> buffer = creator.generate_buf();
    return lt::load_torrent_buffer(lt::span<char const>(buffer.data(), buffer.size()));
}

#endif
