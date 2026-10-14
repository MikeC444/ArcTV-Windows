#include "ctorrent.h"

#include <algorithm>
#include <cstring>
#include <memory>
#include <string>

#include <libtorrent/add_torrent_params.hpp>
#include <libtorrent/file_storage.hpp>
#include <libtorrent/magnet_uri.hpp>
#include <libtorrent/posix_disk_io.hpp>
#include <libtorrent/session.hpp>
#include <libtorrent/session_params.hpp>
#include <libtorrent/settings_pack.hpp>
#include <libtorrent/torrent_flags.hpp>
#include <libtorrent/torrent_handle.hpp>
#include <libtorrent/torrent_info.hpp>
#include <libtorrent/torrent_status.hpp>
#include <libtorrent/version.hpp>
#include <libtorrent/session_params.hpp>

struct ct_engine {
    lt::session session;
    lt::torrent_handle handle;
    std::string save_path;

    static lt::settings_pack makePack() {
        lt::settings_pack pack;
        pack.set_int(lt::settings_pack::alert_mask, 0);
        pack.set_str(lt::settings_pack::user_agent, "ArcTV/1.0 libtorrent");
        // a magnet link has to find its first peers fast: ask every tracker of every tier, not only the first that answers
        pack.set_bool(lt::settings_pack::announce_to_all_trackers, true);
        pack.set_bool(lt::settings_pack::announce_to_all_tiers, true);
        return pack;
    }

    // Plain file reads and writes (posix_disk_io), not libtorrent's default memory-mapped files. Arc TV's video server reads the file with an ordinary file read as soon as libtorrent says a piece
    // has arrived; on Windows a memory-mapped write is not always visible to a separate read yet (the file's valid length lags behind: the read gets zeros), and that showed up as a corrupt
    // picture in the real-transfer test on CI. With ordinary writes, what libtorrent has written is what a read sees.
    static lt::session_params makeParams() {
        lt::session_params params(makePack());
        params.disk_io_constructor = lt::posix_disk_io_constructor;
        return params;
    }

    ct_engine() : session(makeParams()) {}
};

// libtorrent 2.1 (what vcpkg builds) calls the torrent's file list layout(); 2.0 (what apt has) calls it files()
static const lt::file_storage &files_of(const lt::torrent_info &info) {
#if LIBTORRENT_VERSION_NUM >= 20100
    return info.layout();
#else
    return info.files();
#endif
}

static std::shared_ptr<const lt::torrent_info> info_of(ct_engine *e) {
    if (!e->handle.is_valid()) return nullptr;
    return e->handle.torrent_file();
}

