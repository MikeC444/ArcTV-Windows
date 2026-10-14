cmake_minimum_required(VERSION 3.20)
project(ctorrent CXX)

# ctorrent: the small C face on libtorrent that Arc TV loads (ctorrent.dll on Windows). On the build machine libtorrent comes from vcpkg
# (-DCMAKE_TOOLCHAIN_FILE=.../vcpkg.cmake); on Linux, apt's libtorrent-rasterbar-dev is enough to build and try it.
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_VISIBILITY_PRESET hidden)

find_package(LibtorrentRasterbar CONFIG REQUIRED)

add_library(ctorrent SHARED ctorrent.cpp)
target_link_libraries(ctorrent PRIVATE LibtorrentRasterbar::torrent-rasterbar)
if(MSVC)
  target_compile_options(ctorrent PRIVATE /EHsc /utf-8)
endif()

# a quick check that the library loads and a torrent can be added, asked about and stopped (no network needed)
add_executable(ctorrent_smoke smoke.cpp)
target_link_libraries(ctorrent_smoke PRIVATE ctorrent)
# the real thing on one machine: a second session seeds a file on the loopback address and ctorrent streams it from there
add_executable(ctorrent_integration integration.cpp)
target_link_libraries(ctorrent_integration PRIVATE ctorrent LibtorrentRasterbar::torrent-rasterbar)
if(MSVC)
  target_compile_options(ctorrent_integration PRIVATE /EHsc /utf-8)
  target_compile_features(ctorrent_integration PRIVATE cxx_std_20)
else()
  target_compile_features(ctorrent_integration PRIVATE cxx_std_20)
endif()
# a helper for the C# tests: seeds a made-up video on the loopback address
add_executable(ctorrent_seeder seeder.cpp)
target_link_libraries(ctorrent_seeder PRIVATE LibtorrentRasterbar::torrent-rasterbar)
target_compile_features(ctorrent_seeder PRIVATE cxx_std_20)
if(MSVC)
  target_compile_options(ctorrent_seeder PRIVATE /EHsc /utf-8)
endif()
enable_testing()
add_test(NAME smoke COMMAND ctorrent_smoke)
add_test(NAME integration COMMAND ctorrent_integration)
