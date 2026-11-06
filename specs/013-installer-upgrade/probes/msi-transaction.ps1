# Local platform proof. Its random product family and paths are unrelated to Studio.
# Run in PowerShell 7 on Windows x64. Preserve the printed log directory.
[CmdletBinding()]
param([switch]$Run)

if (!$Run) { throw 'This archived negative proof requires -Run in a disposable Windows account.' }
$ErrorActionPreference = 'Stop'
$id = [guid]::NewGuid().ToString('N')
$fixtureName = "DmsF12Probe-$id"
$scratch = (New-Item -ItemType Directory -Path (Join-Path ([IO.Path]::GetTempPath()) $fixtureName)).FullName
$local = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$destination = Join-Path $local $fixtureName
$marker = Join-Path $destination 'marker.txt'
$registry = "HKCU:\Software\$fixtureName"
$engine = New-Object -ComObject WindowsInstaller.Installer
$family = [guid]::NewGuid().ToString('B').ToUpperInvariant()
$component = [guid]::NewGuid().ToString('B').ToUpperInvariant()
$system = [Environment]::SystemDirectory

function Import-Table($database, [string]$folder, [string]$name, [string[]]$lines) {
    $file = "$name.idt"
    $lines.Replace('|', "`t") | Set-Content -LiteralPath (Join-Path $folder $file) -Encoding ascii
    $database.Import($folder, $file)
}

