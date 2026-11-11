using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

// Generic model health check, shared by `validate`, `set` and `verify-reopen`.
// Fails on: rebuild errors (What's Wrong), mate errors, feature errors in any referenced part,
// over-defined or unsolvable sketches, interference (with -Interference) and anything ProjectChecks adds.
// Reports as warnings: under-defined sketches (failures with -Strict), fixed and floating components.
public static class CadValidate {
    public sealed class Options { public bool Rebuild=true, Deep=true, Strict=false, Interference=false; }
    public sealed class Result {
        public List<string> Failures=new List<string>(), Warnings=new List<string>();
        public Dictionary<string,object> Report=new Dictionary<string,object>();
        public bool Passed { get { return Failures.Count==0; } }
    }

    public static Dictionary<string,double> GlobalValues(IModelDoc2 model) {
        return SwCad.Globals(model).ToDictionary(g=>g.Key,g=>g.Value.Item3);
    }

    public static Result Check(SwCad sw, IModelDoc2 model, Options o) {
        var r=new Result(); var started=DateTime.Now;
        if(o.Rebuild) { sw.Activate(model); model.GetEquationMgr().EvaluateAll(); model.EditRebuild3(); }

        // 1. SolidWorks' own What's Wrong list (fast, covers the whole model tree).
        object features, codes, warns;
        if(model.Extension.GetWhatsWrongCount()>0 && model.Extension.GetWhatsWrong(out features,out codes,out warns) && features!=null) {
            var f=(object[])features; var c=(int[])codes; var w=(bool[])warns;
            for(int i=0;i<f.Length;i++) { string text="What's Wrong: "+((IFeature)f[i]).Name+" (code "+c[i]+")"; if(w[i])r.Warnings.Add(text); else r.Failures.Add(text); }
        }

        // 2. Mates and assembly features, 3. every referenced part (deep).
        int mates=0; var underdefined=new List<string>();
        var docs=o.Deep?SwCad.Documents(model):new[]{model};
        foreach(var d in docs) {
            string owner=Path.GetFileName(d.GetPathName());
            foreach(var kv in SwCad.Features(d)) {
                var f=kv.Value; if(f.IsSuppressed()) continue;
                bool warning; int code=f.GetErrorCode2(out warning);
                bool mate=f.GetSpecificFeature2() is IMate2; if(mate&&d==model)mates++;
                if(code!=0) { string text=(mate?"Mate ":"Feature ")+owner+":"+kv.Key+" error "+code; if(warning)r.Warnings.Add(text); else r.Failures.Add(text); }
                if(f.GetTypeName2()=="ProfileFeature") {
                    var sketch=f.GetSpecificFeature2() as ISketch; if(sketch==null) continue;
                    int status=sketch.GetConstrainedStatus();   // 2 under, 3 fully, 4 over, 5 no solution, 6 invalid
                    if(status==2) underdefined.Add(owner+":"+kv.Key);
                    else if(status>=4) r.Failures.Add("Sketch "+owner+":"+kv.Key+" is over-defined or unsolved (status "+status+")");
                }
            }
        }
        if(underdefined.Count>0) { string text="Under-defined sketches: "+String.Join(", ",underdefined); if(o.Strict)r.Failures.Add(text); else r.Warnings.Add(text); }
        var all=SwCad.Components(model).Where(c=>!c.IsSuppressed()).ToArray();
        var lightweight=all.Where(c=>c.GetModelDoc2()==null).Select(c=>c.Name2).ToArray();
        if(o.Deep&&lightweight.Length>0) r.Warnings.Add("Lightweight or unresolved, not checked: "+String.Join(", ",lightweight));

        // 4. Component constraint state (top level). One fixed base component is normal.
        var top=SwCad.Components(model,true).Where(c=>!c.IsSuppressed()).ToArray();
        var fixedComponents=top.Where(c=>c.IsFixed()).Select(c=>c.Name2).ToArray();
        var floating=top.Where(c=>!c.IsFixed()&&c.GetConstrainedStatus()!=3).Select(c=>c.Name2).ToArray();
        if(fixedComponents.Length>1) r.Warnings.Add("Fixed components (more than the base part usually hides a missing mate): "+String.Join(", ",fixedComponents));
        if(floating.Length>0) r.Warnings.Add("Not fully constrained (fine for intended motion): "+String.Join(", ",floating));

        // 5. Interference between all physical parts (optional; slower).
        if(o.Interference) {
            var parts=all.Where(c=>!c.IsEnvelope()&&c.GetModelDoc2() is IPartDoc).SelectMany(c=>SwCad.Bodies(c)).ToList();
            var clashes=SwCad.Intersections(parts,parts);
            foreach(var clash in clashes) r.Failures.Add("Interference "+clash);
            r.Report["interference"]=clashes;
        }

        // 6. Project-specific rules.
        var globals=GlobalValues(model);
        int before=r.Failures.Count;
        try { ProjectChecks.Run(sw,model,globals,r.Failures); }
        catch(Exception e) { r.Failures.Add("ProjectChecks threw: "+e.Message); }
        if(r.Failures.Count==before) Console.WriteLine("PASS project checks");

        r.Report["model"]=model.GetPathName();
        r.Report["checkedAt"]=DateTimeOffset.Now.ToString("o");
        r.Report["seconds"]=Math.Round((DateTime.Now-started).TotalSeconds,1);
        r.Report["documentsChecked"]=docs.Length;
        r.Report["mates"]=mates;
        r.Report["globals"]=globals;
        r.Report["fixedComponents"]=fixedComponents;
        r.Report["notFullyConstrained"]=floating;
        r.Report["underdefinedSketches"]=underdefined;
        r.Report["failures"]=r.Failures;
        r.Report["warnings"]=r.Warnings;
        return r;
    }

    public static void Print(Result r) {
        foreach(var w in r.Warnings) Console.WriteLine("WARNING "+w);
        foreach(var f in r.Failures) Console.WriteLine("FAIL "+f);
        Console.WriteLine(r.Passed?"PASS validation ("+r.Report["documentsChecked"]+" documents, "+r.Report["mates"]+" mates)":"VALIDATION FAILED: "+r.Failures.Count+" problem(s)");
    }

    // Entry point for `cad.ps1 validate`. Rebuilds but never saves. A rebuild can mark documents unsaved even when
    // nothing changed; if everything was saved before, reload from disk afterwards so it stays that way.
    public static void Run(bool quick, bool strict, bool interference) {
        var sw=new SwCad(); var model=sw.Model();
        bool wasSaved=SwCad.Dirty(model).Length==0;
        var r=Check(sw,model,new Options{Deep=!quick,Strict=strict,Interference=interference});
        SwCad.WriteJson("validation.json",r.Report);
        if(wasSaved&&SwCad.Dirty(model).Length>0) model=sw.RevertToDisk(model);
        else if(!wasSaved) Console.WriteLine("Note: the model had unsaved changes before validation; it was checked as it is in memory.");
        Print(r);
        if(!r.Passed) throw new Exception("Validation failed; see reports/validation.json");
    }
}
