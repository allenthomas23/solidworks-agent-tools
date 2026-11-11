param([Alias('AssemblyPath')][string]$ModelPath, [string[]]$Filter)
# Bill of materials with mass: one row per part file and configuration with quantity, material, density,
# unit mass, mass source and custom properties. Writes reports/component-register.csv/.json. Read-only.
$ErrorActionPreference='Stop'
. "$PSScriptRoot\CadEnvironment.ps1"
$cadEnvironment=Initialize-CadEnvironment -ModelPath $ModelPath
Import-CadCode $cadEnvironment -Path "$PSScriptRoot\SwCad.cs","$PSScriptRoot\Register.cs"
[CadRegister]::Run([string[]]@($Filter | ForEach-Object { $_ -split ',' } | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() }))