function New-ProbeMsi([string]$version) {
    $folder = (New-Item -ItemType Directory -Path (Join-Path $scratch $version)).FullName
    $payload = Join-Path $folder 'marker.txt'
    $version | Set-Content -LiteralPath $payload -Encoding ascii
    $cabinet = Join-Path $folder 'payload.cab'
    $ddf = Join-Path $folder 'payload.ddf'
    @('.Set Cabinet=on', '.Set Compress=on', '.Set CompressionType=MSZIP',
        '.Set CabinetNameTemplate=payload.cab', '.Set DiskDirectoryTemplate=.',
        '.Set InfFileName=payload.inf', '.Set RptFileName=payload.rpt',
        '"marker.txt" MarkerFile') | Set-Content -LiteralPath $ddf -Encoding ascii
    Push-Location -LiteralPath $folder
    try {
        & (Join-Path $system 'makecab.exe') /F $ddf | Out-Null
        if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $cabinet)) { throw 'Cabinet build failed.' }
    }
    finally { Pop-Location }
    $product = [guid]::NewGuid().ToString('B').ToUpperInvariant()
    $package = [guid]::NewGuid().ToString('B').ToUpperInvariant()
    $path = Join-Path $folder 'probe.msi'
    $db = $engine.OpenDatabase($path, 3)
    try {
        Import-Table $db $folder _SummaryInformation @('PropertyId|Value', 'i2|l255', '_SummaryInformation|PropertyId',
            '1|1252', '2|Installation Database', '3|DMS F12 platform proof', '4|DMS F12',
            '7|x64;1033', "9|$package", '14|500', '15|10', '18|Windows Installer')
        Import-Table $db $folder Property @('Property|Value', 's72|l0', 'Property|Property',
            "ProductCode|$product", "UpgradeCode|$family", "ProductVersion|$version", 'ProductLanguage|1033',
            "ProductName|$fixtureName", 'Manufacturer|DMS F12', 'INSTALLLEVEL|1', 'ARPNOMODIFY|1',
            'MSIRESTARTMANAGERCONTROL|DisableShutdown', 'SecureCustomProperties|DMS_OLD;DMS_NEWER;DMS_FAIL')
        Import-Table $db $folder Directory @('Directory|Directory_Parent|DefaultDir', 's72|S72|l255', 'Directory|Directory',
            'TARGETDIR||SourceDir', 'LocalAppDataFolder|TARGETDIR|.', "ProbeRoot|LocalAppDataFolder|$fixtureName")
        Import-Table $db $folder Component @('Component|ComponentId|Directory_|Attributes|Condition|KeyPath',
            's72|S38|s72|i2|S255|S72', 'Component|Component', "Marker|$component|ProbeRoot|260||MarkerKey")
        Import-Table $db $folder Registry @('Registry|Root|Key|Name|Value|Component_', 's72|i2|l255|L255|L0|s72',
            'Registry|Registry', "MarkerKey|1|Software\$fixtureName|Version|$version|Marker")
        Import-Table $db $folder File @('File|Component_|FileName|FileSize|Version|Language|Attributes|Sequence',
            's72|s72|l255|i4|S72|S20|I2|i2', 'File|File', "MarkerFile|Marker|marker.txt|$((Get-Item -LiteralPath $payload).Length)|||16384|1")
        Import-Table $db $folder Media @('DiskId|LastSequence|DiskPrompt|Cabinet|VolumeLabel|Source', 'i2|i4|L64|S255|S32|S72',
            'Media|DiskId', '1|1||#payload.cab||')
        Import-Table $db $folder Feature @('Feature|Feature_Parent|Title|Description|Display|Level|Directory_|Attributes',
            's38|S38|L64|L255|I2|i2|S72|i2', 'Feature|Feature', 'Probe||Probe||1|1||0')
        Import-Table $db $folder FeatureComponents @('Feature_|Component_', 's38|s72', 'FeatureComponents|Feature_|Component_', 'Probe|Marker')
        Import-Table $db $folder RemoveFile @('FileKey|Component_|FileName|DirProperty|InstallMode',
            's72|s72|L255|s72|i2', 'RemoveFile|FileKey', 'EmptyProbeRoot|Marker||ProbeRoot|2')
        Import-Table $db $folder Upgrade @('UpgradeCode|VersionMin|VersionMax|Language|Attributes|Remove|ActionProperty',
            's38|S20|S20|S255|i4|S255|s72', 'Upgrade|UpgradeCode|VersionMin|VersionMax|Language|Attributes',
            "$family||$version||0||DMS_OLD", "$family|$version|||2||DMS_NEWER")
        Import-Table $db $folder LaunchCondition @('Condition|Description', 's255|l255', 'LaunchCondition|Condition',
            'NOT ALLUSERS|This fixture supports per-user installation only.',
            'NOT DMS_NEWER|A later fixture version is already installed.',
            'NOT RollbackDisabled|Windows Installer rollback is required for this fixture.')
        Import-Table $db $folder CustomAction @('Action|Type|Source|Target|ExtendedType',
            's72|i2|S72|S255|I4', 'CustomAction|Action', 'ProbeFailure|19||DMS F12 intentional post-execution failure.|')
        Import-Table $db $folder InstallExecuteSequence @('Action|Condition|Sequence', 's72|S255|I2', 'InstallExecuteSequence|Action',
            'FindRelatedProducts||25', 'LaunchConditions||100', 'CostInitialize||800', 'FileCost||900', 'CostFinalize||1000',
            'InstallValidate||1400', 'InstallInitialize||1500', 'RemoveExistingProducts|DMS_OLD|1510',
            'ProcessComponents||1600', 'UnpublishFeatures||1800', 'RemoveRegistryValues||2600', 'RemoveFiles||3500',
            'InstallFiles||4000', 'WriteRegistryValues||5000', 'RegisterUser||6000', 'RegisterProduct||6100',
            'PublishFeatures||6300', 'PublishProduct||6400', 'InstallExecute|DMS_FAIL AND DMS_OLD|6500',
            'ProbeFailure|DMS_FAIL AND DMS_OLD|6550', 'InstallFinalize||6600')
        $stream = $engine.CreateRecord(2)
        $stream.StringData(1) = 'payload.cab'
        $stream.SetStream(2, $cabinet)
        $view = $db.OpenView('INSERT INTO `_Streams` (`Name`, `Data`) VALUES (?, ?)')
        $view.Execute($stream)
        $view.Close()
        $db.Commit()
        [pscustomobject]@{ Path = $path; Product = $product; Hash = (Get-FileHash -LiteralPath $payload).Hash }
    }
    finally {
        if ($view) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
        if ($stream) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($stream) }
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($db)
    }
}

