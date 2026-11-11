# Self-test of the environment discovery in CadEnvironment.ps1. Needs no SolidWorks; uses fake files in %TEMP%.
$ErrorActionPreference='Stop'
. "$PSScriptRoot\CadEnvironment.ps1"
$scratch=Join-Path ([IO.Path]::GetTempPath()) ('cad-env-tests-'+[guid]::NewGuid().ToString('N'))
$previousHome=$env:SOLIDWORKS_HOME; $previousModel=$env:CAD_MODEL
try {
    $fakeInstall=Join-Path $scratch 'Custom SolidWorks Install'
    New-Item -ItemType Directory -Path (Join-Path $fakeInstall 'api\redist') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $fakeInstall 'SLDWORKS.exe'),'test fixture; never executed')
    [IO.File]::WriteAllText((Join-Path $fakeInstall 'api\redist\SolidWorks.Interop.sldworks.dll'),'test fixture; never loaded')
    $model=Join-Path $scratch 'Alternate Assembly.SLDASM'
    [IO.File]::WriteAllText($model,'test assembly fixture')
    $part=Join-Path $scratch 'Some Part.SLDPRT'
    [IO.File]::WriteAllText($part,'test part fixture')
    $env:SOLIDWORKS_HOME=$fakeInstall; $env:CAD_MODEL=$null
    $ctx=Get-CadEnvironment -ModelPath $model
    if($ctx.SolidWorksHome -ne $fakeInstall -or $ctx.ModelPath -ne $model){throw 'Override resolution failed'}
    if((Get-CadEnvironment -ModelPath $part).ModelPath -ne $part){throw 'Part model rejected'}
    $env:CAD_MODEL=$part
    if((Get-CadEnvironment).ModelPath -ne $part){throw 'CAD_MODEL not used'}
    $env:CAD_MODEL=$null
    $failed=$false;try {Get-CadEnvironment -ModelPath $model -RequireSimulation | Out-Null}catch{$failed=$true}
    if(!$failed){throw 'Missing Simulation dependency was not rejected'}
    $text=Join-Path $scratch 'notes.txt'; [IO.File]::WriteAllText($text,'x')
    $failed=$false;try {Get-CadEnvironment -ModelPath $text | Out-Null}catch{$failed=$true}
    if(!$failed){throw 'Non-SolidWorks file was not rejected'}
    $env:SOLIDWORKS_HOME=Join-Path $scratch 'Missing Install'
    $failed=$false;try {Get-CadEnvironment -ModelPath $model | Out-Null}catch{$failed=$true}
    if(!$failed){throw 'Invalid override was not rejected'}
    $env:SOLIDWORKS_HOME=$fakeInstall
    [IO.File]::WriteAllText($model,'version https://git-lfs.github.com/spec/v1')
    $failed=$false;try {Get-CadEnvironment -ModelPath $model | Out-Null}catch{$failed=$true}
    if(!$failed){throw 'LFS pointer was not rejected'}
    Write-Output 'PASS: paths with spaces, parts and assemblies, CAD_MODEL, explicit overrides, wrong file types, missing install/Simulation, and LFS pointer detection.'
} finally {
    $env:SOLIDWORKS_HOME=$previousHome; $env:CAD_MODEL=$previousModel
    # Only delete the exact unique fixture directory created by this test.
    if([IO.Path]::GetFullPath($scratch).StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}
