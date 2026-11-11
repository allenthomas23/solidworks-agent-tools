# Shared bootstrap. Dot-source this file; repository discovery never depends on the caller's CWD.
#
# Which model a command works on, first match wins:
#   1. -ModelPath (alias -AssemblyPath) on the command line
#   2. the CAD_MODEL environment variable
#   3. "model" in cad.config.json at the repository root (a path relative to the root)
#   4. nothing: the command uses the document that is active in SolidWorks
$script:CadRepositoryRoot = Split-Path -Parent $PSScriptRoot

function Get-CadSolidWorksHome {
    $candidates = New-Object 'System.Collections.Generic.List[string]'
    if ($env:SOLIDWORKS_HOME) {
        $candidates.Add($env:SOLIDWORKS_HOME)
    } else {
        Get-Process SLDWORKS -ErrorAction SilentlyContinue | ForEach-Object {
            if ($_.Path) { $candidates.Add((Split-Path -Parent $_.Path)) }
        }
        foreach ($hive in @('HKLM:', 'HKCU:')) {
            $appKey = "$hive\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\SLDWORKS.exe"
            if (Test-Path -LiteralPath $appKey) {
                $exe = (Get-Item -LiteralPath $appKey).GetValue('')
                if ($exe) { $candidates.Add((Split-Path -Parent $exe.Trim('"'))) }
            }
            $swKey = "$hive\SOFTWARE\SolidWorks"
            Get-ChildItem -LiteralPath $swKey -ErrorAction SilentlyContinue | Sort-Object PSChildName -Descending | ForEach-Object {
                $entry = Get-ItemProperty -LiteralPath $_.PSPath
                foreach ($name in @('SolidWorks Folder', 'SolidWorksFolder', 'InstallDir', 'InstallLocation')) {
                    $value = $entry.PSObject.Properties[$name]
                    if ($value -and $value.Value) { $candidates.Add([string]$value.Value) }
                }
            }
        }
        if ($env:ProgramFiles) { $candidates.Add((Join-Path $env:ProgramFiles 'SOLIDWORKS Corp\SOLIDWORKS')) }
    }
    foreach ($candidate in $candidates) {
        if ((Test-Path -LiteralPath (Join-Path $candidate 'SLDWORKS.exe')) -and
            (Test-Path -LiteralPath (Join-Path $candidate 'api\redist\SolidWorks.Interop.sldworks.dll'))) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    throw 'SolidWorks installation not found. Set SOLIDWORKS_HOME to the installation folder containing SLDWORKS.exe and api\redist.'
}

function Get-CadConfiguredModel {
    if ($env:CAD_MODEL) { return $env:CAD_MODEL }
    $config = Join-Path $script:CadRepositoryRoot 'cad.config.json'
    if (Test-Path -LiteralPath $config) {
        $json = Get-Content -Raw -LiteralPath $config | ConvertFrom-Json
        if ($json.model) {
            if ([IO.Path]::IsPathRooted($json.model)) { throw 'cad.config.json "model" must be relative to the repository root (no machine-specific paths).' }
            return (Join-Path $script:CadRepositoryRoot $json.model)
        }
    }
    return $null
}

function Get-CadEnvironment {
    [CmdletBinding()]
    param([Alias('AssemblyPath')][string]$ModelPath, [switch]$RequireSimulation)
    $installationHome = Get-CadSolidWorksHome
    if (!$ModelPath) { $ModelPath = Get-CadConfiguredModel }
    $resolvedModel = ''
    if ($ModelPath) {
        $resolvedModel = (Resolve-Path -LiteralPath $ModelPath -ErrorAction Stop).Path
        if ([IO.Path]::GetExtension($resolvedModel) -notin '.sldasm', '.sldprt') { throw 'The model must be a .SLDASM or .SLDPRT file.' }
        $stream = [IO.FileStream]::new($resolvedModel, [IO.FileMode]::Open, [IO.FileAccess]::Read, ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        try {
            $header = New-Object byte[] 80
            $count = $stream.Read($header, 0, $header.Length)
            if ([Text.Encoding]::UTF8.GetString($header, 0, $count).StartsWith('version https://git-lfs.github.com/spec/')) {
                throw 'The model is a Git LFS pointer. Run git lfs pull before using the CAD tools.'
            }
        } finally { $stream.Dispose() }
    }
    $redist = Join-Path $installationHome 'api\redist'
    if ($RequireSimulation -and !(Test-Path -LiteralPath (Join-Path $redist 'SolidWorks.Interop.cosworks.dll'))) {
        throw 'SolidWorks Simulation interop was not found in this installation.'
    }
    [pscustomobject]@{
        RepositoryRoot = $script:CadRepositoryRoot
        Reports = Join-Path $script:CadRepositoryRoot 'reports'
        SolidWorksHome = $installationHome
        Redist = $redist
        Interop = Join-Path $redist 'SolidWorks.Interop.sldworks.dll'
        ModelPath = $resolvedModel   # empty = the active SolidWorks document
    }
}

function Initialize-CadEnvironment {
    [CmdletBinding()]
    param([Alias('AssemblyPath')][string]$ModelPath, [switch]$RequireSimulation)
    if ($PSVersionTable.PSEdition -ne 'Desktop' -or $PSVersionTable.PSVersion.Major -ne 5) {
        throw 'Run this tool using Windows PowerShell 5.1: powershell.exe -NoProfile -File <script.ps1>'
    }
    $context = Get-CadEnvironment -ModelPath $ModelPath -RequireSimulation:$RequireSimulation
    # Process-scoped values the C# helpers read (SwCad.Model, material libraries).
    $env:CAD_MODEL_PATH = $context.ModelPath
    $env:CAD_SOLIDWORKS_HOME = $context.SolidWorksHome
    $env:CAD_REPOSITORY_ROOT = $context.RepositoryRoot
    [void](New-Item -ItemType Directory -Force -Path $context.Reports)
    $context
}

function Import-CadCode {
    # Compiles C# sources against the SolidWorks interop once and reuses the DLL until a source,
    # the interop or the reference set changes. Cache: %LOCALAPPDATA%\SolidWorksAiCad\build (safe to delete).
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Environment,
        [string[]]$Path,
        [string]$TypeDefinition,
        [switch]$Simulation
    )
    if (!$Path -and !$TypeDefinition) { throw 'Import-CadCode needs -Path or -TypeDefinition.' }
    $interops = @($Environment.Interop)
    if ($Simulation) { $interops += Join-Path $Environment.Redist 'SolidWorks.Interop.cosworks.dll' }
    foreach ($interop in $interops) { Add-Type -Path $interop }
    $references = $interops + @('System.Core', 'System.Web.Extensions', 'System.Drawing')
    $key = New-Object Text.StringBuilder
    foreach ($interop in $interops) { $item = Get-Item -LiteralPath $interop; [void]$key.AppendLine("$($item.FullName)|$($item.Length)|$($item.LastWriteTimeUtc.Ticks)") }
    [void]$key.AppendLine(($references -join '|'))
    [void]$key.AppendLine($PSVersionTable.CLRVersion.ToString())
    $sources = @()
    foreach ($file in $Path) {
        $full = (Resolve-Path -LiteralPath $file).Path
        $sources += $full
        [void]$key.AppendLine([IO.File]::ReadAllText($full))
    }
    if ($TypeDefinition) { [void]$key.AppendLine($TypeDefinition) }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = -join ($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($key.ToString())) | ForEach-Object { $_.ToString('x2') }) } finally { $sha.Dispose() }
    $cacheRoot = Join-Path $env:LOCALAPPDATA 'SolidWorksAiCad\build'
    $dll = Join-Path $cacheRoot ($hash.Substring(0, 32) + '.dll')
    if (!(Test-Path -LiteralPath $dll)) {
        [void](New-Item -ItemType Directory -Force -Path $cacheRoot)
        $temp = Join-Path $cacheRoot ([Guid]::NewGuid().ToString('N') + '.tmp.dll')
        $compile = @{ ReferencedAssemblies = $references; OutputAssembly = $temp; OutputType = 'Library' }
        if ($TypeDefinition) { $compile.TypeDefinition = $TypeDefinition } else { $compile.Path = $sources }
        Add-Type @compile
        try { Move-Item -LiteralPath $temp -Destination $dll -ErrorAction Stop }
        catch { if (Test-Path -LiteralPath $dll) { Remove-Item -LiteralPath $temp -ErrorAction SilentlyContinue } else { throw } }
    }
    Add-Type -Path $dll
}

# The standard C# sources every command compiles with (SwCad helpers plus the project's checks).
function Get-CadCoreSources {
    @((Join-Path $PSScriptRoot 'SwCad.cs'), (Join-Path $script:CadRepositoryRoot 'project\ProjectChecks.cs'))
}
