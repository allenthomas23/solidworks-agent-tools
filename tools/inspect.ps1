param(
    [Alias('AssemblyPath')][string]$ModelPath,
    [switch]$Parameters,
    [switch]$Errors,
    [switch]$DeepErrors,
    [switch]$Components,
    [string[]]$Filter,
    [switch]$Documents,
    [switch]$All,
    [string]$Out
)
# Read-only JSON snapshot of the open model: never rebuilds, selects, moves or saves, so values
# reflect the last rebuild. With no section switches: summary, parameters and errors.
#   -Parameters  equations/global variables with values
#   -Errors      SolidWorks' What's Wrong list (fast)
#   -DeepErrors  also ask every feature and mate in the model and its parts (slow on big assemblies)
#   -Components  per component: file, configuration, constraint state, origin, bounding box, material,
#                mass and custom properties (design notes such as Mass_Basis)
#   -Filter a,b  only components whose name contains a or b (implies -Components; fast)
#   -Documents   open SolidWorks documents and their unsaved state
#   -Out <file>  write the JSON to a file instead of the console
$ErrorActionPreference='Stop'
. "$PSScriptRoot\CadEnvironment.ps1"
$cadEnvironment=Initialize-CadEnvironment -ModelPath $ModelPath
Import-CadCode $cadEnvironment -Path "$PSScriptRoot\SwCad.cs","$PSScriptRoot\Inspect.cs"
$filters=@($Filter | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$p=[bool]$Parameters; $e=[bool]$Errors; $de=[bool]$DeepErrors; $c=[bool]$Components -or $filters.Count -gt 0; $d=[bool]$Documents
if($All){ $p=$true; $e=$true; $c=$true; $d=$true }
elseif(!($p -or $e -or $de -or $c -or $d)){ $p=$true; $e=$true }
$json=[CadInspect]::Run($p,$e,$de,$c,[string[]]$filters,$d)
if($Out){ [IO.File]::WriteAllText([IO.Path]::GetFullPath($Out),$json); "Wrote $Out" } else { $json }
