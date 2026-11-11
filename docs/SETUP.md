# CAD automation setup

## Prerequisites

- Windows with licensed SolidWorks and its API interop files (`<SolidWorks>\api\redist`). Built with SolidWorks 2026; for another release, run `cad.ps1 compile` and validate on a test model first.
- Windows PowerShell 5.1 (`powershell.exe`), not PowerShell 7 (`pwsh.exe`), for the COM/Add-Type tools. Run it as the same Windows user and privilege level as SolidWorks (COM can't reach an elevated SolidWorks from a normal terminal, or the reverse).
- Git and Git LFS, installed before cloning. Run `git lfs pull` before opening models.
- SolidWorks Simulation only if you write Simulation commands (`Import-CadCode -Simulation` adds its interop; see the Simulation section of SOLIDWORKS_API_NOTES.md).

## Which model a command uses

Commands share `tools/CadEnvironment.ps1`. It finds the repository from its own location, so the terminal's working directory doesn't matter. The model is chosen in this order:

1. `-ModelPath <file>` (alias `-AssemblyPath`) on the command
2. the `CAD_MODEL` environment variable
3. `"model"` in `cad.config.json`, a path relative to the repository root
4. the document that is active in SolidWorks

Parts (`.SLDPRT`) and assemblies (`.SLDASM`) both work; `interference`, `sweep` and `-Isolate` need an assembly. The model must already be open in SolidWorks: commands never open it themselves (except `verify-reopen`, which closes and reopens it).

## Finding SolidWorks

SolidWorks is found from the running application, the registry, then the usual Program Files folder. For an unusual install location, set `SOLIDWORKS_HOME` for that terminal:

```powershell
$env:SOLIDWORKS_HOME = 'D:\Applications\SOLIDWORKS'
```

No `.env` file is used. Compiled tool code is cached in `%LOCALAPPDATA%\SolidWorksAiCad\build` and rebuilt automatically when a source or the interop changes; deleting the folder is always safe.

## Checks

```powershell
powershell.exe -NoProfile -File .\tools\cad.ps1 test-env                    # discovery self-test, no SolidWorks needed
powershell.exe -NoProfile -File .\tools\cad.ps1 compile                     # compile all tool code, no model touched
powershell.exe -NoProfile -File .\tools\cad.ps1 check-env                   # finds SolidWorks and the model
powershell.exe -NoProfile -File .\tools\cad.ps1 check-env -CheckOpenModel   # with the model open: missing parts and paths
```

`-CheckOpenModel` reports missing component files, components outside the model's folder (they won't come along with the repository) and material libraries referenced by absolute paths that don't exist. When moving to another computer, also check imported STEP links, templates and appearances. Keep project material libraries (`.sldmat`) in the repository; `assign-material -Database <file>.sldmat` registers their folder with SolidWorks.

The bootstrap detects a Git LFS pointer file (a model that `git lfs pull` hasn't downloaded yet) and stops instead of handing it to SolidWorks.

## Adding a command

1. Write the C# in `tools/` (general) or `project/` (tied to your design). Start from `SwCad` helpers: `sw.Model()`, `SwCad.Components`, `SwCad.Globals`, `sw.Equation`, `SwCad.Bodies`, `SwCad.Intersections`, `SwCad.Save`.
2. Add a small `.ps1` launcher like the existing ones: dot-source `CadEnvironment.ps1`, call `Initialize-CadEnvironment -ModelPath $ModelPath`, `Import-CadCode` the sources, call the C#.
3. Add it to the table in `tools/cad.ps1` with `read`, `moves` or `saves`. `cad.ps1` warns about launchers that aren't listed.
4. Run `cad.ps1 compile`, then try it on the open model.

Rules that keep commands safe: check `SwCad.RequireClean` before anything that saves, restore temporary changes in `finally`, read values back after writing them (SolidWorks often accepts a call and does nothing), and save only documents you changed.
