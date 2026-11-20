[CmdletBinding(DefaultParameterSetName = 'Remote')]
param(
    [Parameter(Mandatory)][version]$Version,
    [Parameter(Mandatory)][string]$InnoSetupCompiler,
    [Parameter(Mandatory, ParameterSetName = 'Remote')]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repository,
    [Parameter(Mandatory, ParameterSetName = 'Local')][string]$BaselineArchive,
    [Parameter(Mandatory, ParameterSetName = 'Local')][version]$BaselineVersion,
    [string]$Destination = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts\release-assets')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$packageRoot = Join-Path $repoRoot 'artifacts\package'
$publishDirectory = Join-Path $packageRoot 'TileStart'
$installer = Join-Path $repoRoot 'artifacts\installer\TileStart-Setup-win-x64.exe'
$archiveName = 'TileStart-portable-win-x64.zip'
$patchName = 'TileStart-Patch-win-x64.exe'
$manifestName = 'TileStart-Update.txt'
$tempPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$scratch = Join-Path $tempPrefix ('TileStart-ReleaseUpdate-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null

function Get-PackageHashes([string]$Root) {
    $hashes = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in (Get-ChildItem -LiteralPath $Root -File -Recurse)) {
        $relative = [IO.Path]::GetRelativePath($Root, $file.FullName).Replace('\', '/')
        if ($relative -notmatch '\A[A-Za-z0-9_.-]+(?<!\.)(?:/[A-Za-z0-9_.-]+(?<!\.))*\z') {
            throw "Unsupported release file path: $relative"
        }
        $hashes.Add($relative, (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant())
    }
    return $hashes
}

function Get-PreviousRelease {
    $headers = @{ Accept = 'application/vnd.github+json'; 'X-GitHub-Api-Version' = '2022-11-28' }
    if ($env:GH_TOKEN) { $headers.Authorization = 'Bearer ' + $env:GH_TOKEN }
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases/latest" -Headers $headers `
        -TimeoutSec 30 -SkipHttpErrorCheck -StatusCodeVariable status
    if ($status -eq 404) { return }
    if ($status -ne 200) { throw "Could not read the previous stable release (HTTP $status)." }
    if ($release.tag_name -notmatch '^v\d+\.\d+\.\d+$' -or [version]$release.tag_name.Substring(1) -ge $Version -or
        $archiveName -notin $release.assets.name -or 'SHA256SUMS.txt' -notin $release.assets.name) {
        return
    }

    & gh release download $release.tag_name --repo $Repository --dir $scratch -p $archiveName -p 'SHA256SUMS.txt'
    if ($LASTEXITCODE -ne 0) { throw 'Failed to download the baseline release.' }
    $zip = Join-Path $scratch $archiveName
    $expected = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    if ((Get-Content -LiteralPath (Join-Path $scratch 'SHA256SUMS.txt')) -notcontains "$expected *$archiveName") {
        throw 'The previous portable archive failed SHA-256 verification.'
    }
    return @{ Archive = $zip; Version = [version]$release.tag_name.Substring(1) }
}

function Build-Patch([string]$Archive, [version]$BaseVersion) {
    if ($BaseVersion -ge $Version) { return }
    $baseDirectory = Join-Path $scratch 'base'
    Expand-Archive -LiteralPath $Archive -DestinationPath $baseDirectory
    $base = Get-PackageHashes $baseDirectory
    $target = Get-PackageHashes $publishDirectory
    if (-not $base.ContainsKey('TileStart.Host.exe') -or -not $base.ContainsKey('TileStart.Host.dll') -or
        -not $target.ContainsKey('TileStart.Host.dll') -or
        (Get-Item -LiteralPath (Join-Path $baseDirectory 'TileStart.Host.exe')).VersionInfo.FileVersion -ne ($BaseVersion.ToString(3) + '.0') -or
        $base.Count -ne $target.Count -or @($base.Keys | Where-Object { -not $target.ContainsKey($_) }).Count -gt 0) {
        Write-Host 'The baseline layout or version differs. Publishing the complete packages.'
        return
    }

    $changed = @($target.Keys | Where-Object { $target[$_] -ne $base[$_] })
    if ($changed.Count -eq 0 -or @($changed | Where-Object { $_ -notmatch '^TileStart\.[^/]+$' }).Count -gt 0) {
        Write-Host 'The runtime or external dependencies differ, or there are no changed files. Publishing the complete packages.'
        return
    }

    $patchFiles = Join-Path $scratch 'files'
    New-Item -ItemType Directory -Path $patchFiles | Out-Null
    foreach ($relative in $changed) {
        Copy-Item -LiteralPath (Join-Path $publishDirectory $relative) -Destination (Join-Path $patchFiles $relative)
    }

    $manifest = Join-Path $scratch $manifestName
    $lines = @('TileStart.Update.v1', $BaseVersion.ToString(3)) + @(
        $base.Keys | Sort-Object | ForEach-Object { "$($base[$_]) *$_" }
    )
    [IO.File]::WriteAllLines($manifest, [string[]]$lines, [Text.UTF8Encoding]::new($false))
    & $InnoSetupCompiler "/DAppVersion=$($Version.ToString(3))" "/DPatchDirectory=$patchFiles" "/DPatchManifest=$manifest" `
        "/O$scratch" (Join-Path $repoRoot 'installer\TileStart.iss') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed to build the incremental installer.' }

    $patch = Join-Path $scratch $patchName
    if ((Get-Item -LiteralPath $patch).Length -ge (Get-Item -LiteralPath $installer).Length) {
        Write-Host 'The patch is larger than the installer. Publishing the complete packages.'
        return
    }

    Write-Host "Patch from $BaseVersion to $Version includes $($changed.Count) changed files."
    return @{ Path = $patch; Manifest = $manifest }
}

try {
    $baseline = if ($Repository) { Get-PreviousRelease } else { @{ Archive = (Resolve-Path -LiteralPath $BaselineArchive).Path; Version = $BaselineVersion } }
    $patch = if ($baseline) { Build-Patch $baseline.Archive $baseline.Version }
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Copy-Item -LiteralPath $installer, (Join-Path $packageRoot $archiveName) -Destination $Destination -Force
    $publishedManifest = Join-Path $Destination $manifestName
    $publishedPatch = Join-Path $Destination $patchName
    $names = @('TileStart-Setup-win-x64.exe', $archiveName, $manifestName)
    if ($patch) {
        Copy-Item -LiteralPath $patch.Manifest -Destination $publishedManifest -Force
        Copy-Item -LiteralPath $patch.Path -Destination $publishedPatch -Force
        $names += $patchName
    } else {
        Set-Content -LiteralPath $publishedManifest -Value @('TileStart.Update.v1', '-') -Encoding utf8NoBOM
        if (Test-Path -LiteralPath $publishedPatch) { Remove-Item -LiteralPath $publishedPatch -Force }
    }

    $checksums = foreach ($name in $names) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $Destination $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash *$name"
    }
    Set-Content -LiteralPath (Join-Path $Destination 'SHA256SUMS.txt') -Value $checksums -Encoding utf8NoBOM
} finally {
    $resolved = (Resolve-Path -LiteralPath $scratch).Path
    if ($resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -and $resolved -ne $tempPrefix) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
