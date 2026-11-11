param([Alias('AssemblyPath')][string]$ModelPath, [switch]$CheckOpenModel)
# Read-only: finds SolidWorks, the interop and the model, and lists project material libraries.
# -CheckOpenModel also inspects the open model for missing parts, parts outside its folder and
# missing material-library paths (things that break on another computer).
$ErrorActionPreference='Stop'
. "$PSScriptRoot\CadEnvironment.ps1"
$context=Initialize-CadEnvironment -ModelPath $ModelPath
$context | Format-List
if (!$context.ModelPath) { Write-Output 'No model configured: commands will use the active SolidWorks document (set "model" in cad.config.json to pin one).' }
$materials=Join-Path $context.RepositoryRoot 'materials'
$libraries=@(if (Test-Path -LiteralPath $materials) { Get-ChildItem -LiteralPath $materials -Filter *.sldmat })
Write-Output "Project material libraries in materials\: $($libraries.Count)"
if ($CheckOpenModel) {
    Import-CadCode $context -TypeDefinition @'
using System;using System.IO;using System.Linq;using System.Runtime.InteropServices;using SolidWorks.Interop.sldworks;
public class CadReferenceInspection {
 public static void Run(string path){
  var sw=(ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
  var a=String.IsNullOrEmpty(path)?(IModelDoc2)sw.ActiveDoc:(IModelDoc2)sw.GetOpenDocumentByName(Path.GetFullPath(path));
  if(a==null)throw new Exception("Open the model in SolidWorks before running -CheckOpenModel.");
  path=a.GetPathName(); var folder=Path.GetDirectoryName(path)+Path.DirectorySeparatorChar;
  int count=0,external=0,missing=0,materialWarnings=0;
  var components=a is IAssemblyDoc?((object[])((IAssemblyDoc)a).GetComponents(false)??new object[0]).Cast<IComponent2>().ToArray():new IComponent2[0];
  foreach(var c in components){
   string file=c.GetPathName();count++;
   if(!File.Exists(file)){Console.WriteLine("MISSING part: "+file);missing++;}
   else if(!Path.GetFullPath(file).StartsWith(folder,StringComparison.OrdinalIgnoreCase)){Console.WriteLine("Outside the model folder: "+file);external++;}
   var d=c.GetModelDoc2() as IPartDoc;
   if(d!=null){string db;string material=d.GetMaterialPropertyName2(c.ReferencedConfiguration,out db);if(!String.IsNullOrEmpty(material)&&!String.IsNullOrEmpty(db)&&Path.IsPathRooted(db)&&!File.Exists(db)){Console.WriteLine("Material library path needs checking: "+material+" -> "+db);materialWarnings++;}}
  }
  Console.WriteLine("Model="+path+" components="+count+" missing="+missing+" outside model folder="+external+" material-path warnings="+materialWarnings);
  if(missing>0)throw new Exception("Some referenced parts are missing.");
 }
}
'@
    [CadReferenceInspection]::Run($context.ModelPath)
}
Write-Output 'Environment checks passed. Reference inspection runs only with -CheckOpenModel.'
