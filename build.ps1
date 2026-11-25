# Carbon Monitor Build Script
#
# Usage:
#   .\build.ps1                           # Build only (Release)
#   .\build.ps1 -Clean                    # Clean build artifacts
#   .\build.ps1 -Test                     # Build and run tests
#   .\build.ps1 -Publish                  # Build and publish self-contained
#   .\build.ps1 -Installer                # Build installer (requires Inno Setup 6)
#   .\build.ps1 -Clean -Test -Publish -Installer  # Full pipeline
#   .\build.ps1 -Configuration Debug      # Debug build

param(
    [switch]$Clean,
    [switch]$Test,
    [switch]$Publish,
    [switch]$Installer,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot

$LocalDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
if ((Test-Path (Join-Path $LocalDotnet "dotnet.exe")) -and ($env:PATH -notlike "*$LocalDotnet*")) {
    $env:PATH = "$LocalDotnet;$env:PATH"
}

Write-Host ""
Write-Host "=== Carbon Monitor Build ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration" -ForegroundColor Gray
Write-Host ""

# Clean
if ($Clean) {
    Write-Host "[1/5] Cleaning..." -ForegroundColor Yellow
    dotnet clean --configuration $Configuration --verbosity quiet 2>$null
    if (Test-Path "$Root\publish") {
        Remove-Item "$Root\publish" -Recurse -Force
        Write-Host "  Removed publish/" -ForegroundColor Gray
    }
    if (Test-Path "$Root\output") {
        Remove-Item "$Root\output" -Recurse -Force
        Write-Host "  Removed output/" -ForegroundColor Gray
    }
    Write-Host "  Clean complete." -ForegroundColor Green
    Write-Host ""
}

# Restore
Write-Host "[2/5] Restoring packages..." -ForegroundColor Yellow
dotnet restore --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "Restore failed with exit code $LASTEXITCODE" }
Write-Host "  Restore complete." -ForegroundColor Green
Write-Host ""

# Build
Write-Host "[3/5] Building..." -ForegroundColor Yellow
dotnet build --configuration $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }
Write-Host "  Build complete." -ForegroundColor Green
Write-Host ""

# Test
if ($Test) {
    Write-Host "[4/5] Running tests..." -ForegroundColor Yellow
    dotnet test --configuration $Configuration --no-build --verbosity normal
    if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE" }
    Write-Host "  All tests passed." -ForegroundColor Green
    Write-Host ""
} else {
    Write-Host "[4/5] Tests skipped (use -Test to run)" -ForegroundColor DarkGray
    Write-Host ""
}

# Publish
if ($Publish) {
    Write-Host "[5a/5] Publishing self-contained x64..." -ForegroundColor Yellow

    $PublishDir = "$Root\publish"
    dotnet publish src/CarbonMonitor.App/CarbonMonitor.App.csproj `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -o $PublishDir

    if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE" }

    # Copy supporting files into the publish directory
    if (Test-Path "$Root\docs\THIRD-PARTY-NOTICES.md") {
        Copy-Item "$Root\docs\THIRD-PARTY-NOTICES.md" "$PublishDir\" -Force
        Write-Host "  Copied THIRD-PARTY-NOTICES.md to publish/" -ForegroundColor Gray
    }
    if (Test-Path "$Root\LICENSE") {
        Copy-Item "$Root\LICENSE" "$PublishDir\" -Force
        Write-Host "  Copied LICENSE to publish/" -ForegroundColor Gray
    }

    Write-Host "  Published to $PublishDir" -ForegroundColor Green
    Write-Host ""
}

# Installer
if ($Installer) {
    Write-Host "[5b/5] Building installer..." -ForegroundColor Yellow

    $InnoCandidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    )
    $InnoSetup = $InnoCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $InnoSetup) {
        Write-Host "  ERROR: Inno Setup 6 (ISCC.exe) not found in standard locations." -ForegroundColor Red
        Write-Host "  Download Inno Setup 6 from: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
        throw "Inno Setup 6 is required to build the installer"
    }

    if (-not (Test-Path "$Root\publish")) {
        Write-Host "  ERROR: publish/ directory not found." -ForegroundColor Red
        Write-Host "  Run with -Publish first to create the self-contained build." -ForegroundColor Yellow
        throw "Publish output required before building the installer"
    }

    & $InnoSetup "$Root\installer\CarbonMonitor.iss"
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed with exit code $LASTEXITCODE" }

    Write-Host "  Installer created at $Root\output\CarbonMonitor-Setup-x64.exe" -ForegroundColor Green
    Write-Host ""
}

# Summary
Write-Host "=== Build Complete ===" -ForegroundColor Cyan
Write-Host ""

if ($Publish -and (Test-Path "$Root\publish\CarbonMonitor.App.exe")) {
    $exeSize = (Get-Item "$Root\publish\CarbonMonitor.App.exe").Length / 1MB
    Write-Host "  Executable size: $([math]::Round($exeSize, 1)) MB" -ForegroundColor Gray
}
if ($Installer -and (Test-Path "$Root\output\CarbonMonitor-Setup-x64.exe")) {
    $installerSize = (Get-Item "$Root\output\CarbonMonitor-Setup-x64.exe").Length / 1MB
    Write-Host "  Installer size:  $([math]::Round($installerSize, 1)) MB" -ForegroundColor Gray
}
Write-Host ""
