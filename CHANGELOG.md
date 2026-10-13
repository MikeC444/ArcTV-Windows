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
  Still blocky afterwards (screenshot), which suggests the addon's picture is tiny (~300-500 wide). The same rule now also asks TMDB's artwork server for its
  w1280 size in place of w300/w780 (that size exists for backdrops), again falling back to the address given. The actual address was never seen, so this too
  is a guess; if it is still blocky, the address itself is needed.
  Still blurry after that, so a temporary tooltip on the banner now shows the address the picture really came from and its size in pixels, to stop guessing.
  The tooltip was removed once the banner was confirmed sharp.
  Cause found with it: the banner came from the "large" address but was decoded 320 x 180 pixels, the default decode width. `RemoteImage` started loading
  the moment its address was set, before the decode width (set after it in the same initializer) was in, so every picture was decoded at 320 wide however
  big it was asked to be: the banner from the start (so the size rewrites above were not what it needed), and posters at 320 instead of 400. Loading now waits
  until the settings are all in (one dispatcher turn).
- Pill text: the pill's content now takes its colour from the button, so the selected pill's black text applies.

**Open:** blocked genres (needs profiles/settings), loading skeletons, a virtualised grid (the grid lays out every loaded poster), opening a title (needs title details).

## 5 -- Search

**Status:** Written without a compiler here (no .NET SDK in the sandbox); built and tested by the Windows build server only (see the run for this commit). Not run or seen on screen by anyone.

**Context:** Next place after Movies / TV Shows. Ported from the Mac app's `CatalogSearch`, `RecentSearches` and `SearchView`.

**Changes:**
- `ArcTV.Core/Search`: `CatalogSearch` (every addon at once; an addon with a searchable catalogue is asked for real, otherwise its base catalogues are filtered by
  title; results are handed over as each addon answers; one that takes over 6 seconds is left behind and counted), `RecentSearches` (newest first, no doubles, 8 kept)
  and `SearchModel` (idle / searching / results / none / error; keeps old results up while new ones load).
- `RecentSearchStore`: recent searches per account in `recent-searches.json` next to the other app data.
- `SearchView`: round search box that searches as you type (after a 250 ms pause, never for one letter), up to five suggestions under it (Up/Down/Return/Escape),
  recent searches as chips, then Movies and TV Shows as groups of posters.
- `PosterGrid`: the poster lines, glide scrolling, resize behaviour and "load more near the end" moved out of `BrowseView` into one control that Movies, TV Shows
  and Search now share (headed groups are new). Behaviour of Movies / TV Shows is meant to be unchanged; resizing keeps your place by position in the list
  rather than by title.

**Tests performed:** see the build run. New: `SearchTests` (recents, searchable vs base-only addons, slow addon, model states, recents on disk) and a screen test.

