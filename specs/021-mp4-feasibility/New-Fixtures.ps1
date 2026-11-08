[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Ffmpeg,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$encoder = (Resolve-Path -LiteralPath $Ffmpeg).Path
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new directory for these fixtures.' }
$output = (New-Item -ItemType Directory -Path $OutputDirectory).FullName

function New-Clip([string]$Name, [string]$Size, [int]$Duration, [switch]$Audio) {
    $path = Join-Path $output $Name
    $arguments = @('-hide_banner', '-loglevel', 'error', '-y', '-f', 'lavfi', '-i', "testsrc2=size=${Size}:rate=30:duration=$Duration")
    if ($Audio) { $arguments += @('-f', 'lavfi', '-i', "sine=frequency=440:sample_rate=48000:duration=$Duration", '-c:a', 'aac') }
    else { $arguments += '-an' }
    $arguments += @('-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', $path)
    & $encoder @arguments
    if ($LASTEXITCODE -ne 0) { throw "Fixture encoding failed for $Name" }
}

New-Clip 'landscape.mp4' '1920x1080' 6 -Audio
New-Clip 'portrait.mp4' '720x1280' 6
New-Clip 'landscape-10s.mp4' '1920x1080' 10
[IO.File]::WriteAllBytes((Join-Path $output 'malformed.mp4'), [byte[]](1, 2, 3, 4))
