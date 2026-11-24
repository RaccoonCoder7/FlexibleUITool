# Changelog

## [1.0.12]

- An object only hidden with `View while editing` no longer counts as having its own layout on the screen.
- After undoing a grid placement, the save button goes back to `Saved` when nothing is left to save.
- With `Move them together` off, `Column` and `Row` move the selected objects by the same amount instead of piling them on one cell.
- `Pin to` scales a single object down when it does not fit, like a group.
- Preview: a rotated device shows the matching device name.
- Check: `Only issues` is on by default, and thumbnails wrap to the window width instead of scrolling sideways.
- The scene's UI prefab list shows repeated prefabs as one row with a count (`Score ×10`).
- `View while editing` scrolls after 8 rows, and the problem boxes on a screen fold into one line with a count.
- Shorter window text: removed explanations that repeat a label or the screen, and shortened long messages and tooltips. `SafeArea` is the one word used for it.
- Removed unused code.

## [1.0.11]

- `Move them together`: one Undo brings back every object. An object whose size did not change could keep its new position, and switching screens then saved it.
- After an Undo or `Back to portrait`, the group's cell fields show where the group really is instead of the last typed cells.
- During Play Mode the Edit tab no longer says the scene has no UI prefabs; it says the list comes back when Play Mode stops.

## [1.0.10]

- A prefab whose root is a canvas is drawn in Preview, Check and the stopped Game view (it could be black or disappear).
- The scene conversion button and its notice are hidden during Play Mode.
- A canvas converted from a `Canvas Scaler` keeps that scaler's rule (Match, Expand, Shrink, Constant Pixel or Physical Size) on the portrait screen, so the portrait screen stays as it was. Square and landscape screens use the short side. Canvases that already use `Flexible Canvas Scaler` do not change.
- Preview, Check and the CLI's `render` / `check` measure a prefab in the units of the canvas that shows it in the game (its own canvas, the canvas it sits under in the open scene, or the one recorded when it was converted), so an object that really leaves the screen on a 720-unit canvas is reported. Prefabs whose canvas is unknown use the project size as before.
- The Edit tab, the grid, `Shrink to fit the cells`, `describe`, specs and the AI use the same units as the game, so a value set while editing lands in the same place at runtime on canvases whose reference is not 1080.
- `Make the scene's UI Flexible UI` picks the unit per canvas: a canvas that is one screen made of parts becomes one prefab; a canvas of full-screen pages gets one prefab per page. `Show the list` can change it per canvas.
- Moving the root of a prefab on a non-portrait screen no longer overwrites its portrait values; the move is undone on save with a notice.
- New check `BLOCKED_INPUT`: a picture or text drawn over the middle of a button takes the press, so the button cannot be used. It stays visible on square and landscape even when `Hide base screen issues` is on.
- After the AI finishes, the result box also shows the tool's own check of the screens it made, so a summary saying nothing is wrong cannot hide a real problem.
- `Move them together`: the group can be placed by typing cells, and labels that hang outside a button count toward the group's size.
- An empty group whose frame leaves the screen is no longer reported when every picture inside it stays on the screen.

## [1.0.9]

- `FlexibleSafeArea.AdjustSafeArea`: the app can correct the safe area (for example, an Android status bar drawn over the game, which Unity leaves out of `Screen.safeArea`). It gets `Screen.safeArea` and returns the rect to use; every `Flexible Safe Area` follows the corrected value and applies it again when it changes. `FlexibleSafeArea.CurrentSafeArea` returns the corrected value. Without it nothing changes.
- Preview and Check turn off `Flexible Safe Area` on their copies after placing them with the device's safe area, so the real screen and the app's correction never overwrite the simulated one in Play Mode.
- `Show the problem` in the Check tab switches to the Edit tab, which shows the problem under the screen buttons (`Select them` selects the objects again, `x` closes it). The fix buttons of 1.0.8 are gone: they used the portrait layout and threw away what you had made on that screen. A new check or closing the prefab clears the problem, and `Only the open prefab` no longer shows the results of another prefab.
- The `Unsaved changes` line under the screen buttons is gone (it pushed the fields down while you typed); the yellow `Save` button shows it.
- Dragging over the grid shows the size in cells. Section headers show their explanation only while closed, so it does not cover the fields. An object not placed on the grid shows which cells it covers now.
- AI: the box shows `Connected to Claude Code` with the version and account; with a Claude subscription the result says the run is not charged separately (the dollar amount is the API price); an English status line before the summary is left out in Korean and Japanese.
- Docs: install steps use the Unity 6.3 menu names.
- Results that only inform (template saved or applied, prefabs made Flexible UI) appear in the window under the screen buttons instead of a dialog (on Windows those dialogs always showed a warning icon).
- Check tab: buttons that are off are drawn dimmed. `Base screen issues hidden (N)` under a thumbnail shows them when clicked.
- With no prefab open and no UI prefab in the scene, the Edit tab says how to start instead of pointing to an empty list.
- Manual: a short "Where to start" for new users and for projects that already have UI.

## [1.0.8]

- Check accuracy: duplicate and false issues are left out (parts of an inner Flexible prefab, objects with nothing visible, objects covering the whole screen, text whose real letters stay inside, scroll bar parts, decorations that overlap, overlaps split by a full-screen dim). Text components that inherit from TextMeshPro are recognized as text. Places other prefabs take are measured at each device size.
- New weak warning `TEXT_OUTSIDE_SAFE_AREA` (yellow in the Check tab): text that is not in a button sits under a notch or home indicator.
- `Show the problem` in the Check tab now shows the problem in the Edit tab with fix buttons: `Move inside the safe area`, `Pin to the nearest edge`, `Shrink to fit its cells`.
- Several objects as one group: with several objects selected, `Move them together` places them with a Scene view drag or `Pin to` as one group, keeping their places and sizes to each other (pieces of a popup without a common parent).
- Objects an outer prefab adds inside an inner Flexible prefab belong to the outer prefab: it places them, checks them and saves their layouts.
- Check tab: `This scene` scope (Flexible UI prefabs placed in the open scene), and each prefab row shows per screen how many objects have their own layout and how many issues. The scene UI list shows `✔ made/other screens`.
- `Shrink to fit the cells` covers visible parts that stick out of the object, may use the grid margin when it spans every column or row, and warns when the object's own picture covers the whole portrait screen.
- `Flexible Preview Items` component: sample items for lists the game fills by code, drawn only in Preview, Check and `describe`.
- Before the tool opens another prefab, unsaved changes of the open one are asked about (`Save` / `Cancel` / `Don't save`) instead of being saved by Prefab Mode auto save.
- Switching screens no longer leaves the prefab marked as changed (faster, no needless saves), and Preview / Check no longer mark dynamic TMP font assets as changed. The Edit tab refreshes its lists on changes instead of on a timer.
- Scene setup names new prefabs after their canvas (`HUDCanvas_Background`) and shows a progress bar.
- `View while editing`: Shift + click selects a range of rows.
- CLI: `review` counts like the window (`hideBaseIssues`, `hiddenBaseIssues`) and names screens like `check`; `-Preset` accepts those names (`landscape@1.24`, `aspect@1.30`); `-Show` also turns on the hidden objects above; panels hidden with `-Hide` are not checked as `HIDDEN_OUT_OF_SCREEN`; a spec may add `fit` blocks to the base profile; `describe` lists objects the prefab added inside inner Flexible prefabs.
- The Workspace and Samples folders the tool makes are kept out of git (`Assets/FlexibleUITool/.gitignore`).

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
