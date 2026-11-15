[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
& (Join-Path $PSScriptRoot 'Build-Payload.ps1') -Version $Version -OutputDirectory $output
$entry = Join-Path $output 'entry/DesktopMotionStudio.Entry.exe'
$dist = Join-Path $output 'setup'
$packaging = @('-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', "-p:Version=$Version",
    '-p:RuntimeFrameworkVersion=10.0.14', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true', '-p:DebugType=None')

Push-Location -LiteralPath $repo
try {
    $dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
    & $dotnet publish src/DesktopMotionStudio.Entry @packaging --artifacts-path (Join-Path $output 'entry-build') -o (Split-Path -Parent $entry)
    if ($LASTEXITCODE -ne 0) { throw 'Stable entry publish failed.' }
    & $dotnet publish src/DesktopMotionStudio.Setup @packaging --artifacts-path (Join-Path $output 'setup-build') -o $dist `
        "-p:DmsArchive=$(Join-Path $output 'payload.zip')" "-p:DmsInventory=$(Join-Path $output 'payload.manifest.json')" "-p:DmsEntry=$entry"
    if ($LASTEXITCODE -ne 0) { throw 'Setup publish failed.' }
}
finally { Pop-Location }

$setup = Join-Path $dist 'DesktopMotionStudio.Setup.exe'
$hash = (Get-FileHash -LiteralPath $setup).Hash
"$hash  DesktopMotionStudio.Setup.exe" | Set-Content -LiteralPath (Join-Path $dist 'setup.sha256') -Encoding ascii
"Built unsigned Windows x64 setup $Version at $setup"
