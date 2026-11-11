param([Alias('AssemblyPath')][string]$ModelPath, [switch]$SkipInitialValidation)
# Proves the saved files hold the design: requires everything saved, records globals and component
# placement, closes the model, reopens it from disk, compares and validates again. Never saves.
# Use after changing references, equations that cross files, or file names.
$ErrorActionPreference='Stop'
. "$PSScriptRoot\CadEnvironment.ps1"
$cadEnvironment=Initialize-CadEnvironment -ModelPath $ModelPath
Import-CadCode $cadEnvironment -Path ((Get-CadCoreSources) + "$PSScriptRoot\Validate.cs","$PSScriptRoot\Reopen.cs")
[CadReopen]::Run($SkipInitialValidation.IsPresent)