**Issues discovered:** the first build of this commit failed: `ShellView` used `Path` without `using System.IO` (the WPF project's implicit usings do not include it). Visual Studio showed "There were build errors" on pulling it.

**Issues fixed:** the missing `using` was added. Layout fix from a screenshot: the search box was centred on the page (a bordered box with a maximum width centres itself) and "Clear" sat right beside "Recent searches"; the box and the recent searches now start at the page's left edge like the title, up to 900 wide, with "Clear" at the far end of its row.

**Open:** choosing a suggestion searches for its title (opening a title needs title details); posters do not open anything yet; searches are not filtered by blocked genres on purpose (as on the Mac).

## 6 -- My List

**Status:** Written without a compiler here (no .NET SDK in the sandbox); built and tested by the Windows build server only (see the run for this commit). Not run or seen on screen.

**Context:** Next place after Search. Ported from the Mac app's `MyListView`, `MyListSort` and `Watchlist`.

**Changes:**
- `ArcTV.Core/MyList`: `WatchlistItem` (GET `/user/watchlist`, shown as a poster card; MOVIE / TV_SHOW become movie / series), `ListSort` (Recently Added, A-Z,
  Highest Rated, Newest; every order starts from newest-added so ties keep that order) and `MyListModel` (the list, kept while it is fetched again, or why it could not be).
- `MyListView`: All / Watched pills, a sort list, the poster grid, and the empty / error / loading states. The list is fetched each time the page opens.
- A watched title shows a green tick on its poster (`MetaPreview.Watched`, set only for My List; the poster card is shared).
- The row style used for Search suggestions moved to the theme (`SuggestionRow`) so the sort list can use it too.

**Tests performed:** see the build run. New: `MyListTests` (the Mac sort tests, decoding, load / failure) and a screen test.

**Issues discovered:** the first build of this commit failed: `MyListTests` used two types that are internal to the core library (`WatchlistResponse`, `ApiHttp`). Only the tests failed to compile; the app built. It was not noticed for two commits because the results were not read before moving on.

**Issues fixed:** the test reads the answer through its own small type.

**Open:** adding and removing titles (needs title details); the saved list is not cached on this PC.

## 7 -- Title details (first part)

**Status:** Written without a compiler here (no .NET SDK in the sandbox); built and tested by the Windows build server only (see the run for this commit). Not run or seen on screen.

**Context:** Clicking a poster now opens the title's page. Ported from the Mac app's `DetailView`, `Details` and `DetailExtras`. This is the first part: what the page shows, and My List / Watched. Sources, Play, Trailer and Download come with the player.

**Changes:**
- `ArcTV.Core/Details`: `MetaDetail` / `EpisodeInfo` / `CastMember` (an addon's meta answer: cast with photos first, episodes by season with specials left out),
  `AddonClient.FetchMetaAsync` (and `AddonUrl.MetaUrl`, ids keep their colons), `Format` (runtime, release date), `StreamIds` and `DetailModel` (the card's basics at once, then the
  full details from Cinemeta merged with what the card had).
- `MyListModel` can now save, remove and mark watched (shown at once, undone if the account cannot be reached); `SaveToWatchlistAsync` / `RemoveFromWatchlistAsync` call the account.
- `DetailView`: the artwork behind everything (asked for in its bigger size, as the Home banner is), the logo or title, year and length, genres, a few lines about it, Play (switched off
  for now), My List and Watched buttons, the IMDb rating, a show's seasons and episodes, and the cast.
- Navigation: any poster (Home, Movies, TV Shows, Search, My List) or search suggestion that is clicked opens its title over the page you were on; Back (the button, Escape or the mouse's back button)
  puts that page back as it was. A poster is now a button (`PosterButton`).
- `Artwork.Bigger` (the bigger-picture rule from the Home banner) is shared; the Home banner starts rotating again after coming back from a title.

**Tests performed:** see the build run. New: `DetailsTests` (formats, cast order, seasons, addresses, model merging, season choice, My List toggles and undo) and a screen test.

**Issues discovered:** none yet.

**Issues fixed:** none yet.

**Open:** Play, sources, Trailer, Download; release date, trailer and cast photos from the account's server; the episode watched ticks; "You May Also Like"; the three-dot "more" menu and Like / Not for me (Plus).

**Follow-up (screenshot): pills.** The chosen pill (e.g. "Featured") was white with near-white text: pill text takes its colour from the app's TextBlock style, not from the button, so
the black text the style asked for never applied. Every chosen pill is now the logo's blue running into its purple (`PillGradient`) with light text: the Movies / TV Shows / My List / Search
choice pills, the top bar's current place, and the chosen season circle. An unchosen choice pill gets a gradient outline under the pointer.

**Follow-up (screenshot): account menu.** The menu from the account button (top right) was the default Windows one: a white box whose words were invisible because the app's text colour is light. It now has its
own dark, rounded style (`DarkMenu`, `DarkMenuItem`, `DarkMenuSeparator`) with the email dimmed, a divider, and "Sign out" lit under the pointer.

**Follow-up (owner asked where the Home banner's buttons were): banner buttons.** They had never been built (they were on the "not done on Home" list). Each banner slide now has Play (switched off until the
player exists), More info (opens the title's page) and My List (adds or removes it; reads "In My List" when it is on it, whichever page changed it). Trailer is left out: it needs the account
server's trailer lookup, which is not ported yet.

**Follow-up (owner asked where the Mac app's other title features were): the account-server extras.** Ported from `Trailers.swift`, `DetailExtras.swift` and the Mac `DetailView`:
- Trailer (title page and Home banner): the account's server (`/user/trailer`) gives a YouTube id; the button is on only when there is one, and opens it in the browser.
- A movie's release date (`/user/release-date`) in place of just the year ("Mar 5, 2024   2h 17m"); a show keeps its year.
- Cast photos and characters (`/user/cast`) filled in by name where the addon sent none (`CastMerge`: accents and letter case ignored).
- "You May Also Like" at the end of a movie's page: other titles from Home's rows, preferring ones that share a genre (`SimilarTitles`).
Each of these stays quiet when the server has nothing. Still not on the title page: Play and sources, Download, Like / Not for me and the three-dot menu (Plus), the episode watched ticks and per-episode Play / Download, Resume.
Still not on Home: Continue Watching, Picked for you, hiding the top bar on scroll.

**Follow-up: the build of the extras failed one test** (`ModelAddsTheTrailer...`): the test expected "2014   2h 49m" before the account had answered, but a movie shows only its length until the release date is known (as on the Mac, so the year does not appear and then swap to a full date). The test now expects that.

**Follow-up (owner: posters look too squared off): rounded corners.** A WPF border draws round corners but does not clip what is inside it, so a picture's corners stayed square whatever the radius. `RoundedBorder` clips its contents to the rounded shape.
Posters (every page), episode pictures, round cast photos and search-suggestion pictures use it; posters now have a 16 radius and a faint outline.

**Follow-up (owner: fade the Home hero picture): banner fade.** The banner's bottom edge showed a line where the picture ended and the page began: the old fade started from "transparent black" and only
went over the lower half in one step. It now fades in six steps from 30% down to fully the page's colour at the bottom edge (every step in the page's own colour, so it never passes through grey), and the left
darkening is smoother too.

**Follow-up (owner: scrolling snaps): smooth scrolling everywhere.** Movies, TV Shows, Search and My List already glided (in `PosterGrid`); Home and the title page still used WPF's wheel step (a few lines at a time, then a jump).
The glide moved into one helper (`SmoothScroller`: each notch moves the target further, every frame eases most of the way there) that `PosterGrid`, Home and the title page now share. Shift-wheel still moves a row sideways (that is not smoothed).
Not changed: the scroll bar, the keyboard (PageUp / PageDown / arrows) and Shift-wheel along a row still move in steps.
The first build of this failed (one missing `using` in `PosterGrid`; Visual Studio showed "There were build errors" on pulling it) and was fixed in the next commit.

**Follow-up (owner: a way to scroll on rows): arrows on rows.** Rows (Home's rows, a title page's seasons, episodes, cast and similar titles) only moved sideways with Shift plus the wheel, which is easy to miss.
`ArrowScrollViewer` (the "ArrowRowScroll" style) puts a round arrow at each end of a row while the pointer is over it, only the one that leads somewhere (left once scrolled along, right while there is more), each
click glides about 80% of the row's width; Shift plus the wheel now glides along the row too. `SmoothScroller` gained a sideways mode for this. Home's rows scroll in pixels (not whole posters) so the glide is smooth.
Not done: sideways swipes on a touchpad (WPF does not report them without more work).

**Follow-up (owner: a smoother horizontal swipe).** Two changes: the glide along a row is gentler (it eased a quarter of the way per frame; now an eighth), and the glide of every scroller is now worked out from the time since the
last frame instead of assuming 60 frames a second, so it feels the same on a fast screen. Two-finger sideways swipes on a touchpad (and a tilt wheel) now move the row under the pointer: WPF has no event for them, so the
window's own `WM_MOUSEHWHEEL` message is listened for. Not tested on a touchpad (none here): how far a swipe goes may need tuning.

## 8 -- Sources page

**Status:** Written without a compiler here (no .NET SDK in the sandbox); built and tested by the Windows build server only (see the run for this commit). Not run or seen on screen.

**Context:** What Play opens. The engine (`ArcTV.Core/Sources`) was built and tested first (previous commit); this is the page, ported from the Mac app's `SourcesView`.

**Changes:**
- `SourcesView`: the title on the left (poster, year, logo, facts, description), "Select a source" on the right: quality pills (All, 4K, 1080p, 720p, Other), a sort (Quality, Seeders, Size), a row for each stream (quality box, release name, codec, audio, seeders, size, addon, "Recommended" on the best direct link),
  which addons were skipped or failed (and why), and a "No sources found" page with the reason and Try again.
- Play now opens it: on a movie's page, on a show's page (the first episode), on an episode's card, and on the Home banner for a movie (for a show the banner opens the show to choose an episode).
- Choosing a direct link copies it (so it can be opened in another player) and remembers it as the last source for that title or episode; torrents are listed but cannot be started until the torrent engine exists.

**Tests performed:** see the build run. New: a screen test that opens the page with fake addons (engine tests were added in the previous commit).

**Issues discovered:** the page was pushed once before its screen test, change log and a nullable-pattern tidy-up (a script error that was not noticed before committing); they follow in the next commit.

**Issues fixed:** see above.

**Open:** playing (the built-in player), starting torrents, "play the last source again without asking" and Resume, smart source picking (Plus), Download, a shown "Manage Addons" button (needs Settings).

**Follow-up (owner: make the title on the sources page bigger).** The title column was small: the poster is now 240 x 360 (was 132 x 198, with rounder corners), the title 34 (was 24), the year, facts, description and the "What are sources?" note about a quarter bigger, the logo up to 340 x 110, and the column takes 40% of the width (was 37%).

**Follow-up (owner: remove the scroll bar on the sources page when the window is small).** The title's column on the sources page showed the default Windows scroll bar (a white strip). That column now has no scroll bar (the wheel still moves it, smoothly), and every scroll bar in the app
is now a thin dark one with a rounded handle and no arrows or white track (an implicit `ScrollBar` style in the theme; the handle turns blue-to-purple while dragged). `PosterGrid`'s width sums use the new 10-pixel bar.

**Follow-up (owner: remove all scroll bars).** Instead of the thin dark bar added a moment earlier, no scroll bar is shown anywhere: the implicit `ScrollBar` style in the theme gives every bar no width or height and an empty look (a scroll viewer works the same with its bars out of sight).
Pages, lists and rows still scroll with the wheel, a touchpad and the keyboard, and rows have their arrows. What is lost: dragging a bar with the mouse, and seeing how far down a page is. `PosterGrid` counts no bar in its width sums.

**Follow-up (owner: put the row arrow in the middle of the episode picture).** A row's arrows were centred on the whole row, so on an episode row (picture, then its name and a few lines) they sat well below the picture's middle. `ArrowScrollViewer` has an `ArrowCenter` (how far down the arrows'
middle sits): episodes use the middle of the 158-high picture, the cast the middle of the photo, and the posters on Home and in "You May Also Like" the middle of the poster.

**Follow-up (owner: remove the black bar at the bottom of the app).** The "Arc TV for Windows · dev" text in the bottom-left corner (a development aid since the first commit) sat on an empty strip under the last row of every page; it is gone, and the empty space under Home's last row and under a poster grid is smaller
(8 and 12 pixels instead of 40). The version number will be shown in Settings when that page is built.

**Follow-up (owner: the empty space on the left of the app).** Pages started 44 pixels in from the left (and ended 44 from the right), which looks like a wide empty strip, most of all in a narrow window. The side gutter is now 24 pixels on every page (Home, Movies, TV Shows, Search, My List, title and the banner's text), the
top bar sits 24 in from each side (it was 36), rows that ended 28 from the right now end 24 from it, and the poster grid's width sums count the smaller gutter. Posters per line may go up by one in some window widths.

**Follow-up (owner: make Home's rows come out of the left side as they do on the right).** A Home row had its left gutter as padding on the scroll viewer, so posters scrolled along were cut off at an invisible line 24 pixels in. The gutter is now space at the start of the row itself (on the items), so the row starts 24 in
and the posters slide on out under the window's left edge, as they already do off the right. (Title-page rows already worked this way.)

**Follow-up (owner: the positioning of the Home banner's info).** The info block (title, facts, description, buttons) had 70 pixels under it, but Home's first row overlaps the bottom of the banner by 60, so the buttons almost touched the "Popular" heading: it now has 112. It also had no space on the right, so in a narrow window the description ran off the edge instead of wrapping:
it now keeps 24 pixels from the right edge (and still holds to 600 wide when there is room).

**Follow-up (owner: make the "You May Also Like" posters horizontal).** The row of similar titles on a movie's page now uses landscape cards (300 x 169) showing each title's wide picture (`MetaPreview.WidePicture`: its landscape artwork, else its poster, cropped to fit) with the name under it, instead of tall posters. The row's arrows are centred on those pictures.

**Follow-up (owner: the Home banner's info is still too low).** The banner was 62% of the window's height (at least 500), so its info had to sit near the bottom. The banner is now 78% of the height (at least 580), and the info block sits 150 pixels above its bottom edge (was 112), so the buttons end well above the first row and the first row shows its title and the top of its posters under it at a normal window height. In a short window the info may reach up to the top bar.

**Follow-up (owner: a small logo on the bottom-left of each "You May Also Like" picture).** Each landscape card now shows the title's logo (up to 140 x 44) over its picture's bottom-left corner, on a soft dark shade so it reads on any picture. A title with no logo shows just the picture and its name under it.

**Follow-up (owner: the Home banner's info is still too low, it needs to be top right).** The info block (logo or title, facts, genres, description, buttons) is now at the banner's top right, 120 pixels down (under the top bar) and 24 from the right edge, still up to 600 wide with its text left-aligned inside; the shade that keeps it readable
moved from the left of the picture to the right. The banner went back to 62% of the window's height (at least 500): with the info at the top there is no need for it to be taller. The page dots stay at the bottom right. If "top right" was meant as "top left", it is one setting (`HorizontalAlignment`) and the shade's direction.

**Follow-up (owner: top left, not top right; scale with the window).** The Home banner's info block is back on the left, at the top (under the top bar), with the shade that keeps it readable on the left again. It now scales with the window: its size follows the smaller of how wide and how tall the banner is (measured against a 1300 x 560 banner,
kept between 70% and 160%), and its distance from the top is a fifth of the banner's height (at least 104 pixels, so never under the top bar). The earlier try at "top right" is undone.

**Follow-up (owner: keep the banner's buttons in the exact same spot on every slide).** The info block grew and shrank with what a title had (a tall or short logo, no genres, one line of description), so the buttons under it moved from slide to slide. Each part now has a fixed height whatever it holds: the logo (or title) area 110, the facts line 20, genres 18, the
description 66 (cut off with "..." if longer), so Play, Trailer, More info and My List are in the same place on every slide at a given window size (and scale with it as before).

## 9 -- Player (libmpv)

**Status:** Written without a compiler here (no .NET SDK in the sandbox); built and tested by the Windows build server only (see the run for this commit). Not run or seen on screen, and no video has been played: whether libmpv downloads and loads is unverified until a Windows machine runs it.

**Context:** What choosing a source opens. Ported from the Mac app's player (mpv, same options where they apply) and laid out like it.

**Changes:**
- `ArcTV.Core/Player`: `PlayerModel` (clock, play/pause, seek and skip, volume, speed, audio and subtitle tracks, "loading" while buffering or seeking, the "not ready yet" slate check) and `PlaceholderVideo`; tests in `PlayerTests`.
- `ArcTV.App/Player`: P/Invoke for `libmpv-2.dll` (`NativeMpv`) and `MpvEngine` behind an `IVideoEngine` interface (the tests use a stand-in). If the DLL is missing the player shows a message saying so rather than crashing.
- The picture is drawn by mpv into a child window (`MpvHost`); WPF cannot draw over such a window, so the controls live in a transparent owned window (`PlayerOverlay`) kept over the video. Loading cover: the title's artwork dimmed, the logo in the middle, a spinner under it. Once playing, the logo sits top right (with the episode name for a show).
- Controls: back, -10 / +10, play/pause, time, timeline (click and drag), time left, mute and volume, Audio / Subtitles / Speed menus, full screen; they hide after 3 seconds of no movement. Keys: Space, Left/Right, Up/Down, M, F, Esc.
- Sources page: clicking a direct link now plays it (a torrent is still listed only). `ShellView` hides the top bar while playing.
- libmpv is fetched by `scripts/get-libmpv.ps1` (from shinchiro/mpv-winbuild-cmake; the first run found no LGPL-only asset in its releases, so the standard x86_64 build is used, whose licence still needs checking) by the CI workflow and, on a Windows build, by the project when the file is missing; copied beside `ArcTV.exe`. Not kept in git. `THIRD_PARTY_NOTICES.md` added.

**Tests performed:** see the build run. New: `PlayerTests` (Core) and a screen test that opens the player with a stand-in engine.

**Issues discovered:** none yet; the download source and the overlay-window approach are untested on a real PC.

**Issues fixed:** n/a.

**Open:** torrents (MonoTorrent), Resume / Continue Watching and reporting progress to the account, next-episode, the Episodes / Player / Sources pills, subtitle styling, a placeholder-slate prompt flow, picture-in-picture.

## 10 -- Settings > Addons

**Status:** Written without a compiler here (no .NET SDK in the sandbox); built and tested by the Windows build server only (see the run for this commit). Not run or seen on screen.

**Context:** The sources page said "No sources found" because the account's only addons were Cinemeta and OpenSubtitles, and Windows had no page to add a stream addon. Ported from the Mac app's Settings > Addons.

**Changes:**
- Core: `SyncedAddon.RawManifest` (the manifest exactly as stored, read in a second pass), `AddonsApi.SaveAddonAsync` / `RemoveAddonAsync` / `SaveBody` (POST and DELETE `/user/addons`), `AddonInstall` (fetch and check a pasted link), `IAddonStore` + `AccountAddonStore`, and `AddonsModel` (list, install, switch on/off, remove, messages, "Provides: ...").
- App: `SettingsView` (Settings now opens it instead of "coming soon"): add-by-link box, a card for each addon with version, description, what it provides, an on/off switch (`SwitchToggle` style) and an inline "Remove from every device?" confirmation. Changing addons clears the cached sources and browse pages and reloads Home.
- Tests: `AddonsTests` (install order and place, bad link, switching keeps the manifest, remove, a failing account, save body, provides text) and a screen test for the page.

**Tests performed:** see the build run.

**Issues discovered:** the Settings page holds only Addons so far.

**Issues fixed:** n/a.

**Open:** the other Settings sections (profiles, blocked genres, subtitles, account, version), reordering addons, an install confirmation that shows what the addon offers first.

## 11 -- Settings (all the places that do not need Plus)

**Status:** Written without a compiler here; built and tested by the Windows build server only (see the run for this commit). Not run or seen on screen.

**Context:** "Build all the settings, not just addons." Ported from the Mac app's `SettingsView` / `SettingsPanes`. The four Plus pages (Arc TV Plus, Plus settings, Recommendations, Your stats) need the Plus account and recommendation engine and are not built yet: they are in the sidebar marked "Soon".

**Changes:**
- Core (`ArcTV.Core/Settings`): `AccountSettings` (read tolerant of missing keys; an empty language is sent as null), `SettingsApi` (GET/PUT `/user/settings`), `SettingsModel` (change shown at once, saved, put back if the save fails; blocked genres, row visibility and order), `LanguageChoices`, `HomeRowOrder`, `SubtitleSize` / `SubtitleColor`, `BlockedGenres`, `PlaybackPreferences`. `LocalSettings` now keeps the subtitle size and colour on this PC and no longer drops one value when another is written.
- Blocked genres are applied in `HomeModel` (rows and banner), `BrowseModel` (Movies, TV Shows) and `SearchModel`; row order and visibility are applied to Home. Changing blocked genres makes Movies and TV Shows start over.
- Player: `MpvEngine` follows subtitles on/off, the subtitle and audio language, and subtitle size and colour.
- App: `SettingsView` (grouped sidebar, the chosen place on the right) with panes Account, Addons, Home Rows, Blocked Genres, Player, Subtitles (with a preview), Audio and Updates (current version, check for updates, notes, download link).
- Tests: `SettingsTests` (parse and send, save and put back, genres, row moving, languages, subtitle style, local choices) and a screen test that opens every place.

**Tests performed:** see the build run.

**Issues discovered:** the Mac app's torrent buffer and storage limit settings (Player) and the Mac subtitle preview picture are not ported (no torrents yet; the preview is a plain frame). The "Auto play next episode" switch is saved to the account, but next-episode playback is not built.

**Issues fixed:** n/a.

**Open:** the four Plus pages, profiles (Watching as, kids genres), torrent buffer and storage, the next-episode behaviour.

**Follow-up (owner: scale Settings as the window size changes).** In a narrow window the list of places filled the whole width and the chosen page was out of sight. `SettingsView` now scales with the window (the smaller of its width against 1300 and its height against 800, between 0.6 and 1.6, as the Home banner does), and when the window is narrow (under about 760 once scaled) the list of places goes above the page instead of beside it. The page scrolls as one. Not seen on screen.

## 12 -- The Plus pages

**Status:** Written without a compiler here; built and tested by the Windows build server only (see the run for this commit). Not run or seen on screen, and nothing was tried against a real Plus account or Stripe.

**Context:** "Build the Plus pages": the four Plus places in Settings (Arc TV Plus, Plus settings, Recommendations, Your stats), ported from the Mac app's `PlusViews`, `RecommendationsPane` and `ArcTVCore` (`PlusAccount`, `Feedback`, `WatchStats`, `RecommendSummary`).

**Changes:**
- Core (`ArcTV.Core/Plus`): `PlusStatus` (tolerant read), `PlusPlan`, `PlusCopy`, `CheckoutLink`, `FeedbackItem` / `Rating`, `PlusApi` (GET `/user/plus`, POST `/user/plus/checkout`, POST `/user/plus/cancel`, GET and DELETE `/user/feedback`), `RecommendConfig`, `RecommendationSummary` (points as the engine counts them), `WatchStats` and `HistoryApi` (GET `/user/history`, paged, up to 5,000 rows), and `PlusModel` (status, ratings, stats, checkout that only opens an https address and then asks every 4 seconds for up to 10 minutes until Plus is on, cancel, remove and reset ratings with put-back on failure).
- App: `ArcTvPlusPane` (status, renewal and cancel with an inline confirmation, what Plus adds, plan cards, steps), `PlusSettingsPane` (Smart source picking, kept on this PC), `RecommendationsPane` (Liked / Not for me lists with View and Remove, what shapes your picks, how it works, reset), `StatsPane` (total, tiles, movies-or-shows bar, 13-week map, busiest day). Without Plus the three Plus-only pages show a lock note and their sidebar rows are tagged "Plus"; with the paywall off everyone is active, as on the other apps.
- `LocalSettings` keeps the Smart picking switch.
- Tests: `PlusTests` (status read, summary points, stats from a fixed history, durations, week change, activity grid, ratings, put-back, checkout watch, cancel) and a screen test that opens all four pages.

**Tests performed:** see the build run.

**Issues discovered:** the Plus pages are only half of Plus: Like / Not for me buttons on titles, the Picked for you row (the recommendation engine), profiles, smart source picking itself and the Plus popups are not built. Recommendations lists ratings made on other devices (and Remove works), but ratings cannot yet be made from Windows. The rated-title list shows an initial, not a poster.

**Issues fixed:** n/a.

**Open:** Like / Not for me, the Picked for you engine and row, profiles, smart source picking, Download, the Plus popups.

**Follow-up (owner: show the Liked and Not for me lists as posters, in a row, with the picture).** The two lists were plain rows with a letter. They are now a sideways-scrolling row of the same poster cards Home uses (a click opens the title), with a small cross on each to take the rating off. The account keeps the rating but not the picture, so `PlusModel.LoadPostersAsync` looks up each rated title's poster from Cinemeta (four at a time, announced together when done; a title whose lookup fails keeps its name on the card). Also fixes the CI build: `PlusPane.Tag` hid `FrameworkElement.Tag` (warning CS0108, an error on the build server), renamed `Chip`. Not seen on screen.

## 13 -- Like and Not for me

**Status:** Written without a compiler here; built and tested by the Windows build server only (see the run for this commit). Not run or seen on screen, and not tried against a real account.

**Context:** First of three requested together (Like / Not for me, Continue Watching and Resume, Picked for you). Ported from the Mac app's `AppModel.rate`.

**Changes:**
- Core: `PlusApi.SetFeedbackAsync` (POST `/user/feedback`), `IPlusStore.SetFeedbackAsync`, and `PlusModel.RateAsync` / `RatingOf` (only with Plus; the same rating again takes it off, the other one changes it; shown at once and put back if the account could not be reached; the poster is kept for the Recommendations page).
- App: `DetailView` has Like and Not for me buttons next to My List and Mark as watched, shown once the account has Plus, ticked for what the title already has.
- Tests: rating, un-rating, switching, rollback, and no Plus.

**Tests performed:** see the build run.

**Issues discovered:** none yet.

**Issues fixed:** n/a.

**Open:** the same buttons on a poster's menu (the Mac has a three-dot menu); Picked for you (uses these ratings).

## 14 -- Continue Watching and Resume

**Status:** Written without a compiler here; built and tested by the Windows build server only (see the run for this commit). Not run or seen on screen; never tried against a real account or a real video.

**Context:** Second of the three requested together. Ported from the Mac app's `AppModel` (`loadContinueWatching`, `reportProgress`, `resumeItem`) and `Account.swift`.

**Changes:**
- Core (`ArcTV.Core/Continue`): `ContinueItem` (progress, "S1 E3" label, `ToTarget`), `ContinueApi` (GET `/user/continue-watching`, POST `/user/watch-progress`, DELETE `/user/continue-watching`), `IContinueStore` / `AccountContinueStore`, and `ContinueModel` (reports with the Mac's rules: nothing under 5 seconds in or for a length under 30 seconds, finished at 92 percent; a finished movie is marked watched once; the row is read again after each report; remove with put-back; `ResumeItem` and `Resume`, which start a target from where it was left unless it is nearly finished or already has a place).
- Player: `PlayerView` reports the position every 30 seconds and once more on leaving.
- Home: a "Continue Watching" row above the other rows (`ContinueRow`): poster, a progress line, the episode for a show, a cross to take it off, a click plays it from where it was left.
- Title page: Play says "Resume" (and, for a show, starts the episode Continue Watching holds, from where it was left); the sources page starts any target that matches Continue Watching from its place.
- Tests: `ContinueTests`.

**Tests performed:** see the build run.

**Issues discovered:** playing a title still goes through the source list (the Mac's "play the last source again without asking" is not ported); episode ticks and the "next episode after the furthest watched" rule (which need the watch history) are not ported: a show's Play starts the episode in Continue Watching, else the first.

**Issues fixed:** n/a.

**Open:** play the last source without asking, episode watched ticks and part-way bars, next-episode logic from the history, Picked for you.

## 15 -- Picked for you

**Status:** Written without a compiler here; built and tested by the Windows build server only (see the run for this commit). Not run or seen on screen; never tried against a real Plus account.

**Context:** Third of the three requested together. A port of the Mac app's recommendation engine (`RecommendSignals`, `RecommendFeatures`, `RecommendPreferences`, `RecommendScoring`, `RecommendRotation`, `RecommendEngine`, `PickedRow`, `FeatureCache`) and of `AppModel.schedulePicked`, with the same numbers (they come from the Fire TV and web apps, so every Arc TV app ranks the same way).

**Changes:**
- Core (`ArcTV.Core/Recommend`): interactions from My List and ratings (like 5, Not for me -5, finished 2, saved 1; the strongest applies, a dislike stays negative), normalised features (genres, directors, cast), taste preferences, cosine scoring with renormalised categories, reasons ("Because you liked X", "More from directors you enjoy"), the diversity step (one title explains at most 3 picks), composition (the best 5 stay, the rest follow the profile's genre split by Sainte-Lague, drawn with a seeded generator so a refresh changes most of them), the shortlist steps, the popular fallback, a feature cache (30 days, 800 titles) and loader, `PickedRow`, five-day removals (kept in `picked.json` and on the account, `GET`/`POST /user/picked-dismissals`), and `PickedModel` (works the row out again when ratings, My List, Continue Watching or the rows on offer change; a title rated, finished or started disappears at once).
- App: `PickedRowView` on Home under Continue Watching (poster, name, the reason, a cross to take it out); the shell reads what it needs (Plus status, ratings, removals, My List, Continue Watching) and refreshes the row; "Refresh Home" and changing addons start a new draw.
- Tests: `RecommendTests` (signals, features, preferences, scoring, reasons, diversity, composition, seeded generator, fallback, a personal run, the row, removals, the cache and loader, and the model).

**Tests performed:** see the build run.

**Issues discovered:** the feature lookups use Cinemeta only (as on the Mac); the row appears once Home, My List and the ratings have loaded. The Mac's three-dot card menu (Like, Not for me, Not now on a poster) is not ported: Like and Not for me are on the title page, "not now" is the cross.

**Issues fixed:** n/a.

**Open:** profiles (each profile has its own picks), the card menu, a loading skeleton for the row.

**Follow-up (owner: horizontal pictures for Continue Watching; the Picked for you posters did not lift under the pointer like the other rows).** Continue Watching cards are now wide (320 x 180) and show the title's landscape artwork (its poster when there is none); the progress line, episode label and cross are as before. The cards `ContinueRow` and `PickedRowView` make in code did not have the small lift the poster template gives the other rows: `HoverLift` (a 6 percent grow on entering, settling back on leaving, with the same easing and timings) is now on both. Not seen on screen.

**Follow-up (owner: extend the Home banner down and fade it into black, the cut-off was still visible; and the title logo at the top left of each Continue Watching picture).** The banner is now 140 pixels taller than the part with the words on it (`HeroBanner.FadeExtra`): the picture carries on under them and fades into the page's own colour more slowly (the fade now starts a little higher and is fully the page colour by 97 percent of the height), the rows start where they did, and the words and buttons keep their place and size (they follow the height without the extra). A 6-pixel strip of the page's own colour at the very bottom makes sure no row of picture shows through at an uneven window height (the thin line in the screenshot). Continue Watching cards show the title's logo at the top left on a soft dark corner: the account keeps no logo, so `ContinueModel.LogoLookup` asks Cinemeta for each title's logo (four at a time, announced together, a failure leaves that card without one). Not seen on screen.

**Fix (owner: the banner's dots could not be clicked).** When the banner grew by `FadeExtra`, the dots stayed at their old distance from its bottom, which put them under the rows (the rows overlap the banner's lower part and sit on top). The dots now sit `FadeExtra` higher, where they were on screen before. Not seen on screen.

**Fix (owner: after using the dots to change the banner slide, the fade into Continue Watching did not match the page colour).** The banner sat on the lighter card colour (`SurfaceBrush`, #1C1C20) instead of the page colour (#08080A). Each slide fades in over 450 ms, and a picture that is still loading shows what is behind it, so for that time the bottom of the banner showed the lighter colour above the darker page, with a hard edge where the banner ends. The banner now sits on the page colour (`BgBrush`), so a slide changing, fading in or loading never shows anything but the page colour at the bottom. Not seen on screen.

**Follow-up (owner: the fade should not start so high, it should only hide the cut-off).** The banner's fade now starts 58 percent of the way down (it was 26) and is the page colour from 99.5 percent, so the picture stays clear for most of its height and only its lower part melts into the page. The extra banner height, the page-colour base and the 6-pixel bottom strip are unchanged. Not seen on screen.

**Follow-up (owner: fewer, bigger posters across the screen).** Posters (Home rows, Movies, TV Shows, Search, My List, Liked / Not for me, Picked for you) are 288 x 432 pixels instead of 192 x 288 (half as big again; first made 240 x 360, then the owner asked for bigger), so about a third fewer fit across a window. They come from one shared template, so every list changed together; the arrows on the rows moved down to the new middle (226) and the picture is decoded at 600 pixels wide so it stays sharp. The wide Continue Watching cards are unchanged. Not seen on screen.

**Follow-up (owner: bigger row titles on Home).** The titles above the Home rows (the addon rows, Continue Watching and Picked for you) are 26 instead of 20. Not seen on screen.

**Follow-up (owner: bigger scroll arrows on Home).** The round arrows at the ends of a row are 60 pixels across (were 44) with a larger arrow inside (24 instead of 16). They are one shared style, so every sideways row got them, not just Home (title pages, My List's rows, Recommendations); their height is read from the style where they are placed, so they still centre on the pictures. Not seen on screen.

**Follow-up (owner: Like and Not for me as a thumbs up and down, not text).** On a title's page the two buttons show a thumbs up and a thumbs down (Segoe MDL2 Assets E8E1 and E8E0, size 20) instead of words; the one that is the title's rating takes the pill's selected look (no tick any more), and each has a tooltip and a screen-reader name ("Like", "Not for me"). Not seen on screen: the icons come from the Windows icon font, which this sandbox cannot show.

**Follow-up (owner: "You May Also Like" always at the bottom of the title page).** The page's content is now a two-row grid kept at least as tall as the window (`PageBody.MinHeight` follows the scroll area's height, less the 48 pixels left under it): the title, buttons, seasons and cast take the top row and "You May Also Like" sits at the bottom of the rest, so on a short page it is at the bottom edge of the window and on a long one it simply follows the content. Not seen on screen.

**Follow-up (owner: make the top of the title page bigger).** The title is 58 (was 44), the logo can be 520 x 145 (was 400 x 110), the year and length 21 (was 16), the genres 17 (was 13), the description 20 (was 15) and up to 840 wide, and the buttons 60 high with 18-point words (were 48 and 14), with wider spacing; the thumbs are 26. The column holding the text is wider (1000, was 800) so the bigger text does not wrap early. Seasons, Cast and You May Also Like are unchanged. Not seen on screen.

**Follow-up (owner: bigger "You May Also Like" heading).** The headings Seasons, Cast and You May Also Like on the title page are 30 (were 22); the three are kept the same size so they match. Not seen on screen.

## 16 -- Playback behaviour: last source, smart picking, next episode, episode ticks

**Status:** Built and tested by the Windows build server (the xUnit tests and the screen tests); not run on a screen here (see below). Ported from the Mac app's `SourcesView.load`, `EpisodeProgress`, `DetailView` and `MPVPlayerScreen`.

**Context:** Choosing a source every time, no next episode, and no sign of what had been watched were the biggest gaps once the player worked.

**Changes:**
- `AutoPick.Choose` (Core) decides when the source page skips itself: the next episode of a binge takes the best source that plays; otherwise the source last used for this title or episode (only a direct link, since torrents cannot play yet); otherwise, with Plus and smart source picking on, the best that plays; "Sources" on the title page always shows the list. `SourcesModel` takes the rule, stays "loading" while it hands over (so the list never flashes), and raises `AutoChose`. A page picked this way is not put on the Back stack, so Back from the video goes to the page before it, and coming back to a list does not reload it (which would start the last source again).
- Next episode: `MetaDetail.EpisodeAfter` (same season, else the next season, specials skipped). The player shows a Next episode button in the last minute and, when an episode ends and Auto Play Next Episode is on, starts the next one; it takes the player's place, with its source picked without asking.
- Episode ticks: `EpisodeProgress` (statuses from the watch history, next episode after the furthest watched, entries for the list). Each episode card has a tick (green once watched, click to change) and a bar for a part-watched one; "Mark season watched"; Play and Resume start the episode after the furthest watched. `ContinueModel.SetEpisodesWatchedAsync` reports each episode as finished (or back to the start) one after another and puts back the Continue Watching pointer the reports would have cleared.

**Tests performed:** New xUnit tests for the episode order, history statuses, next episode, the auto-pick rules, the model's hand-over, and marking watched or not (including the restored pointer and a failure). Build and tests run on the Windows server; nothing was run on a screen from this sandbox.

**Issues discovered:** The sandbox cannot reach the .NET installer (blocked by the network policy), so this change was not compiled locally.

**Issues fixed:** None yet.

## 17 -- Profiles: Who's watching, manage profiles, switching

**Status:** Built and tested by the Windows build server (xUnit and the screen tests); not seen on a screen from this sandbox. Ported from the Mac app's `Profiles.swift`, `ProfilesView.swift` and the profile part of `AppModel`.

**Context:** A Plus account can have up to five profiles, each with its own My List, Continue Watching, ratings, settings and addons. Windows only ever used the account's own profile.

**Changes:**
- Core: `Profile`, the sixteen avatars (the Mac app's pictures, turned into PNGs because WPF cannot read WebP), `ProfilesApi` (list, create, edit, delete, check a PIN; the PIN of a locked profile travels in `X-ArcTV-Pin`), `ProfilesModel` (reading the list, PIN check with words for each outcome, name rules). `AccountModel` now knows the profile in use: library calls carry `X-ArcTV-Profile` (calls about the account itself, such as the profile list and Plus, do not), and `Scope` names what this PC's local copies (recent searches, remembered sources, the Picked for you memory) are filed under, so the account's own profile keeps the names it had.
- App: `ProfilePickerView` ("Who's watching?", the PIN question, Manage Profiles with add and edit, delete asking twice). `MainWindow` reads Plus and the profiles after sign-in, shows the picker when there is more than one profile, and starts the whole app (`ShellView`) again for the profile chosen, so nothing of the last profile stays in memory. The account menu has Switch profile (Plus accounts) and the account button shows the profile's picture and name. A kids profile hides the kids list of genres on top of the account's own blocked ones.

**Tests performed:** xUnit: avatars, PINs, account-level paths, the model's loading, PIN outcomes and edits, the API calls and headers (the profile header on library calls and not on profile calls, the PIN header, kept/removed/set PIN), the profile in use and its scope, the kids genres. Screen tests: the picker lists the profiles, opens one without a PIN at once, refuses a wrong PIN and opens on the right one, and adds a profile from Manage Profiles.

**Issues discovered:** The window rebuilt the whole app each time the account announced `IsSignedIn`, which it also does whenever the sign-in token is renewed, so the app could start over by itself (back to Home, in the middle of a video) about when a token ran out.

**Issues fixed:** `MainWindow` now only reacts when signing in or out actually changes. The kids profile's genre list and the profile picker could not be tried against a real account.

**Follow-up (owner: player controls).** Play / pause is 68 pixels (was 44) with a 30-point symbol; Back and the two 10-second skips are 64 pixels (symbols 26 and 19); the title logo at the top right can be 560 x 150 (was 280 x 56), its words 40 and the episode line 20; the bottom controls and the pills sit lower (28 pixels from the bottom became 8, the pills 4 under the bar). `Timeline` starts and ends 7 pixels in, so its round handle is never cut off at either end (the volume bar showed half a handle at full volume); this also shifts where a click lands on the position bar by the same 7 pixels at each end. After pausing or playing, a round symbol (pause or play) shows in the middle for about two seconds, then fades; it does not show when the video first starts. The episode line on the player read "S1 E3 S1 E3 Name" after resuming from Continue Watching, because the player reports "S1 E3 Name" as the episode title and `ContinueItem.ToTarget` put the number in front again; it now only adds the number to a bare name. Not seen on screen.

**Follow-up (owner: the loading cover showed the movie's name in words, not its logo).** A title started from Continue Watching (and some rows) carries only its poster and backdrop, so the sources page, the loading cover and the top-right logo had no logo to show and used the words. `ShellView.LogoOfAsync` now finds it (Continue Watching's own lookup if it has it, else Cinemeta, remembered for the session) and the sources page and the player put it in place of the words when it arrives. Not seen on screen.

**Follow-up (owner: resuming from Continue Watching with the remembered source just kept loading in the player).** The cause is not known: the code path for a remembered source is the same as choosing it by hand except that the source list is skipped, and nothing in it explains a stream that never starts (a link that stopped working, or a seek to the resume point on a server that cannot jump, are the likely ones). So that it can never again be a dead end: after 45 seconds without a first picture the player says the source isn't starting (`PlayerView.StartTimeout`), the failure message offers "Choose another source", and a new Sources button among the player's pills does the same at any time: the player gives way to the title's source list (from the point reached, with the list forced) and Back from that list goes where Back from the player would have. Not seen on screen, and the real cause is still to be found.

**Follow-up (owner: the player's controls were still far too high; clicking an open Audio / Subtitles / Speed pill should close its list).** The controls are at the bottom of the overlay window, so they were high because the overlay was short of the picture. It was sized in device-independent units from `ActualWidth` / `ActualHeight` and `PointToScreen`, which does not hold when the window and the overlay are on screens of different scaling (the app is per-monitor DPI aware). It is now laid over the real rectangle of the video's own window in screen pixels (`GetWindowRect` / `SetWindowPos`, only when it differs), falling back to the page's own position. This is a diagnosis from the screenshots, not something seen running. The lists close when their own pill is clicked again: the click that lands on a pill has already closed the popup (a click outside does), so a list that closed less than a quarter of a second ago for the same pill counts as showing.

**Follow-up (owner: the player's controls were still too high, with the overlay now confirmed to cover the whole picture).** The real cause, found from the owner's screenshot with the window's bottom edge in it: the controls' panel sat in the bottom row of the overlay without being told to sit at the bottom of it. That row is 230 pixels tall (the dark fade behind the controls is 230 tall and shares it), a stack panel left to stretch lays its contents out from the top, so the controls started at the top of the row and about 120 pixels of empty row were left under the pills. It is now `VerticalAlignment="Bottom"`. The earlier change that lays the overlay over the video window's real rectangle in pixels stays (harmless, and it removes a possible mismatch between screens), but it was not the cause. Not seen on screen.

**Follow-up (owner: the app crashed on an unhandled TaskCanceledException from a web request).** `ApiHttp.SendAsync` puts a time limit on every request by cancelling it, and a request that hit the limit threw a `TaskCanceledException`. The rest of the code treats cancellations as "someone chose to stop, ignore it" (its `catch` filters let `OperationCanceledException` through), so a plain timeout went all the way up, and out of an `async void` handler, unhandled. A timeout is now an `HttpRequestException` ("The server took too long to answer."), which every caller already treats as a failure, while a cancellation the caller asked for stays a cancellation. As a safety net, `App` now marks an unhandled network exception (`HttpRequestException`, `OperationCanceledException`, `ApiException`) as handled, so a background request that fails can never close the app. The exact call that timed out in the owner's run is not known (the screenshot shows only the shared send). Test: a server that never answers gives a failure, and a cancel gives a cancellation. `ArcTV.Core.Tests` can now see Core's internals for that.

**Follow-up (owner: the player's controls now sit right at the bottom; bring them up a bit).** With the controls finally at the bottom of the window, the gap under the pills went from 8 to 32 pixels. Not seen on screen.

**Follow-up (owner: a bigger top bar).** The bar is 124 pixels tall (was 96): the logo 52 (was 38), the menu buttons' words 19 (was 15) with more room round them, the profile button 58 tall with a 46-pixel picture and 18-point name (was 44, 36 and 15). Every page that starts below the bar moved down the same 28 pixels (Home without a banner, a title's page, the source list, Movies and TV Shows, Search, My List, Settings, and the banner's own words) so nothing sits under it. The Home rows still start where they did under the banner. Not seen on screen.

**Follow-up (owner: Continue Watching just loads in the player; picking the source by hand always works).** Likely cause (a diagnosis, not seen running): the remembered source was found again in the fresh list by its release name, and addons often give every quality of a film the same first line, so the first stream with that line was started, which can be a huge 4K file or one that never starts, not the one that was chosen. `LastSource` now remembers each stream's whole write-up (`SourceItem.Signature`: name, title, description, file name, size, file number; not its link, which can change) and finds exactly that; the torrent hash and release name are only used when they fit one stream, and when they fit several nothing is guessed and the list is shown, where the choice is made again and remembered properly (older remembered entries mend themselves that way). Test: three qualities with one release name, remembered and found again in another order with other links; an old name-only entry fits three and picks none. The 45-second "isn't starting" message and the player's Sources button from before remain for a source that still hangs.

**Follow-up (owner: the player's episode line still said "S1 E3 S1 E3 Name"; resizing the window made the player stutter and shake).** (1) The earlier fix only stopped *adding* a second number; but the doubled line had already been sent to the account (the player reports the episode line it shows) and came back from Continue Watching with both numbers. `ContinueItem.ToTarget` now drops every season-and-episode number at the front of what the account holds and puts the one true label on, and the next report saves it clean, so old doubled entries mend themselves. (2) The controls are a separate transparent window kept over the picture from every layout pass, so while the window was dragged to a new size that window chased the picture through every size and the whole player shook. Now, as soon as the main window's size changes the controls fade out (opacity 0) and are laid over the picture once, 180 ms after the last change; moving the window (a position change with no size change) also moves them, which it did not before. Not seen on screen: the resize fix is a diagnosis, and if the player still stutters while resizing, the picture itself (mpv redrawing at each size) is the next thing to look at.

**Follow-up (owner: remove Copy link from the source list).** The small "Copy link" button on each source row is gone, and a click on a row (which was only told apart from a click on that button) plays it directly. The link is still copied when the page has no player to play it. Not seen on screen.

**Follow-up (owner: more space between Home's rows; bigger title text under the posters).** The gap under each row is 44 pixels (was 16) for the addon rows, Continue Watching and Picked for you. The title under a poster is 17 (was 13) on every poster (the shared poster design, so Movies, TV Shows, Search and My List too) and under Continue Watching's and Picked for you's cards; the reason line under a Picked for you card is 14 (was 11). Not seen on screen.

**Follow-up (owner: Settings > Addons in two columns, not one).** The addons are in a two-column grid (messages such as "Loading your addons…" and "No addons yet" still take the full width), and the Addons page is allowed to be wider than the other settings pages (1180, the others stay 700) so each card keeps about the width it had. Not seen on screen: a narrow window gives each card less room for its description, which is trimmed to two lines.

**Follow-up (owner: Continue Watching still did not play; start the recommended source, only for Continue Watching).** A click on a Continue Watching card now asks for the best source that plays (the one marked Recommended in the list: the page's `AutoPick` rule, the same one the next episode uses) and starts it from where it was left, without looking for the source used last time. Everywhere else (a title's Play button, an episode, Resume on a title's page) the remembered source and the list behave as before. If no source can play, the list is shown. Not seen on screen.

**Follow-up (owner: Continue Watching pictures bigger; clicking one still only loads in the player and never starts).** (1) The cards are 440 x 248 (were 320 x 180), the logo on them up to 180 x 64, the row's arrows at the new middle. (2) The hang: with the recommended source now chosen for Continue Watching, the source is not the cause (picking the same kind of source by hand works), so the difference is in how the video is started. The one real difference is that the player used to be made at a click, and for an automatic pick it was made inside the source page's own loading code. The automatic pick now waits until the window is idle (the list page has finished its work) and then starts the video, as a click would. This is a guess: I could not see the cause. So the player now keeps a log: `player.log` (what the app did: the address of the server, where in the video it starts, the answers mpv gave when told to start, when the first picture arrived or the end of the file with its reason, and when the 45-second wait ran out) and `mpv.log` (mpv's own log of the same run), both in `%LOCALAPPDATA%\ArcTV`. Links are never written down, only the server's address. If a video still hangs, those two files say where it stopped.

**Follow-up (owner: keep the top bar inside the window; Continue Watching starting a source by itself clearly does not work, just show the source list).** (1) Continue Watching cards now ask for the source list (`ChooseSource`), as every earlier route did and as you said works; the video starts from where it was left once a source is picked. Starting the remembered or the recommended source by itself stays for the next episode of a binge and for Plus's smart picking; the player and mpv logs from before stay, so a hang can still be looked into. (2) The top bar was three columns that grew with the bigger buttons until the profile button left the window in a narrow one. The columns are now logo, places (centred in what is left) and profile button, and `ShellView.FitBar` shrinks the whole bar (not below half size) until it fits the window's width. Not seen on screen.

**Follow-up (owner: the arrows on Settings > Home Rows were empty squares).** The move up / move down buttons used the ghost button style, whose own padding leaves no room in a 32-pixel square, so the arrows were cut away. They are now the round 40-pixel button the season numbers use, with a chevron from the Windows icon font (up and down), a tooltip, and dimmed when the row cannot move that way. Not seen on screen.

## 18 -- Home polish: saved rows, top bar that hides, card menu

**Status:** Built and tested by the Windows build server (the xUnit tests and the screen tests); not seen on a screen from this sandbox. Ported from the Mac app's `HomeCache`, the loading in `AppModel.loadHome` and `CardMenu.swift`.

**Context:** What was left of Home's polish: it started empty every time, the top bar always sat over the page, and a poster had no menu.

**Changes:**
- `HomeCache` (Core): the last rows, up to 30 titles each, in `home-<account>[-<profile>].json` in the app data folder, thrown away after a week. `HomeModel` shows them at once as the first thing it does (state Loaded, banner picked) and replaces them with the first fresh batch that arrives, then saves the fresh rows when loading is done; if nothing fresh arrives the saved rows stay on screen and in the file.
- The top bar (`ShellView`) slides up and fades out while a page is scrolled down (a vertical scroll of more than 4 pixels, past the top 40) and comes back on scrolling up, near the top of the page, on moving to another page, coming back from a video, or putting the pointer in the top 24 pixels of the window. Rows that scroll sideways do not count.
- The card menu (`CardMenuView`): right-click a poster, a Continue Watching card, a Picked for you card, the banner's buttons or a card in You May Also Like. The title's picture and logo (looked up when missing), a big Play (or Resume from the time reached), My List / Watched / Like / Not for me (the last two with Plus) that stay open and show their state, then Remove from Picked for you (on that row), View details, Remove from Continue Watching and Choose source. Resume and Choose source open the source list (as Continue Watching does), Escape or a click outside closes it. Cards made in code are marked with the attached property `CardMenu.Item`.
- The banner's Play, Trailer, More info and My List buttons were already in place from earlier.

**Tests performed:** xUnit: the cache keeps rows per account, trims them, forgets them after a week and survives a bad file or nothing to save; `HomeModel` shows the saved rows before any addon has answered, replaces them with the fresh ones and saves those, and keeps the saved rows when nothing fresh arrives. The menu and the bar's sliding are screen features: they compile and the shell opens in the screen test, but nobody has seen them.

**Issues discovered:** None yet.

**Issues fixed:** None yet.

**Follow-up (owner: the right-click card menu a bit bigger).** The menu panel is drawn 1.3 times its old size (a layout transform on the whole panel, so the picture, buttons and words all grow together); in a window too small for that it shrinks to fit (not below 0.6), and its height is limited to the window's. Not seen on screen.

