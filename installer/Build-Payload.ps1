# Requires PowerShell 7 and the SDK in global.json.
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})\.(0|[1-9][0-9]{0,4})$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$runtimeVersion = '10.0.14'
$rid = 'win-x64'
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a new output directory for each payload build.' }
New-Item -ItemType Directory -Path $output | Out-Null
$build = Join-Path $output 'build'
$payload = Join-Path $output 'payload'
$frameworks = [ordered]@{}

Push-Location -LiteralPath $repo
try {
    $dotnet = (Get-Command dotnet -CommandType Application | Select-Object -First 1).Source
    foreach ($component in 'Studio', 'Launcher', 'MotionHost') {
        $project = "DesktopMotionStudio.$component"
        $destination = if ($component -eq 'Studio') { $payload } else { Join-Path $payload $component }
        & $dotnet publish (Join-Path $repo "src/$project/$project.csproj") -c Release -r $rid --self-contained true `
            --artifacts-path $build -o $destination "-p:Version=$Version" "-p:RuntimeFrameworkVersion=$runtimeVersion" `
            -p:StageBundledTools=false -p:DebugType=None -p:DebugSymbols=false
        if ($LASTEXITCODE -ne 0) { throw "Publish failed for $component." }

        foreach ($file in "$project.exe", 'hostfxr.dll', 'coreclr.dll') {
            if (!(Test-Path -LiteralPath (Join-Path $destination $file) -PathType Leaf)) {
                throw "Missing payload file $component/$file."
            }
        }
        $config = Get-Content -LiteralPath (Join-Path $destination "$project.runtimeconfig.json") -Raw | ConvertFrom-Json
        $included = @($config.runtimeOptions.includedFrameworks)
        $expectedNames = if ($component -eq 'Launcher') { @('Microsoft.NETCore.App') }
            else { @('Microsoft.NETCore.App', 'Microsoft.WindowsDesktop.App') }
        if ((Compare-Object $expectedNames $included.name) -or ($included | Where-Object version -ne $runtimeVersion)) {
            throw "Incorrect bundled runtime for $component."
        }
        $frameworks[$component] = $included
    }

    $assets = Get-Content -LiteralPath (Join-Path $build 'obj/DesktopMotionStudio.Studio/project.assets.json') -Raw | ConvertFrom-Json
    $noticeRoot = (New-Item -ItemType Directory -Path (Join-Path $payload 'Notices')).FullName
    $packs = @(
        @{ Name = 'Microsoft.NETCore.App.Runtime.win-x64'; Files = @('LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT') }
        @{ Name = 'Microsoft.WindowsDesktop.App.Runtime.win-x64'; Files = @('LICENSE') }
    )
    foreach ($pack in $packs) {
        $relative = "$($pack.Name.ToLowerInvariant())/$runtimeVersion"
        $source = $assets.packageFolders.PSObject.Properties.Name |
            ForEach-Object { Join-Path $_ $relative } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
        if (!$source) { throw "Bundled runtime source not found for $($pack.Name)." }
        $notices = (New-Item -ItemType Directory -Path (Join-Path $noticeRoot $pack.Name)).FullName
        foreach ($file in $pack.Files) { Copy-Item -LiteralPath (Join-Path $source $file) -Destination $notices }
    }
}
finally { Pop-Location }

$archive = Join-Path $output 'payload.zip'
$stream = [IO.File]::Open($archive, [IO.FileMode]::CreateNew)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        $inventory = foreach ($file in Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName -CaseSensitive) {
            $path = [IO.Path]::GetRelativePath($payload, $file.FullName).Replace('\', '/')
            $entry = $zip.CreateEntry($path, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $source = [IO.File]::OpenRead($file.FullName)
            try {
                $target = $entry.Open()
                try { $source.CopyTo($target) }
                finally { $target.Dispose() }
            }
            finally { $source.Dispose() }
            [ordered]@{ path = $path; bytes = $file.Length; sha256 = (Get-FileHash -LiteralPath $file.FullName).Hash }
        }
    }
    finally { $zip.Dispose() }
}
finally { $stream.Dispose() }

$digest = (Get-FileHash -LiteralPath $archive).Hash
[ordered]@{
    schemaVersion = 1
    productVersion = $Version
    runtimeIdentifier = $rid
    archiveSha256 = $digest
    frameworks = $frameworks
    files = @($inventory)
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'payload.manifest.json') -Encoding utf8
"$digest  payload.zip" | Set-Content -LiteralPath (Join-Path $output 'payload.sha256') -Encoding ascii
"Built $Version with $($inventory.Count) verified payload files at $output"
