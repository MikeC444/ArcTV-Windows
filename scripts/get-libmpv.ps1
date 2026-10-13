# Downloads libmpv (mpv's library, the video player's engine) next to the app's project, where the build copies it beside ArcTV.exe.
#
#   powershell -ExecutionPolicy Bypass -File scripts\get-libmpv.ps1
#
# It takes the newest LGPL x64 build from https://github.com/shinchiro/mpv-winbuild-cmake/releases (the LGPL build is used so Arc TV does not have to be released under the
# GPL; mpv itself is LGPL when built that way, and libmpv-2.dll is loaded as a separate file). Nothing happens if libmpv-2.dll is already there. A download that fails
# prints why and exits with 1; the app still builds, and the player says the engine is missing until the file is in place.
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\src\ArcTV.App\libmpv')
)

$ErrorActionPreference = 'Stop'
$target = Join-Path $Destination 'libmpv-2.dll'
if (Test-Path $target) {
    Write-Host "libmpv-2.dll is already in $Destination"
    exit 0
}

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13
    $headers = @{ 'User-Agent' = 'ArcTV-Windows-build'; 'Accept' = 'application/vnd.github+json' }
    if ($env:GITHUB_TOKEN) { $headers['Authorization'] = "Bearer $($env:GITHUB_TOKEN)" }

    $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/shinchiro/mpv-winbuild-cmake/releases/latest' -Headers $headers
    $asset = $release.assets | Where-Object { $_.name -match '^mpv-dev-lgpl-x86_64-\d' } | Select-Object -First 1
    if (-not $asset) {
        throw "No LGPL x64 libmpv build was found in the latest release ($($release.tag_name)). Download libmpv-2.dll yourself (an LGPL build) and put it in $Destination."
    }

    $work = Join-Path ([IO.Path]::GetTempPath()) ('libmpv-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    $archive = Join-Path $work $asset.name
    Write-Host "Downloading $($asset.name) ..."
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $archive -Headers @{ 'User-Agent' = 'ArcTV-Windows-build' }

    # 7-Zip is on the GitHub build machines; Windows 11's own tar also reads .7z
    $seven = Get-Command 7z -ErrorAction SilentlyContinue
    if ($seven) { & $seven.Source x $archive "-o$work\out" -y | Out-Null }
    else { New-Item -ItemType Directory -Path "$work\out" | Out-Null; & tar -xf $archive -C "$work\out" }

    $dll = Get-ChildItem -Path "$work\out" -Recurse -Filter 'libmpv-2.dll' | Select-Object -First 1
    if (-not $dll) { throw 'libmpv-2.dll was not inside the download.' }

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item $dll.FullName $target
    Write-Host "libmpv-2.dll is now in $Destination ($([math]::Round((Get-Item $target).Length / 1MB, 1)) MB)"
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    exit 0
}
catch {
    Write-Host "Could not get libmpv: $($_.Exception.Message)"
    exit 1
}
