# ClassIsland Plugin Packaging Script
# Builds TWO versions of the plugin:
#   1. .NET 8 compatible version (ClassIsland.PluginSdk/Core 2.0.1.1) for CI2
#   2. .NET 10 new version (ClassIsland.PluginSdk/Core 2.1.1.1) for CI2
# Usage: .\Build-Package.ps1 [-Target both|compat|new] [-NoServerShutdown]
# Supports: Windows x64
#
# 打包结束后会在 cipx\checksum.md 生成本次产出插件包的 MD5 校验值，
# 每个包一行，格式为 ClassIsland 可识别的 CLASSISLAND_PKG_MD5 注释。

param(
    [ValidateSet("both", "compat", "new")]
    [string]$Target = "both",
    [switch]$NoServerShutdown
)

$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot
if ([string]::IsNullOrEmpty($ProjectRoot)) {
    $ProjectRoot = "C:\Users\seewo\RiderProjects\DeepSeek_balance_forClassIsland"
}

function Test-RestoreNeeded {
    param(
        [string]$ProjectFile,
        [string]$TargetFramework,
        [string]$RuntimeIdentifier
    )

    $assetsPath = Join-Path (Split-Path -Parent $ProjectFile) "obj\project.assets.json"
    if (-not (Test-Path $assetsPath)) { return $true }

    try {
        $assets = Get-Content -Raw $assetsPath | ConvertFrom-Json -ErrorAction Stop
        if (-not $assets -or -not $assets.targets) { return $true }

        if ($RuntimeIdentifier) {
            $needle = "$TargetFramework/$RuntimeIdentifier"
            foreach ($t in $assets.targets.PSObject.Properties) {
                if ($t.Name -ieq $needle) { return $false }
            }
        } else {
            $needle = $TargetFramework + '/'
            foreach ($t in $assets.targets.PSObject.Properties) {
                if ($t.Name -ieq $TargetFramework) { return $false }
                if ($t.Name.StartsWith($needle, [StringComparison]::OrdinalIgnoreCase)) { return $false }
            }
        }
        return $true
    } catch {
        return $true
    }
}

