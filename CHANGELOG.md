# Changelog

## [1.0.7]

- Scene setup: with no prefab open, the Edit tab lists what in the open scene is not ready (canvases without `Flexible Canvas Scaler`, full-screen prefabs that are not Flexible UI, UI placed straight in the scene) and `Make the scene's UI Flexible UI` fixes them; loose UI groups are saved as new prefabs.
- View while editing: show or hide blocks only while you edit (panels the game opens by code, also panels hidden by turning their Canvas off and prefabs saved turned off). Nothing is saved; Preview, Check and the AI use the same view.
- While the editor is stopped, scene UI shows the layout of the Game view's shape (landscape / square) without changing the scene (`Tools > FlexibleUITool > Show Screen Layouts While Stopped`).
- Opening an inner prefab (or going back to the outer one) keeps the screen you were on.
- The Edit tab warns about blocks that stick out of the screen (`Fit them all into the screen`, `Fit into the screen`), lists blocks with their own layout on the screen, and edits position / size / scale of the selected blocks directly.
- `Choose the screen by` in the Flexible Layout inspector: the whole screen, or the prefab's own size (for panels).
- New checks (Edit tab, Check tab, CLI): panels the game opens later (`HIDDEN_OUT_OF_SCREEN`), blocks squashed to a negative size (`COLLAPSED`), list items cut off across the scroll direction (`CLIPPED_IN_LIST`), and buttons under or blocks covering a place another prefab takes (`UNDER_RESERVED`, `COVERS_RESERVED`). The Edit tab lists them under `problems you cannot see right now`, without the ones the portrait screen already has, with `Show` for hidden panels.
- Places other prefabs take: `Other prefabs keep clear of this` on a block (such as a menu bar) draws its place in the Scene view of other prefabs and feeds the check and the AI. `... is shown over this prefab` turns it off for prefabs the game shows without the bar.
- Grid placement: `Use its own size` (sticking to an edge or centering keeps the cells' size), `Shrink to fit the cells` (keeps the portrait size and scales the block down to fit on every screen size, recalculated at runtime), and `Pin to` (same distance from a corner of the safe area as on portrait).
- CLI / specs: `keepSize`, `pin`, `fit`, `scaleBy`, axis mode `keep`, `follow` (keep the portrait relation to a sibling such as a card background), `-Hide` / `-Show` for `describe` / `render` / `check`, `reserved` and `inside` in `describe`, `detail` on issues.
- Fixed: the Check tab and `check` did not look inside full-screen containers for out-of-screen and overlapping blocks; prefabs whose root Canvas is saved turned off rendered empty in the CLI; a backdrop button covering the whole screen was reported under the notch.
- The AI auto-layout section can be folded.

## [1.0.6]

- Prefab grids: turn on `Use a grid only for this prefab` in the Edit tab to give a prefab its own grids (one per screen, starting as a copy of the project grids). With it off, the Edit tab shows the project grid read-only. The Scene view, grid placement, templates, AI auto-layout and `apply-spec` / `describe` use the grid the prefab uses (`describe` reports `ownGrid`).
- Project grids window (`Tools > FlexibleUITool > Project Grids`, or `Project grids...` in the Edit tab) edits the grid of each screen for all prefabs without their own grid. Changes are saved to the settings file right away.
- `Keep the selected blocks inside the safe area` moved next to the block's grid fields. It is a per-block, per-screen value: changing it places the selected blocks right away, like the other fields.
- Fixed: a block placed on the grid lost its grid settings (cells, modes, safe area) after switching screens when its result was the same as on the portrait screen.
- The tutorial practice prefab uses its own grid, so grid changes in the tutorial never touch other prefabs.

## [1.0.5]

- Several blocks at once: with more than one block selected, the Edit tab works like Unity's Inspector. A value shared by all selected blocks is shown as it is, a value that differs is shown as `—`, and changing a value (role, column, row, width, height, horizontal, vertical, safe area) sets it on all of them with one Undo. `Back to Portrait` resets the selected blocks that have their own layout. Dragging in the Scene view works with one block at a time.
- Fixed: the orange frame in the Scene view kept the cells of the previous screen after switching screens. It now shows the cells the selected blocks sit on in the current screen (or the range being dragged), and the column / row / mode fields follow the selected block and screen.
- The `(optional)` label is removed from the `Place on a grid` and `Templates` headers.
- New look for the tutorial raccoon.

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
