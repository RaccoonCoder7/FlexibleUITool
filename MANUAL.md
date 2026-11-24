# FlexibleUITool Manual

FlexibleUITool lets **one UI prefab serve portrait, square and landscape screens**.
You build the portrait screen as usual, then press **Landscape** (or **Square**) in Prefab Mode and move objects where you want them on that screen. The prefab remembers each screen, and the right one is shown at runtime from the screen's aspect ratio.

**You do not need to read this manual.** In Unity, open `Tools > FlexibleUITool > Tutorial`. A short tutorial inside the tool window walks you through everything in about 2 minutes, with buttons that light up where to click. No AI is needed.

**Where to start**
- **New to the tool**: [install it](#0-installation), then run the tutorial.
- **Your project already has UI**: after the tutorial, read [Setup](#2-setup-programmers) once. It turns your scene's canvases and screens into Flexible UI; then follow the [Quick start](#1-quick-start).

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

1. Open `Window > Package Management > Package Manager`, press `+`, choose `Install package from git URL...`, and paste the URL from the [README](README.md) (`https://github.com/RaccoonCoder7/FlexibleUITool.git` with the latest version tag such as `#v1.0.11` at the end; without a tag you get the newest commit). Older Unity versions call these `Window > Package Manager` and `Add package from git URL...`.
2. Or add it to `Packages/manifest.json`:

   ```json
   "dependencies": {
     "com.raccooncoder7.flexibleuitool": "https://github.com/RaccoonCoder7/FlexibleUITool.git"
   }
   ```

   Unity runs `git` itself to fetch the package, so git must be installed on the machine.

From a local folder (while developing the tool): `"com.raccooncoder7.flexibleuitool": "file:C:/path/to/FlexibleUITool"`.

Everything the tool creates in your project stays in `Assets/FlexibleUITool/`: `Settings/FlexibleProfileSettings.asset` (created on first use; commit it), `Prefabs/` (prefabs made from UI placed straight in a scene; `Change...` can put them elsewhere) and `Samples/` (created by the tutorial). `Workspace/` (temporary work copies) and, when the tool is installed as a package, `Samples/` are kept out of git by `Assets/FlexibleUITool/.gitignore`, which the tool writes and which does not show up in git itself; delete its first line to keep your own edits.

---

## 1. Quick start

1. **Open** a UI prefab: while no prefab is open, the Edit tab lists `UI prefabs in this scene` in Hierarchy order (prefabs inside others fold under them, ✔ = already Flexible UI). Press `Open` or double-click a name. You can also double-click a prefab in the Project window.
2. **Pick a screen**: press `Portrait`, `Square` or `Landscape`. The buttons are in the Scene view (top, "Flexible UI") and in `Tools > FlexibleUITool > Flexible UI Window`.
   - The first time, the window may show a checklist. Press `Set up this prefab` once.
3. **Move objects** as you like on that screen: position, size, show / hide. Then **Ctrl+S**.

That is all. Good to know:

- **Portrait is the base screen.** What you build there is used by every screen. On the other screens you only change what should be different; objects you do not touch stay the same as portrait.
- **Switching screens saves the prefab.** You never lose work by switching.
- New objects, sprites, text and colors are shared by every screen. Only layout (position, size, anchors, scale, visibility, Layout Group settings) is per screen.
- Select an object to see whether it has its own layout on this screen. `Back to Portrait` undoes it for that object.
- The Game view switches to the screen's size automatically and goes back when you close the prefab.

---

## 2. Setup (programmers)

1. Replace the `Canvas Scaler` of every top-level canvas with `Flexible Canvas Scaler`. On square and landscape screens it scales by the short side of the reference resolution, so UI keeps its real size when the device rotates. A canvas converted from a `Canvas Scaler` keeps that scaler's rule (Match, Expand, Shrink, Constant Pixel or Physical Size) on the portrait screen, so the portrait screen stays as it was. With no prefab open, the Edit tab shows what in the open scene is not ready yet; press `Make the scene's UI Flexible UI`. It replaces the Canvas Scaler of every top-level canvas and, if you choose `Do all`, makes every full-screen UI prefab in the scene (nested ones too) Flexible UI. UI placed straight under a canvas without being a prefab (for example a title logo and a start button) cannot have its own screen layouts, because Flexible UI is edited per prefab: `Do all` saves each checked group as a new prefab in the folder shown above the button (`Assets/FlexibleUITool/Prefabs/` by default; `Change...` picks another), makes it Flexible UI and puts it back in the scene looking the same. A canvas whose children are mostly parts of one screen (buttons, logos) becomes one prefab, named after the scene and canvas (`Main_Loadout`), so the whole screen is laid out and checked in one place. A canvas whose children are mostly full-screen pages gets one prefab per page, named after the canvas (`HUDCanvas_Background`). `Show the list` lists these with check boxes and lets you change `One prefab` / `One per child` per canvas. In the list of the scene's UI, `✔ 1/2` means the prefab is Flexible UI and has its own layout on 1 of the 2 other screens (hover it for the number of changed objects per screen). Canvases and prefabs are changed in the prefab files, and the scene is saved if it had no other unsaved changes.
2. Make a full-screen UI prefab's root **full-stretch** (anchors 0..1, offsets 0), or make the prefab's root the canvas itself. (`Set up this prefab` in the window does this.)
   - Dropping a prefab into a scene can put the Scene-view position into the root's offsets and Pos Z. Use `Fit to parent (offsets 0, Z 0)` in the Flexible Layout inspector.
3. Put elements that must avoid notches and home indicators under a full-stretch container with `Flexible Safe Area`.
   - If the app must correct the safe area (for example, an Android status bar drawn over the game, which Unity leaves out of `Screen.safeArea`), set `FlexibleSafeArea.AdjustSafeArea` once at startup. It gets `Screen.safeArea` (pixels, origin bottom-left) and returns the rect to use. Every `Flexible Safe Area` follows the returned rect and applies it again when it changes; `FlexibleSafeArea.CurrentSafeArea` gives the same value to your code. It is called every frame, so only calculate in it (get the status bar height elsewhere). Leave it null to use `Screen.safeArea`. Preview and Check do not use it; they use each device's safe area from the settings.
     ```csharp
     FlexibleSafeArea.AdjustSafeArea = safe =>
     {
         float top = Mathf.Max(Screen.height - safe.yMax, statusBarHeight); // notch depth or status bar, whichever is larger
         return Rect.MinMaxRect(safe.xMin, safe.yMin, safe.xMax, Screen.height - top);
     };
     ```
4. Screen ranges, work resolutions, grids, the role list and preview devices live in `Assets/FlexibleUITool/Settings/FlexibleProfileSettings.asset`. It is shared by the whole project.
5. Code can listen to `FlexibleLayout.ProfileChanged` to adapt non-UI things (cameras, etc.).
6. Landscape only appears if the app may rotate: set `Player Settings > Resolution and Presentation > Default Orientation` to `Auto Rotation`. A portrait-only app still gets the square screen on foldables and tablets.

### Bringing in an existing project

You do not need new prefabs. The existing prefab becomes the portrait (base) layout.

1. Fastest: open the scene, open `Tools > FlexibleUITool > Flexible UI Window`, and press `Make the scene's UI Flexible UI` in the Edit tab (no prefab open). Or open one prefab and press `Set up this prefab`; it also offers to set up the full-screen prefabs inside it.
   - A prefab whose root is a canvas: it already fills the screen; its `Canvas Scaler` is replaced with `Flexible Canvas Scaler` (same reference resolution; the portrait screen keeps the old scaling rule).
   - A prefab that does not fill the screen: you are asked whether it is full screen (stretched) or a panel (kept as it is).
2. Make the landscape and square layouts with the screen buttons or `Auto-layout with AI`.
3. **Nested prefabs** (a prefab inside another prefab):
   - Set up the inner prefab only if its inside should change per screen (for example a full-screen diary or shop). Small parts such as buttons need nothing; the outer prefab places them.
   - Set up inner prefabs first, then the outer one.
   - The outer prefab records where the inner prefab sits; the inner prefab records its own inside. They never overwrite each other.
   - Objects the outer prefab added inside an inner prefab (for example a history panel added into a chat screen only in this prefab) belong to the outer prefab: you place them while editing the outer prefab, they are listed in its checks and `describe`, and their layouts are saved in the outer prefab. Selecting such an object says so. Selecting an object of the inner prefab itself says which prefab keeps its layout, with an `Open` button.
   - While you edit the outer prefab on another screen, the inner prefabs show their own layout for that screen too (view only), and so do the Preview and Check tabs.
   - An inner prefab follows the whole screen even if it is a small panel. To follow its own width and height instead, set `Choose the screen by` to `Its own size (for panels)` in its Flexible Layout inspector.
4. **Several canvases in a scene** work independently; each Flexible Layout decides on its own. Give every top-level canvas a `Flexible Canvas Scaler` so sizes match.

---

## 3. The Flexible UI window

`Tools > FlexibleUITool > Flexible UI Window`. One window, three tabs.

| Tab | What it does |
|---|---|
| **Edit** | Screen buttons, then folding sections in this order: `View while editing`, `Selected object` (state, role, position / size / scale, `Fit into the screen`), `Place on a grid`, `Auto-layout with AI`, `Templates`, and `More` (undo all changes of a screen, Game view option). |
| **Preview** | Shows the screen you are editing by default (`Screen being edited`). Pick `Free (window size)` and drag the window edge to watch the layout switch, or pick a device to see its notch / home bar area. Issues are listed under the picture. |
| **Check** | Shows your prefabs on many phones and tablets at once and finds objects that stick out of the screen (also windows inside full-screen panels), overlap, or sit under a notch. Hidden panels the game opens later, squashed objects, cut-off lists and places other prefabs take are checked too (see [Checks](#checks)). Choose what to check: `Only the open prefab`, `This scene` (the Flexible UI prefabs placed in the open scene, nested ones too) or `Folder`. Each prefab row shows, per screen, how many objects have their own layout and how many issues it has. Issues of the same kind are grouped, and `Hide base screen issues` (on by default) hides the issues the portrait design already had. |

**View while editing** (folded while no top-level object is hidden): panels that cover the screen (shop, notice, settings ...) are often saved turned on, or their Canvas turned off, because the game's code opens and closes them. `View while editing` shows the prefab as a tree, like the Hierarchy, with a check box on each row. Turning a row on or off here lasts only while you edit: nothing is saved on any screen, and the Preview and Check tabs use the same view. Unchecked means the object or its Canvas is off; grey rows are hidden by themselves or by an object above them, and bold rows are the ones you changed here (a folded row says how many changed inside it). Turning a row on also turns on the hidden objects above it. Click a name to select it (Ctrl / Shift + click selects several, as in the Hierarchy); selecting an object in the Hierarchy unfolds the tree down to it. A prefab inside (blue icon, ✔ = already Flexible UI) is not unfolded: use its `Open` button (or double-click its name) to open it and show or hide what is inside it there. A prefab whose root is saved turned off (the object or its Canvas) is turned on automatically while you edit, and is still saved turned off.

**Selected object**: change `Pos X / Pos Y / Width / Height / Scale` for the current screen right in the window (several objects at once too). `Fit into the screen` shrinks an object evenly until it fits the screen and moves it inside. On a screen other than portrait, the window warns about objects that stick out (`Fit them all into the screen`) and lists the objects with their own layout on that screen (`Undo this one` puts one back).

**Prefabs inside prefabs**: the `Open` buttons for a prefab inside the one you edit remember where you came from; the inner prefab shows `← Back to "..."`. Both open on the screen you were on (for example Landscape), without saving anything.

**Grid (optional)**: open `Place on a grid`, select an object, then drag over the blue cells in the Scene view. The object snaps to the cells and follows other screen sizes nicely. Dragging puts the object back to its original scale, so it looks exactly as big as the dragged cells. The orange frame shows the cells the selected object sits on in this screen (or the range you are dragging); objects not placed on the grid have none. `Horizontal` / `Vertical` choose how it follows: stretch (the default), stick to an edge, or center. Sticking to an edge or centering keeps the size of the cells the object was placed on, not the object's own size; press `Use its own size` to put back its portrait size. `Shrink to fit the cells` keeps the portrait size and shape and scales the object down on every screen size so it fits the cells (pictures, popups, button rows); the scale is recalculated at runtime when the screen size changes. The size it fits includes visible parts that stick out of the object (an open arrow, a button below it; inside a mask only up to the mask), so they stay inside the cells too. An object that spans every column (or every row) may also use the grid margin on that side. Do not shrink an object whose own picture covers the whole portrait screen (a dim or page background): the background would shrink too and leave the screen edges uncovered, so the window and the CLI warn about it; fit the parts inside it instead. An object whose size follows its contents (`Content Size Fitter`) is placed this way first, because the fitter would override the cell size. When its shape then differs from the cells, the Scene view and the window tell you, and you choose: `Fill the cells` makes it take the cells' size. If it has a scroll area, the Content Size Fitter stays on: the scroll area becomes shorter or longer so the whole object matches the cells, wide parts inside are narrowed (pictures, text, scroll areas) or scaled down (other groups) while keeping the side margins of the portrait screen, text that wraps into more lines gets a taller box so the gap below it stays as on the portrait screen, and titles and buttons keep their size. If the other parts need more height than the cells, the whole object is also scaled down a little and widened so it still fills the cells; only this screen changes. Without a scroll area, its Content Size Fitter is turned off on this screen only (on every screen when you are on the base screen). Either way, parts that still stick out are listed; `Keep it as it is` keeps the shape and is not asked again for that object on that screen. `Pin to` (nine arrows) puts the selected objects at the same distance from that corner or edge of the safe area as on the portrait screen, with their portrait size. Changing `Column` / `Row` / `Width` / `Height` or a mode moves the selected object right away (no extra button); hand nudges on top of the cells are kept. Save the prefab as usual to keep the result. The green frame is the safe area and the red area is the notch / home bar of the device with the largest one. When a screen's device has none, the frame says `Safe area = whole screen` and the safe-area check box is turned off. Each screen (portrait, square, landscape) has its own grid. By default every prefab uses the project grids, shown read-only under `Grid settings` at the bottom of the section; change them in `Tools > FlexibleUITool > Project Grids` (or the `Project grids...` button). Turn on `Use a grid only for this prefab` to give this prefab its own grids (one per screen, starting as a copy of the project grids) that you can edit here without affecting other prefabs. Templates keep cell numbers, so on a prefab with its own grid they may come out a different size. `Keep the selected objects inside the safe area` is set per object on each screen: keep it on for buttons and text, and turn it off for backgrounds and bars that should reach the screen edge.

**Several objects at once**: select several objects (Ctrl / Shift + click in the Hierarchy). Like Unity's Inspector, a value shared by all of them is shown as it is, and a value that differs is shown as `—`. Changing a value (role, column, row, width, height, horizontal, vertical, safe area) sets it on all selected objects, and one Undo takes it back. `Back to Portrait` resets the selected objects that have their own layout.

**Several objects as one group**: pieces of one popup are often siblings without a common parent (dim, frame, title, list, buttons). With several objects selected, `Move them together` (on by default, under `Place on a grid`) treats them as one group: drag over the cells in the Scene view, type the cells, or use `Pin to`, and the whole group is placed there, keeping the pieces' places and sizes to each other as on the portrait screen (scaled down evenly if it does not fit; never larger than on the portrait screen). Nothing is added to the Hierarchy; each piece gets its own layout on this screen. Turn it off to set the same value on every selected object instead.

**Templates (optional)**: give objects a role (Header, Main, ...), place them on the grid, then save the layout as a template and apply it to other prefabs with the same roles.

---

## 4. GUI tutorial

Open `Tools > FlexibleUITool > Tutorial` (or press `Tutorial` at the top of the window). The tutorial makes a practice prefab (`Assets/FlexibleUITool/Samples/TutorialPractice.prefab`), opens it, highlights the control to press, and moves on by itself as you do each step.

**Basic** (11 steps):

1. Press `Landscape`, move the `Main` object, save with Ctrl+S.
2. Press `Portrait`: Main is back in its portrait spot. Press `Landscape` again: your landscape layout is still there.
3. (Optional) Place an object on the grid.
4. Press `Square`: it works the same way.
5. Open the `Preview` tab and change the window's shape.
6. Open the `Check` tab and press `Run check`.
7. Finish, and keep or delete the practice prefab. Deleting also removes the tutorial sample files in `Samples/` (the next tutorial brings them back); files you added there and an open scene are kept.

**Advanced** (on the landscape screen, 6 parts):

1. **Grid size**: columns, rows, margin and gutter.
2. **Place on cells**: drag over the blue cells in the Scene view.
3. **Following the screen size**: `Stretch` (default), `Stick left / right`, `Center`.
4. **Fine-tuning by hand**: adjust an object after placing it; the adjustment is kept when the grid changes.
5. **Roles**: name what each object is for.
6. **Templates**: save the layout of roles and apply it again in one click.

After the basic tutorial, `Tutorial` opens a list where you can pick the basic tutorial, the whole advanced tutorial, or one advanced part to do again. A single part sets the practice prefab up so you can start there directly.

The practice prefab uses its own grid, so grid changes in the tutorial do not affect other prefabs. Templates are shared by the whole project, so the tutorial deletes the templates it made when it ends or when you press `Quit tutorial`.

To see the result at runtime: open `SampleScene` in the sample folder, drop the prefab onto its Canvas, press Play, and switch the Game view between `1080x1920` and `1920x1080`.

---

## 5. Working with an agent (CLI)

### One button: Auto-layout with AI

The Edit tab has an **Auto-layout with AI (Claude Code)** box under the screen buttons. It makes the landscape and square layouts of the open prefab for you. No terminal needed.

1. Press `Connect AI` once (if Claude Code is already set up on this PC, the box shows `✔ Connected to Claude Code` instead). The tool checks Claude Code on your PC:
   - Not installed: press `Install Claude Code`. A window runs the official installer (about a minute). Then press `Check again`.
   - Not logged in: press `Log in`. A window and your browser open; log in with your Claude account (Pro, Max, Team or Enterprise; the free plan does not include Claude Code). Then press `Check again`.
2. Choose the screens to make (`Landscape`, `Square`), the `Model` and the `Effort` level, and press `Auto-layout with AI`.
   - `Model`: Sonnet (recommended) is balanced. Opus and Fable are smarter but use more of your Claude usage; Haiku is the fastest and uses the least. Some models may not be available on every plan.
   - `Effort`: Medium (recommended). Higher levels think longer and use more; Low is the fastest.
   - `Extra instructions (optional)`: write what you want in plain words, for example "In landscape, put the menu buttons on the right. Hide the banner on the square screen." The AI follows it before its own design rules. Leave it empty and the AI decides by itself. The text is kept for each prefab; `Clear` empties it. The AI still cannot change roles, grid settings, the portrait layout or other files, even if you ask.
   - When it finishes, the result box shows how many tokens the run used. The dollar amount is the API price of those tokens: when Claude Code is logged in with a Claude subscription, the run counts toward your subscription usage and is not charged separately. Every run is also logged in `Library/FlexibleUITool/ai-runs.log`.
   - The prefab is saved and closed, and a copy is kept as a backup. The box at the top of the window shows what the AI is doing.
   - The AI sees the same `View while editing` as you: hide popups that cover the main screen (shop, notice ...) before you start, and the AI lays out the screen behind them. The view comes back when the prefab opens again.
   - The AI gets the parts inside full-screen containers (not only the containers), and moves a row or column made by a layout group as one object.
   - It takes a few minutes and uses your Claude usage limit. `Stop` ends it at any time.
3. When it is done, the prefab opens again with a short summary and the result of the tool's own check of those screens (it does not depend on what the AI reports). Check each screen with the screen buttons, fix things by hand if you like, or press `Undo AI changes` to put the prefab back as it was.

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
| **Object** | A RectTransform in the prefab (usually the root's direct children). |
| **Role** | A label on an object (Header, Main, Side, Footer, ...), used by templates and checks. |
| **Grid / cells** | Each screen's columns × rows with margin and gutter. `(0, 0)` is the top-left cell. |
| **Adjustment** | Position / size / scale changed by hand after grid placement. Saved with templates. |

Canvas units: the real units of the canvas that shows the prefab in the game. With a 1080 reference, landscape 16:9 is 1920 × 1080; with a 720 reference it is 1280 × 720. `describe` reports the size for each screen as `canvas`.

### How saving works

- The prefab file always holds the portrait layout plus, for each other screen, only the objects that differ.
- When you save on another screen, the tool records the differences and keeps the file's portrait layout as it is. After saving you stay on the same screen.
- Switching screens with the buttons saves the prefab only when something changed; the file is not rewritten when you just look at another screen.
- When the tool opens another prefab (`Open`, `Show the problem`, `← Back to ...`, the AI) while the open one has unsaved changes, it asks first (`Save` / `Cancel` / `Don't save`), because Unity's Prefab Mode auto save would otherwise save them without asking.
- Recorded per screen: RectTransform (anchors, pivot, position, size, scale), active state, Layout Group settings, Content Size Fitter settings. For an object inside a Layout Group, its position (and the size on axes where the group controls child size) comes from the group, so only its own size and scale are recorded. Children placed by a Layout Group follow the group's settings.
- An object with its own `FlexibleLayout` records only itself; its inside is handled by its own `FlexibleLayout`. Objects the outer prefab added inside it are the exception: the outer prefab records them.
- Drawing the Preview, Check and `describe` can add letters to dynamic TMP fonts; this does not mark the font files as changed.
- Grid placements on the portrait screen are remembered only while the prefab is open (portrait is the prefab itself), except objects placed with `Shrink to fit the cells`: their cell area is saved for the portrait screen too, so on a shorter or longer portrait screen (in the game and in Preview) they are scaled again to fit their cells.

### Checks

Preview, Check, and the CLI report the same issues. They measure a prefab in the units of the canvas that shows it in the game (its own canvas, the canvas it sits under in the open scene, or the one recorded when it was converted); if that is unknown, they use the project's work size. In the Check tab, `Show the problem` under a thumbnail opens the prefab on that screen and selects the objects with the issue. The window switches to the Edit tab, which shows the problem under the screen buttons (`Select them` selects the objects again, `×` closes it). Fix it with the usual tools (drag, `Place on a grid`, `Pin to`), then run the check again; a new check or closing the prefab clears the problem. On the portrait screen a fix changes every portrait device:

| Issue | CLI code | Meaning |
|---|---|---|
| Out of screen | `OUT_OF_SCREEN` | An object leaves the screen. |
| Overlap | `OVERLAP` | Two objects overlap. An object fully inside another is ignored only if it is also inside it on the portrait screen (intentional layering, like a title on a bar). |
| Button covered | `BLOCKED_INPUT` | Another picture or text is drawn over the middle of a button and takes the press. A full-screen dim over the button is not counted (a popup blocks on purpose). Turn off `Raycast Target` on the picture or move it. On square and landscape it is shown even when `Hide base screen issues` is on. |
| Control outside SafeArea | `OUTSIDE_SAFE_AREA` | A button sits under a notch or home indicator. |
| Text outside SafeArea | `TEXT_OUTSIDE_SAFE_AREA` | A weak warning (yellow in the Check tab): text that is not in a button sits under a notch or home indicator. It may be decoration; fix it if it must be readable. |
| Out of screen when shown | `HIDDEN_OUT_OF_SCREEN` | Something inside a hidden panel leaves the screen once the game shows the panel. Panels with their Canvas off and large objects saved turned off are turned on in a copy for this check (small parts such as list templates or badges are not). Panels you turned off in `View while editing` (or with `-Hide` in the CLI) are not turned on for it. |
| Squashed | `COLLAPSED` | A stretched object got a negative width or height: its fixed margins are larger than the space this screen gives it, so it and its contents disappear. Hidden objects are checked too. |
| Cut off in a list | `CLIPPED_IN_LIST` | A list that scrolls one way cuts its items off the other way (for example two columns wider than the list). When the game makes the items, the list's grid settings are used. |
| Under another prefab | `UNDER_RESERVED` | A button sits under a place another prefab takes (see below) and is drawn below it, so it cannot be pressed. A backdrop button that covers the whole place is fine. |
| Covers another prefab | `COVERS_RESERVED` | An object drawn above such a place covers part of it. |

`Screen edge ratios` also checks each screen at the narrowest and widest ratio where it can appear. Fixed-size objects overflow there first.

To keep the list short, these are not reported: objects inside an inner Flexible prefab (that prefab is checked on its own), objects with nothing visible (no picture, mask or text), objects that cover the whole screen, texts whose letters stay on the screen even if their box does not, and the inner parts of scroll bars. For overlaps: plain decorations that cannot be pressed or read (no button, no text), texts whose actual letters do not touch, and two objects with a full-screen backdrop (a dim) drawn between them, since only the top one can be seen and pressed.

**Lists filled by code**: a list that the game fills at runtime (diary days, shop items ...) is empty in the prefab, so problems with its items cannot be seen. Add `Flexible Preview Items` (`Add Component > Layout`) to the list's content object, set an item prefab and a count. Preview, Check and `describe` then draw that many sample items in their copy. Nothing is added to the prefab, and the component does nothing in the game. Lists that already have an item turned on are left as they are.

In the Edit tab, the last five appear under `problems you cannot see right now`, without the ones the portrait screen already has. `Show` turns on the hidden panel while you edit (not saved) so you can fix it on that screen.

**Places other prefabs take**: a menu bar that stays on top of other screens lives in another prefab, so it is not visible while you edit those screens. Open the prefab with the bar, select the bar, and turn on `Other prefabs keep clear of this` (under `Selected object`). The tool records where the bar sits on each screen and at which Canvas order it is drawn (saved in the project settings, updated whenever that prefab is saved). Checks measure the bar again at the exact size of each device, so a bar that grows or moves with the screen is checked where it really is. While you edit any other prefab, that place is drawn with a blue frame in the Scene view, the check reports buttons under it, and the AI gets it as `reserved` in `describe`. If the game hides the bar while some prefab is open (a title screen, a full-screen conversation ...), open that prefab and turn off `... is shown over this prefab` in the Edit tab (it appears only when buttons of that prefab overlap the place); its place is then not drawn there and not checked.

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
| The screen says Portrait but looks wide (or the grid looks wrong) | The prefab takes its shape from the Game view's size. The tool fixes it by itself when you open a prefab or switch screens; if it is still wrong, press `Fix the Game view size` in the Edit tab. The Edit tab warns whenever the Game view's shape differs from the screen's work size by more than 1% (for example a tablet size while editing portrait), because changes made then would be measured at that other size. |
| Dragging in the Scene view picks cells instead of objects | While the grid is open and an object is selected, clicks pick cells. Close `Place on a grid`, or select in the Hierarchy. |
| A change appeared on every screen | Non-layout changes (sprites, text, new objects) are shared. Only layout is per screen. |
| The Preview tab is empty | No prefab is open or selected. |
| In a scene, a UI does not follow the Game view shape while stopped | `Tools > FlexibleUITool > Show Screen Layouts While Stopped` is off, the canvas is World Space, or the prefab has no changes for that screen. |
| Layout does not switch in Play Mode | The prefab root is not full-stretch, or the canvas uses the default Canvas Scaler instead of `Flexible Canvas Scaler`. |
| The agent says `PREFAB_OPEN` | Save and close Prefab Mode, then ask it to retry. |
| Auto-layout with AI says Claude Code is not logged in | Press `Connect AI`, then `Log in`. Logging in to the Claude desktop app does not log in the `claude` command; it needs its own login once. |
| Auto-layout with AI seems stuck | The AI sometimes thinks for a minute or two without any visible step. Wait, or press `Stop` and `Undo AI changes`. |
