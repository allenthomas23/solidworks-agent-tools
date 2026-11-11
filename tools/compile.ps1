# Compiles every C# source against the installed SolidWorks interop without attaching to SolidWorks
# or touching a model. Run after editing any .cs file; a clean compile does not prove a COM call works.
$ErrorActionPreference='Stop'
. "$PSScriptRoot\CadEnvironment.ps1"
$cadEnvironment=Initialize-CadEnvironment
$tools=$PSScriptRoot
Import-CadCode $cadEnvironment -Path ((Get-CadCoreSources) + @('Validate.cs','Parameters.cs','Reopen.cs','Interference.cs','Sweep.cs','Register.cs','Materials.cs','Views.cs','Inspect.cs' | ForEach-Object { Join-Path $tools $_ }))
Write-Output 'PASS tools compile'
