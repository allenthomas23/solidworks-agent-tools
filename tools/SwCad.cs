using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;

// Reusable session helpers. Geometry/design choices belong in the caller.
// Units: SolidWorks works in meters and radians internally; convert only when reporting.
public sealed class SwCad {
    public readonly ISldWorks App;
    private readonly Dictionary<string, Dictionary<string,int>> equationIndices = new Dictionary<string, Dictionary<string,int>>();
    public SwCad() { App=(ISldWorks)Marshal.GetActiveObject("SldWorks.Application"); }

    // ---------- documents ----------
    public IModelDoc2 OpenDocument(string path) {
        var d=(IModelDoc2)App.GetOpenDocumentByName(Path.GetFullPath(path));
        if(d==null) throw new Exception("Required document is not open: "+path);
        return d;
    }
    // The model a command works on: the given path, else CAD_MODEL_PATH (set by CadEnvironment.ps1), else the active document.
    public IModelDoc2 Model(string path=null) {
        if(String.IsNullOrEmpty(path)) path=System.Environment.GetEnvironmentVariable("CAD_MODEL_PATH");
        if(!String.IsNullOrEmpty(path)) return OpenDocument(path);
        var d=App.ActiveDoc as IModelDoc2;
        if(d==null) throw new Exception("No model given and no document is active in SolidWorks. Open the model or pass -ModelPath.");
        if(d.GetType()!=1&&d.GetType()!=2) throw new Exception("The active document is a drawing; activate the part or assembly or pass -ModelPath.");
        if(String.IsNullOrEmpty(d.GetPathName())) throw new Exception("The active document has never been saved; save it first.");
        return d;
    }
    public void Activate(IModelDoc2 d) { int e=0; d.Visible=true; App.ActivateDoc3(d.GetTitle(),false,0,ref e); if(e!=0)throw new Exception("Activate "+d.GetTitle()+": "+e); }
    public static string RepositoryRoot() { return System.Environment.GetEnvironmentVariable("CAD_REPOSITORY_ROOT") ?? Directory.GetCurrentDirectory(); }
    public static string Reports(string name) { string dir=Path.Combine(RepositoryRoot(),"reports"); Directory.CreateDirectory(dir); return Path.Combine(dir,name); }
    public static void WriteJson(string name,object value) { File.WriteAllText(Reports(name),new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(value)); }

