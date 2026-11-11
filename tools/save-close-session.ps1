param([ValidateSet('Inspect','Save','Close')][string]$Mode='Inspect')
$ErrorActionPreference='Stop'
# Run using Windows PowerShell 5.1. Save/Close are only for an explicit user request to save/close the entire session.
. "$PSScriptRoot\CadEnvironment.ps1"
$cadEnvironment=Initialize-CadEnvironment
Import-CadCode $cadEnvironment -TypeDefinition @'
using System;using System.Linq;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Web.Script.Serialization;using SolidWorks.Interop.sldworks;
public class SafeCadSessionClose {
static ISldWorks sw;static IModelDoc2[] Docs(){var x=(object[])sw.GetDocuments();return x==null?new IModelDoc2[0]:x.Cast<IModelDoc2>().ToArray();}
public static void Run(string mode){sw=(ISldWorks)Marshal.GetActiveObject("SldWorks.Application");var json=new JavaScriptSerializer();if(mode=="Inspect"){Console.WriteLine(json.Serialize(Docs().Select(d=>new{title=d.GetTitle(),path=d.GetPathName(),dirty=d.GetSaveFlag()}).ToArray()));return;}
for(int pass=0;pass<3;pass++){var pending=Docs().Where(d=>d.GetSaveFlag()).OrderBy(d=>d.GetType()==1?0:1).ToArray();if(pending.Length==0)break;Console.WriteLine("Saving pass "+(pass+1)+": "+pending.Length+" documents");foreach(var d in pending){string path=d.GetPathName();if(String.IsNullOrEmpty(path))throw new Exception("Untitled document needs a user-selected filename: "+d.GetTitle());int error=0,warning=0;bool ok=d.Save3(1,ref error,ref warning);Console.WriteLine("Saved "+path+" ok="+ok+" errors="+error+" warnings="+warning);if(!ok||error!=0)throw new Exception("Save failed: "+path);}}
var remaining=Docs().Where(d=>d.GetSaveFlag()).Select(d=>d.GetPathName()).ToArray();Console.WriteLine("Remaining modified documents: "+json.Serialize(remaining));if(remaining.Length>0)throw new Exception("Documents keep becoming modified; session left open for inspection");if(mode=="Close"){Console.WriteLine("All documents saved; requesting normal SolidWorks exit");sw.ExitApp();}}
}
'@
[SafeCadSessionClose]::Run($Mode)
