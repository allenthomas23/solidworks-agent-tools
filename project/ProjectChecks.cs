using System;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

// Your design's own rules. `cad.ps1 validate` and `cad.ps1 set` run this after their generic checks
// (rebuild errors, mates, sketches), and `set` rolls a change back when anything here fails.
// It starts empty. Add a failure with failures.Add("..."); keep it fast (seconds) and never save here.
//
// Examples (globals are the model's global variables in system units: meters, radians):
//
//   // a global must stay in range
//   if (globals["Wall_Thickness"] < 0.0015) failures.Add("Wall_Thickness below 1.5 mm");
//
//   // a part dimension must follow a global
//   var part = (IModelDoc2)SwCad.Components(model).First(c => c.Name2.StartsWith("Base-")).GetModelDoc2();
//   double width = ((IDimension)part.Parameter("D1@Base_Sketch")).SystemValue;
//   if (Math.Abs(width - globals["Base_Width"]) > 1e-7) failures.Add("Base plate does not follow Base_Width");
//
//   // a component must stay fully constrained
//   foreach (var c in SwCad.Components(model).Where(c => c.Name2.StartsWith("Bracket-")))
//       if (c.GetConstrainedStatus() != 3) failures.Add(c.Name2 + " is not fully constrained");
//
//   // two groups of parts must never touch
//   var lid = SwCad.Components(model).Where(c => c.Name2.Contains("Lid")).SelectMany(c => SwCad.Bodies(c)).ToList();
//   var pcb = SwCad.Components(model).Where(c => c.Name2.Contains("PCB")).SelectMany(c => SwCad.Bodies(c)).ToList();
//   foreach (var clash in SwCad.Intersections(lid, pcb)) failures.Add("Interference " + clash);
public static class ProjectChecks {
    public static void Run(SwCad sw, IModelDoc2 model, Dictionary<string,double> globals, List<string> failures) {
    }
}
