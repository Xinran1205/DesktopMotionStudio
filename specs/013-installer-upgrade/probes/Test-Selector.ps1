$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'selector'
dotnet run --project $project -c Release
if ($LASTEXITCODE -ne 0) { throw 'Selector process interruption proof failed.' }

$exe = Join-Path $project 'bin/Release/net10.0/SelectorProbe.exe'
$target = [IO.Directory]::CreateTempSubdirectory('dms-f12-selector-').FullName
$link = Join-Path ([IO.Path]::GetTempPath()) ('dms-f12-selector-link-' + [guid]::NewGuid().ToString('N'))
[IO.File]::WriteAllText((Join-Path $target 'active'), 'A')
New-Item -ItemType Junction -Path $link -Target $target | Out-Null
try {
    $value = & $exe --read $target
    if ($LASTEXITCODE -ne 0 -or $value -cne 'A') { throw 'Invalid reparse fixture.' }
    & $exe --read $link
    if ($LASTEXITCODE -ne 3) { throw 'Selector opened through a redirected fixture root.' }
    'PASS reparse root is rejected while the direct selector remains readable'
}
finally { (Get-Item -LiteralPath $link).Delete() }