    // The model itself plus every resolved document it references, one per file.
    public static IModelDoc2[] Documents(IModelDoc2 model) {
        var result=new List<IModelDoc2>{model};
        foreach(var c in Components(model)){var d=c.GetModelDoc2() as IModelDoc2;if(d!=null)result.Add(d);}
        return result.GroupBy(d=>d.GetPathName(),StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToArray();
    }
    public static IComponent2[] Components(IModelDoc2 model,bool topLevelOnly=false) {
        var a=model as IAssemblyDoc; if(a==null) return new IComponent2[0];
        return ((a.GetComponents(topLevelOnly) as object[])??new object[0]).Cast<IComponent2>().ToArray();
    }
    public static string[] Dirty(IModelDoc2 model) { return Documents(model).Where(d=>d.GetSaveFlag()).Select(d=>d.GetPathName()).ToArray(); }
    // Throws away in-memory changes: reloads changed referenced documents and closes/reopens the model from disk.
    // Only call this when everything was saved before the command started (check with Dirty/RequireClean).
    public IModelDoc2 RevertToDisk(IModelDoc2 model) {
        string path=model.GetPathName(); int type=model.GetType();
        foreach(var d in Documents(model).Where(d=>d!=model&&d.GetSaveFlag())) d.ReloadOrReplace(false,d.GetPathName(),true);
        if(model.GetSaveFlag()) {
            App.CloseDoc(model.GetTitle());
            int e=0,w=0; model=App.OpenDoc6(path,type,1,"",ref e,ref w) as IModelDoc2;
            if(model==null) throw new Exception("Could not reopen "+path+" (error "+e+"); open it again in SolidWorks without saving anything.");
            Activate(model);
        }
        var dirty=Dirty(model);
        if(dirty.Length>0) Console.WriteLine("Note: still marked unsaved after reload: "+String.Join(", ",dirty.Select(Path.GetFileName))+" (don't save them)");
        return model;
    }
    public static void RequireClean(IModelDoc2 model,string action) {
        var dirty=Dirty(model);
        if(dirty.Length>0) throw new Exception("Unsaved changes in "+String.Join(", ",dirty.Select(Path.GetFileName))+". Save or discard them in SolidWorks before "+action+", so this command never saves someone else's work.");
    }

    // ---------- equations and global variables ----------
    public void Equation(IModelDoc2 d,string lhs,string rhs) {
        string key=d.GetPathName(); if(key=="")key=d.GetTitle();
        Dictionary<string,int> map;
        var em=d.GetEquationMgr();
        if(!equationIndices.TryGetValue(key,out map)) {
            map=new Dictionary<string,int>();
            for(int i=0,n=em.GetCount();i<n;i++)map[EquationName(em.Equation[i])]=i;
            equationIndices[key]=map;
        }
        string text="\""+lhs+"\" = "+rhs;
        int index;
        if(map.TryGetValue(lhs,out index)) {
            if(index>=em.GetCount()||EquationName(em.Equation[index])!=lhs) {
                equationIndices.Remove(key);Equation(d,lhs,rhs);return;
            }
            em.SetEquationAndConfigurationOption(index,text,2,null); em.Equation[index]=text;
        }
        else { index=em.Add3(-1,text,false,2,null); if(index<0)index=em.Add2(-1,text,false); if(index<0)throw new Exception("Equation rejected: "+text); map[lhs]=index; }
        string actual=em.Equation[index];
        if(Regex.Replace(actual,@"\s+","")!=Regex.Replace(text,@"\s+",""))throw new Exception("Equation write did not persist in "+d.GetTitle()+": requested "+text+"; actual "+actual);
    }
    public static string EquationName(string equation) { return equation.Split('=')[0].Trim().Trim('"'); }
    public static string EquationValue(string equation) { int i=equation.IndexOf('='); return i<0?"":equation.Substring(i+1).Trim(); }
    // name -> (index, full equation text, evaluated value in system units) for global variables only.
    public static Dictionary<string,Tuple<int,string,double>> Globals(IModelDoc2 d) {
        var em=d.GetEquationMgr(); var result=new Dictionary<string,Tuple<int,string,double>>();
        for(int i=0,n=em.GetCount();i<n;i++) if(em.GlobalVariable[i]) result[EquationName(em.Equation[i])]=Tuple.Create(i,em.Equation[i],em.Value[i]);
        return result;
    }
    public static string Mm(double value) { return value.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+"mm"; }
    public void RemoveEquation(IModelDoc2 d,string lhs) {
        var em=d.GetEquationMgr();
        for(int i=em.GetCount()-1;i>=0;i--)if(EquationName(em.Equation[i])==lhs)em.Delete(i);
        string key=d.GetPathName();if(key=="")key=d.GetTitle();equationIndices.Remove(key);
    }

    // ---------- features ----------
    public static Dictionary<string,IFeature> Features(IModelDoc2 d) {
        var map=new Dictionary<string,IFeature>();
        for(var f=(IFeature)d.FirstFeature();f!=null;f=(IFeature)f.GetNextFeature()) {
            map[f.Name]=f;
            if(f.GetTypeName2()=="MateGroup") for(var sf=(IFeature)f.GetFirstSubFeature();sf!=null;sf=(IFeature)sf.GetNextSubFeature())map[sf.Name]=sf;
        }
        return map;
    }
    public void InComponent(IModelDoc2 assembly,IComponent2 component,Action<IModelDoc2> edit) {
        Activate(assembly); assembly.ClearSelection2(true); component.Select4(false,null,false);
        var ad=(IAssemblyDoc)assembly; int info=0;
        try { ad.EditPart2(true,false,ref info); edit((IModelDoc2)component.GetModelDoc2()); }
        finally { ad.EditAssembly(); assembly.ClearSelection2(true); }
    }
    public void Rebuild(IModelDoc2 d) { Activate(d); d.GetEquationMgr().EvaluateAll(); d.EditRebuild3(); }
    public static void Save(IModelDoc2 d) { int e=0,w=0; if(!d.Save3(1,ref e,ref w)||e!=0)throw new Exception("Save "+d.GetTitle()+": errors="+e+", warnings="+w); if(w!=0)Console.WriteLine("Save warning "+d.GetTitle()+": "+w); }
    public static void Property(IModelDoc2 d,string name,string value) { d.Extension.CustomPropertyManager[""].Add3(name,30,value,1); }
    public void Plane(IModelDoc2 d,string name,string basis,bool negative,double distance,string expression) {
        Activate(d); d.ClearSelection2(true);
        if(!d.Extension.SelectByID2(basis,"PLANE",0,0,0,false,0,null,0))throw new Exception("Missing basis "+basis);
        var f=(IFeature)d.FeatureManager.InsertRefPlane(negative?264:8,distance,0,0,0,0);
        if(f==null)throw new Exception("Plane creation: "+name);
        f.Name=name; Equation(d,"D1@"+name,expression); f.Select2(false,0);d.BlankRefGeom();d.ClearSelection2(true);
    }
    // Coincident mate between two named datum planes of two components (aligned).
    public static void Pair(IModelDoc2 assembly,string name,IComponent2 first,string firstPlane,IComponent2 second,string secondPlane) {
        assembly.ClearSelection2(true); string title=Path.GetFileNameWithoutExtension(assembly.GetPathName());
        if(!assembly.Extension.SelectByID2(firstPlane+"@"+first.Name2+"@"+title,"PLANE",0,0,0,false,1,null,0)||!assembly.Extension.SelectByID2(secondPlane+"@"+second.Name2+"@"+title,"PLANE",0,0,0,true,1,null,0))throw new Exception("Mate datum selection: "+name);
        int e; var mate=((IAssemblyDoc)assembly).AddMate5(0,2,false,0,0,0,0,0,0,0,0,false,false,0,out e);
        if(mate==null||e!=1)throw new Exception("Mate "+name+": "+e);
        ((IFeature)assembly.Extension.GetLastFeatureAdded()).Name=name;assembly.ClearSelection2(true);
    }
    public static void AssertNear(double actual,double expected,string label,double tolerance) { if(Math.Abs(actual-expected)>tolerance)throw new Exception(label+": actual="+actual+", expected="+expected); }

    // ---------- geometry ----------
    public static bool Matches(string name,string[] filters) {
        return filters==null||filters.Length==0||filters.Any(f=>name.IndexOf(f,StringComparison.OrdinalIgnoreCase)>=0);
    }
    public static IBody2[] LocalBodies(IComponent2 c) { object info; var b=c.GetBodies3(0,out info) as object[]; return b==null?new IBody2[0]:b.Cast<IBody2>().ToArray(); }
    public static double Volume(IComponent2 c) { return LocalBodies(c).Sum(b=>((double[])b.GetMassProperties(1))[3]); }
    public sealed class WorldBody { public string Component, Name; public IBody2 Solid; public double[] Box; }
    // Copies of a component's configuration-correct bodies, moved into assembly coordinates.
    public static List<WorldBody> Bodies(IComponent2 c) {
        string n=c.Name2; var transform=(MathTransform)c.Transform2; var result=new List<WorldBody>();
        foreach(var b in LocalBodies(c)) { var world=(IBody2)b.Copy(); if(transform!=null)world.ApplyTransform(transform); result.Add(new WorldBody{Name=b.Name,Component=n,Solid=world,Box=(double[])world.GetBodyBox()}); }
        return result;
    }
    // Exact solid overlaps (bounding-box broad phase, then Boolean intersect on copies). Returns "a vs b: volume mm3".
    public static List<string> Intersections(List<WorldBody> first,List<WorldBody> second,double minimumMm3=1e-3) {
        var result=new List<string>(); var seen=new HashSet<string>();
        foreach(var b in first) foreach(var c in second) {
            if(b.Component==c.Component) continue;
            string key=String.CompareOrdinal(b.Component+"/"+b.Name,c.Component+"/"+c.Name)<0?b.Component+"/"+b.Name+"|"+c.Component+"/"+c.Name:c.Component+"/"+c.Name+"|"+b.Component+"/"+b.Name;
            if(!seen.Add(key)) continue;
            if(Enumerable.Range(0,3).Any(k=>Math.Min(b.Box[k+3],c.Box[k+3])-Math.Max(b.Box[k],c.Box[k])<=1e-9)) continue;
            int error; var overlap=((IBody2)b.Solid.Copy()).Operations2(15901,(IBody2)c.Solid.Copy(),out error) as object[];
            if(overlap==null) continue;
            double volume=overlap.Cast<IBody2>().Sum(p=>((double[])p.GetMassProperties(1))[3])*1e9;
            if(volume>minimumMm3) result.Add(b.Component+"/"+b.Name+" vs "+c.Component+"/"+c.Name+": "+Math.Round(volume,3)+" mm3");
        }
        return result;
    }
    // Origin (mm) and rotation rows of a component, for reporting where it really is.
    public static object Placement(IComponent2 c) {
        var t=c.Transform2; if(t==null) return null; var m=(double[])t.ArrayData;
        return new { origin_mm=new[]{Math.Round(m[9]*1000,4),Math.Round(m[10]*1000,4),Math.Round(m[11]*1000,4)}, rotation=m.Take(9).Select(v=>Math.Round(v,6)).ToArray() };
    }
}
