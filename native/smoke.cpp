#include "ctorrent.h"

#include <cstdio>
#include <cstring>

// Creates the engine, adds a made-up magnet link, reads the status (no file list can arrive), then stops and destroys it. Fails with a message on anything unexpected.
int main() {
    ct_engine *engine = ct_create();
    if (!engine) { std::puts("ct_create failed"); return 1; }

    if (ct_start(engine, "not a magnet", ".") == 0) { std::puts("a bad magnet link was accepted"); return 2; }

    const char *magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=smoke";
    if (ct_start(engine, magnet, ".") != 0) { std::puts("ct_start failed"); return 3; }

    ct_status status;
    ct_get_status(engine, &status);
    if (status.has_metadata != 0 || status.file_count != 0) { std::puts("a made-up torrent has a file list"); return 4; }
    char name[64];
    long long size = 0;
    if (ct_file_info(engine, 0, name, sizeof name, &size) == 0) { std::puts("file info for a torrent without a file list"); return 5; }
    if (ct_piece_length(engine) != 0) { std::puts("piece length before the file list"); return 6; }
    if (ct_have_range(engine, 0, 0, 10) != 0) { std::puts("have_range before the file list"); return 7; }

    ct_stop(engine, 1);
    ct_destroy(engine);
    std::puts("ok");
    return 0;
}
