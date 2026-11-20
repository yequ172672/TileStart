[CmdletBinding()]
param(
    [switch]$SkipInstaller,
    [string]$Version,
    [string]$InformationalVersion,
    [string]$ReleaseRepository,
    [switch]$IncludeFrameworkDependent
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$artifactsRoot = Join-Path $repoRoot 'artifacts'
$packageRoot = Join-Path $artifactsRoot 'package'
$publishDirectory = Join-Path $packageRoot 'TileStart'
$frameworkDependentDirectory = Join-Path $packageRoot 'TileStart-FrameworkDependent'
$portableArchive = Join-Path $packageRoot 'TileStart-portable-win-x64.zip'
$checksumFile = Join-Path $packageRoot 'SHA256SUMS.txt'
$nugetSource = 'https://api.nuget.org/v3/index.json'
$hostProject = Join-Path $repoRoot 'src\TileStart.Host\TileStart.Host.csproj'
$artifactsFullPath = [IO.Path]::GetFullPath($artifactsRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$packageFullPath = [IO.Path]::GetFullPath($packageRoot)
if (-not $packageFullPath.StartsWith($artifactsFullPath, [StringComparison]::OrdinalIgnoreCase))
{
    throw "Package directory must stay under artifacts: $packageFullPath"
}

if ([string]::IsNullOrWhiteSpace($Version))
{
    [xml]$project = Get-Content -LiteralPath $hostProject -Raw
    $Version = [string]$project.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+$')
{
    throw "Version must use major.minor.patch format: $Version"
}
if ([string]::IsNullOrWhiteSpace($InformationalVersion))
{
    $InformationalVersion = $Version
}
if ($InformationalVersion -notmatch '^\d+\.\d+\.\d+(?:\+[0-9A-Za-z.-]+)?$')
{
    throw "InformationalVersion must use semantic version format: $InformationalVersion"
}
$assemblyVersion = "$Version.0"

function Write-Checksums([string[]]$Paths)
{
    $lines = foreach ($path in $Paths)
    {
        $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash *$(Split-Path $path -Leaf)"
    }
    [IO.File]::WriteAllLines($checksumFile, $lines, [Text.UTF8Encoding]::new($false))
    Write-Host "Checksums: $checksumFile"
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild)
{
    throw 'MSBuild was not found.'
}

& $msbuild (Join-Path $repoRoot 'TileStart.sln') /restore /p:RestoreSources=$nugetSource /p:Configuration=Release /p:Platform=x64 /p:Version=$Version /p:AssemblyVersion=$assemblyVersion /p:FileVersion=$assemblyVersion /p:InformationalVersion=$InformationalVersion /m /v:minimal
if ($LASTEXITCODE -ne 0)
{
    throw "MSBuild failed with exit code $LASTEXITCODE."
}

if (Test-Path $packageRoot)
{
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
$nativeDirectory = Join-Path $artifactsRoot 'Release\x64'

function Publish-Host([string]$Directory, [string]$SelfContained) {
    & dotnet publish $hostProject `
        -c Release `
        -r win-x64 `
        --self-contained $SelfContained `
        --source $nugetSource `
        -p:Version=$Version `
        -p:AssemblyVersion=$assemblyVersion `
        -p:FileVersion=$assemblyVersion `
        -p:InformationalVersion=$InformationalVersion `
        -o $Directory
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    Copy-Item (Join-Path $nativeDirectory 'TileStart.Injector.exe') $Directory
    Copy-Item (Join-Path $nativeDirectory 'TileStart.ShellHook.dll') $Directory
}

Publish-Host $publishDirectory 'true'

Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $portableArchive -CompressionLevel Optimal
Write-Host "Portable package: $portableArchive"

if ($SkipInstaller)
{
    Write-Checksums @($portableArchive)
    return
}

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc)
{
    throw 'Inno Setup 6 was not found. Install it or run this script with -SkipInstaller.'
}

& $iscc "/DAppVersion=$Version" (Join-Path $repoRoot 'installer\TileStart.iss')
if ($LASTEXITCODE -ne 0)
{
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

$installerPath = Join-Path $artifactsRoot 'installer\TileStart-Setup-win-x64.exe'
$packagePaths = @($installerPath, $portableArchive)
if ($ReleaseRepository -or $IncludeFrameworkDependent) {
    Publish-Host $frameworkDependentDirectory 'false'
    $bundledRuntimeFiles = foreach ($file in (Get-ChildItem -LiteralPath $publishDirectory -Recurse -File)) {
        $relative = [IO.Path]::GetRelativePath($publishDirectory, $file.FullName)
        if (-not (Test-Path -LiteralPath (Join-Path $frameworkDependentDirectory $relative) -PathType Leaf)) {
            $relative
        }
    }
    [IO.File]::WriteAllLines((Join-Path $packageRoot 'TileStart-BundledRuntime.txt'), [string[]]$bundledRuntimeFiles,
        [Text.UTF8Encoding]::new($false))
    & $iscc "/DAppVersion=$Version" /DFrameworkDependent (Join-Path $repoRoot 'installer\TileStart.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed to build the framework-dependent installer.' }
    $packagePaths += Join-Path $artifactsRoot 'installer\TileStart-Setup-no-runtime-win-x64.exe'
}

Write-Checksums $packagePaths

if ($ReleaseRepository) {
    & (Join-Path $PSScriptRoot 'Build-ReleaseUpdate.ps1') -Version $Version -Repository $ReleaseRepository -InnoSetupCompiler $iscc
}
