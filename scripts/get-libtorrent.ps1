# Downloads ctorrent.dll (a small C face on libtorrent, the torrent engine the Mac app uses) and the DLLs it needs, next to the app's project, where the build copies them beside ArcTV.exe.
#
#   powershell -ExecutionPolicy Bypass -File scripts\get-libtorrent.ps1          (-Force to fetch the newest again)
#
# The Build libtorrent workflow (.github/workflows/native.yml) builds them and publishes libtorrent-win-x64.zip as the "native-libtorrent" pre-release of this repository. Nothing happens if
# ctorrent.dll is already there. A download that fails prints why and exits with 1; the app still builds, and torrents then play with MonoTorrent (built into the app) until the files are in place.
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\src\ArcTV.App\libtorrent'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$target = Join-Path $Destination 'ctorrent.dll'
if ((Test-Path $target) -and -not $Force) {
    Write-Host "ctorrent.dll is already in $Destination (use -Force to fetch the newest)"
    exit 0
}

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
    $repo = if ($env:GITHUB_REPOSITORY) { $env:GITHUB_REPOSITORY } else { 'MikeC444/ArcTV-Windows' }
    $url = "https://github.com/$repo/releases/download/native-libtorrent/libtorrent-win-x64.zip"

    $work = Join-Path ([IO.Path]::GetTempPath()) ('libtorrent-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    $archive = Join-Path $work 'libtorrent-win-x64.zip'
    Write-Host "Downloading $url ..."
    Invoke-WebRequest -Uri $url -OutFile $archive -Headers @{ 'User-Agent' = 'ArcTV-Windows-build' }

    Expand-Archive -Path $archive -DestinationPath "$work\out" -Force
    if (-not (Test-Path "$work\out\ctorrent.dll")) { throw 'The download has no ctorrent.dll in it.' }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item "$work\out\*.dll" $Destination -Force
    Write-Host "ctorrent.dll and its libraries are now in $Destination"
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
catch {
    Write-Host "Could not get libtorrent: $($_.Exception.Message)"
    Write-Host 'The app still builds; torrents play with MonoTorrent until the Build libtorrent workflow has published the files (or you put ctorrent.dll in the libtorrent folder).'
    exit 1
}
