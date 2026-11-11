using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;

// `cad.ps1 set`: the one way to change master inputs (global variables in the open model).
// Changes -> full rebuild -> CadValidate (+ ProjectChecks) -> save only the documents whose
// geometry or equations actually changed. Any failure restores the old values and saves nothing.
// With test=true it validates the change and then restores the old values without saving.
public static class CadParameters {
    static readonly Regex Literal=new Regex(@"^\s*([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)\s*([A-Za-z""'°]*)\s*$");

    public static void Set(string changesJson, bool test, bool strict, bool interference) {
        var sw=new SwCad(); var model=sw.Model();
        var requested=new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(changesJson);
        if(requested.Count==0) throw new Exception("No changes given.");
        // Everything must match the files on disk, so a failed change or a test can simply reload them.
        SwCad.RequireClean(model,test?"testing a change":"changing parameters");

        var globals=SwCad.Globals(model);
        var before=new Dictionary<string,string>(); var after=new Dictionary<string,string>();
        foreach(var kv in requested) {
            Tuple<int,string,double> g;
            if(!globals.TryGetValue(kv.Key,out g)) throw new Exception("Unknown global variable '"+kv.Key+"'. Existing globals: "+String.Join(", ",globals.Keys.OrderBy(k=>k)));
            string oldValue=SwCad.EquationValue(g.Item2);
            var old=Literal.Match(oldValue);
            if(!old.Success) throw new Exception(kv.Key+" is derived (= "+oldValue+"); change the inputs it depends on instead.");
            string value=kv.Value.Trim();
            var number=Literal.Match(value);
            if(!number.Success) throw new Exception(kv.Key+": give a number with an optional unit, e.g. 120mm or 15deg (got '"+value+"').");
            if(number.Groups[2].Value==""&&old.Groups[2].Value!="") value=number.Groups[1].Value+old.Groups[2].Value;   // keep the existing unit
            if(Regex.Replace(value,@"\s+","")==Regex.Replace(oldValue,@"\s+","")) continue;
            before[kv.Key]=oldValue; after[kv.Key]=value;
        }
        if(after.Count==0) { Console.WriteLine("All requested values already set; nothing to do."); return; }

        var docs=SwCad.Documents(model);
        var signatures=docs.ToDictionary(d=>d.GetPathName(),d=>Signature(d),StringComparer.OrdinalIgnoreCase);
        var beforeValues=CadValidate.GlobalValues(model);
        var serializer=new JavaScriptSerializer{MaxJsonLength=int.MaxValue};
        CadValidate.Result result=null;
        try {
            Apply(sw,model,after);
            result=CadValidate.Check(sw,model,new CadValidate.Options{Rebuild=false,Strict=strict,Interference=interference});
            CadValidate.Print(result);
            if(!result.Passed) throw new Exception(String.Join("; ",result.Failures.Take(10)));
        } catch(Exception failure) {
            Console.WriteLine("Change rejected: "+failure.Message);
            model=Revert(sw,model,beforeValues);
            SwCad.WriteJson("last-parameter-failure.json",new{attempted=after,previous=before,error=failure.Message,validation=result==null?null:result.Report});
            Console.WriteLine("Model reloaded from the saved files; original values back, nothing saved.");
            throw;
        }
        var afterValues=CadValidate.GlobalValues(model);
        if(test) {
            model=Revert(sw,model,beforeValues);
            SwCad.WriteJson("parameter-test.json",new{tested=after,previous=before,valuesDuringTest=afterValues,validation=result.Report});
            Console.WriteLine("Test passed; model reloaded from the saved files, original values back. Nothing saved.");
            return;
        }

        // Save parts first, then the model. Documents the rebuild touched without changing anything are
        // reloaded from disk instead, so they don't stay marked unsaved (or get rewritten for nothing).
        // Reload first: reloading a part marks its assembly changed again, so the model must be saved last.
        var changed=docs.Where(d=>d.GetSaveFlag()&&(d==model||Signature(d)!=signatures[d.GetPathName()])).OrderBy(d=>d==model?1:0).ToList();
        var untouched=docs.Where(d=>d.GetSaveFlag()&&!changed.Contains(d)).Select(d=>d.GetPathName()).ToList();
        foreach(var d in docs.Where(d=>untouched.Contains(d.GetPathName()))) d.ReloadOrReplace(false,d.GetPathName(),true);
        var saved=new List<string>();
        foreach(var d in changed) { SwCad.Save(d); saved.Add(d.GetPathName()); }
        var stillUnsaved=SwCad.Dirty(model);
        if(stillUnsaved.Length>0) Console.WriteLine("Note: still marked unsaved: "+String.Join(", ",stillUnsaved.Select(Path.GetFileName)));
        SwCad.WriteJson("last-parameter-change.json",new{changed=after,previous=before,valuesBefore=beforeValues,valuesAfter=afterValues,saved=saved,rebuildOnlyNotSaved=untouched,validation=result.Report});
        Console.WriteLine("Saved "+saved.Count+" document(s): "+String.Join(", ",saved.Select(Path.GetFileName)));
        if(untouched.Count>0) Console.WriteLine("Rebuilt but unchanged, reloaded instead of saved: "+String.Join(", ",untouched.Select(Path.GetFileName)));
    }

    static void Apply(SwCad sw, IModelDoc2 model, Dictionary<string,string> values) {
        sw.Activate(model);
        foreach(var kv in values) sw.Equation(model,kv.Key,kv.Value);
        model.GetEquationMgr().EvaluateAll();
        model.ForceRebuild3(false);   // full rebuild so parts driven from these globals follow
        model.EditRebuild3();
    }

    // Back to the saved files (safe because Set requires everything saved before it starts), then check the values.
    static IModelDoc2 Revert(SwCad sw, IModelDoc2 model, Dictionary<string,double> expected) {
        model=sw.RevertToDisk(model);
        var actual=CadValidate.GlobalValues(model);
        foreach(var kv in expected) if(actual.ContainsKey(kv.Key)) SwCad.AssertNear(actual[kv.Key],kv.Value,"Reverted "+kv.Key,1e-9);
        return model;
    }
    // What "changed" means for saving: equation values plus body volumes/bounds (parts) or component placement (assemblies).
    static string Signature(IModelDoc2 d) {
        var parts=new List<string>(); var ci=CultureInfo.InvariantCulture;
        var em=d.GetEquationMgr();
        for(int i=0,n=em.GetCount();i<n;i++) parts.Add(em.Equation[i]+"="+em.Value[i].ToString("R",ci));
        var part=d as IPartDoc;
        if(part!=null) {
            foreach(IBody2 b in (part.GetBodies2(-1,false) as object[])??new object[0]) {
                parts.Add(Math.Round(((double[])b.GetMassProperties(1))[3]*1e12).ToString(ci));
                parts.Add(String.Join(",",((double[])b.GetBodyBox()).Select(v=>Math.Round(v*1e7).ToString(ci))));
            }
        }
        foreach(var c in SwCad.Components(d,true)) {
            var t=c.Transform2; if(t==null) continue;
            parts.Add(c.Name2+":"+String.Join(",",((double[])t.ArrayData).Take(12).Select(v=>Math.Round(v*1e7).ToString(ci))));
        }
        return String.Join("|",parts);
    }
}
