param(
    [string[]]$RuntimeIdentifier = @("win-x64", "win-x86", "win-arm64"),
    [string]$Configuration = "Release",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$publishRoot = Join-Path $repo "artifacts\installer-publish"
$installerDir = Join-Path $repo "artifacts\installer"
$scriptPath = Join-Path $repo "installer\FControl.iss"

if (-not $Version) {
    [xml]$manifest = Get-Content (Join-Path $repo "Package.appxmanifest")
    $Version = $manifest.Package.Identity.Version -replace "\.0$", ""
}

$assemblyVersionParts = @($Version.Split("."))
if ($assemblyVersionParts.Count -gt 4) {
    throw "Version '$Version' has too many parts for AssemblyVersion."
}
while ($assemblyVersionParts.Count -lt 4) {
    $assemblyVersionParts += "0"
}
$assemblyVersion = $assemblyVersionParts -join "."

foreach ($path in @($publishRoot, $installerDir)) {
    $fullPath = [System.IO.Path]::GetFullPath($path)
    if (-not $fullPath.StartsWith($repo.Path, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean path outside repository: $fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $fullPath | Out-Null
}

$iscc = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
if (-not $iscc) {
    $candidates = @(
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )
    $isccPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
} else {
    $isccPath = $iscc.Source
}

if (-not $isccPath) {
    throw "ISCC.exe not found. Install Inno Setup 6 first: winget install --id JRSoftware.InnoSetup -e"
}

$targets = @{
    "win-x64" = @{
        Platform = "x64"
        ArchitecturesAllowed = "x64compatible"
        ArchitecturesInstallIn64BitMode = "x64compatible"
    }
    "win-x86" = @{
        Platform = "x86"
    }
    "win-arm64" = @{
        Platform = "ARM64"
        ArchitecturesAllowed = "arm64"
        ArchitecturesInstallIn64BitMode = "arm64"
    }
}

foreach ($rid in $RuntimeIdentifier) {
    if (-not $targets.ContainsKey($rid)) {
        throw "Unsupported RuntimeIdentifier '$rid'. Supported values: $($targets.Keys -join ', ')"
    }

    $target = $targets[$rid]
    $publishDir = Join-Path $publishRoot $rid
    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

    dotnet publish (Join-Path $repo "FControl.csproj") `
        -c $Configuration `
        -r $rid `
        -p:Platform=$($target.Platform) `
        -p:WindowsPackageType=None `
        -p:Version=$Version `
        -p:AssemblyVersion=$assemblyVersion `
        -p:FileVersion=$assemblyVersion `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $rid with exit code $LASTEXITCODE"
    }

    Get-ChildItem -Path $publishDir -Filter "*.pdb" -File -Recurse | Remove-Item -Force

    $isccArgs = @(
        "/DMyAppVersion=$Version",
        "/DSourceDir=$publishDir",
        "/DOutputDir=$installerDir",
        "/DArchName=$rid"
    )

    if ($target.ContainsKey("ArchitecturesAllowed")) {
        $isccArgs += "/DArchitecturesAllowed=$($target.ArchitecturesAllowed)"
    }

    if ($target.ContainsKey("ArchitecturesInstallIn64BitMode")) {
        $isccArgs += "/DArchitecturesInstallIn64BitMode=$($target.ArchitecturesInstallIn64BitMode)"
    }

    & $isccPath @isccArgs $scriptPath

    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compiler failed for $rid with exit code $LASTEXITCODE"
    }
}

Get-ChildItem -Path $installerDir -Filter "*.exe" | Sort-Object Name | Select-Object FullName, Length, LastWriteTime
