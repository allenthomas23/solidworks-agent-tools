param([Alias('AssemblyPath')][string]$ModelPath, [string[]]$Filter, [string[]]$Against, [string[]]$Ignore, [switch]$IncludeHidden)
# Exact solid overlaps between visible physical parts (envelopes skipped). Read-only.
#   cad.ps1 interference                              every pair
#   cad.ps1 interference -Filter Bracket              brackets against everything
#   cad.ps1 interference -Filter Bracket -Against Plate -Ignore Screw,Insert
# Name filters match any part of the component name, case-insensitive. Writes reports/interference.json.
$ErrorActionPreference='Stop'
. "$PSScriptRoot\CadEnvironment.ps1"
$cadEnvironment=Initialize-CadEnvironment -ModelPath $ModelPath
Import-CadCode $cadEnvironment -Path "$PSScriptRoot\SwCad.cs","$PSScriptRoot\Interference.cs"
function Split-List($v){ [string[]]@($v | ForEach-Object { $_ -split ',' } | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() }) }
[CadInterference]::Run((Split-List $Filter),(Split-List $Against),(Split-List $Ignore),$IncludeHidden.IsPresent)
