<#
.SYNOPSIS
    Build, test, package et manifest pour NinaTheSkyX.
#>
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root   = $PSScriptRoot
$plugin = Join-Path $root "NinaTheSkyX.csproj"
$tests  = Join-Path (Split-Path $root -Parent) "NinaTheSkyX.Tests\NinaTheSkyX.Tests.csproj"

Write-Host "=== Build NinaTheSkyX ===" -ForegroundColor Cyan
dotnet build $plugin -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Build échoué." }

if (Test-Path $tests) {
    Write-Host "=== Tests NinaTheSkyX ===" -ForegroundColor Cyan
    dotnet test $tests -c $Configuration --nologo --no-build
    if ($LASTEXITCODE -ne 0) { throw "Tests échoués." }
} else {
    Write-Host "Projet de tests introuvable, ignoré." -ForegroundColor Yellow
}

$dll  = Join-Path $root "bin\$Configuration\NinaTheSkyX.dll"
$zip  = Join-Path $root "bin\$Configuration\NinaTheSkyX.zip"

if (-not (Test-Path $dll)) { throw "DLL introuvable : $dll" }

Write-Host "=== Package ===" -ForegroundColor Cyan
Compress-Archive -Path $dll -DestinationPath $zip -Force

$sha = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
$manifest = [ordered]@{
    Name        = "TheSkyX Guider"
    Identifier  = "c3d4e5f6-a7b8-9012-cdef-012345678912"
    Version     = "1.1.0"
    Author      = "Alexandre"
    Homepage    = "https://example.com/nina-theskyx"
    Repository  = "https://github.com/example/nina-theskyx"
    License     = "MIT"
    LicenseURL  = "https://opensource.org/licenses/MIT"
    ChangelogURL= "https://example.com/nina-theskyx/CHANGELOG"
    Tags        = @("TheSkyX","Guider","Autoguide","TCP","Calibration")
    MinimumApplicationVersion = "3.0.0.1000"
    Installer   = @{
        URL    = "https://example.com/nina-theskyx/NinaTheSkyX.zip"
        Sha256 = $sha
    }
}
$manifest | ConvertTo-Json -Depth 5 |
    Set-Content (Join-Path $root "bin\$Configuration\manifest.json") -Encoding UTF8

Write-Host "✓ Package OK : $zip" -ForegroundColor Green
Write-Host "  SHA256 : $sha"
