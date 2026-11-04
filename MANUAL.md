# FlexibleUITool Manual

FlexibleUITool lets **one UI prefab serve portrait, square and landscape screens**.
You build the portrait screen as usual, then press **Landscape** (or **Square**) in Prefab Mode and move blocks where you want them on that screen. The prefab remembers each screen, and the right one is shown at runtime from the screen's aspect ratio.

**You do not need to read this manual.** In Unity, open `Tools > FlexibleUITool > Tutorial`. A short tutorial inside the tool window walks you through everything in about 2 minutes, with buttons that light up where to click. No AI is needed.

- **Language**: the tool is shown in English, Korean or Japanese. Change it with the dropdown at the top right of the Flexible UI window (or `Tools > FlexibleUITool > Language`). `Auto` follows the Unity editor language. This manual quotes the English labels.
- **Coding agents** (Claude Code and others) can also do the work for you: see [section 5](#5-working-with-an-agent-cli).

## Contents

0. [Installation](#0-installation)
1. [Quick start](#1-quick-start)
2. [Setup (programmers)](#2-setup-programmers)
3. [The Flexible UI window](#3-the-flexible-ui-window)
4. [GUI tutorial](#4-gui-tutorial)
5. [Working with an agent (CLI)](#5-working-with-an-agent-cli)
6. [Reference](#6-reference)
7. [FAQ](#7-faq)

---

## 0. Installation

The tool is a Unity package (`com.raccooncoder7.flexibleuitool`). Tested on Unity 6.3. It needs `com.unity.ugui`, which Package Manager installs with it.

1. Open `Window > Package Manager`, press `+`, choose `Add package from git URL...`, and paste `https://github.com/RaccoonCoder7/FlexibleUITool.git` (add `#<tag>` at the end to pin a version).
2. Or add it to `Packages/manifest.json`:

   ```json
   "dependencies": {
     "com.raccooncoder7.flexibleuitool": "https://github.com/RaccoonCoder7/FlexibleUITool.git"
   }
   ```

   Unity runs `git` itself to fetch the package, so git must be installed on the machine.

From a local folder (while developing the tool): `"com.raccooncoder7.flexibleuitool": "file:C:/path/to/FlexibleUITool"`.

Everything the tool creates in your project stays in `Assets/FlexibleUITool/`: `Settings/FlexibleProfileSettings.asset` (created on first use; commit it) and `Samples/` (created by the tutorial).

---

## 1. Quick start

1. **Open** a UI prefab: double-click it in the Project window.
2. **Pick a screen**: press `Portrait`, `Square` or `Landscape`. The buttons are in the Scene view (top, "Flexible UI") and in `Tools > FlexibleUITool > Flexible UI Window`.
   - The first time, the window may show a checklist. Press `Set up this prefab` once.
3. **Move blocks** as you like on that screen: position, size, show / hide. Then **Ctrl+S**.

That is all. Good to know:

- **Portrait is the base screen.** What you build there is used by every screen. On the other screens you only change what should be different; blocks you do not touch stay the same as portrait.
- **Switching screens saves the prefab.** You never lose work by switching.
- New objects, sprites, text and colors are shared by every screen. Only layout (position, size, anchors, scale, visibility, Layout Group settings) is per screen.
- Select a block to see whether it has its own layout on this screen. `Back to Portrait` undoes it for that block.
- The Game view switches to the screen's size automatically and goes back when you close the prefab.

---

## 2. Setup (programmers)

1. Replace the `Canvas Scaler` of every top-level canvas with `Flexible Canvas Scaler`. It always scales by the short side (1080), so UI has the same real size in portrait and landscape, and every canvas follows the same rule. With no prefab open, the Edit tab shows what in the open scene is not ready yet; press `Make the scene's UI Flexible UI`. It replaces the Canvas Scaler of every top-level canvas and, if you choose `Do all`, makes every full-screen UI prefab in the scene (nested ones too) Flexible UI. UI placed straight under a canvas without being a prefab (for example a title logo and a start button) cannot have its own screen layouts, because Flexible UI is edited per prefab: `Do all` saves each checked group as a new prefab in a folder you pick, makes it Flexible UI and puts it back in the scene looking the same. `Show the list` lists these groups with check boxes. Canvases and prefabs are changed in the prefab files, and the scene is saved if it had no other unsaved changes.
2. Make a full-screen UI prefab's root **full-stretch** (anchors 0..1, offsets 0), or make the prefab's root the canvas itself. (`Set up this prefab` in the window does this.)
   - Dropping a prefab into a scene can put the Scene-view position into the root's offsets and Pos Z. Use `Fit to parent (offsets 0, Z 0)` in the Flexible Layout inspector.
3. Put elements that must avoid notches and home indicators under a full-stretch container with `Flexible Safe Area`.
4. Screen ranges, work resolutions, grids, the role list and preview devices live in `Assets/FlexibleUITool/Settings/FlexibleProfileSettings.asset`. It is shared by the whole project.
5. Code can listen to `FlexibleLayout.ProfileChanged` to adapt non-UI things (cameras, etc.).
6. Landscape only appears if the app may rotate: set `Player Settings > Resolution and Presentation > Default Orientation` to `Auto Rotation`. A portrait-only app still gets the square screen on foldables and tablets.

### Bringing in an existing project

You do not need new prefabs. The existing prefab becomes the portrait (base) layout.

1. Fastest: open the scene, open `Tools > FlexibleUITool > Flexible UI Window`, and press `Make the scene's UI Flexible UI` in the Edit tab (no prefab open). Or open one prefab and press `Set up this prefab`; it also offers to set up the full-screen prefabs inside it.
   - A prefab whose root is a canvas: it already fills the screen; its `Canvas Scaler` is replaced with `Flexible Canvas Scaler` (same reference resolution).
   - A prefab that does not fill the screen: you are asked whether it is full screen (stretched) or a panel (kept as it is).
2. Make the landscape and square layouts with the screen buttons or `Auto-layout with AI`.
3. **Nested prefabs** (a prefab inside another prefab):
   - Set up the inner prefab only if its inside should change per screen (for example a full-screen diary or shop). Small parts such as buttons need nothing; the outer prefab places them.
   - Set up inner prefabs first, then the outer one.
   - The outer prefab records where the inner prefab sits; the inner prefab records its own inside. They never overwrite each other.
   - While you edit the outer prefab on another screen, the inner prefabs show their own layout for that screen too (view only), and so do the Preview and Check tabs.
   - An inner prefab follows the whole screen even if it is a small panel. To follow its own width and height instead, set `Choose the screen by` to `Its own size (for panels)` in its Flexible Layout inspector.
4. **Several canvases in a scene** work independently; each Flexible Layout decides on its own. Give every top-level canvas a `Flexible Canvas Scaler` so sizes match.

---

## 3. The Flexible UI window

`Tools > FlexibleUITool > Flexible UI Window`. One window, three tabs.

| Tab | What it does |
|---|---|
| **Edit** | Screen buttons, `View while editing`, the selected block (state, role, position / size / scale, `Fit into the screen`), optional grid placement, templates, AI auto-layout (folded), and `More` (undo all changes of a screen, Game view option). |
| **Preview** | Shows the screen you are editing by default (`Screen being edited`). Pick `Free (window size)` and drag the window edge to watch the layout switch, or pick a device to see its notch / home bar area. Issues are listed under the picture. |
| **Check** | Shows your prefabs on many phones and tablets at once and finds blocks that stick out of the screen (also windows inside full-screen panels), overlap, or sit under a notch. Hidden panels the game opens later, squashed blocks, cut-off lists and places other prefabs take are checked too (see [Checks](#checks)). Issues of the same kind are grouped, and `Hide base screen issues` (on by default) hides the issues the portrait design already had. |

**View while editing**: panels that cover the screen (shop, notice, settings ...) are often saved turned on, or their Canvas turned off, because the game's code opens and closes them. Use the check boxes in `View while editing` to show or hide top-level blocks while you edit. Nothing there is saved on any screen, and the Preview and Check tabs use the same view. A prefab whose root is saved turned off (the object or its Canvas) is turned on automatically while you edit, and is still saved turned off. Panels deeper inside that are hidden by turning their Canvas off are listed too, with their path; turning one on also turns on the hidden blocks above it.

**Selected block**: change `Pos X / Pos Y / Width / Height / Scale` for the current screen right in the window (several blocks at once too). `Fit into the screen` shrinks a block evenly until it fits the screen and moves it inside. On a screen other than portrait, the window warns about blocks that stick out (`Fit them all into the screen`) and lists the blocks with their own layout on that screen (`Undo this one` puts one back).

**Prefabs inside prefabs**: the `Open` buttons for a prefab inside the one you edit remember where you came from; the inner prefab shows `← Back to "..."`. Both open on the screen you were on (for example Landscape), without saving anything.

**Grid (optional)**: open `Place on a grid`, select a block, then drag over the blue cells in the Scene view. The block snaps to the cells and follows other screen sizes nicely. The orange frame shows the cells the selected block sits on in this screen (or the range you are dragging); blocks not placed on the grid have none. `Horizontal` / `Vertical` choose how it follows: stretch (the default), stick to an edge, or center. Sticking to an edge or centering keeps the size of the cells the block was placed on, not the block's own size; press `Use its own size` to put back its portrait size. `Shrink to fit the cells` keeps the portrait size and shape and scales the block down on every screen size so it fits the cells (pictures, popups, button rows); the scale is recalculated at runtime when the screen size changes. `Pin to` (nine arrows) puts the selected blocks at the same distance from that corner or edge of the safe area as on the portrait screen, with their portrait size. Changing `Column` / `Row` / `Width` / `Height` or a mode moves the selected block right away (no extra button); hand nudges on top of the cells are kept. Save the prefab as usual to keep the result. The red area is the notch / home bar of the device with the largest one. Each screen (portrait, square, landscape) has its own grid. By default every prefab uses the project grids, shown read-only here; change them in `Tools > FlexibleUITool > Project Grids` (or the `Project grids...` button). Turn on `Use a grid only for this prefab` to give this prefab its own grids (one per screen, starting as a copy of the project grids) that you can edit here without affecting other prefabs. Templates keep cell numbers, so on a prefab with its own grid they may come out a different size. `Keep the selected blocks inside the safe area` is set per block on each screen: keep it on for buttons and text, and turn it off for backgrounds and bars that should reach the screen edge.

**Several blocks at once**: select several blocks (Ctrl / Shift + click in the Hierarchy). Like Unity's Inspector, a value shared by all of them is shown as it is, and a value that differs is shown as `—`. Changing a value (role, column, row, width, height, horizontal, vertical, safe area) sets it on all selected blocks, and one Undo takes it back. `Back to Portrait` resets the selected blocks that have their own layout. Dragging in the Scene view works with one block at a time.

**Templates (optional)**: give blocks a role (Header, Main, ...), place them on the grid, then save the layout as a template and apply it to other prefabs with the same roles.

---

## 4. GUI tutorial

Open `Tools > FlexibleUITool > Tutorial` (or press `Tutorial` at the top of the window). The tutorial makes a practice prefab (`Assets/FlexibleUITool/Samples/TutorialPractice.prefab`), opens it, highlights the control to press, and moves on by itself as you do each step.

**Basic** (11 steps):

1. Press `Landscape`, move the `Main` block, save with Ctrl+S.
2. Press `Portrait`: Main is back in its portrait spot. Press `Landscape` again: your landscape layout is still there.
3. (Optional) Place a block on the grid.
4. Press `Square`: it works the same way.
5. Open the `Preview` tab and change the window's shape.
6. Open the `Check` tab and press `Run check`.
7. Finish, and keep or delete the practice prefab. Deleting also removes the tutorial sample files in `Samples/` (the next tutorial brings them back); files you added there and an open scene are kept.

**Advanced** (on the landscape screen, 6 parts):

1. **Grid size**: columns, rows, margin and gutter.
2. **Place on cells**: drag over the blue cells in the Scene view.
3. **Following the screen size**: `Stretch` (default), `Stick left / right`, `Center`.
4. **Fine-tuning by hand**: adjust a block after placing it; the adjustment is kept when the grid changes.
5. **Roles**: name what each block is for.
6. **Templates**: save the layout of roles and apply it again in one click.

After the basic tutorial, `Tutorial` opens a list where you can pick the basic tutorial, the whole advanced tutorial, or one advanced part to do again. A single part sets the practice prefab up so you can start there directly.

The practice prefab uses its own grid, so grid changes in the tutorial do not affect other prefabs. Templates are shared by the whole project, so the tutorial deletes the templates it made when it ends or when you press `Quit tutorial`.

To see the result at runtime: open `SampleScene` in the sample folder, drop the prefab onto its Canvas, press Play, and switch the Game view between `1080x1920` and `1920x1080`.

---

## 5. Working with an agent (CLI)

### One button: Auto-layout with AI

The Edit tab has an **Auto-layout with AI (Claude Code)** box under the screen buttons. It makes the landscape and square layouts of the open prefab for you. No terminal needed.

1. Press `Connect AI` once. The tool checks Claude Code on your PC:
   - Not installed: press `Install Claude Code`. A window runs the official installer (about a minute). Then press `Check again`.
   - Not logged in: press `Log in`. A window and your browser open; log in with your Claude account (Pro, Max, Team or Enterprise; the free plan does not include Claude Code). Then press `Check again`.
2. Choose the screens to make (`Landscape`, `Square`), the `Model` and the `Effort` level, and press `Auto-layout with AI`.
   - `Model`: Sonnet (recommended) is balanced. Opus and Fable are smarter but use more of your Claude usage; Haiku is the fastest and uses the least. Some models may not be available on every plan.
   - `Effort`: Medium (recommended). Higher levels think longer and use more; Low is the fastest.
   - `Extra instructions (optional)`: write what you want in plain words, for example "In landscape, put the menu buttons on the right. Hide the banner on the square screen." The AI follows it before its own design rules. Leave it empty and the AI decides by itself. The text is kept for each prefab; `Clear` empties it. The AI still cannot change roles, grid settings, the portrait layout or other files, even if you ask.
   - When it finishes, the result box shows how many tokens the run used. Every run is also logged in `Library/FlexibleUITool/ai-runs.log`.
   - The prefab is saved and closed, and a copy is kept as a backup. The box at the top of the window shows what the AI is doing.
   - The AI sees the same `View while editing` as you: hide popups that cover the main screen (shop, notice ...) before you start, and the AI lays out the screen behind them. The view comes back when the prefab opens again.
   - The AI gets the parts inside full-screen containers (not only the containers), and moves a row or column made by a layout group as one block.
   - It takes a few minutes and uses your Claude usage limit. `Stop` ends it at any time.
3. When it is done, the prefab opens again with a short summary. Check each screen with the screen buttons, fix things by hand if you like, or press `Undo AI changes` to put the prefab back as it was.

The AI can only run the tool's layout commands and write layout plans to `Library/FlexibleUITool/specs`. It does not use your personal Claude Code settings, and it cannot change other files.

### Talking to an agent in a terminal

An agent (Claude Code or similar) in a terminal can read, change and render layouts through `flexui.ps1` while Unity stays open. You talk to it in plain words.

1. Open the project in Unity (not in Play Mode).
2. Open a terminal in the project folder, start your agent, and tell it: `Read Library/FlexibleUITool/AGENTS.md`.
3. The agent shows a menu in your language: tutorial, manual, adapt a portrait prefab to landscape, check or adjust an existing layout.

Good to know:

- While the prefab is open in Prefab Mode, the agent's writes to it are refused, so your manual work is never overwritten. Save and close it first.
- Rendered images are saved in `Library/FlexibleUITool/renders` (not in git).
- If the agent cannot connect, run `powershell -ExecutionPolicy Bypass -File Library/FlexibleUITool/flexui.ps1 ping` yourself. `"ok":true` means the bridge works.

The full command reference (options, spec format, errors) is in `AGENTS.md`.

---

## 6. Reference

### Terms

| Term | Meaning |
|---|---|
| **Screen (profile)** | An aspect-ratio range with its own layout. Defaults: `portrait` (up to 0.8, the base), `square` (0.8 to 1.25), `landscape` (1.25 and wider). Aspect = width / height. |
| **Base screen** | Portrait. The prefab itself. |
| **Block** | A RectTransform in the prefab (usually the root's direct children). |
| **Role** | A label on a block (Header, Main, Side, Footer, ...), used by templates and checks. |
| **Grid / cells** | Each screen's columns × rows with margin and gutter. `(0, 0)` is the top-left cell. |
| **Adjustment** | Position / size / scale changed by hand after grid placement. Saved with templates. |

Canvas units: the **short side of the screen is always 1080**. Landscape 16:9 is 1920 × 1080.

### How saving works

- The prefab file always holds the portrait layout plus, for each other screen, only the blocks that differ.
- When you save on another screen, the tool records the differences and keeps the file's portrait layout as it is. After saving you stay on the same screen.
- Recorded per screen: RectTransform (anchors, pivot, position, size, scale), active state, Layout Group settings. Children placed by a Layout Group follow the group's settings.
- A block with its own `FlexibleLayout` records only itself; its inside is handled by its own `FlexibleLayout`.
- Grid placements on the portrait screen are remembered only while the prefab is open (portrait is the prefab itself).

### Checks

Preview, Check, and the CLI report the same issues:

| Issue | CLI code | Meaning |
|---|---|---|
| Out of screen | `OUT_OF_SCREEN` | A block leaves the screen. |
| Overlap | `OVERLAP` | Two blocks overlap. A block fully inside another is ignored only if it is also inside it on the portrait screen (intentional layering, like a title on a bar). |
| Control outside SafeArea | `OUTSIDE_SAFE_AREA` | A button sits under a notch or home indicator. |
| Out of screen when shown | `HIDDEN_OUT_OF_SCREEN` | Something inside a hidden panel leaves the screen once the game shows the panel. Panels with their Canvas off and large objects saved turned off are turned on in a copy for this check (small parts such as list templates or badges are not). |
| Squashed | `COLLAPSED` | A stretched block got a negative width or height: its fixed margins are larger than the space this screen gives it, so it and its contents disappear. Hidden blocks are checked too. |
| Cut off in a list | `CLIPPED_IN_LIST` | A list that scrolls one way cuts its items off the other way (for example two columns wider than the list). When the game makes the items, the list's grid settings are used. |
| Under another prefab | `UNDER_RESERVED` | A button sits under a place another prefab takes (see below) and is drawn below it, so it cannot be pressed. A backdrop button that covers the whole place is fine. |
| Covers another prefab | `COVERS_RESERVED` | A block drawn above such a place covers part of it. |

`Screen edge ratios` also checks each screen at the narrowest and widest ratio where it can appear. Fixed-size blocks overflow there first.

In the Edit tab, the last five appear under `problems you cannot see right now`, without the ones the portrait screen already has. `Show` turns on the hidden panel while you edit (not saved) so you can fix it on that screen.

**Places other prefabs take**: a menu bar that stays on top of other screens lives in another prefab, so it is not visible while you edit those screens. Open the prefab with the bar, select the bar, and turn on `Other prefabs keep clear of this` (under `Selected block`). The tool records where the bar sits on each screen and at which Canvas order it is drawn (saved in the project settings, updated whenever that prefab is saved). While you edit any other prefab, that place is drawn with a blue frame in the Scene view, the check reports buttons under it, and the AI gets it as `reserved` in `describe`. If the game hides the bar while some prefab is open (a title screen, a full-screen conversation ...), open that prefab and turn off `... is shown over this prefab` in the Edit tab; its place is then not drawn there and not checked.

### Runtime behavior

- `FlexibleLayout` picks the screen from the width / height of the screen it is shown on (its top-level canvas), with a small margin (hysteresis, default 0.01) at boundaries so it does not flicker. With `Choose the screen by: Its own size`, it uses its own rect instead.
- It switches in Play Mode and on devices. In the editor, use the screen buttons or the Preview tab.
- While the editor is stopped, scenes still show the layout for the Game view's shape. The tool draws a copy with that layout over the UI and hides the original's drawing; nothing in the scene or prefab changes, and the copy is never saved. Changes you make in the scene go to the base layout (the Scene view says so when you select such a UI). Turn it off with `Tools > FlexibleUITool > Show Screen Layouts While Stopped`.
- Do not register objects that code or animation moves; switching overwrites them. Register their parent container instead.

### Languages

- Window text comes from `Editor/Localization/Strings.tsv`: English (the key) / Korean / Japanese, separated by tabs. Keep `{0}` placeholders and write line breaks as `\n`.
- CLI results and error messages are always English.

### Older versions

Versions up to 1.0.2 edited other screens through work copies in `Assets/FlexibleUITool/Workspace/`. If one is left over, the Edit tab shows it with `Apply it` / `Discard it`.

---

## 7. FAQ

| Symptom | Cause and fix |
|---|---|
| No screen buttons | The prefab is not open in Prefab Mode, or it is not set up yet. Open the Flexible UI window and press `Set up this prefab`. |
| The Scene view buttons are gone | Show the "Flexible UI" overlay from the Scene view's overlay menu (`` ` `` key), or use the window. |
| The screen says Portrait but looks wide (or the grid looks wrong) | The prefab takes its shape from the Game view's size. The tool fixes it by itself when you open a prefab or switch screens; if it is still wrong, press `Fix the Game view size` in the Edit tab. |
| Dragging in the Scene view picks cells instead of objects | While the grid is open and a block is selected, clicks pick cells. Close `Place on a grid`, or select in the Hierarchy. |
| A change appeared on every screen | Non-layout changes (sprites, text, new objects) are shared. Only layout is per screen. |
| The Preview tab is empty | No prefab is open or selected. |
| In a scene, a UI does not follow the Game view shape while stopped | `Tools > FlexibleUITool > Show Screen Layouts While Stopped` is off, the canvas is World Space, or the prefab has no changes for that screen. |
| Layout does not switch in Play Mode | The prefab root is not full-stretch, or the canvas uses the default Canvas Scaler instead of `Flexible Canvas Scaler`. |
| The agent says `PREFAB_OPEN` | Save and close Prefab Mode, then ask it to retry. |
| Auto-layout with AI says Claude Code is not logged in | Press `Connect AI`, then `Log in`. Logging in to the Claude desktop app does not log in the `claude` command; it needs its own login once. |
| Auto-layout with AI seems stuck | The AI sometimes thinks for a minute or two without any visible step. Wait, or press `Stop` and `Undo AI changes`. |
