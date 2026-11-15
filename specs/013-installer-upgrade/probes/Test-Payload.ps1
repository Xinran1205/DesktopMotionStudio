[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $OutputDirectory).Path
$manifest = Get-Content -LiteralPath (Join-Path $root 'payload.manifest.json') -Raw | ConvertFrom-Json
$archive = Join-Path $root 'payload.zip'
if ((Get-FileHash -LiteralPath $archive).Hash -ne $manifest.archiveSha256) { throw 'Archive digest mismatch.' }

$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $inventory = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $manifest.files) { $inventory.Add($file.path, $file) }
    foreach ($entry in $zip.Entries) {
        $file = $null
        if (!$inventory.Remove($entry.FullName, [ref]$file) -or $entry.Length -ne $file.bytes) {
            throw "Unexpected archive entry $($entry.FullName)."
        }
        $stream = $entry.Open()
        try { $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if ($digest -ne $file.sha256) { throw "Payload digest mismatch for $($entry.FullName)." }
    }
    if ($inventory.Count -ne 0) { throw 'Archive entries are missing.' }
    "PASS archive and all $($zip.Entries.Count) payload hashes"
}
finally { $zip.Dispose() }

$payload = Join-Path $root 'payload'
$project = 'DesktopMotionStudio'
foreach ($component in 'Launcher', 'MotionHost') {
    if (Test-Path -LiteralPath (Join-Path $payload "$project.$component.exe")) { throw 'Duplicate root apphost.' }
}
if ($manifest.files.path -match 'Fixture|WindowHost\.Poc|\.pdb$') { throw 'Unexpected test or debug payload.' }
foreach ($notice in @(
    'Notices/Microsoft.NETCore.App.Runtime.win-x64/LICENSE.TXT',
    'Notices/Microsoft.NETCore.App.Runtime.win-x64/THIRD-PARTY-NOTICES.TXT',
    'Notices/Microsoft.WindowsDesktop.App.Runtime.win-x64/LICENSE'
)) {
    if ($manifest.files.path -cnotcontains $notice) { throw "Missing runtime notice $notice." }
}
'PASS payload layout and runtime notices'

$variables = @('DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_HOST_TRACE', 'DOTNET_HOST_TRACEFILE')
$previous = @{}
foreach ($name in $variables) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    $env:DOTNET_ROOT = Join-Path $root 'no-external-runtime'
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
    $env:DOTNET_HOST_TRACE = '1'
    foreach ($component in 'Studio', 'Launcher', 'MotionHost') {
        $dir = if ($component -eq 'Studio') { $payload } else { Join-Path $payload $component }
        $exe = Join-Path $dir "$project.$component.exe"
        $env:DOTNET_HOST_TRACEFILE = Join-Path $root "$component-host.log"
        $process = Start-Process -FilePath $exe -ArgumentList '--invalid' -WindowStyle Hidden -PassThru -Wait
        try { if ($process.ExitCode -ne 2) { throw "$component did not reach its argument parser." } }
        finally { $process.Dispose() }
        $trace = Get-Content -LiteralPath $env:DOTNET_HOST_TRACEFILE -Raw
        if (!$trace.Contains('Executing as a self-contained app') -or
            !$trace.Contains("Loaded library from $(Join-Path $dir 'hostfxr.dll')")) {
            throw "$component did not load its app-local runtime."
        }
        "PASS $component apphost loads its app-local runtime"
    }
}
finally {
    foreach ($name in $variables) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
