# SolidWorks API notes for future agents

These are reusable lessons verified while editing native models with SolidWorks 2026 from Windows PowerShell 5.1 and C#. Project dimensions, component placements and historical decisions belong in the project's design documentation, not here. "Here" and "in this session" mean the lesson was observed once on a real model: treat it as a strong hint, and verify on your own model. Add new lessons the same way: what you tried, what actually happened, what worked.

## Connect and inspect before editing

- On this Windows installation, use Windows PowerShell 5.1 (`powershell.exe`) and the installed `SolidWorks.Interop.sldworks.dll`, then `Marshal.GetActiveObject("SldWorks.Application")`. Attach to the user's existing session. Do not start another SolidWorks instance unnecessarily.
- Normalize paths with `Path.GetFullPath` before `GetOpenDocumentByName`; mixed forward/backward separators can cause a loaded document lookup to return null.
- Use typed interop and inspect the actual installed signatures when uncertain. Some setters, including `IPartDoc.SetMaterialPropertyName2`, return void. A successful compilation does not prove a COM operation succeeded.
- Activate the intended owning document before geometry/equation operations. For equations that refer to an assembly context, edit the component in that assembly context. Clear selections before every independent operation; lingering marks can change what a feature acts on.
- Internal linear units are meters and angles are radians. State units in equations (`mm`, `deg`) and convert only at reporting boundaries.
- Native API is preferred for modeling, measuring, mates, saving and CAD image export. Use UI automation only where a suitable API is unavailable.

## Session and license

- With a network license, a running SolidWorks keeps its seat; a fresh start off the license network (e.g. VPN dropped) hangs on the splash screen with "Cannot connect to license server". Never kill SolidWorks to recover from a stuck call without asking the user, and back up dirty documents first (SaveAs a copy).
- Long Simulation sessions degrade: after many solves, random calls failed with RPC_E_SERVERFAULT (0x80010105). Restart SolidWorks before a long analysis run.

## Equations and configurations

- Match equation left sides exactly and update an existing driver instead of adding another. `SetEquationAndConfigurationOption` did not reliably change existing equations in this session; setting `Equation[index]` explicitly, then evaluating/rebuilding and measuring the result, did.
- Apply shared controls to all intended configurations. Explicitly suppress configuration-specific machining in other configurations; adding a feature while one configuration is active can affect the others.
- Avoid using the same name for a global variable and feature: SolidWorks can interpret a colliding name as a feature suppression equation.
- A copied dimension can retain `ReadOnly=true`. Remove a conflicting local dimension equation and clear ReadOnly before assigning the deliberate external driver.
- Use official assembly equation reference syntax, including component instance qualifiers. Resolve and verify real names; filenames and component instance names are different. Never assume a variable changed just because the equation text was accepted.
- Keep one owner for each design input and a one-way dependency graph. Do not scale purchased hardware when resizing a support structure.

## Native geometry and selection

- `InsertMoveCopyBody2` needs selected bodies/surfaces with `SelectData.Mark=1`; rotation references use mark2, translation references mark4. Check the resulting bounds and axis, rather than trusting a feature name or assumed rotation composition. The observed successive body rotations did not produce the intended world orientation.
- SolidWorks MathTransform array entries0..8 are the rotation rows for its row-vector convention,9..11 are translation,12 is scale. Verify against a known point before transforming vendor geometry.
- Use `component.GetBodies3` for configuration-correct component bodies. Copy each body and apply the component transform before assembly-space collision checks. A part's active configuration alone is not a reliable assembly mass/geometry source.
- `IComponent2.GetBox` is approximate: after a rebuild its reported height of a flat plate face missed the actual planar face and broke an FEA load-area projection. For exact load geometry, copy the configuration-correct body, apply the component transform, and query `IBody2.GetExtremePoint` along the six assembly axes. Cache bounds within one layout read. A live body can also be copied into an analysis-only part with `CreateFeatureFromBody3`, avoiding a disk-based `InsertPart3` reference that can miss unsaved source edits; verify the copied volume and assembly transform.
- Body Boolean operation codes observed:15901 intersect,15902 subtract,15903 union. Work on copies because operations can consume their inputs. Broad-phase bounds checks should precede exact intersections.
- Cache COM names, transforms and bounds once per pass. Repeated `Name` calls inside nested LINQ/body loops can multiply thousands of cross-process calls and make otherwise small audits very slow.
- Each cross-process call costs roughly 45 ms here. Walking every feature of every part with `GetErrorCode2` took about 64 s for an 80-component assembly; `Extension.GetWhatsWrong` took 57 ms. Prefer whole-model queries, and do loops inside one C# method rather than from PowerShell.

## Mates, verification and saving

