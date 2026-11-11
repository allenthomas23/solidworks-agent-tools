using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

// `cad.ps1 verify-reopen`: proves the saved files, not just the session, hold the design.
// Requires everything saved; records globals and component placement; closes the model and its hidden
// documents; reopens from disk; compares; rebuilds and validates. Never saves.
public static class CadReopen {
    // swFileLoadWarning_e bits worth naming; "needs rebuild" is normal after saving an assembly.
    static string LoadWarnings(int w) {
        if(w==0) return "";
        var names=new List<string>();
        var known=new[]{Tuple.Create(1,"ID mismatch"),Tuple.Create(2,"read-only"),Tuple.Create(4,"file in use"),Tuple.Create(32,"needs rebuild"),Tuple.Create(64,"base part not loaded"),Tuple.Create(128,"already open"),Tuple.Create(1024,"missing references")};
        foreach(var k in known) if((w&k.Item1)!=0) names.Add(k.Item2);
        return " (load warnings "+w+(names.Count>0?": "+String.Join(", ",names):"")+")";
    }
    public static void Run(bool skipValidation) {
        var sw=new SwCad(); var model=sw.Model();
        string path=model.GetPathName(); int type=model.GetType();
        SwCad.RequireClean(model,"verifying a reopen");
        if(!skipValidation) {
            var first=CadValidate.Check(sw,model,new CadValidate.Options{Rebuild=false});
            CadValidate.Print(first);
            if(!first.Passed) throw new Exception("Fix validation failures before verifying a reopen.");
        }
        var config=((IConfiguration)model.GetActiveConfiguration()).Name;
        var globals=CadValidate.GlobalValues(model);
        var placements=SwCad.Components(model).Where(c=>!c.IsSuppressed()&&c.Transform2!=null)
            .GroupBy(c=>c.Name2).ToDictionary(g=>g.Key,g=>(double[])g.First().Transform2.ArrayData);
        var referenced=SwCad.Documents(model).Select(d=>d.GetPathName()).Where(p=>!p.Equals(path,StringComparison.OrdinalIgnoreCase)).ToArray();

        sw.App.CloseDoc(model.GetTitle());
        var stillOpen=new List<string>();
        foreach(var p in referenced) {
            var d=sw.App.GetOpenDocumentByName(p) as IModelDoc2; if(d==null) continue;
            if(d.Visible) { stillOpen.Add(p); continue; }   // the user has it open in its own window; leave it
            sw.App.CloseDoc(d.GetTitle());
        }
        int errors=0,warnings=0;
        var reopened=sw.App.OpenDoc6(path,type,1,"",ref errors,ref warnings) as IModelDoc2;
        if(reopened==null||errors!=0) throw new Exception("Reopen failed: errors="+errors+" warnings="+warnings);
        sw.Activate(reopened);
        Console.WriteLine("Reopened "+Path.GetFileName(path)+" from disk"+LoadWarnings(warnings));
        if(stillOpen.Count>0) Console.WriteLine("Note: kept in memory because they are open in their own windows, so not reloaded from disk: "+String.Join(", ",stillOpen.Select(Path.GetFileName)));

        var problems=new List<string>();
        string reopenedConfig=((IConfiguration)reopened.GetActiveConfiguration()).Name;
        if(reopenedConfig!=config) problems.Add("Active configuration "+reopenedConfig+", was "+config);
        var after=CadValidate.GlobalValues(reopened);
        foreach(var kv in globals) {
            if(!after.ContainsKey(kv.Key)) problems.Add("Global missing after reopen: "+kv.Key);
            else if(Math.Abs(after[kv.Key]-kv.Value)>1e-9) problems.Add("Global "+kv.Key+" "+kv.Value+" -> "+after[kv.Key]);
        }
        var now=SwCad.Components(reopened).Where(c=>!c.IsSuppressed()&&c.Transform2!=null).GroupBy(c=>c.Name2).ToDictionary(g=>g.Key,g=>(double[])g.First().Transform2.ArrayData);
        foreach(var kv in placements) {
            if(!now.ContainsKey(kv.Key)) { problems.Add("Component missing after reopen: "+kv.Key); continue; }
            for(int i=0;i<12;i++) if(Math.Abs(now[kv.Key][i]-kv.Value[i])>2e-7) { problems.Add("Component moved after reopen: "+kv.Key); break; }
        }
        var check=CadValidate.Check(sw,reopened,new CadValidate.Options{Rebuild=true});
        CadValidate.Print(check);
        problems.AddRange(check.Failures);
        reopened=sw.RevertToDisk(reopened);   // the check's rebuild marks documents unsaved; they still match the files
        SwCad.WriteJson("reopen-verification.json",new{model=path,verifiedAt=DateTimeOffset.Now.ToString("o"),openWarnings=warnings,keptInMemory=stillOpen,globalsCompared=globals.Count,componentsCompared=placements.Count,problems=problems,validation=check.Report});
        if(problems.Count>0) { foreach(var p in problems) Console.WriteLine("FAIL "+p); throw new Exception("Reopen verification failed; see reports/reopen-verification.json"); }
        Console.WriteLine("PASS reopen: "+globals.Count+" globals and "+placements.Count+" component placements match the saved files");
    }
}
