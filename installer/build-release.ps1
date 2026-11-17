param(
    [ValidateSet("x64", "x86", "arm64")]
    [string]$Arch = "x64",
    [string]$Version = "2.0.0",
    [ValidateSet("self-contained", "framework-dependent", "both")]
    [string]$Flavor = "both",
    [switch]$SkipPublish,
    [switch]$CreatePortableZip
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Csproj = Join-Path $RepoRoot "OsuBackgroundReplacerMain\OsuBackgroundReplacerMain.csproj"
$OutDir = Join-Path $RepoRoot "out"
$IssPath = Join-Path $PSScriptRoot "OsuBackgroundReplacer.iss"

$IsccCandidates = @(
    (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
    "D:\Apps\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) }

$Iscc = $IsccCandidates | Select-Object -First 1
if (-not $Iscc) {
    throw "Inno Setup Compiler (ISCC.exe) not found."
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

$FlavorsToBuild = if ($Flavor -eq "both") {
    @("self-contained", "framework-dependent")
} else {
    @($Flavor)
}

foreach ($CurrentFlavor in $FlavorsToBuild) {
    $IsFrameworkDependent = $CurrentFlavor -eq "framework-dependent"
    $PublishSubdir = if ($IsFrameworkDependent) { "publish-fd" } else { "publish" }
    $FlavorSuffix = if ($IsFrameworkDependent) { "-framework-dependent" } else { "" }
    $PublishDir = Join-Path $RepoRoot "OsuBackgroundReplacerMain\bin\Release\net11.0-windows10.0.19041.0\win-$Arch\$PublishSubdir"

    if (-not $SkipPublish) {
        Write-Host "Publishing Release win-$Arch ($CurrentFlavor)..." -ForegroundColor Cyan
        if ($IsFrameworkDependent) {
            & dotnet publish $Csproj -c Release -p:Platform=$Arch -p:RuntimeIdentifier="win-$Arch" -p:SelfContained=false -p:PublishTrimmed=false -p:WindowsAppSDKSelfContained=false -p:PublishDir="$PublishDir\"
        } else {
            & dotnet publish $Csproj -c Release -p:Platform=$Arch -p:PublishProfile="win-$Arch.pubxml"
        }
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish ($CurrentFlavor) failed with exit code $LASTEXITCODE"
        }
    }

    if (-not (Test-Path (Join-Path $PublishDir "OsuBackgroundReplacer.exe"))) {
        throw "Publish output not found at $PublishDir"
    }

    Write-Host "Building Inno Setup installer ($CurrentFlavor) with $Iscc..." -ForegroundColor Cyan
    & $Iscc "/DMyArch=$Arch" "/DMyAppVersion=$Version" "/DFlavor=$CurrentFlavor" "/DPublishDir=$PublishDir" $IssPath
    if ($LASTEXITCODE -ne 0) {
        throw "ISCC.exe ($CurrentFlavor) failed with exit code $LASTEXITCODE"
    }

    if ($CreatePortableZip) {
        $ZipPath = Join-Path $OutDir "OsuBackgroundReplacer-$Version-win-$Arch$FlavorSuffix-portable.zip"
        if (Test-Path $ZipPath) {
            Remove-Item $ZipPath -Force
        }
        $StagingDir = Join-Path $OutDir "_portable_staging_${Arch}_${CurrentFlavor}"
        if (Test-Path $StagingDir) {
            Remove-Item $StagingDir -Recurse -Force
        }
        Copy-Item -Path $PublishDir -Destination $StagingDir -Recurse -Force
        Get-ChildItem -Path $StagingDir -Filter "*.pdb" -Recurse | Remove-Item -Force
        [System.IO.Compression.ZipFile]::CreateFromDirectory($StagingDir, $ZipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)
        Remove-Item $StagingDir -Recurse -Force
        Write-Host "Created portable zip: $ZipPath" -ForegroundColor Green
    }
}

Write-Host "Release packaging complete. Output in: $OutDir" -ForegroundColor Green
