param(
    [Alias('AssemblyPath')][string]$ModelPath,
    [Parameter(Mandatory)][string]$Dimension,
    [Parameter(Mandatory)][string[]]$Values,
    [string[]]$Moving,
    [string[]]$Track,
    [string[]]$Ignore
)
# Steps a mechanism through poses by setting one dimension, checks each pose, then restores it. Never saves.
#   cad.ps1 sweep -Dimension D1@Angle1 -Values 0deg,45deg,90deg -Moving Arm,Holder
# At each pose: rebuild errors, whether the dimension really reached the value (limits), where the -Track
# components are (position and rotation; defaults to -Moving) and interference of -Moving against the rest.
# Writes reports/sweep.json. Discrete poses only: add values where clearance is tight.
$ErrorActionPreference='Stop'
. "$PSScriptRoot\CadEnvironment.ps1"
$cadEnvironment=Initialize-CadEnvironment -ModelPath $ModelPath
Import-CadCode $cadEnvironment -Path "$PSScriptRoot\SwCad.cs","$PSScriptRoot\Interference.cs","$PSScriptRoot\Sweep.cs"
function Split-List($v){ [string[]]@($v | ForEach-Object { $_ -split ',' } | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() }) }
[CadSweep]::Run($Dimension,(Split-List $Values),(Split-List $Moving),(Split-List $Track),(Split-List $Ignore))
