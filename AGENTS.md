Keep this file minimal: entry points and non-obvious rules only. Current dimensions and decisions go in the design docs; reusable API lessons in SOLIDWORKS_API_NOTES.md.

# Layout

- The model: the `model` path in `cad.config.json` (relative to the repository root), or whatever document is active in SolidWorks. Project material libraries (`.sldmat`), if any, live in the repository.
- `tools/`: `cad.ps1` (lists and runs every maintained command), `CadEnvironment.ps1` (setup and `Import-CadCode`), `SwCad.cs` (shared helpers) and the general commands.
- `project/`: this design's own code. `ProjectChecks.cs` holds design rules that `validate` and `set` enforce; design-specific commands go here too.
- `reports/`: command output (JSON, CSV, images).

Before editing, read the design docs listed under Project rules below; before API work, read [SOLIDWORKS_API_NOTES.md](SOLIDWORKS_API_NOTES.md).

# Working in SolidWorks

- Work through the API on the SolidWorks session that is already open (Windows PowerShell 5.1, C# interop); never start a second instance. Use computer use on that window only for what the API can't do, or to look at the result.
- If the user is working in SolidWorks, ask before running commands: API calls wait while it is busy, and a save can catch their unsaved work. Never kill SolidWorks without asking.
- Run commands through `tools\cad.ps1`; with no arguments it lists them as read, moves or saves. For model state (parameters, errors, placement, bounds, mass, part notes) use `cad.ps1 inspect` (`-Filter <name>`) instead of writing a probe. It shows the last rebuild, and its default error check is What's Wrong; add `-DeepErrors` (or run `validate`) before calling the model clean.
- Extend a maintained command rather than writing a near-duplicate. Throwaway scripts go in `scratch/` (gitignored) and load C# with `CadEnvironment.ps1` and `Import-CadCode`. When you need the same script or steps a second time, make it a command and add it to `cad.ps1`: in `tools/` if it works on any model, in `project/` if it depends on this design's names or geometry. Run `cad.ps1 compile` after editing C#.
- When an API call behaves unexpectedly and you find what works, add the lesson to SOLIDWORKS_API_NOTES.md.
- No hardcoded user or SolidWorks paths and no `.env` ([docs/SETUP.md](docs/SETUP.md)). If the SolidWorks license or VPN drops, ask the user to restore it.

# Design rules

- Master inputs are global variables in the top model; part dimensions follow them through equations. Change them only with `cad.ps1 set`. One owner per input, one-way dependencies, no competing drivers.
- Use native editable features, fully defined sketches and datum/axis mates; never Fix a component to hide a missing mate.
- Record a part's assumptions and status (proxy, purchased, mass source, what isn't modeled) as custom properties on that part, not in Markdown; `inspect` and `register` show them. Don't put measurable values (dimensions, counts, positions) in docs: they drift, and `inspect` reads them live.
- Equations and tools refer to component instance names; don't rename instances without updating them.
- Purchased hardware keeps its real size. Envelope components are fit references, not hardware or mass.
- For a mechanism, check position and rotation at several poses (`cad.ps1 sweep`), not just the driving angle.

# Validate and save

Run `cad.ps1 validate` after any change; `cad.ps1 verify-reopen` after changing references or dependencies. Restore temporary tests in `finally` before saving, and don't save or close documents you didn't change. After a geometry change, show the user an image of the result in the chat: `cad.ps1 views`, or a screenshot of the SolidWorks window. Don't make snapshot copies or Pack and Go archives; Git LFS keeps the history.

# Project rules

Add this design's own rules here, and link its design docs (master parameters and what they drive, status, decisions). Examples: coordinate convention (which axis is up and front), parts that share a file with different configurations, parts that must never be resized.