function Create-CipxPackage {
    param(
        [string]$OutputDir,
        [string]$PackageName
    )

    $PackagePath = Join-Path $OutputDir $PackageName

    Write-Host "`nPackaging $PackageName..."
    Write-Host "Output directory: $OutputDir"
    Write-Host "Package file: $PackagePath"

    if (-not (Test-Path $OutputDir)) {
        Write-Error "Error: Output directory not found. Please run Release build first."
        return $false
    }

    $manifestPath = Join-Path $OutputDir "manifest.yml"
    if (-not (Test-Path $manifestPath)) {
        Write-Error "Error: manifest.yml not found."
        return $false
    }

    $tempDir = Join-Path $env:TEMP "DeepSeekBalance_Package_$(Get-Date -Format 'yyyyMMddHHmmss')_$(Get-Random)"
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

    try {
        Write-Host "Copying files..."
        Copy-Item "$OutputDir\*" $tempDir -Recurse -Force

        Remove-Item "$tempDir\*.pdb" -Force -ErrorAction SilentlyContinue
        Remove-Item "$tempDir\*.cipx" -Force -ErrorAction SilentlyContinue
        Remove-Item "$tempDir\*.zip" -Force -ErrorAction SilentlyContinue

        foreach ($ridSub in @("win-x64", "android-arm64")) {
            $ridPath = Join-Path $tempDir $ridSub
            if (Test-Path $ridPath) {
                Remove-Item $ridPath -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        Write-Host "Creating .cipx package..."

        Add-Type -AssemblyName System.IO.Compression.FileSystem

        if (Test-Path $PackagePath) {
            Remove-Item $PackagePath -Force
        }

        [System.IO.Compression.ZipFile]::CreateFromDirectory($tempDir, $PackagePath)

        Write-Host "Packaging successful!"
        Write-Host "Package location: $PackagePath"
        return $true
    }
    finally {
        if (Test-Path $tempDir) {
            Remove-Item $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

function Build-Variant {
    param(
        [string]$TargetFramework,
        [string]$Label,
        [string]$PackageBaseName
    )

    Write-Host "`n========================================"
    Write-Host "Building $Label ($TargetFramework)..."
    Write-Host "========================================"

    $ProjectFile = Join-Path $ProjectRoot "DeepSeekBalance.csproj"
    $OutputDir = Join-Path $ProjectRoot "bin\Release\$TargetFramework\win-x64"

    if ((Test-RestoreNeeded -ProjectFile $ProjectFile -TargetFramework $TargetFramework -RuntimeIdentifier "win-x64")) {
        Write-Host "`nRestoring packages ($TargetFramework / win-x64)..."
        dotnet restore $ProjectFile -p:TargetFramework=$TargetFramework --runtime win-x64
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Restore failed for $Label ($TargetFramework)"
            return $false
        }
    }
    else {
        Write-Host "`nSkipping restore: assets already contain target '$TargetFramework/win-x64'."
    }

    Write-Host "`nBuilding project (Release, win-x64)..."
    dotnet build $ProjectFile --configuration Release --runtime win-x64 -p:TargetFramework=$TargetFramework --no-restore

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for $Label ($TargetFramework)"
        return $false
    }

    return Create-CipxPackage -OutputDir $OutputDir -PackageName "$PackageBaseName.cipx"
}

function New-ChecksumFile {
    param(
        [string]$OutputDir,
        [string[]]$PackageNames
    )

    $lines = @()
    foreach ($name in $PackageNames) {
        $packagePath = Join-Path $OutputDir $name
        if (-not (Test-Path $packagePath)) {
            Write-Host "Skipping checksum: package not found - $name"
            continue
        }
        $hash = (Get-FileHash $packagePath -Algorithm MD5).Hash
        $lines += '<!-- CLASSISLAND_PKG_MD5 {"' + $name + '": "' + $hash + '"} -->'
    }

    if ($lines.Count -eq 0) {
        Write-Host "`nNo package generated, checksum file skipped."
        return
    }

    $content = ($lines -join "`r`n") + "`r`n"
    $checksumPath = Join-Path $OutputDir "checksum.md"

    # 用无 BOM 的 UTF8 写出，避免发布日志粘贴时出现多余字符
    [System.IO.File]::WriteAllText($checksumPath, $content, (New-Object System.Text.UTF8Encoding $false))

    Write-Host "`nChecksums:"
    Write-Host $content
    Write-Host "Checksum file: $checksumPath"
}

Write-Host "DeepSeekBalance Plugin Build Script"
Write-Host "===================================="

try {
    Write-Host "Target: $Target"

    $cipxDir = Join-Path $ProjectRoot "cipx"
    if (-not (Test-Path $cipxDir)) {
        New-Item -ItemType Directory -Path $cipxDir -Force | Out-Null
    }

    $results = [ordered]@{}
    $packageNames = @()

    if ($Target -eq "both" -or $Target -eq "compat") {
        $ok = Build-Variant -TargetFramework "net8.0" -Label "compat (.NET 8)" -PackageBaseName "DeepSeekBalance-net8.0-compat"
        if ($ok) {
            $src = Join-Path $ProjectRoot "bin\Release\net8.0\win-x64\DeepSeekBalance-net8.0-compat.cipx"
            Copy-Item $src (Join-Path $cipxDir "DeepSeekBalance-net8.0-compat.cipx") -Force
            $packageNames += "DeepSeekBalance-net8.0-compat.cipx"
        }
        $results["net8.0 compat"] = $(if ($ok) { 'SUCCESS' } else { 'FAILED' })
    }

    if ($Target -eq "both" -or $Target -eq "new") {
        $ok = Build-Variant -TargetFramework "net10.0" -Label "new (.NET 10 / Misha)" -PackageBaseName "DeepSeekBalance-net10.0"
        if ($ok) {
            $src = Join-Path $ProjectRoot "bin\Release\net10.0\win-x64\DeepSeekBalance-net10.0.cipx"
            Copy-Item $src (Join-Path $cipxDir "DeepSeekBalance-net10.0.cipx") -Force
            $packageNames += "DeepSeekBalance-net10.0.cipx"
        }
        $results["net10.0 new"] = $(if ($ok) { 'SUCCESS' } else { 'FAILED' })
    }

    if ($packageNames.Count -gt 0) {
        New-ChecksumFile -OutputDir $cipxDir -PackageNames $packageNames
    }

    Write-Host "`n========================================"
    Write-Host "Build Summary"
    Write-Host "========================================"
    foreach ($k in $results.Keys) {
        Write-Host "$k : $($results[$k])"
    }
    Write-Host "`nPackages output: $cipxDir"

    if ($results.Values -contains 'FAILED') { exit 1 } else { exit 0 }
}
finally {
    if (-not $NoServerShutdown) {
        dotnet build-server shutdown 2>$null
    }
}
