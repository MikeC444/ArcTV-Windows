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

