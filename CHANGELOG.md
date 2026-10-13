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

## 2 -- Sign-in: log in, sign up, phone QR code, stay signed in

**Status:** Built and tested in the Linux sandbox (.NET 8): compiles with `CI=true` (warnings as errors), 41 tests pass. The sign-in screen itself has not been seen or clicked through on Windows yet.

**Context:** First real screen. Ported from the Mac app's account code, so the same backend accepts it as another device ("windows").

**Changes:**
- Core: `AuthClient` (login, register, refresh, logout, QR create and poll, authorized calls with the profile header), `ApiException` and `ApiHttp` (time limits, the
  server's error text), `Credentials`, `IsoDate`, and `AccountModel` (all sign-in behaviour: checks input first, 30 s pause after "too many attempts", the friendly
  no-connection message, one refresh-and-retry on an expired token, sign-out on a refused refresh, the QR wait loop).
- Storage: `SessionStore` (session kept encrypted with Windows DPAPI in `%LOCALAPPDATA%\ArcTV`, so the app opens signed in), `DeviceIdentity` (one id per install),
  `LocalSettings` (server address).
- App: the sign-in view (hero photo fading into the page, Log In / Sign Up, email and password with Show password, a QR panel that checks every 2 s), a
  stand-in signed-in screen with Sign out, the app icon. Ctrl-click the logo or heading to reveal a server-address field.

**Tests performed:** `CI=true dotnet build ArcTV.sln` (0 warnings, 0 errors) and `dotnet test ArcTV.sln` on Linux: 41 passed, covering the account client against a fake server, the
model (rate limit, refresh, QR), and the file storage. The DPAPI protector and all the WPF screens were compiled only: they cannot run here.

**Issues discovered:** a catch clause cannot use a property pattern (compile error, fixed); a window method named `Show` hid the real one (warning, renamed); the first push failed the Windows build because `byte[].Reverse().ToArray()` in a test resolves differently on the newer SDK there (it picked the in-place span version, which returns nothing).

**Issues fixed:** all of the above (the test now copies and uses `Array.Reverse`).

## 3 -- Home and the top bar

**Status:** Built and unit-tested in the Linux sandbox (66 Core tests pass; compiles with warnings as errors). The screens themselves have not been seen. New screen tests (below) are written to run on the Windows build server, not here.

**Context:** After sign-in the app lands on Home, built from the account's own addons the way the Fire TV, web and Mac apps do it.

**Changes:**
- Core: addon manifest and catalogue models (resources may be names or objects; an odd entry never breaks a manifest), `AddonClient`, the account's addon list (`/user/addons`),
  and the Home row builder ported from the Mac app: Cinemeta-shaped addons get Popular, New, Top rated and the wide-appeal genres; others get their base row and a row per genre;
  rows load four at a time; each row reads a page chosen from the account and the day, with a gentle seeded shuffle (checked against the web app's numbers); the person's row order
  and hidden rows are applied; a title shows in one row only. `HomeModel` ties it together (falls back to Cinemeta when there are no addons) and picks the banner titles.
- App: the shell with a floating top bar (Home, Movies, TV Shows, Search, My List, Settings; the account button has Sign out), the Home screen (banner that rotates every 6 s and
  pauses under the pointer, rows of posters that lift under the pointer, loading and "try again" states), remote images decoded at display size. The other places show "Coming soon".
- `ArcTV.App.Tests` (runs only on Windows): opens the sign-in screen, Home and the shell in a real window and fails the build if any XAML, style or binding is broken.

**Tests performed:** `CI=true dotnet build ArcTV.sln` (0 warnings, 0 errors) and `dotnet test tests/ArcTV.Core.Tests` on Linux: 66 passed (manifest parsing, row building against a fake addon server, ordering, de-duplication, seeded numbers, the model).
The WPF screens and `ArcTV.App.Tests` compiled only.

**Issues discovered:** the Home model's last state change could land after `LoadAsync` returned when a synchronization context was present (a test caught it); now awaited.

**Issues fixed:** the above. Not done yet: hide-the-bar-on-scroll, Continue Watching, Picked for you, the hero's Play/Trailer/My List/Info buttons, the saved row cache (needs the details and player first).
