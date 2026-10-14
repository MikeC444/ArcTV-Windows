#ifndef CTORRENT_H
#define CTORRENT_H

/* A small C face on libtorrent (the torrent engine): one session that holds one torrent at a time, the one being watched.
   Shared with the Mac app's CTorrent; the additions for Windows are CT_API, piece length and the extra status fields (marked). */

#ifdef _WIN32
#define CT_API __declspec(dllexport)
#else
#define CT_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct ct_engine ct_engine;

typedef struct {
    int has_metadata;        /* 1 once the torrent's file list is known */
    int state;               /* 0 checking, 1 getting file list, 2 downloading, 3 finished or seeding */
    int num_peers;
    int num_seeds;
    double progress;         /* 0...1 of the files being downloaded */
    long long download_rate; /* bytes per second */
    int file_count;
    /* added for Windows, at the end so the first fields match the Mac's */
    int num_connections;     /* open connections */
    int list_peers;          /* peers known and not yet tried */
    int dht_nodes;           /* nodes in the DHT routing table (0 while it is still bootstrapping) */
} ct_status;

CT_API ct_engine *ct_create(void);
CT_API void ct_destroy(ct_engine *engine);

/* Replaces any torrent in the session with this magnet link, saving under save_path (UTF-8). 0 on success. */
CT_API int ct_start(ct_engine *engine, const char *magnet, const char *save_path);
/* Stops the torrent and, with delete_files, removes what it downloaded. */
CT_API void ct_stop(ct_engine *engine, int delete_files);

CT_API void ct_get_status(ct_engine *engine, ct_status *out);
/* Name (path inside the torrent, UTF-8) and size of a file. 0 on success. */
CT_API int ct_file_info(ct_engine *engine, int index, char *name, int name_len, long long *size);
/* Where the file is (or will be) on disk (UTF-8). 0 on success. */
CT_API int ct_file_path(ct_engine *engine, int index, char *path, int path_len);
/* Downloads only this file, in order. 0 on success. */
CT_API int ct_select_file(ct_engine *engine, int index);
/* 1 if every piece covering bytes [offset, offset + length) of the file has arrived. */
CT_API int ct_have_range(ct_engine *engine, int index, long long offset, long long length);
/* Asks for the next `pieces` pieces from this byte on first (the player jumped there). */
CT_API void ct_stream_from(ct_engine *engine, int index, long long offset, int pieces);
/* Added for Windows: the size of a piece in bytes (0 until the file list is known). */
CT_API int ct_piece_length(ct_engine *engine);

#ifdef __cplusplus
}
#endif

#endif