extern "C" {

CT_API ct_engine *ct_create(void) {
    try { return new ct_engine(); } catch (...) { return nullptr; }
}

CT_API void ct_destroy(ct_engine *engine) {
    delete engine;
}

CT_API int ct_start(ct_engine *engine, const char *magnet, const char *save_path) {
    if (!engine || !magnet || !save_path) return -1;
    try {
        if (engine->handle.is_valid()) {
            engine->session.remove_torrent(engine->handle, lt::session::delete_files);
            engine->handle = lt::torrent_handle();
        }
        lt::error_code ec;
        lt::add_torrent_params params = lt::parse_magnet_uri(magnet, ec);
        if (ec) return -2;
        params.save_path = save_path;
        params.flags |= lt::torrent_flags::sequential_download;
        engine->save_path = save_path;
        engine->handle = engine->session.add_torrent(params);
        return 0;
    } catch (...) {
        return -3;
    }
}

CT_API void ct_stop(ct_engine *engine, int delete_files) {
    if (!engine) return;
    try {
        if (engine->handle.is_valid()) {
            engine->session.remove_torrent(engine->handle, delete_files ? lt::session::delete_files : lt::remove_flags_t{});
        }
        engine->handle = lt::torrent_handle();
    } catch (...) {
    }
}

CT_API void ct_get_status(ct_engine *engine, ct_status *out) {
    if (!out) return;
    std::memset(out, 0, sizeof(*out));
    if (!engine) return;
    try {
        if (!engine->handle.is_valid()) return;
        lt::torrent_status status = engine->handle.status();
        out->has_metadata = status.has_metadata ? 1 : 0;
        switch (status.state) {
            case lt::torrent_status::checking_files:
            case lt::torrent_status::checking_resume_data: out->state = 0; break;
            case lt::torrent_status::downloading_metadata: out->state = 1; break;
            case lt::torrent_status::downloading: out->state = 2; break;
            default: out->state = 3; break;
        }
        out->num_peers = status.num_peers;
        out->num_seeds = status.num_seeds;
        out->progress = status.progress;
        out->download_rate = status.download_payload_rate;
        auto info = info_of(engine);
        out->file_count = info ? files_of(*info).num_files() : 0;
        out->num_connections = status.num_connections;
        out->list_peers = status.list_peers;
        try {
            // the DHT's routing table: how many nodes it has found so far
            lt::session_params state = engine->session.session_state(lt::session::save_dht_state);
            out->dht_nodes = static_cast<int>(state.dht_state.nodes.size() + state.dht_state.nodes6.size());
        } catch (...) {
        }
    } catch (...) {
    }
}

CT_API int ct_file_info(ct_engine *engine, int index, char *name, int name_len, long long *size) {
    try {
        auto info = info_of(engine);
        if (!info || index < 0 || index >= files_of(*info).num_files()) return -1;
        lt::file_index_t file{index};
        std::string path = files_of(*info).file_path(file);
        if (name && name_len > 0) {
            std::strncpy(name, path.c_str(), static_cast<size_t>(name_len) - 1);
            name[name_len - 1] = 0;
        }
        if (size) *size = files_of(*info).file_size(file);
        return 0;
    } catch (...) {
        return -2;
    }
}

CT_API int ct_file_path(ct_engine *engine, int index, char *path, int path_len) {
    try {
        auto info = info_of(engine);
        if (!info || index < 0 || index >= files_of(*info).num_files() || !path || path_len <= 0) return -1;
        std::string full = engine->save_path + "/" + files_of(*info).file_path(lt::file_index_t{index});
        std::strncpy(path, full.c_str(), static_cast<size_t>(path_len) - 1);
        path[path_len - 1] = 0;
        return 0;
    } catch (...) {
        return -2;
    }
}

CT_API int ct_select_file(ct_engine *engine, int index) {
    try {
        auto info = info_of(engine);
        if (!info || index < 0 || index >= files_of(*info).num_files()) return -1;
        for (int i = 0; i < files_of(*info).num_files(); i++) {
            engine->handle.file_priority(lt::file_index_t{i}, i == index ? lt::default_priority : lt::dont_download);
        }
        return 0;
    } catch (...) {
        return -2;
    }
}

CT_API int ct_have_range(ct_engine *engine, int index, long long offset, long long length) {
    try {
        auto info = info_of(engine);
        if (!info || index < 0 || index >= files_of(*info).num_files() || length <= 0) return 0;
        lt::file_index_t file{index};
        long long size = files_of(*info).file_size(file);
        if (offset < 0 || offset >= size) return 0;
        long long last = std::min(offset + length, size) - 1;
        int first_piece = static_cast<int>(info->map_file(file, offset, 1).piece);
        int last_piece = static_cast<int>(info->map_file(file, last, 1).piece);
        for (int piece = first_piece; piece <= last_piece; piece++) {
            if (!engine->handle.have_piece(lt::piece_index_t{piece})) return 0;
        }
        return 1;
    } catch (...) {
        return 0;
    }
}

CT_API void ct_stream_from(ct_engine *engine, int index, long long offset, int pieces) {
    try {
        auto info = info_of(engine);
        if (!info || index < 0 || index >= files_of(*info).num_files()) return;
        lt::file_index_t file{index};
        long long size = files_of(*info).file_size(file);
        if (offset < 0 || offset >= size) return;
        int first_piece = static_cast<int>(info->map_file(file, offset, 1).piece);
        int last_piece = static_cast<int>(info->map_file(file, size - 1, 1).piece);
        engine->handle.clear_piece_deadlines();
        if (pieces < 4) pieces = 4;
        for (int k = 0; k < pieces && first_piece + k <= last_piece; k++) {
            engine->handle.set_piece_deadline(lt::piece_index_t{first_piece + k}, 150 + k * 100);
        }
    } catch (...) {
    }
}

CT_API int ct_piece_length(ct_engine *engine) {
    try {
        auto info = info_of(engine);
        return info ? info->piece_length() : 0;
    } catch (...) {
        return 0;
    }
}

}
