<#
  Tries a branch of Arc TV for Windows: fetches it, switches to it, builds it and opens the app.
  Usage (PowerShell, from anywhere inside the cloned repo):   .\scripts\try-branch.ps1 <branch>
  Needs Git and the .NET 8 SDK (Visual Studio 2022 brings both).
#>
param([Parameter(Mandatory = $true)][string]$Branch)

$ErrorActionPreference = "Stop"
Set-Location (git rev-parse --show-toplevel)
git fetch origin $Branch
git switch $Branch
git pull --ff-only origin $Branch
dotnet run --project src/ArcTV.App -c Debug
