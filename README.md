# Editing SolidWorks with an AI agent

This kit lets an AI coding agent (Claude Code, Codex, or similar) edit a SolidWorks model safely. The agent drives the SolidWorks you already have open through its API, with no clicking and no screen control. It works from a set of tested commands: they check the model after every change, undo changes that break it, and only save files that actually changed.

You describe the change ("make the base plate 20 mm wider", "check the arm clears the battery at every fold angle", "what does this weigh?"). The agent reads the model, makes the change through these commands, validates it, and shows you an image of the result.

## How it works

```
You  ->  AI agent (reads AGENTS.md)  ->  tools\cad.ps1 <command>  ->  PowerShell 5.1 + C#  ->  COM API  ->  your open SolidWorks
```

- **AGENTS.md** holds the rules the agent follows: attach to the open session, ask before running if you are working in SolidWorks, never fix components to hide missing mates, validate after every change, and so on. Claude Code reads it through `CLAUDE.md`.
- **tools\cad.ps1** is the single entry point. Run it with no arguments to see every command, labeled `read`, `moves` (changes the open model without saving) or `saves`.
- **The model is parametric.** Master inputs are global variables (Tools > Equations), and parts follow them through equations. The agent changes those inputs with `cad.ps1 set`; it doesn't drag geometry around.
- **project\ProjectChecks.cs** holds your design's own rules, for example "this hole must stay aligned with that bracket". `validate` and `set` run it, and `set` rolls a change back if it fails.
- **SOLIDWORKS_API_NOTES.md** collects API lessons learned the hard way: COM quirks, selection marks, equations that silently don't apply, Simulation API traps. The agent reads it before API work and adds to it when it learns something new.
- **Git + Git LFS** keep the history of the CAD files, so the agent never makes backup copies.

## Commands

| Command | Kind | What it does |
|---|---|---|
| `inspect` | read | JSON snapshot: global variables, errors; with `-Filter name`: component placement, bounds, material, mass, custom properties |
| `validate` | read | Rebuilds and checks What's Wrong, mate errors, every part's features, over/under-defined sketches, fixed or floating components, optional interference, then your project checks |
| `set` | saves | Changes global variables (`-Set Width=120mm,Height=40`). Rebuilds, validates, saves only the files that changed; on any failure it restores the old values and saves nothing. `-Test` validates and then restores |
| `verify-reopen` | read | Closes the model, reopens it from disk, and checks that globals and every component's position match, then validates. Proves the saved files hold the design |
| `interference` | read | Exact solid overlaps between parts (`-Filter`, `-Against`, `-Ignore`) |
| `sweep` | moves | Steps a mechanism through poses by setting a mate dimension (`-Dimension D1@Angle1 -Values 0deg,45deg,90deg -Moving Arm`). At each pose it checks errors, real position and rotation, and interference, then restores |
| `register` | read | Bill of materials with quantity, material, mass, mass source and part notes (CSV and JSON) |
| `assign-material` | saves | Assigns a material to parts matching a name, checks the geometry didn't change, records how the mass was derived |
| `views` | read | PNG images of the model from any view, optionally with only some components shown |
| `session` | read | Lists open documents and their unsaved state; saves or closes everything only when you ask |
| `check-env`, `test-env`, `compile` | read | Setup checks, and a compile of all tool code without touching a model |

Every command takes `-ModelPath`. Without it, a command uses `model` from `cad.config.json`, or else the document that is active in SolidWorks. Results go to `reports\`.

## Set up a project

1. Install Git, Git LFS and an AI coding agent. Run `git lfs install` once.
2. Copy this folder's contents into your project's repository (or start a new one from it). Put your SolidWorks files in a folder such as `design\`, and set `"model": "design/YourAssembly.SLDASM"` in `cad.config.json`.
3. Open SolidWorks and the model. In a terminal at the repository root:
   ```powershell
   powershell.exe -NoProfile -File .\tools\cad.ps1 check-env -CheckOpenModel
   powershell.exe -NoProfile -File .\tools\cad.ps1 inspect -Parameters
   powershell.exe -NoProfile -File .\tools\cad.ps1 validate
   ```
4. Give your design a few master global variables (Tools > Equations) and link part dimensions to them. `set` only changes globals that hold a plain value; globals defined by an expression are derived, and `set` refuses them.
5. Fill in **Project rules** in `AGENTS.md` (coordinates, naming, what must not change, links to your design docs). Put checks the design must always pass in `project\ProjectChecks.cs`.
6. Start your agent in the repository and ask for something. It reads `AGENTS.md` first.

Details on discovery, overrides and other computers: [docs/SETUP.md](docs/SETUP.md).

## Working with the agent

- **Ask for outcomes, not clicks.** For example, "Widen the body to 320 mm and make sure nothing clashes", or "Find out why the bracket mate fails after the last change".
- **Let it make commands.** When the agent needs the same steps twice, AGENTS.md tells it to turn them into a `cad.ps1` command: in `tools\` if it's general, in `project\` if it's specific to your design. Over time your command set grows to fit your design. Examples from the project this kit came from: a validator for a specific mechanism, a whole-system weight estimate, an automated FEA run.
- **Watch for "ask first".** If you're working in SolidWorks at the same time, the agent should ask before running anything. API calls wait while SolidWorks is busy, and a save could catch your unsaved work. Commands that save refuse to run on models with unsaved changes.
- **Keep notes on the parts.** Assumptions like "proxy geometry", "mass from supplier datasheet" or "not modeled: threads" go in the part's custom properties, where `inspect` and `register` show them. Don't put them in a document that drifts.
- **Look at the picture.** After each geometry change the agent shows an image (`cad.ps1 views`). Glance at it, since a passing validation can't tell you the design is what you meant.
- **Coordinate on files.** Git can't merge two people's edits to the same SolidWorks file. Agree who edits which parts, and close or save SolidWorks documents before pulling or switching branches.

## Limits

- Windows only, SolidWorks with its API files installed; Windows PowerShell 5.1 (not PowerShell 7). Built and used with SolidWorks 2026; check other versions with `cad.ps1 compile` and a `validate` run.
- Validation finds rebuild errors, broken mates, bad sketches, overlaps and whatever your project checks test. It doesn't qualify strength, tolerances or manufacturability.
- `sweep` checks discrete poses, not a continuous swept volume.
