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

1. Replace the screen canvas's `Canvas Scaler` with `Flexible Canvas Scaler`. It always scales by the short side (1080), so UI has the same real size in portrait and landscape.
2. Make the UI prefab's root **full-stretch** (anchors 0..1, offsets 0). The root's aspect must follow the screen for screens to switch. (`Set up this prefab` in the window does this.)
   - Dropping a prefab into a scene can put the Scene-view position into the root's offsets and Pos Z. Use `Fit to parent (offsets 0, Z 0)` in the Flexible Layout inspector.
3. Put elements that must avoid notches and home indicators under a full-stretch container with `Flexible Safe Area`.
4. Screen ranges, work resolutions, grids, the role list and preview devices live in `Assets/FlexibleUITool/Settings/FlexibleProfileSettings.asset`. It is shared by the whole project.
5. Code can listen to `FlexibleLayout.ProfileChanged` to adapt non-UI things (cameras, etc.).

---

## 3. The Flexible UI window

`Tools > FlexibleUITool > Flexible UI Window`. One window, three tabs.

| Tab | What it does |
|---|---|
| **Edit** | Screen buttons, the selected block's state, optional grid placement, templates, and `More` (undo all changes of a screen, Game view option). |
| **Preview** | Shows the prefab at the window's own shape, live. Drag the window edge to make it wide or narrow and watch the layout switch. Device presets show the notch / home bar area. |
| **Check** | Shows your prefabs on many phones and tablets at once and finds blocks that stick out of the screen, overlap, or sit under a notch. |

**Grid (optional)**: open `Place on a grid`, select a block, then drag over the blue cells in the Scene view. The block snaps to the cells and follows other screen sizes nicely. `Horizontal` / `Vertical` choose how it follows: stretch (the default), stick to an edge, or center. Changing `Column` / `Row` / `Width` / `Height` or a mode moves the selected block right away (no extra button); hand nudges on top of the cells are kept. Save the prefab as usual to keep the result. The red area is the notch / home bar of the device with the largest one.

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
7. Finish, and keep or delete the practice prefab.

**Advanced** (on the landscape screen, 6 parts):

1. **Grid size**: columns, rows, margin and gutter.
2. **Place on cells**: drag over the blue cells in the Scene view.
3. **Following the screen size**: `Stretch` (default), `Stick left / right`, `Center`.
4. **Fine-tuning by hand**: adjust a block after placing it; the adjustment is kept when the grid changes.
5. **Roles**: name what each block is for.
6. **Templates**: save the layout of roles and apply it again in one click.

After the basic tutorial, `Tutorial` opens a list where you can pick the basic tutorial, the whole advanced tutorial, or one advanced part to do again. A single part sets the practice prefab up so you can start there directly.

The grid and templates are shared by the whole project, so the tutorial puts back any grid change and deletes the templates it made when it ends or when you press `Quit tutorial`.

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

Preview, Check, and the CLI report the same three issues:

| Issue | CLI code | Meaning |
|---|---|---|
| Out of screen | `OUT_OF_SCREEN` | A block leaves the screen. |
| Overlap | `OVERLAP` | Two blocks overlap. A block fully inside another is ignored only if it is also inside it on the portrait screen (intentional layering, like a title on a bar). |
| Control outside SafeArea | `OUTSIDE_SAFE_AREA` | A button sits under a notch or home indicator. |

`Screen edge ratios` also checks each screen at the narrowest and widest ratio where it can appear. Fixed-size blocks overflow there first.

### Runtime behavior

- `FlexibleLayout` picks the screen from its own rect's width / height, with a small margin (hysteresis, default 0.01) at boundaries so it does not flicker.
- It switches in Play Mode and on devices. In the editor, use the screen buttons or the Preview tab.
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
| Layout does not switch in Play Mode | The prefab root is not full-stretch, or the canvas uses the default Canvas Scaler instead of `Flexible Canvas Scaler`. |
| The agent says `PREFAB_OPEN` | Save and close Prefab Mode, then ask it to retry. |
| Auto-layout with AI says Claude Code is not logged in | Press `Connect AI`, then `Log in`. Logging in to the Claude desktop app does not log in the `claude` command; it needs its own login once. |
| Auto-layout with AI seems stuck | The AI sometimes thinks for a minute or two without any visible step. Wait, or press `Stop` and `Undo AI changes`. |
