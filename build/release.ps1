<#
.SYNOPSIS
Builds the release files into artifacts\release: the app and the CLI as self-contained single-file
win-x64 executables, a portable zip, the installer (needs Inno Setup) and SHA256SUMS.txt.

.EXAMPLE
./build/release.ps1 -Iscc 'C:\Program Files (x86)\Inno Setup 7\ISCC.exe'
#>
param(
    # dotnet to use; the user-local SDK isn't on PATH on Dale's PC.
    [string]$Dotnet = 'dotnet',
    # Inno Setup compiler. Without it only the zip is built, unless -RequireInstaller.
    [string]$Iscc = '',
    [switch]$RequireInstaller,
    # Fail unless <Version> in Directory.Build.props is this (the tag, on a release).
    [string]$ExpectVersion = ''
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$appProject = Join-Path $root 'src\Cleaner.App\Cleaner.App.csproj'
$cliProject = Join-Path $root 'src\Cleaner.Cli\Cleaner.Cli.csproj'

function Get-BuildProperty([string]$project, [string]$name) {
    $value = (& $Dotnet msbuild $project -nologo "-getProperty:$name" | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or -not $value) { throw "Could not read $name from $project" }
    return $value
}

# Names and version come from Directory.Build.props and the projects, never from this script.
$product = Get-BuildProperty $appProject 'Product'
$company = Get-BuildProperty $appProject 'Company'
$version = Get-BuildProperty $appProject 'Version'
$appExe = (Get-BuildProperty $appProject 'AssemblyName') + '.exe'
$cliExe = (Get-BuildProperty $cliProject 'AssemblyName') + '.exe'
$fileBase = $product -replace '[^A-Za-z0-9]', ''

# The installer's file version (VersionInfoVersion) only takes numbers, so no pre-release suffixes.
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version '$version' must be MAJOR.MINOR.PATCH (numbers only)"
}
if ($ExpectVersion -and $ExpectVersion -ne $version) {
    throw "Tag says $ExpectVersion but Directory.Build.props says $version"
}

$out = Join-Path $root 'artifacts\release'
$stage = Join-Path $root 'artifacts\stage'
foreach ($dir in $out, $stage) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Path $dir | Out-Null
}

foreach ($project in $appProject, $cliProject) {
    & $Dotnet publish $project -nologo -c Release -r win-x64 --self-contained -o $stage `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:GenerateDocumentationFile=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project" }
}
Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'README.md') $stage

# Only the two executables and the docs ship; anything else publish left behind is unexpected.
$expected = @($appExe, $cliExe, 'LICENSE', 'README.md')
$extra = Get-ChildItem $stage | Where-Object { $expected -notcontains $_.Name }
if ($extra) { throw "Unexpected files in the publish output: $($extra.Name -join ', ')" }

$zip = Join-Path $out "$fileBase-$version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip

if (-not $Iscc) {
    $found = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($found) { $Iscc = $found.Source }
}
if ($Iscc) {
    & $Iscc /Qp `
        "/DAppName=$product" "/DAppVersion=$version" "/DAppPublisher=$company" `
        "/DAppExe=$appExe" "/DCliExe=$cliExe" "/DSourceDir=$stage" `
        "/DOutputDir=$out" "/DOutputBaseName=$fileBase-$version-setup" `
        (Join-Path $root 'installer\SimpliSweep.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed' }
}
elseif ($RequireInstaller) {
    throw 'Inno Setup (ISCC.exe) not found; pass -Iscc'
}
else {
    Write-Warning 'Inno Setup not found: built the portable zip only.'
}

$sums = Get-ChildItem $out -File | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
# LF endings and no BOM, so `sha256sum -c SHA256SUMS.txt` works as well as Get-FileHash.
[IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"))

Get-ChildItem $out -File | ForEach-Object { '{0,-48} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
