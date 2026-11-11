using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using SolidWorks.Interop.sldworks;

// `cad.ps1 views`: PNG images of the real model from standard or named views, optionally with only
// some components shown (-Isolate). Restores the view and visibility afterwards; never saves.
// Use it to show a geometry change to the user.
public static class CadViews {
    public static void Run(string[] views, string[] isolate, string outDir, int width, int height, string prefix) {
        var sw=new SwCad(); var model=sw.Model();
        sw.Activate(model);
        if(String.IsNullOrEmpty(outDir)) outDir=SwCad.Reports("views");
        outDir=Path.GetFullPath(outDir); Directory.CreateDirectory(outDir);
        if(String.IsNullOrEmpty(prefix)) prefix=Path.GetFileNameWithoutExtension(model.GetPathName());
        var view=model.ActiveView as IModelView;
        MathTransform orientation=null; MathVector translation=null; double scale=0;
        try { orientation=view.Orientation3; translation=view.Translation3; scale=view.Scale2; } catch { }
        var assembly=model as IAssemblyDoc; bool isolated=false; var written=new List<string>();
        try {
            if(isolate!=null&&isolate.Length>0) {
                if(assembly==null) throw new Exception("-Isolate needs an assembly.");
                model.ClearSelection2(true);
                var picked=SwCad.Components(model).Where(c=>!c.IsSuppressed()&&SwCad.Matches(c.Name2,isolate)).ToArray();
                if(picked.Length==0) throw new Exception("No components match -Isolate.");
                foreach(var c in picked) c.Select4(true,null,false);
                if(!assembly.Isolate()) throw new Exception("Isolate failed");
                isolated=true; model.ClearSelection2(true);
            }
            foreach(var name in views) {
                string named=name.StartsWith("*")?name:"*"+name;
                model.ShowNamedView2(named,-1);
                model.ViewZoomtofit2(); model.GraphicsRedraw2();
                string file=prefix+"-"+named.TrimStart('*').Replace(' ','-').ToLowerInvariant()+(isolated?"-isolated":"");
                string bmp=Path.Combine(outDir,file+".bmp"), png=Path.Combine(outDir,file+".png");
                if(!model.SaveBMP(bmp,width,height)) throw new Exception("SaveBMP failed for view "+named);
                using(var image=Image.FromFile(bmp)) image.Save(png,ImageFormat.Png);
                File.Delete(bmp); written.Add(png); Console.WriteLine(png);
            }
        } finally {
            if(isolated) assembly.ExitIsolate();
            model.ClearSelection2(true);
            try { if(orientation!=null) { view.Orientation3=orientation; view.Translation3=translation; view.Scale2=scale; } } catch { }
            model.GraphicsRedraw2();
        }
    }
}
