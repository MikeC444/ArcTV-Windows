<h1 align="center">Arc TV for Windows</h1>

<p align="center"><b>Your streaming, your way, on your PC.</b><br>
A native, Netflix-style streaming app for Stremio-protocol addons, built with C# and WPF.</p>

> **Work in progress.** The foundations are in place; sign-in, Home and the player are being built, following the [Mac app](https://github.com/MikeC444/ArcTV-Mac).

Arc TV is a media center for Windows 10 and 11. Content comes from **Stremio addons** rather than a fixed catalog, so you choose the addons you want, the same
way Stremio itself works. It is a native Windows app (not a web page in a window) and uses the same account as the other Arc TV apps.

## 📱📺 Arc TV everywhere

| | |
|---|---|
| 🪟 **Windows** | this repository |
| 💻 **Mac** | [ArcTV-Mac](https://github.com/MikeC444/ArcTV-Mac) |
| 📺 **Fire TV / Firestick / Android TV / Google TV** | [ArcTV-AndroidTV](https://github.com/MikeC444/ArcTV-AndroidTV), also home of the account backend (`server/`) |
| 📱 **Android phones and tablets** | [ArcTV-MobileAPK](https://github.com/MikeC444/ArcTV-MobileAPK) |
| 🌐 **Web** | [web.arctv.org](https://web.arctv.org) ([ArcTV-Web](https://github.com/MikeC444/ArcTV-Web)) |

## 🧪 Trying a build

Every push is built on GitHub (see **Actions**). Open a run, download the `ArcTV-win-x64-<branch>-build<N>` artifact, unzip it and run `ArcTV.exe`. It carries
everything it needs. The app is not signed, so Windows may show "Windows protected your PC": choose **More info**, then **Run anyway**.

## 🛠️ For developers

Needs Visual Studio 2022 (the **.NET desktop development** workload) or the .NET 8 SDK.

```
git clone https://github.com/MikeC444/ArcTV-Windows
cd ArcTV-Windows
dotnet test ArcTV.sln                  # the tests (ArcTV.Core)
dotnet run --project src/ArcTV.App     # run the app (or open ArcTV.sln in Visual Studio and press F5; if Visual Studio says a class library cannot be started, right-click **ArcTV.App** in Solution Explorer and choose **Set as Startup Project**)
.\scripts\try-branch.ps1 <branch>      # fetch, build and open any branch
```

- `src/ArcTV.Core`: everything that is not on screen (ported from the Mac app's `ArcTVCore`)
- `src/ArcTV.App`: the WPF app
- `tests/ArcTV.Core.Tests`: xUnit tests

Arc TV only plays what an addon provides and does not host or supply any content. It is not affiliated with or endorsed by Stremio.