function Invoke-ProbeMsi([string]$action, [string]$value, [string]$step, [string]$property = '') {
    $log = Join-Path $scratch "$step.log"
    $args = @($action, ('"{0}"' -f $value), '/qn', '/norestart', '/l*v', ('"{0}"' -f $log))
    if ($property) { $args += $property }
    $p = Start-Process -FilePath (Join-Path $system 'msiexec.exe') -ArgumentList $args -WindowStyle Hidden -PassThru -Wait
    try { $p.ExitCode }
    finally { $p.Dispose() }
}

function Assert-Probe([bool]$condition, [string]$description) {
    if (!$condition) { throw "FAIL $description (logs: $scratch)" }
    "PASS $description"
}

"Proof log directory: $scratch"
$a = New-ProbeMsi '1.0.0'
$b = New-ProbeMsi '1.0.1'
try {
    Assert-Probe ((Invoke-ProbeMsi '/i' $a.Path '01-install-a') -eq 0) 'per-user install A'
    Assert-Probe (($engine.ProductState($a.Product) -eq 5) -and ((Get-ItemProperty -LiteralPath $registry).Version -eq '1.0.0') -and
        ((Get-FileHash -LiteralPath $marker).Hash -eq $a.Hash)) 'A bytes and current-user registration'
    $foreign = Join-Path $destination 'foreign.txt'
    $id | Set-Content -LiteralPath $foreign -Encoding ascii
    $foreignHash = (Get-FileHash -LiteralPath $foreign).Hash
    Assert-Probe ((Invoke-ProbeMsi '/i' $b.Path '02-fail-b' 'DMS_FAIL=1') -eq 1603) 'B fails before finalize'
    $rollback = Get-Content -LiteralPath (Join-Path $scratch '02-fail-b.log') -Raw
    Assert-Probe ($rollback -match '(?s)Executing op: FileCopy.*DMS F12 intentional post-execution failure') 'B file operation executed before failure'
    Assert-Probe (($engine.ProductState($a.Product) -eq 5) -and ($engine.ProductState($b.Product) -ne 5) -and
        ((Get-ItemProperty -LiteralPath $registry).Version -eq '1.0.0') -and
        ((Get-FileHash -LiteralPath $marker).Hash -eq $a.Hash)) 'rollback restores exact A bytes and registration'
    Assert-Probe ((Invoke-ProbeMsi '/i' $b.Path '03-retry-b') -eq 0) 'B retry succeeds'
    Assert-Probe (($engine.ProductState($a.Product) -ne 5) -and ($engine.ProductState($b.Product) -eq 5) -and
        ((Get-ItemProperty -LiteralPath $registry).Version -eq '1.0.1') -and
        ((Get-FileHash -LiteralPath $marker).Hash -eq $b.Hash)) 'same path now has B bytes and one installed product'
    Assert-Probe ((Invoke-ProbeMsi '/i' $a.Path '04-downgrade-a') -eq 1603) 'downgrade is rejected'
    Assert-Probe ((Invoke-ProbeMsi '/i' $b.Path '05-repeat-b') -eq 0) 'repeated B uses same registration'
    Assert-Probe ((Invoke-ProbeMsi '/x' $b.Product '06-remove-b') -eq 0) 'ordinary uninstall'
    Assert-Probe (($engine.ProductState($b.Product) -ne 5) -and !(Test-Path -LiteralPath $marker) -and
        ((Get-FileHash -LiteralPath $foreign).Hash -eq $foreignHash)) 'uninstall removes owned file and preserves foreign bytes'
}
finally {
    foreach ($product in $a.Product, $b.Product) {
        if ($engine.ProductState($product) -eq 5) {
            $removed = Invoke-ProbeMsi '/x' $product "recovery-$($product.Trim('{}'))"
            if ($removed -ne 0) { Write-Warning "Fixture removal returned $removed for $product." }
        }
    }
    if ($foreign -and (Test-Path -LiteralPath $foreign) -and (Get-FileHash -LiteralPath $foreign).Hash -eq $foreignHash) {
        Remove-Item -LiteralPath $foreign
        if ((Test-Path -LiteralPath $destination) -and !(Get-ChildItem -LiteralPath $destination -Force)) {
            Remove-Item -LiteralPath $destination
        }
    }
}