- Prefer origins, named planes, axes and purposeful mounting cylinders to incidental perimeter edges. Do not use Fix as a substitute for design intent. Check mate and feature error codes after solving, not only component transforms.
- In the observed `AddMate5` calls, error1 accompanied success; verify the returned mate as well. A fully constrained component reports status3. Sketch constraint status3 is fully defined, but an intentionally articulating mechanism need not fully constrain every component.
- Unlocked concentric mates with explicit datum-plane clocking were more reliable than locking an imported datum-axis mate. Preserve intended degrees of freedom.
- Rotation alone cannot establish which branch a linkage (e.g. a four-bar) solved to. Check translation and orientation against intended motion and verify limit bounds (`cad.ps1 sweep` reports both). Testing a few poses is not a continuous swept-volume check.
- Wrap parameter/pose experiments in `try/finally`, restore the exact agreed values and original pose, and validate the restored state before saving. Keep native limit bounds unchanged unless deliberately recalibrating.
- Save changed active-design parts and the working assembly with `Save3`; inspect return status, error/warning codes and dirty flags. Do not overwrite historical references or blanket-close the user's open documents.
- Export actual CAD views with `SaveBMP`. Isolation and view changes are useful for presentation but do not substitute for a saved geometry change. Preserve useful working visibility after export.

## Script hygiene

- Keep current reusable commands separate from one-time migration/constructor scripts. Constructors should fail if their target files/features already exist; diagnose partial execution before resuming.
- Windows PowerShell5.1 `Add-Type` uses an older C# compiler: avoid declaring a local variable with the same name as an earlier lambda variable in a nested scope. Compile before performing mutations.
- Record only tested behavior and useful failure modes. Link primary API documentation where it resolves an ambiguity; do not turn every failed attempt into a rule.

