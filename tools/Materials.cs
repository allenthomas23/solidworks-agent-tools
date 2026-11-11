using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

// `cad.ps1 assign-material`: give every part matching -Filter one material in all its configurations,
// check the geometry didn't change, record Mass_Basis on the part and save those parts only.
// -Database: omitted = SolidWorks' standard library; a file name or relative path = a .sldmat in this
// repository (its folder is added to SolidWorks' material locations); or an absolute path.
public static class CadMaterials {
    public static string ResolveDatabase(SwCad sw, string database) {
        string path;
        if(String.IsNullOrEmpty(database)) path=Path.Combine(System.Environment.GetEnvironmentVariable("CAD_SOLIDWORKS_HOME"),@"lang\english\sldmaterials\solidworks materials.sldmat");
        else if(Path.IsPathRooted(database)) path=database;
        else {
            path=Path.Combine(SwCad.RepositoryRoot(),database);
            if(!File.Exists(path)) path=Path.Combine(SwCad.RepositoryRoot(),"materials",database);
        }
        path=Path.GetFullPath(path);
        if(!File.Exists(path)) throw new Exception("Material library not found: "+path);
        string folder=Path.GetDirectoryName(path), folders=sw.App.GetUserPreferenceStringValue(28)??"";   // 28 = material database locations
        if(folders.Split(';').All(f=>!f.Trim().TrimEnd('\\').Equals(folder.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)))
            sw.App.SetUserPreferenceStringValue(28,folders==""?folder:folders+";"+folder);
        sw.App.GetMaterialDatabases();   // refresh: assignment silently fails until the library is listed
        return path;
    }

    public static void Run(string[] filters, string material, string database) {
        if(filters==null||filters.Length==0) throw new Exception("Give -Filter so the material goes only where you mean it.");
        var sw=new SwCad(); var model=sw.Model();
        var targets=(model is IPartDoc)
            ? new[]{Tuple.Create(model,(IComponent2)null)}
            : SwCad.Components(model).Where(c=>!c.IsSuppressed()&&SwCad.Matches(c.Name2,filters)&&c.GetModelDoc2() is IPartDoc)
                .GroupBy(c=>c.GetPathName(),StringComparer.OrdinalIgnoreCase).Select(g=>Tuple.Create((IModelDoc2)g.First().GetModelDoc2(),g.First())).ToArray();
        if(targets.Length==0) throw new Exception("No resolved parts match the filter.");
        var dirty=targets.Where(t=>t.Item1.GetSaveFlag()).Select(t=>Path.GetFileName(t.Item1.GetPathName())).ToArray();
        if(dirty.Length>0) throw new Exception("Unsaved changes in "+String.Join(", ",dirty)+"; save or discard them first.");
        string db=ResolveDatabase(sw,database);
        var rows=new List<object>();
        foreach(var t in targets) {
            var d=t.Item1; var part=(IPartDoc)d; bool wasVisible=d.Visible;
            Func<double> volume=()=>((object[])part.GetBodies2(0,false)??new object[0]).Cast<IBody2>().Sum(b=>((double[])b.GetMassProperties(1))[3]);
            string unused; string previous=part.GetMaterialPropertyName2(t.Item2==null?"":t.Item2.ReferencedConfiguration,out unused);
            double before=volume();
            bool done=false;
            try {
                sw.Activate(d);
                foreach(string cfg in (object[])d.GetConfigurationNames()) part.SetMaterialPropertyName2(cfg,db,material);   // returns void: read back below
                d.EditRebuild3();
                foreach(string cfg in (object[])d.GetConfigurationNames()) {
                    string actualDb; string actual=part.GetMaterialPropertyName2(cfg,out actualDb);
                    if(actual!=material) throw new Exception("Material '"+material+"' did not stick on "+Path.GetFileName(d.GetPathName())+" ("+cfg+": '"+actual+"'). Names are exact; check the library: "+db);
                }
                SwCad.AssertNear(volume(),before,"Geometry unchanged by material change "+Path.GetFileName(d.GetPathName()),1e-12);
                SwCad.Property(d,"Mass_Basis","CAD volume x "+material+" density");
                SwCad.Save(d); done=true;
            } finally {
                if(!done&&d.GetSaveFlag()) d.ReloadOrReplace(false,d.GetPathName(),true);   // was saved before: back to the file
                if(d!=model) { d.Visible=wasVisible; sw.Activate(model); }
            }
            var mp=(IMassProperty)d.Extension.CreateMassProperty(); mp.UseSystemUnits=true;
            rows.Add(new{part=d.GetPathName(),previousMaterial=previous,material=material,density_kg_m3=mp.Density,mass_g=Math.Round(mp.Mass*1000,3),massOverride=mp.OverrideMass});
            Console.WriteLine("Assigned "+material+" to "+Path.GetFileName(d.GetPathName())+" ("+Math.Round(mp.Mass*1000,2)+" g"+(mp.OverrideMass?", mass override still active":"")+")");
        }
        SwCad.WriteJson("materials.json",new{database=db,material=material,parts=rows});
    }
}
