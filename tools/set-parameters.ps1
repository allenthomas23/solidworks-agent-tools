param(
    [Alias('AssemblyPath')][string]$ModelPath,
    [string[]]$Set,
    [string]$ChangesJson,
    [switch]$Test,
    [switch]$Strict,
    [switch]$Interference
)
# The one way to change master inputs: global variables in the open model (Tools > Equations).
#   cad.ps1 set -Set Width=120mm,Height=40          (no unit = keep the variable's current unit)
#   cad.ps1 set -ChangesJson changes.json            ({"Width": "120mm", "Count": "6"})
#   cad.ps1 set -Set Width=140 -Test                 (validate, then restore; saves nothing)
# Applies the change, fully rebuilds, validates (validate.ps1 checks + project/ProjectChecks.cs) and saves
# only the documents that really changed. On any failure it restores the old values and saves nothing.
# Refuses to run if the model has unsaved changes, so it never saves someone else's work.
# Derived globals (defined by an expression) are rejected: change their inputs instead.
$ErrorActionPreference='Stop'
. "$PSScriptRoot\CadEnvironment.ps1"
$cadEnvironment=Initialize-CadEnvironment -ModelPath $ModelPath
$changes=[ordered]@{}
foreach($item in @($Set | ForEach-Object { $_ -split ',' } | Where-Object { $_.Trim() })){
    $pair=$item -split '=',2
    if($pair.Count -ne 2){ throw "Use Name=Value (got '$item')." }
    $changes[$pair[0].Trim()]=$pair[1].Trim()
}
if($ChangesJson){ (Get-Content -Raw -LiteralPath $ChangesJson | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $changes[$_.Name]=[string]$_.Value } }
if($changes.Count -eq 0){ throw 'Give -Set Name=Value[,Name=Value] or -ChangesJson <file>. Read current values with cad.ps1 inspect -Parameters.' }
Import-CadCode $cadEnvironment -Path ((Get-CadCoreSources) + "$PSScriptRoot\Validate.cs","$PSScriptRoot\Parameters.cs")
[CadParameters]::Set(($changes | ConvertTo-Json -Compress),$Test.IsPresent,$Strict.IsPresent,$Interference.IsPresent)
