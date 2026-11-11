using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;

// Read-only JSON snapshot of an open part or assembly. Never rebuilds, selects, moves, opens or saves anything,
// so values reflect the last rebuild. Lengths in mm, assembly coordinates.
public static class CadInspect {
    public static string Run(bool parameters, bool errors, bool deepErrors, bool components, string[] filters, bool documents) {
        var sw = new SwCad();
        var a = sw.Model();
        var all = SwCad.Components(a);
        var cfg = a.GetActiveConfiguration() as IConfiguration;
        var result = new Dictionary<string, object>();
        result["model"] = a.GetPathName();
        result["unsavedChanges"] = a.GetSaveFlag();
        result["configuration"] = cfg == null ? null : cfg.Name;
        result["componentCount"] = all.Length;
        result["suppressedCount"] = all.Count(c => c.IsSuppressed());
        if (parameters) result["parameters"] = Parameters(a);
        if (errors || deepErrors) result["errors"] = Errors(a, all, deepErrors);
        if (components) result["components"] = Components(all, filters);
        if (documents) result["documents"] = Documents(sw);
        return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(result);
    }

    static double R(double v) { return Math.Round(v, 4); }

    static List<object> Parameters(IModelDoc2 a) {
        var em = a.GetEquationMgr();
        var rows = new List<object>();
        for (int i = 0; i < em.GetCount(); i++) {
            string eq = em.Equation[i];
            rows.Add(new { name = eq.Split('=')[0].Trim().Trim('"'), value = R(em.Value[i]), global = em.GlobalVariable[i], equation = eq });
        }
        return rows;
    }

    // SolidWorks' What's Wrong list (~0.1 s). The deep scan asks every feature in every part (~1 min here).
    static List<object> Errors(IModelDoc2 a, IComponent2[] all, bool deep) {
        var rows = new List<object>();
        object features, codes, warnings;
        if (a.Extension.GetWhatsWrongCount() > 0 && a.Extension.GetWhatsWrong(out features, out codes, out warnings) && features != null) {
            var f = (object[])features; var c = (int[])codes; var w = (bool[])warnings;
            for (int i = 0; i < f.Length; i++) { var ft = (IFeature)f[i]; rows.Add(new { owner = "whatsWrong", feature = ft.Name, type = ft.GetTypeName2(), code = c[i], warning = w[i] }); }
        }
        if (!deep) return rows;
        AddErrors(rows, "assembly", a);
        var docs = all.Where(c => !c.IsSuppressed()).Select(c => c.GetModelDoc2() as IModelDoc2).Where(d => d != null)
            .GroupBy(d => d.GetPathName(), StringComparer.OrdinalIgnoreCase).Select(g => g.First());
        foreach (var d in docs) AddErrors(rows, Path.GetFileName(d.GetPathName()), d);
        foreach (var c in all.Where(c => !c.IsSuppressed() && c.GetModelDoc2() == null))
            rows.Add(new { owner = "assembly", feature = c.Name2, type = "Component", code = -1, warning = true, note = "lightweight or unresolved; part errors not checked" });
        return rows;
    }

    static void AddErrors(List<object> rows, string owner, IModelDoc2 d) {
        foreach (var kv in SwCad.Features(d)) {
            bool warning;
            int code = kv.Value.GetErrorCode2(out warning);
            if (code != 0 || warning) rows.Add(new { owner = owner, feature = kv.Key, type = kv.Value.GetTypeName2(), code = code, warning = warning });
        }
    }

    static List<object> Components(IComponent2[] all, string[] filters) {
        var rows = new List<object>();
        foreach (var c in all) {
            if (filters != null && filters.Length > 0 && !filters.Any(f => c.Name2.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
            var row = new Dictionary<string, object>();
            bool suppressed = c.IsSuppressed();
            row["name"] = c.Name2;
            row["file"] = c.GetPathName();
            row["configuration"] = c.ReferencedConfiguration;
            row["suppressed"] = suppressed;
            row["visible"] = c.Visible == 1;
            row["fixed"] = c.IsFixed();
            row["envelope"] = c.IsEnvelope();
            if (!suppressed) {
                int status = c.GetConstrainedStatus();
                row["constrainedStatus"] = status;
                row["fullyConstrained"] = status == 3;
                var t = c.Transform2;
                if (t != null) { var m = (double[])t.ArrayData; row["origin_mm"] = new[] { R(m[9] * 1000), R(m[10] * 1000), R(m[11] * 1000) }; }
                var box = c.GetBox(false, false) as double[];
                if (box != null) row["box_mm"] = box.Select(v => R(v * 1000)).ToArray();
                var d = c.GetModelDoc2() as IModelDoc2;
                if (d == null) row["lightweight"] = true;
                else {
                    var props = Properties(d, c.ReferencedConfiguration);
                    if (props.Count > 0) row["properties"] = props;
                    var part = d as IPartDoc;
                    if (part != null) { string db; row["material"] = part.GetMaterialPropertyName2(c.ReferencedConfiguration, out db); }
                    object info;
                    var bodies = c.GetBodies3(0, out info) as object[];
                    double volume = bodies == null ? 0 : bodies.Cast<IBody2>().Sum(b => ((double[])b.GetMassProperties(1))[3]);
                    row["volume_mm3"] = R(volume * 1e9);
                    var mp = d.Extension.CreateMassProperty() as IMassProperty;
                    if (mp != null) {
                        mp.UseSystemUnits = true;
                        row["density_kg_m3"] = R(mp.Density);
                        row["massOverride"] = mp.OverrideMass;
                        row["mass_g"] = R(mp.OverrideMass ? mp.Mass * 1000 : volume * mp.Density * 1000);
                    }
                }
            }
            rows.Add(row);
        }
        return rows;
    }

    // File-level custom properties, overridden by the referenced configuration's; resolved values, blanks skipped.
    static Dictionary<string, string> Properties(IModelDoc2 d, string configuration) {
        var result = new Dictionary<string, string>();
        foreach (var scope in new[] { "", configuration }) {
            if (scope == null) continue;
            var manager = d.Extension.CustomPropertyManager[scope];
            var names = manager == null ? null : manager.GetNames() as object[];
            if (names == null) continue;
            foreach (string name in names) {
                string value, resolved; bool wasResolved, linked;
                manager.Get6(name, false, out value, out resolved, out wasResolved, out linked);
                string text = string.IsNullOrEmpty(resolved) ? value : resolved;
                if (string.IsNullOrWhiteSpace(text)) continue;
                // Vendor legal/title-block text can run to paragraphs; keep enough to recognise it.
                result[name] = text.Length > 200 ? text.Substring(0, 200) + "..." : text;
            }
        }
        return result;
    }

    static List<object> Documents(SwCad sw) {
        var docs = sw.App.GetDocuments() as object[] ?? new object[0];
        return docs.Cast<IModelDoc2>().Select(d => (object)new { title = d.GetTitle(), path = d.GetPathName(), unsavedChanges = d.GetSaveFlag(), visible = d.Visible }).ToList();
    }
}
