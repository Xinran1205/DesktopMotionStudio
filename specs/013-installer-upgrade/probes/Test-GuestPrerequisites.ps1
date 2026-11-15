[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateA,
    [Parameter(Mandatory)][string]$CandidateB
)

# Run in the fresh interactive guest before installing anything. This probe
# only reads the machine and candidate media and writes its record to stdout.
$ErrorActionPreference = 'Stop'

function Read-Candidate([string]$Directory) {
    $exe = Join-Path $Directory 'DesktopMotionStudio.Setup.exe'
    $manifest = (Get-Content -LiteralPath (Join-Path $Directory 'setup.sha256') -Raw).Trim()
    if ($manifest -notmatch '^([0-9A-Fa-f]{64})  DesktopMotionStudio\.Setup\.exe$') {
        throw 'The Setup checksum file has an unexpected format.'
    }
    $expectedHash = $Matches[1]
    $actualHash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
    if ($actualHash -ne $expectedHash) { throw 'The Setup executable does not match its checksum.' }
    $version = (Get-Item -LiteralPath $exe).VersionInfo
    return [ordered]@{
        version = '{0}.{1}.{2}' -f $version.FileMajorPart, $version.FileMinorPart, $version.FileBuildPart
        sha256 = $actualHash
    }
}

$a = Read-Candidate $CandidateA
$b = Read-Candidate $CandidateB
$os = Get-CimInstance -ClassName Win32_OperatingSystem
$processors = @(Get-CimInstance -ClassName Win32_Processor)
$groups = & (Join-Path $env:SystemRoot 'System32/whoami.exe') /groups /fo csv /nh
if ($LASTEXITCODE -ne 0 -or !$groups) { throw 'Cannot inspect the current Windows token.' }
# A filtered administrator still carries this SID with the deny-only flag.
$adminGroup = [bool]($groups -match ',"S-1-5-32-544",')
$connected = @([Net.NetworkInformation.NetworkInterface]::GetAllNetworkInterfaces() |
    Where-Object { $_.OperationalStatus -eq 'Up' -and $_.NetworkInterfaceType -ne 'Loopback' })
$local = [Environment]::GetFolderPath('LocalApplicationData', 'DoNotVerify')
$menu = [Environment]::GetFolderPath('Programs', 'DoNotVerify')
if (!$local -or !$menu) { throw 'Current-user Windows folders are unavailable.' }

$runtimeRoots = @(
    foreach ($programFiles in $env:ProgramFiles, ${env:ProgramFiles(x86)}) {
        if ($programFiles) { Join-Path $programFiles 'dotnet' }
    }
    (Join-Path $env:USERPROFILE '.dotnet')
    $env:DOTNET_ROOT
    $env:DOTNET_ROOT_X64
    $env:DOTNET_ROOT_X86
)
$runtimeFolders = @($runtimeRoots | Where-Object { $_ } | Select-Object -Unique |
    Where-Object { Test-Path -LiteralPath (Join-Path $_ 'shared/Microsoft.WindowsDesktop.App') })
$runtimeKeys = @(foreach ($root in 'HKLM:\SOFTWARE\dotnet', 'HKLM:\SOFTWARE\WOW6432Node\dotnet') {
    foreach ($architecture in 'x64', 'x86', 'arm64') {
        $key = Join-Path $root "Setup\InstalledVersions\$architecture\sharedfx\Microsoft.WindowsDesktop.App"
        if (Test-Path -LiteralPath $key) { $key }
    }
})
$slots = [ordered]@{
    programRoot = Test-Path -LiteralPath (Join-Path $local 'Programs/DesktopMotionStudio')
    userData = Test-Path -LiteralPath (Join-Path $local 'DesktopMotionStudio')
    startMenuLink = Test-Path -LiteralPath (Join-Path $menu 'Desktop Motion Studio.lnk')
    uninstallKey = Test-Path -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopMotionStudio'
}
$checks = [ordered]@{
    windows11X64 = $os.ProductType -eq 1 -and [int]$os.BuildNumber -ge 22000 -and
        [Environment]::Is64BitProcess -and $processors.Count -gt 0 -and
        @($processors | Where-Object Architecture -NE 9).Count -eq 0
    interactive = [Environment]::UserInteractive -and [Diagnostics.Process]::GetCurrentProcess().SessionId -ne 0
    standardToken = !$adminGroup
    adaptersDisconnected = $connected.Count -eq 0
    noDesktopRuntimeFound = $runtimeFolders.Count -eq 0 -and $runtimeKeys.Count -eq 0
    productSlotsEmpty = $slots.Values -notcontains $true
    orderedCandidates = [version]$a.version -lt [version]$b.version -and $a.sha256 -ne $b.sha256
}
$record = [ordered]@{
    utc = [DateTime]::UtcNow.ToString('o')
    windowsBuild = $os.BuildNumber
    checks = $checks
    connectedAdapterCount = $connected.Count
    desktopRuntimeFolderCount = $runtimeFolders.Count
    desktopRuntimeRegistryCount = $runtimeKeys.Count
    occupiedSlots = $slots
    candidateA = $a
    candidateB = $b
}
$record | ConvertTo-Json -Depth 5
if ($checks.Values -contains $false) {
    $failed = @($checks.Keys | Where-Object { !$checks[$_] })
    throw "Clean guest preflight failed ($($failed -join ', '))."
}