Primary references: [assembly equation syntax](https://help.solidworks.com/2023/english/SolidWorks/sldworks/c_wn_equations_assemblies.htm), [body move/copy selection and parameters](https://help.solidworks.com/2022/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IFeatureManager~InsertMoveCopyBody2.html).

## More lessons

- Normalize **every native file API path**, including SaveAs and AddComponent5. An insertion with a mixed-separator path returned null although the part was loaded. A generated document initially saved through such a path later rejected Save3 with generic error1; saving under a clean normalized path restored repeated dirty saves. Treat this as an observed recovery, not proof that every error1 has the same cause.
- After SaveAs changes a part filename, read back the component name/path. The component name changed automatically in this session; a Name2 assignment did not restore the assumed label. Update maintained consumers from observed names rather than guessing.
- Compile mutually dependent C# files in **one** Add-Type -Path invocation. Separate in-memory Add-Type assemblies do not automatically reference each other, and anonymous types can collide between compilations in the same PowerShell process.
- Restoring an equation string alone did not immediately restore geometry in both configurations. Activate the owning part, evaluate/rebuild each relevant configuration, return to the assembly, and assert the actual dimensions after rollback.
- ExcludeFromBOM is not the same as a native envelope classification. For reference-only fit geometry, select the component and use IAssemblyDoc.CompConfigProperties6 with the intended IsEnvelope flag while preserving suppression/configuration/solving settings; verify IsEnvelope() afterward. Keep such geometry out of physical mass accounting.
- Capture comparison geometry with the feature tree rolled fully forward, outside sketch edit mode. A body read during sketch editing differed from the final solid because of rollback state; comparison against the saved model confirmed the solid was unchanged.
- Reacquire sketch entities from `GetActiveSketch2()` after entering edit mode. The stale closed-sketch entity route faulted in this session; fresh entities selected with `Select4`/`Select2` plus `SketchAddConstraints` worked.
- An underdefined sketch can have every segment fully constrained while a standalone user point remains free. Inspect `ISketchPoint.Status` as well as segment Status. Anchor the actual missing relationship; do not dimension an already constrained infinite centerline or apply Fix to the whole sketch.
- `ForceRebuild3(false)` on an assembly rebuilds and dirties every referenced part. Prefer `ForceRebuild3(true)` (top level) after assembly-only changes. `Save3` with only the Silent option saved just the assembly (no referenced part files were rewritten), so it is safe when parts are dirty only from a rebuild.
- Temp directories can differ between execution contexts. Use an explicit shared path for a temporary CAD comparison file, and close/delete only that generated scratch document after checking it.
- IEquationMgr.Status reports the result of the preceding equation-manager operation; it is not a per-row error array. A successful equation read returned its row index here, while failed resolution returned -1. The indexed Disabled getter yielded misleading values in this interop path; use GetDisabledEquationCount and native evaluation/feature checks rather than interpreting those values alone.
- Deleting and recreating an assembly master global left dependent part aliases at zero in the reopened assembly. Rebinding those aliases in EditPart2 context, rebuilding each owning part and then the assembly restored evaluated values. Prefer updating existing globals, and validate downstream values after a saved-file reopen.
- When generating layout sketches, avoid inference to visible component geometry: preserve and temporarily disable swSketchInferFromModel (95) and swSketchInference (249), create the rectangle with AddToDB, then explicitly constrain its center point to Point1@Origin. Restore preferences in finally. Check actual parameter propagation and feature error codes, not sketch constrained status alone.
- AddDimension2 can block on the Modify dialog when swInputDimValOnCreate (10) is enabled. Preserve and temporarily disable that preference during scripted dimension creation. If recovery needs UI interaction, target the screenshot belonging to the modal dialog; clicking the disabled parent window did not dismiss it here.
- Equation writes can be silently rejected. Read the stored expression back immediately and verify downstream dimensions after evaluation. Validate cached equation indexes against their current left-hand name before reuse, especially after a deletion.
- Windows PowerShell could not cast the active COM object directly to ISldWorks in this environment even though an explicit C# cast worked. Keep typed SolidWorks orchestration in C# compiled with the interop assembly; use PowerShell as the runner.
- Split line (project) via `IModelDoc2.InsertSplitLineProject` needs the sketch selected with mark 4 and the target faces with mark 1; with default marks it silently does nothing. Check the body face count afterwards.
- After adding a project .sldmat file, refresh the registered material-folder list and GetMaterialDatabases before assigning the new material. In this session assignment silently failed until the new library appeared in the returned database list.
- Imported native parts can have localized datum names (for example `Plano frontal`, `Plano superior`, `Plano direito`). Inspect native RefPlane names rather than assuming English names when replacing face mates with datum mates.
- Closing an assembly can release its hidden part documents even while C# retains COM wrappers. Cache file path strings before CloseDoc; reacquire any remaining open documents by path rather than calling methods on the old wrappers (RPC_E_DISCONNECTED was observed).

## Simulation (cosworks) API

- `CosmosWorks.ActiveDoc` is whatever document is active *now*. Reading another open model (equations, components) can leave it active, and a study was once created in the design assembly that way. Re-activate the analysis document and assert `ISldWorks.ActiveDoc` is it immediately before every `CreateNewStudy3` or plot.
- Get the API from `ISldWorks.GetAddInObject("SldWorks.Simulation")` (load `Simulation\cosworks.dll` if null), cast to `CwAddincallback`, use `.CosmosWorks.ActiveDoc` for the active model. Compile against `api\redist\SolidWorks.Interop.cosworks.dll`.
- Entity arguments (remote loads, restraints, forces, distributed masses, result entities) must be plain `object[]`. A typed `IFace2[]` crashed the server (RPC_E_SERVERFAULT) or returned "select entity" errors.
- Remote load/mass: `SetMassValues2` needs `object[]{m, Ixx, Iyy, Izz, Ixy, Iyz, Ixz}`; a `double[]` returned "invalid mass" (10) from `RemoteLoadEndEdit`. In frequency studies every remote-mass type/coupling returned "invalid for analysis" (12); distributed mass on faces worked.
- A normal-force value applies to each selected face, not as a total. Verify loads with a restrained check study and summed reactions; `GetReactionForcesAndMoments` returns one 9-value record per node, not a total.
- Orthotropic solid material with a reference plane: material X/Y lie in the plane and material Z is its normal (verified with a cube referenced to the Top Plane, EX/EY/EZ = 100/10/1 GPa: apparent moduli 100 GPa along global X, 10 along global Z, 1 along global Y, the plane normal). Do not infer the mapping from a "change only the thickness modulus" sensitivity on a bolted/bonded assembly: joint bearing makes through-thickness stiffness genuinely matter (about 18% deflection change here), which once looked like a mapping error. Swapping the candidate mappings on the whole structure is not a reliable check either (the two effects nearly cancelled: 4.8% difference). Verify on a small loaded cube instead.
- `GetStressForEntities3`/`GetDisplacementForEntities` return {node, value} pairs; `Mesh.GetNodes()` returns {node, x, y, z} in metres. `GetDisplacementAtPoints` rejected surface points (invalid entity), so sample nodes instead.
- Inertia relief (`StaticStudyOptions.UseInertialRelief = 1`) solves unrestrained balanced cases; it also silently balances wrongly directed loads, so check load directions separately.
- A free-free frequency study failed (AnalysisFailed, 24) at about 170k elements but ran on a coarser mesh.
- `ICWPlot.ShowPlotOnSelEntities` with component body faces crashed SolidWorks (RPC 0x800706BE) on a two-plate assembly, reproducibly. Write results summaries before plotting so a crash there loses nothing.
- For clean result exports, set `ICWStudy.ShowOrHideForce` and `ShowOrHideFixtures` false and clear selections. Explicitly call `ICWPlot.SetComponentUnitAndValueByElem` after `CreatePlot`: displacement plots initially displayed angstroms here despite creating with millimeter unit code 0; the explicit setter produced verified millimeter legends. Use `SaveBMP(path,0,0)` for the current window size; a smaller requested bitmap clipped the numeric legend. Check the actual exported legend, not just the plot title.
