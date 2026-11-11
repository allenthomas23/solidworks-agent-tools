# No param block on purpose: every argument after the command is passed through untouched
# (an advanced script would claim names like -Out or -Verbose for itself).
$Command = if ($args.Count) { [string]$args[0] } else { '' }
$Arguments = @(if ($args.Count -gt 1) { $args[1..($args.Count - 1)] })
# One entry point for the maintained CAD commands. With no command, lists them.
#   powershell.exe -NoProfile -File .\tools\cad.ps1                  # list
#   powershell.exe -NoProfile -File .\tools\cad.ps1 validate         # run
#   powershell.exe -NoProfile -File .\tools\cad.ps1 inspect -Filter Bracket
# Every model command accepts -ModelPath; without it they use cad.config.json or the active SolidWorks document.
# Add new maintained commands to this table: general ones in tools\, ones tied to your design in project\.
$ErrorActionPreference='Stop'
$repo = Split-Path -Parent $PSScriptRoot
$commands = [ordered]@{
    'inspect'           = @('tools\inspect.ps1', '', 'read', 'JSON snapshot: parameters, errors; -Components/-Filter for placement, bounds, mass, properties')
    'validate'          = @('tools\validate.ps1', '', 'read', 'Rebuild and check errors, mates, sketches, constraints, project checks (-Quick, -Strict, -Interference)')
    'set'               = @('tools\set-parameters.ps1', '', 'saves', 'Change global variables (-Set Name=Value); validates, rolls back on failure; -Test restores')
    'verify-reopen'     = @('tools\verify-reopen.ps1', '', 'read', 'Close, reopen from disk, compare globals and placement, validate (needs everything saved)')
    'interference'      = @('tools\interference.ps1', '', 'read', 'Exact part overlaps (-Filter, -Against, -Ignore)')
    'sweep'             = @('tools\sweep.ps1', '', 'moves', 'Step a mechanism (-Dimension D1@Mate -Values 0deg,45deg -Moving ...); restores')
    'register'          = @('tools\register.ps1', '', 'read', 'BOM with quantity, material, mass and part properties (writes reports)')
    'assign-material'   = @('tools\assign-material.ps1', '', 'saves', 'Material for parts matching -Filter; checks geometry unchanged')
    'views'             = @('tools\views.ps1', '', 'read', 'PNG images of the model (-View, -Isolate) to reports\views')
    'session'           = @('tools\save-close-session.ps1', '', 'read', 'List open documents; -Mode Save|Close only when the user asks')
    'check-env'         = @('tools\check-cad-environment.ps1', '', 'read', 'Find SolidWorks and the model; -CheckOpenModel checks references')
    'test-env'          = @('tools\test-cad-environment.ps1', '', 'read', 'Self-test of the environment discovery (no SolidWorks needed)')
    'compile'           = @('tools\compile.ps1', '', 'read', 'Compile every C# source without touching a model')
    # Project commands go here, e.g.:
    # 'frame-weight'    = @('project\frame-weight.ps1', '', 'read', 'Weight of the frame parts')
}

function Show-Commands {
    'Usage: powershell.exe -NoProfile -File .\tools\cad.ps1 <command> [arguments]'
    ''
    foreach ($name in $commands.Keys) { '  {0,-19} {1,-6} {2}' -f $name, $commands[$name][2], $commands[$name][3] }
    ''
    'read = leaves the design files unchanged (may rebuild, write reports); moves = changes the open model without saving; saves = changes and saves the design.'
    $listed = @($commands.Values | ForEach-Object { (Join-Path $repo $_[0]).ToLowerInvariant() })
    $helpers = @('tools\cad.ps1', 'tools\CadEnvironment.ps1') | ForEach-Object { (Join-Path $repo $_).ToLowerInvariant() }
    $unlisted = foreach ($dir in 'tools', 'project') {
        $full = Join-Path $repo $dir
        if (Test-Path -LiteralPath $full) {
            Get-ChildItem -LiteralPath $full -Filter *.ps1 | Where-Object { $listed -notcontains $_.FullName.ToLowerInvariant() -and $helpers -notcontains $_.FullName.ToLowerInvariant() }
        }
    }
    foreach ($file in $unlisted) { "WARNING: $($file.FullName.Substring($repo.Length + 1)) is not listed in tools\cad.ps1" }
}

if (!$Command -or $Command -in 'help', '-h', '--help', '/?') { Show-Commands; exit 0 }
if (!$commands.Contains($Command)) { Show-Commands; throw "Unknown command '$Command'." }
$entry = $commands[$Command]
$fixed = @($entry[1] -split ' ' | Where-Object { $_ })
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo $entry[0]) @fixed @Arguments
exit $LASTEXITCODE
