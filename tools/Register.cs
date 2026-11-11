using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using SolidWorks.Interop.sldworks;

// `cad.ps1 register`: bill of materials with mass. One row per part file and configuration, with quantity,
// material, density, unit mass, where the mass comes from and the part's custom properties
// (Status, Mass_Basis, ...). Writes reports/component-register.csv and .json. Read-only.
public static class CadRegister {
    static string Q(object v) { return "\""+Convert.ToString(v,CultureInfo.InvariantCulture).Replace("\"","\"\"")+"\""; }

    public static void Run(string[] filters) {
        filters=filters??new string[0];
        var sw=new SwCad(); var model=sw.Model();
        var parts=SwCad.Components(model).Where(c=>!c.IsSuppressed()&&SwCad.Matches(c.Name2,filters)&&!(c.GetModelDoc2() is IAssemblyDoc)).ToArray();
        var rows=new List<Dictionary<string,object>>();
        var csv=new List<string>{"Component,Quantity,File,Configuration,Envelope,ExcludedFromBOM,Material,Density_kg_m3,UnitMass_g,TotalMass_g,MassSource,Properties"};
        double physical=0; int unknown=0;
        foreach(var group in parts.GroupBy(c=>c.GetPathName()+"|"+c.ReferencedConfiguration,StringComparer.OrdinalIgnoreCase)) {
            var c=group.First(); var d=c.GetModelDoc2() as IModelDoc2; int qty=group.Count();
            var row=new Dictionary<string,object>();
            row["component"]=c.Name2; row["quantity"]=qty; row["file"]=c.GetPathName(); row["configuration"]=c.ReferencedConfiguration;
            row["envelope"]=c.IsEnvelope(); row["excludedFromBOM"]=c.ExcludeFromBOM;
            string material="", source; double? density=null, unit=null; var props=new Dictionary<string,string>();
            if(d==null) source="lightweight or unresolved: resolve it to read mass";
            else {
                var part=d as IPartDoc; string db;
                if(part!=null) material=part.GetMaterialPropertyName2(c.ReferencedConfiguration,out db)??"";
                var mp=(IMassProperty)d.Extension.CreateMassProperty(); mp.UseSystemUnits=true;
                density=mp.Density;
                if(mp.OverrideMass) { unit=mp.Mass*1000; source="mass override"; }
                else { unit=SwCad.Volume(c)*mp.Density*1000; source=material==""?"no material: SolidWorks default density, not a real mass":"CAD volume x material density"; }
                foreach(var scope in new[]{"",c.ReferencedConfiguration}) {
                    var manager=d.Extension.CustomPropertyManager[scope]; var names=manager==null?null:manager.GetNames() as object[]; if(names==null) continue;
                    foreach(string name in names) { string value,resolved; bool wasResolved,linked; manager.Get6(name,false,out value,out resolved,out wasResolved,out linked); string text=String.IsNullOrEmpty(resolved)?value:resolved; if(!String.IsNullOrWhiteSpace(text)) props[name]=text.Length>200?text.Substring(0,200)+"...":text; }
                }
            }
            row["material"]=material; row["density_kg_m3"]=density; row["unitMass_g"]=unit; row["totalMass_g"]=unit*qty; row["massSource"]=source; row["properties"]=props;
            if(!c.IsEnvelope()) { if(unit.HasValue&&material!=""||source=="mass override") physical+=unit.Value*qty; else unknown+=qty; }
            rows.Add(row);
            csv.Add(String.Join(",",new object[]{c.Name2,qty,c.GetPathName(),c.ReferencedConfiguration,c.IsEnvelope(),c.ExcludeFromBOM,material,density.HasValue?Math.Round(density.Value,3):(double?)null,unit.HasValue?Math.Round(unit.Value,3):(double?)null,unit.HasValue?Math.Round(unit.Value*qty,3):(double?)null,source,String.Join("; ",props.Select(p=>p.Key+"="+p.Value))}.Select(Q)));
        }
        double? assemblyMass=null;
        try { var mp=(IMassProperty)model.Extension.CreateMassProperty(); mp.UseSystemUnits=true; assemblyMass=mp.Mass*1000; } catch {}
        SwCad.WriteJson("component-register.json",new{model=model.GetPathName(),filters=filters,parts=rows,knownMass_g=Math.Round(physical,2),instancesWithoutRealMass=unknown,solidWorksMassProperties_g=assemblyMass,
            note="knownMass_g sums parts with a material or a mass override (envelopes excluded). Parts without a material use SolidWorks' default density and are counted in instancesWithoutRealMass instead."});
        File.WriteAllLines(SwCad.Reports("component-register.csv"),csv);
        Console.WriteLine(rows.Count+" part rows, "+parts.Length+" instances. Known mass "+Math.Round(physical,1)+" g; "+unknown+" instance(s) without a material or mass override"+(assemblyMass.HasValue&&filters.Length==0?"; SolidWorks mass properties "+Math.Round(assemblyMass.Value,1)+" g":""));
        Console.WriteLine("Wrote reports/component-register.csv and .json");
    }
}
