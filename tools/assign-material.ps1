param(
    [Alias('AssemblyPath')][string]$ModelPath,
    [Parameter(Mandatory)][string[]]$Filter,
    [Parameter(Mandatory)][string]$Material,
    [string]$Database
)
# Assigns one material to every part whose component name matches -Filter (all configurations), checks the
# geometry is unchanged, sets the part's Mass_Basis property and saves those parts only.
#   cad.ps1 assign-material -Filter Bracket -Material "6061 Alloy"
#   cad.ps1 assign-material -Filter Arm -Material PLA -Database "Project Polymers.sldmat"   (a library in this repository)
# Material names must match the library exactly. Refuses parts with unsaved changes.
$ErrorActionPreference='Stop'
. "$PSScriptRoot\CadEnvironment.ps1"
$cadEnvironment=Initialize-CadEnvironment -ModelPath $ModelPath
Import-CadCode $cadEnvironment -Path "$PSScriptRoot\SwCad.cs","$PSScriptRoot\Materials.cs"
[CadMaterials]::Run([string[]]@($Filter | ForEach-Object { $_ -split ',' } | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() }),$Material,$Database)
