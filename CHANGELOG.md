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

## 4 -- Movies and TV Shows: browse by genre

**Status:** Written without a compiler (the sandbox could not download the .NET SDK). The Windows build server then built it, ran all tests (including the new ones and the Movies screen test) and published the app: green. Not run or seen on screen by a person.

**Context:** First of the places after Home. Ported from the Mac app's `Browse.swift`, `BrowseView.swift` and `HomeSource.browsePage`.

**Changes:**
- `ArcTV.Core/Browse`: `BrowseSort` (Featured, Highest Rated, Newest), `BrowsePaging.BrowsePageAsync` (one page of a type from an
  addon, for a genre or all) and `BrowseModel` (first page, then more as you scroll; a genre's loaded titles kept while the shell lives).
- `AddonSources`: the "which addons can fill pages" rule (enabled addons with catalogues, else Cinemeta), lifted out of `HomeModel` so Home and Browse share it.
- `BrowseView`: title, genre pill with a two-column list, sort pills, poster grid that loads more near the bottom. The poster card moved to
  `PosterTemplate.xaml` so Home and Browse share it. New `ChoicePill` style.

**Tests performed:** the Windows build server (build, test, publish all passed). New: `BrowseTests` (sorting, year, genre filtering, paging, cache) and a screen test that opens the Movies page.

**Issues discovered:** the first build failed: a second `Genres` class clashed with the one Home already had (ambiguous in the tests).

**Issues fixed:** the duplicate was removed; Browse uses the existing lists.

**Follow-up (from a screenshot on the owner's PC):** posters loaded slowly and the selected sort pill ("Featured") had white text on white.
- Slow: the grid was a plain wrap panel, so every loaded poster (100 per page, more as you scroll) was created and started its download at once. The posters
  are now lines in a virtualised list (as many per line as fit), so only the lines on screen exist and only their pictures are fetched. The title, genre and
  sort pills now stay put above the grid instead of scrolling away with it. This is a diagnosis from reading the code; load times were not measured.
- Some posters still stayed blank after that (screenshot): pictures were loaded by WPF's own downloader, which gave up silently on any failure and started
  every download at once. `RemoteImage` now fetches a few at a time (6), tries a failed picture up to 3 times, stops when its poster leaves the screen and
  keeps the last 120 decoded pictures. If a poster is still blank after that, its address is probably bad in the addon's data (not confirmed).
- Empty strip on the right of a wide window (screenshot): a line holds a whole number of posters, so the rest was blank. Lines now share the spare width as
  extra space between posters (`SpacedRowPanel`), until another poster fits. Every line uses the same space, so the last, shorter line stays in step.
- Snappiness pass on Movies and TV Shows (no timings were measured; the app has not been seen running here):
  - resizing the window no longer throws you back to the top when the number of posters across changes: the same titles stay at the top of the screen;
  - the wheel scrolls further per notch and glides to a stop (eased on each frame) instead of stepping;
  - a page of lines above and below the screen is kept ready, so scrolling does not wait on pictures; pictures fade in as they arrive; up to 8 download at once;
  - the hover lift on a poster eases in and out instead of snapping (this card is shared with Home).
- Bigger posters on request: the shared card went from 156x234 to 192x288 (a line is now 208 wide with its gap) and pictures are decoded at 400 wide. Home's rows
  use the same card, so they grow too.
- Home banner looked soft (screenshot). Two changes, neither confirmed by looking at the result: the banner now asks the artwork server (Cinemeta's) for the
  "large" background instead of the "medium" one the addon lists, and falls back to the listed address when there is no large one (a missing picture is not
  retried; `RemoteImage` gained a `FallbackUrl`); and it is no longer shrunk to 1600 wide when decoded, but kept at the size it arrives. That the artwork
  server has a "large" size is an assumption (it could not be reached from here); if it does not, nothing changes from before.
- Pill text: the pill's content now takes its colour from the button, so the selected pill's black text applies.

**Open:** blocked genres (needs profiles/settings), loading skeletons, a virtualised grid (the grid lays out every loaded poster), opening a title (needs title details).
