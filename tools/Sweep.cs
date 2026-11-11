using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;

// `cad.ps1 sweep`: drive a mechanism through discrete poses by setting one dimension (usually an angle or
// distance mate, e.g. "D1@Angle1"), and at each pose check rebuild errors, report where the -Track
// components really are (position and rotation, not just the driven value) and, with -Moving, check the
// moving parts against everything else for interference. Always restores the original value. Never saves.
// Discrete poses are not a continuous swept-volume check: add intermediate values where clearance is tight.
public static class CadSweep {
    static double ToSystem(string text) {
        var m=Regex.Match(text.Trim(),@"^([-+]?[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?)\s*(mm|cm|m|in|deg|rad)$",RegexOptions.IgnoreCase);
        if(!m.Success) throw new Exception("Give each value with a unit (mm, cm, m, in, deg, rad): '"+text+"'");
        double v=Double.Parse(m.Groups[1].Value,CultureInfo.InvariantCulture);
        switch(m.Groups[2].Value.ToLowerInvariant()) { case "mm": return v/1000; case "cm": return v/100; case "in": return v*0.0254; case "deg": return v*Math.PI/180; default: return v; }
    }

    public static void Run(string dimensionName, string[] values, string[] moving, string[] track, string[] ignore) {
        var sw=new SwCad(); var model=sw.Model();
        var dimension=model.Parameter(dimensionName) as IDimension;
        if(dimension==null) throw new Exception("Dimension not found: "+dimensionName+" (use the full name, e.g. D1@Angle1 for a mate or D1@Sketch1@Part.SLDPRT)");
        double original=dimension.SystemValue;
        bool wasSaved=SwCad.Dirty(model).Length==0;
        var parts=CadInterference.PhysicalParts(model,ignore,false);
        var movers=moving==null||moving.Length==0?new IComponent2[0]:parts.Where(c=>SwCad.Matches(c.Name2,moving)).ToArray();
        var others=parts.Where(c=>!movers.Contains(c)).ToArray();
        if(moving!=null&&moving.Length>0&&movers.Length==0) throw new Exception("No visible physical parts match -Moving.");
        var tracked=track==null||track.Length==0?movers:SwCad.Components(model).Where(c=>!c.IsSuppressed()&&SwCad.Matches(c.Name2,track)).ToArray();
        var poses=new List<object>(); var problems=new List<string>();
        try {
            foreach(var text in values) {
                double target=ToSystem(text);
                int status=dimension.SetSystemValue3(target,1,null);   // 1 = this configuration
                if(status!=0) { problems.Add(text+": value rejected (status "+status+"); outside a limit?"); continue; }
                model.EditRebuild3();
                double actual=dimension.SystemValue;
                var errors=new List<string>();
                object features,codes,warns;
                if(model.Extension.GetWhatsWrongCount()>0&&model.Extension.GetWhatsWrong(out features,out codes,out warns)&&features!=null) {
                    var f=(object[])features; var w=(bool[])warns;
                    for(int i=0;i<f.Length;i++) if(!w[i]) errors.Add(((IFeature)f[i]).Name);
                }
                if(Math.Abs(actual-target)>1e-7) errors.Add("dimension settled at "+actual+" instead of "+target+" (limits or mates)");
                var clashes=movers.Length==0?new List<string>():SwCad.Intersections(CadInterference.Bodies(movers),CadInterference.Bodies(others));
                foreach(var e in errors) problems.Add(text+": "+e);
                foreach(var c in clashes) problems.Add(text+": interference "+c);
                poses.Add(new{value=text,systemValue=actual,errors=errors,interference=clashes,placements=tracked.ToDictionary(c=>c.Name2,c=>SwCad.Placement(c))});
                Console.WriteLine((errors.Count+clashes.Count==0?"PASS ":"FAIL ")+text+(movers.Length>0?" ("+clashes.Count+" interference)":"")+(errors.Count>0?" errors: "+String.Join(", ",errors):""));
            }
        } finally {
            dimension.SetSystemValue3(original,1,null); model.EditRebuild3();
            SwCad.AssertNear(dimension.SystemValue,original,"Restored "+dimensionName,1e-8);
            // Moving parts marks the model unsaved; if it was saved before, reload it so it matches the files again.
            if(wasSaved) { model=sw.RevertToDisk(model); Console.WriteLine("Restored "+dimensionName+" and reloaded the model from its saved files; nothing saved"); }
            else Console.WriteLine("Restored "+dimensionName+"; nothing saved (the model had unsaved changes before the sweep, so it was not reloaded)");
        }
        SwCad.WriteJson("sweep.json",new{model=model.GetPathName(),dimension=dimensionName,originalSystemValue=original,moving=movers.Select(c=>c.Name2),poses=poses,problems=problems,
            limits="Discrete poses only, not a continuous swept-volume check."});
        if(problems.Count>0) throw new Exception(problems.Count+" problem(s); see reports/sweep.json");
    }
}
