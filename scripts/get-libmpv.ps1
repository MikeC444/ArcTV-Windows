# Downloads libmpv (mpv's library, the video player's engine) next to the app's project, where the build copies it beside ArcTV.exe.
#
#   powershell -ExecutionPolicy Bypass -File scripts\get-libmpv.ps1
#
# It takes the newest x64 LGPL build of libmpv (built without mpv's GPL parts, so Arc TV does not have to be released under the GPL; libmpv-2.dll is loaded as a separate file) from
# https://github.com/zhongfly/mpv-winbuild/releases ("mpv-dev-lgpl-x86_64-..."), or from https://github.com/shinchiro/mpv-winbuild-cmake/releases if that ever publishes one. Without
# -RequireLgpl a standard (GPL) build is used when no LGPL one can be found; the release workflow passes -RequireLgpl, and the library is then also checked not to list the GPL feature.
# Nothing happens if libmpv-2.dll is already there. A download that fails prints why and exits with 1; the app still builds, and the player says the engine is missing until the file is in place.
param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\src\ArcTV.App\libmpv'),
    [switch]$RequireLgpl
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

    # LGPL builds first, from each source's newest releases; the standard (GPL) build only when allowed.
    $sources = @('zhongfly/mpv-winbuild', 'shinchiro/mpv-winbuild-cmake')
    $asset = $null
    $isLgpl = $false
    $lists = @{}
    foreach ($source in $sources) {
        try { $lists[$source] = Invoke-RestMethod -Uri "https://api.github.com/repos/$source/releases?per_page=8" -Headers $headers }
        catch { Write-Host "Could not read the releases of ${source}: $($_.Exception.Message)"; $lists[$source] = @() }
        foreach ($release in $lists[$source]) {
            $asset = $release.assets | Where-Object { $_.name -match '^mpv-dev-lgpl-x86_64-\d' -and $_.name -like '*.7z' } | Select-Object -First 1
            if ($asset) { $isLgpl = $true; break }
        }
        if ($asset) { break }
    }
    if (-not $asset -and -not $RequireLgpl) {
        foreach ($release in $lists['shinchiro/mpv-winbuild-cmake']) {
            $asset = $release.assets | Where-Object { $_.name -match '^mpv-dev-x86_64-\d' -and $_.name -like '*.7z' } | Select-Object -First 1
            if ($asset) { Write-Host 'Using the standard (GPL) build, not an LGPL one; see THIRD_PARTY_NOTICES.md.'; break }
        }
    }
    if (-not $asset) {
        throw "No x64 libmpv$(if ($RequireLgpl) { ' LGPL' }) build was found in the newest releases. Download libmpv-2.dll yourself and put it in $Destination."
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
    if (-not $dll) { $dll = Get-ChildItem -Path "$work\out" -Recurse -Filter 'mpv-2.dll' | Select-Object -First 1 } # older builds name it mpv-2.dll
    if (-not $dll) { throw 'The library (libmpv-2.dll) was not inside the download.' }

    if ($RequireLgpl) {
        # a GPL build of mpv lists "gpl" among its enabled features (a string inside the library); an LGPL one does not
        $text = [Text.Encoding]::Latin1.GetString([IO.File]::ReadAllBytes($dll.FullName))
        if (-not $isLgpl -or $text -match 'List of enabled features:[^\x00]*? gpl[ \x00]') { throw 'This libmpv is a GPL build, not LGPL: refusing to use it.' }
    }

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
