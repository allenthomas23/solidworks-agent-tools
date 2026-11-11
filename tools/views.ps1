param(
    [Alias('AssemblyPath')][string]$ModelPath,
    [string[]]$View = @('Isometric'),
    [string[]]$Isolate,
    [string]$OutDir,
    [int]$Width = 0,
    [int]$Height = 0,
    [string]$Prefix
)
# Saves PNG images of the real model, by default to reports/views/. Restores the view afterwards; never saves.
#   cad.ps1 views                                   isometric
#   cad.ps1 views -View Isometric,Front,Top,Right
#   cad.ps1 views -Isolate Bracket,Plate            only those components
# -Width/-Height default to the SolidWorks window size (other sizes can crop or shrink the fitted view).
# -View takes standard or saved named views. Show the PNG to the user after a geometry change.
$ErrorActionPreference='Stop'
. "$PSScriptRoot\CadEnvironment.ps1"
$cadEnvironment=Initialize-CadEnvironment -ModelPath $ModelPath
Import-CadCode $cadEnvironment -Path "$PSScriptRoot\SwCad.cs","$PSScriptRoot\Views.cs"
function Split-List($v){ [string[]]@($v | ForEach-Object { $_ -split ',' } | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() }) }
[CadViews]::Run((Split-List $View),(Split-List $Isolate),$OutDir,$Width,$Height,$Prefix)
