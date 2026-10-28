# Changelog

## [1.0.4]

- Grid placement in the Edit tab applies right away: changing `Column` / `Row` / `Width` / `Height` (when you finish typing) or `Horizontal` / `Vertical` moves the selected block at once. The `Apply to selected block` button is removed. Hand nudges on top of the cells are kept.
- The `Auto` axis mode is removed. New blocks use `Stretch` by default, also in specs without `modeX` / `modeY` (AI auto-layout and agents). Blocks placed with `Auto` by older versions keep their layout and show their actual mode.
- Fixed: the overlap check ignored any block fully inside another, even blocks that were apart on the portrait screen. Full containment is now ignored only if the two blocks are also nested on the portrait screen (for example a title on a bar). The Edit / Preview / Check tabs, `check` and the AI auto-layout use the new rule.

## [1.0.3]

Reworked for people who use only the editor GUI.

- One window, `Tools > FlexibleUITool > Flexible UI Window`, with `Edit` / `Preview` / `Check` tabs, replaces the Layout, Preview and Review windows.
- Other screens are edited in place: open the prefab and press a screen button (Portrait / Landscape / Square) in Prefab Mode. The portrait layout stays on disk; work copies in `Assets/FlexibleUITool/Workspace/` are no longer made (a leftover one can still be applied or discarded from the Edit tab).
- The Game view follows the screen being edited, and is set back if its shape does not match.
- Built-in tutorial in the window: a basic tutorial (11 steps, including Preview and Check) and an advanced one (grid size, placing on cells, axis modes, fine-tuning, roles, templates). Any chapter can be run again from the tutorial menu. Shared settings changed by the tutorial are restored when it ends.
- Auto-layout with AI: after `Connect AI`, `Auto-layout with AI` runs Claude Code on this PC (subscription login, no API key) to make the landscape and square layouts. You can choose the model and effort level and add extra instructions (kept per prefab). The prefab is backed up first and `Undo AI changes` restores it. Token usage is shown and logged to `Library/FlexibleUITool/ai-runs.log`. The AI run is isolated from personal Claude Code settings and may only run the tool's layout commands.

## [1.0.2]

- The tutorial sample is imported into `Assets/FlexibleUITool/Samples/` with `Tools > FlexibleUITool > Import Tutorial Sample` (or the agent's `import-samples`), so everything the tool creates stays in `Assets/FlexibleUITool/`. The Package Manager sample entry is removed, because it always imports into `Assets/Samples/<package>/<version>/`.
- A sample imported by an earlier version is moved to the new folder, and the empty `Assets/Samples/...` folders are removed.
- Fixed: a block hidden by one profile stayed hidden after switching to a profile that has its own layout for the block but does not set its visibility (also for Layout Group settings). Such profiles now fall back to the prefab's original value. This affected Play Mode and the Preview / Review / CLI renders.
- Fixed: `game-view` captures could show a stale frame right after a resolution change (for example a zoomed-in looking layout). Before each capture the tool now resets the Game view zoom, checks that the render size, canvas scale and FlexibleLayout profile match the new resolution (waiting up to 3 more seconds and warning if not), and renders the Game view once.

## [1.0.1]

Editor performance and memory.

- Profile settings lookup is cached instead of searching the project on every repaint (Preview and Review windows redraw much faster).
- Review Window thumbnails are rendered at the current thumbnail size instead of a fixed 400 px, using about a quarter of the GPU memory by default. A hint appears when you enlarge them past the rendered size.
- The source prefab structure used for scale adjustments is cached while a workspace is open.
- The agent command bridge checks its inbox every 0.1 s instead of on every editor update.

## [1.0.0]

First package release.

- One prefab for portrait, square and landscape: `FlexibleLayout`, `FlexibleCanvasScaler`, `FlexibleSafeArea`.
- Layout Window (workspaces, grid placement, roles, templates), Preview Window, Review Window (device presets and profile edge ratios).
- Editor UI in English, Korean and Japanese.
- Command line for coding agents (`Library/FlexibleUITool/flexui.ps1`, guide in `AGENTS.md`).
- Tutorial sample.
