using System;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

// `cad.ps1 interference`: exact solid overlaps between parts, in assembly coordinates. Read-only.
// Envelope components are skipped (they are fit references). Expect real overlaps for press fits and
// modeled threads; list those with -Ignore.
public static class CadInterference {
    public static List<SwCad.WorldBody> Bodies(IEnumerable<IComponent2> components) {
        return components.SelectMany(c=>SwCad.Bodies(c)).ToList();
    }
    public static IComponent2[] PhysicalParts(IModelDoc2 model, string[] ignore, bool includeHidden) {
        return SwCad.Components(model).Where(c=>!c.IsSuppressed()&&!c.IsEnvelope()&&c.GetModelDoc2() is IPartDoc
            &&(includeHidden||c.Visible!=0)&&!(ignore!=null&&ignore.Length>0&&SwCad.Matches(c.Name2,ignore))).ToArray();
    }

    public static void Run(string[] filters, string[] against, string[] ignore, bool includeHidden) {
        var sw=new SwCad(); var model=sw.Model();
        if(!(model is IAssemblyDoc)) throw new Exception("Interference needs an assembly.");
        var parts=PhysicalParts(model,ignore,includeHidden);
        var first=parts.Where(c=>SwCad.Matches(c.Name2,filters)).ToArray();
        var second=parts.Where(c=>SwCad.Matches(c.Name2,against)).ToArray();
        if(first.Length==0) throw new Exception("No visible physical parts match the filter.");
        var started=DateTime.Now;
        var clashes=SwCad.Intersections(Bodies(first),Bodies(second));
        SwCad.WriteJson("interference.json",new{model=model.GetPathName(),checkedParts=first.Length,againstParts=second.Length,clashes=clashes,seconds=Math.Round((DateTime.Now-started).TotalSeconds,1)});
        foreach(var c in clashes) Console.WriteLine("CLASH "+c);
        Console.WriteLine(clashes.Count==0?"PASS no interference ("+first.Length+" x "+second.Length+" parts)":clashes.Count+" interference(s); see reports/interference.json");
        if(clashes.Count>0) throw new Exception(clashes.Count+" interference(s)");
    }
}
