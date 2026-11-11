param([Alias('AssemblyPath')][string]$ModelPath, [switch]$Quick, [switch]$Strict, [switch]$Interference)
# Rebuilds the open model and checks it: What's Wrong, mates, every part's features and sketches,
# component constraint state, optional interference, then project/ProjectChecks.cs. Never saves.
#   -Quick         skip the per-part feature/sketch scan (the slow part on big assemblies)
#   -Strict        treat under-defined sketches as failures
#   -Interference  also check every pair of physical parts for overlap
# Writes reports/validation.json; exits non-zero on failure.
$ErrorActionPreference='Stop'
. "$PSScriptRoot\CadEnvironment.ps1"
$cadEnvironment=Initialize-CadEnvironment -ModelPath $ModelPath
Import-CadCode $cadEnvironment -Path ((Get-CadCoreSources) + "$PSScriptRoot\Validate.cs")
[CadValidate]::Run($Quick.IsPresent,$Strict.IsPresent,$Interference.IsPresent)
