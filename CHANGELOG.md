# Changelog

Engineering log for Arc TV for Windows, oldest first.

## 1 -- Foundations: solution, build workflow, first ported pieces

**Status:** Built and tested in the Linux sandbox (.NET 8): the solution compiles, WPF included, and the 10 tests pass. Not run on Windows yet.

**Context:** Arc TV already runs on Fire TV, Android phones, the web and the Mac. This starts the native Windows app the way the Mac app was done:
a logic library ported from Swift, with its tests, under a native UI.

**Changes:**
- `ArcTV.sln` with `ArcTV.Core` (net8.0), `ArcTV.App` (net8.0-windows, WPF, assembly name `ArcTV`) and `ArcTV.Core.Tests` (xUnit).
- `AppInfo` (version from the build, the server address rules, the Cinemeta defaults), `AddonUrl` (manifest and catalogue addresses) and `UpdateCheck`
  (version comparison, GitHub release parsing; the installer `.exe` is preferred, else the zip), ported from the Mac app with their tests.
- A themed starter window: the same near-black surfaces and cyan, blue and violet as the other apps.
- `build.yml`: builds and tests on a Windows machine for every push, publishes a self-contained zip and keeps it for 14 days. `scripts/try-branch.ps1`
  fetches, builds and opens any branch.

**Tests performed:** `dotnet build ArcTV.sln` and `dotnet test ArcTV.sln` on Linux with Microsoft's .NET 8.0.425 SDK: 0 warnings, 0 errors, 10 passed.
The workflow has not run yet, and the app has not been started on any Windows PC.

**Issues discovered:** the first time the solution was opened in Visual Studio it tried to start `ArcTV.Core` (a class library, which cannot run) because it was the first project in the solution file; `ArcTV.App` now comes first, so it is the startup project by default. The Ubuntu `dotnet-sdk-8.0` package cannot build WPF projects (no WindowsDesktop SDK); Microsoft's own install script can.

**Issues fixed:** the startup project (above). Hardened against a PC with a newer .NET than 8: `global.json` rolls forward to any newer SDK, the app is allowed to run on a newer desktop runtime, and warnings are errors only on the build server (`CI=true`). (A first Visual Studio build on the owner's PC reported build errors that the GitHub Windows build did not; the exact text was not yet known when this was changed.)
