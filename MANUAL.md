# FlexibleUITool Manual

FlexibleUITool lets **one UI prefab serve portrait, square and landscape screens**.
The prefab itself is the portrait (base) layout. The other layouts are stored as data in the `FlexibleLayout` component on the prefab root, and the right one is picked at runtime from the screen's aspect ratio.

This is the only manual. It replaces the earlier Korean and Japanese PDF guides.

- **Coding agents** (Claude Code and others): start from `Library/FlexibleUITool/AGENTS.md` (the editor writes it there when the project opens). Choosing "Manual" in its menu makes the agent read this file and explain it in your language.
- **Language**: the tool's windows are available in English, Korean and Japanese. Switch with `Tools > FlexibleUITool > Language` or the language dropdown at the top left of the Layout Window. `Auto` follows the Unity editor language. This manual quotes the English labels; the label table is `Editor/Localization/Strings.tsv` (English, Korean, Japanese).

## Contents

0. [Installation](#0-installation)
1. [Concepts](#1-concepts)
2. [Setup (programmers)](#2-setup-programmers)
3. [Everyday workflow (artists)](#3-everyday-workflow-artists)
4. [GUI tutorial](#4-gui-tutorial)
5. [Working with an agent (CLI)](#5-working-with-an-agent-cli)
6. [Reference](#6-reference)
7. [FAQ](#7-faq)

---

## 0. Installation

The tool is a Unity package (`com.raccooncoder7.flexibleuitool`). Tested on Unity 6.3. It needs `com.unity.ugui`, which Package Manager installs with it.

**From a git repository (recommended)**

1. Open `Window > Package Manager`, press `+`, choose `Add package from git URL...`, and paste `https://github.com/RaccoonCoder7/FlexibleUITool.git#v1.0.0` (the part after `#` pins a version tag; leave it out to follow the latest commit).
2. Or add it to `Packages/manifest.json` yourself:

   ```json
   "dependencies": {
     "com.raccooncoder7.flexibleuitool": "https://github.com/RaccoonCoder7/FlexibleUITool.git#v1.0.0"
   }
   ```

   Unity runs `git` itself to fetch the package, so git must be installed on the machine.

**From a local folder** (while developing the tool): `"com.raccooncoder7.flexibleuitool": "file:C:/path/to/FlexibleUITool"`.

**After installing**

- Import the tutorial sample if you want it: `Package Manager > Flexible UI Tool > Samples > Tutorial sample > Import`, or let the agent run `import-samples`. It lands in `Assets/Samples/Flexible UI Tool/<version>/Tutorial sample/`.
- The tool keeps project data in `Assets/FlexibleUITool/`: `Settings/FlexibleProfileSettings.asset` (created on first use; commit it) and `Workspace/` (temporary, git-ignored automatically).
- Open the project once in Unity so the agent files appear in `Library/FlexibleUITool/`.

To update, change the tag in `manifest.json` (or remove the package's entry from `packages-lock.json` to fetch the latest commit).

The tool can also live directly in `Assets/FlexibleUITool/` (copy the package folder there). Everything works the same; the sample is then already in the project.

---

## 1. Concepts

| Term | Meaning |
|---|---|
| **Profile** | An aspect-ratio range with its own layout. Defaults: `portrait` (up to 0.8, the base), `square` (0.8 to 1.25), `landscape` (1.25 and wider). Aspect = width / height. |
| **Base profile** | Portrait. The prefab itself; nothing extra is stored for it. |
| **Block** | A RectTransform in the prefab that moves between layouts (usually the root's direct children). |
| **Workspace** | A temporary copy of the prefab for editing one profile by hand. Committing it writes the differences back as profile data and deletes the copy. |
| **Grid** | Each profile's columns × rows, with margin and gutter. Blocks are placed on cells. |
| **Axis mode** | How a placed block follows other screen sizes: stretch, stick to an edge, or stay centered. |
| **Adjustment** | Position / size / scale added on top of a grid placement. |
| **Role** | A label on a block (Header, Main, Side, Footer, ...). Used by templates and the review. |
| **Template** | Role-to-cell placements saved per profile, reusable on other screens. |
| **Hysteresis** | A small margin (default 0.01) at profile boundaries, so the layout does not flicker when the ratio sits right on a boundary. |

Canvas units: the **short side of the screen is always 1080**. Landscape 16:9 is 1920 × 1080, portrait 9:16 is 1080 × 1920.

### Folders

Tool (package or `Assets/FlexibleUITool/`):

| Path | Contents |
|---|---|
| `Runtime/` | `FlexibleLayout` (runtime switching), `FlexibleCanvasScaler` (short-side scaling), `FlexibleSafeArea`, profile settings and state data |
| `Editor/` | Layout / Preview / Review windows, workspace create and commit, grid placement, templates, preview renderer, CLI bridge, `Localization/Strings.tsv` |
| `Cli/` | `flexui.ps1`, the command line used by agents |
| `Samples~/Tutorial` | Tutorial sample: prefab `FlexibleSample`, test scene `SampleScene`, default settings (imported on request) |

Project data (`Assets/FlexibleUITool/`, always in the project):

| Path | Contents |
|---|---|
| `Settings/` | Project-wide settings: `FlexibleProfileSettings.asset` (profiles, grids, roles, templates, preview presets) |
| `Workspace/` | Temporary workspaces (not in git, deleted on commit) |

Per machine: `Library/FlexibleUITool/` holds `AGENTS.md`, `flexui.ps1`, the CLI inbox and outbox, and rendered images.

The tool has no dependency on project code (it references `UnityEngine.UI` only) and is split into two assemblies, `FlexibleUITool.Runtime` and `FlexibleUITool.Editor`.

### Windows (`Tools > FlexibleUITool`)

| Window | Use |
|---|---|
| **Layout Window** | Create and commit workspaces per profile, grid placement, roles, templates |
| **Preview Window** | Renders the selected prefab at the window's own aspect ratio, live. Drag the window wider or narrower to see where the layout switches. |
| **Review Window** | Renders every prefab that has `FlexibleLayout` on every device preset (and profile edge ratio) in one table of thumbnails |

---

## 2. Setup (programmers)

1. Replace the screen canvas's `Canvas Scaler` with `Flexible Canvas Scaler`. It always scales by the short side (1080). Its inspector shows only `Reference Resolution`.
2. Make the UI prefab's root **full-stretch** (anchors 0..1, offsets 0). The root's aspect must follow the screen for profiles to switch.
   - Dropping a prefab into a scene can put the Scene-view position into the root's offsets and Pos Z. Prefab-root position and size cannot be reverted with Overrides, so use the `Fit to parent (offsets 0, Z 0)` button in the yellow warning of the Flexible Layout inspector.
3. Put elements that must avoid notches and home indicators under a full-stretch container with `Flexible Safe Area` (directly under a parent that covers the whole screen).
4. Profile ranges, work resolutions, grids, the role list and preview presets live in `Assets/FlexibleUITool/Settings/FlexibleProfileSettings.asset` (created on first use). It is shared by the whole project.
5. Code can listen to `FlexibleLayout.ProfileChanged` to adapt non-UI things (cameras, etc.) when the profile changes.

---

## 3. Everyday workflow (artists)

1. **Build the portrait prefab** as usual. Make the inside of each block tolerate size changes with anchors and Layout Groups.
2. **Set roles** (optional): open the prefab, select a block, and pick its role in the Layout Window.
3. **Create a workspace**: in the Layout Window, press `Create workspace` on the landscape or square row. A copy of the latest portrait prefab opens with the existing records of that profile applied.
4. **Game view** switches to the profile's work resolution automatically and switches back when the workspace closes. Only the ratio matters; sizes are converted to the 1080 short side.
5. **Place blocks**: apply a template with one click, or select a block and drag over cells in the Scene view. Choose an axis mode per axis.
6. **Fine-tune** by hand in the workspace like any prefab (RectTransform, anchors, pivot, active state, Layout Group settings).
7. **Check** in the Preview Window while resizing it. In the profile you are editing it shows the workspace; elsewhere it shows the saved data.
8. **Commit** with `Commit`. Only blocks that differ from the portrait prefab are saved; the workspace is deleted.
9. **Review** all prefabs at once with `Run review` in the Review Window.

To change a layout later, repeat from step 3. Selecting a block again fills in its last cell and modes.

---

## 4. GUI tutorial

About 30 to 40 minutes. You try every feature on a practice copy of the sample prefab.

| Feature | Where | Step |
|---|---|---|
| Live preview by window size, device presets, SafeArea | Preview Window | 1 |
| Roles (Header, Main, ...) | Layout Window, prefab mode | 2 |
| Landscape / square workspaces | Layout Window | 3 |
| Grid placement by drag, axis modes | Layout Window + Scene view | 4 |
| Manual fine-tuning, grid vs manual blocks | Workspace | 5 |
| Commit | Layout Window | 6 |
| Templates | Layout Window | 7 |
| Re-placing after a grid change | Layout Window | 8 |
| Reviewing every prefab | Review Window | 9 |
| Runtime switching in Play Mode | Game view | 10 |

### Preparation

1. Open the project in Unity and check the Console for compile errors.
2. Find the sample prefab `FlexibleSample` in the Project window (import the tutorial sample first if the tool is installed as a package, see [Installation](#0-installation)). Duplicate it with `Ctrl+D`, and rename the copy `FlexibleTutorial`. Do the whole tutorial on this copy and leave the sample untouched.
3. Open the **Layout Window** and the **Preview Window** from `Tools > FlexibleUITool` and dock them where you can see them.
4. Make sure the Game view resolution list has `1080x1920` (portrait) and `1920x1080` (landscape). Workspaces switch automatically, but you pick them by hand in step 10. Add missing ones with `+` under the resolution dropdown.

### Step 1. Change the aspect ratio in the Preview Window (5 min)

The Preview Window treats its own width and height as a device screen and shows, live, which layout that ratio gets.

1. Select `FlexibleTutorial` in the Project window. Its name appears at the top right of the Preview Window.
2. With the dropdown on `Free (window size)`, drag the window edge slowly wider and narrower.
   - The info line shows the profile, ratio, logical size and number of issues.
   - The bar below splits portrait, square and landscape by color; the white line is the current ratio.
3. Past about 0.8 the layout becomes square, past about 1.25 landscape. Near a boundary the switch point differs by 0.01 depending on direction: that is the hysteresis (setting `Hysteresis` in the settings asset).
4. Pick `Phone landscape (19.5:9)` in the dropdown. The ratio is fixed regardless of window size, and the sides and bottom are shaded: that is the area outside the SafeArea (notch and home indicator).
5. Press `Rotate` to swap the orientation, and toggle `SafeArea` to see the shading go away.
6. Try `Tablet portrait (3:4)` and `Foldable (open)`. The foldable selects the square profile.
7. Read the warnings at the bottom left (out of screen, overlap). `Show issues` toggles them.
8. `Lock` keeps showing this prefab when you select something else. Turn it off again.

**Check**: the sample's landscape layout was made in a portrait Game view, so `Out of screen: Main` is expected. Steps 4 to 6 fix it.

### Step 2. Read the Layout Window and set roles (5 min)

With an original prefab selected, the Layout Window shows what each profile has recorded. In prefab mode you can set roles.

1. Select `FlexibleTutorial` and look at the Layout Window.
   - `Portrait (portrait)  — base (the prefab itself)`: the saved prefab layout is the portrait layout.
   - The `Square (square)` and `Landscape (landscape)` rows show the number of records (grid / manual) and two buttons, `Create workspace` and `Delete records`.
   - The Flexible Layout component in the Inspector shows the same summary.
2. Double-click `FlexibleTutorial` to open it in prefab mode.
3. Select `Header` in the Hierarchy. A `Role: Header` dropdown appears at the bottom of the Layout Window. Choose `Header`.
4. Set the other roles the same way:

   | Block | Role |
   |---|---|
   | Header | Header |
   | Main | Main |
   | Side | Side |
   | Footer | Footer |

5. Save the prefab with `Ctrl+S`. Unsaved changes disable `Create workspace` in the next step.

Roles are used in step 7 (templates) and step 9 (review). The role list lives in `Settings/FlexibleProfileSettings.asset`.

### Step 3. Create a landscape workspace (3 min)

A workspace is a temporary copy of the latest portrait prefab. It opens with the existing landscape records applied and disappears when you commit.

1. On the `Landscape (landscape)` row of the Layout Window, press `Create workspace`.
2. Prefab mode switches to `FlexibleTutorial__landscape__xxxxxxxx`. The file lives in `Assets/FlexibleUITool/Workspace/` and is not committed to git.
3. The Layout Window switches to its workspace view: source path, profile, and current root size.
4. The Game view switches to the landscape work resolution `1920x1080` (added to the list if missing) and switches back when the workspace closes.
   - `Current root size: 1920 x 1080  (ratio 1.78)` means it worked.
   - You can turn the automatic switch off with the checkbox at the top of the Layout Window. If the resolution then does not match, a yellow warning "The root ratio is outside this profile (…)" appears; `Switch now` fixes it.
   - Sizes are converted to the 1080 short side, so any landscape ratio such as `2340x1080` works.
5. Blue cells (the grid, 8 × 5 by default for landscape) overlay the Scene view. If not, turn on `Show grid`.

The blocks now sit at their old landscape positions, so Main hangs off the bottom. The next step fixes it.

### Step 4. Place blocks on the grid (10 min)

Select a block and drag over cells in the Scene view. When you release, anchors, pivot and size are set for you.

**SafeArea**: the red area in the Scene view is outside the SafeArea of the device with the largest notch and home indicator in this profile. With `Place inside SafeArea only (avoid notch and home indicator)` (on by default), the grid is divided only inside the red area, so blocks placed on the grid never land under a notch. Turn it off for backgrounds and header bars that must reach the screen edge. Blocks you move by hand are not guaranteed; check them with the SafeArea display in the Preview Window.

In this tutorial, turn the checkbox off only for Header.

1. Select `Header` in the Hierarchy.
2. In the Scene view, drag from the leftmost to the rightmost cell of the top row. An orange rectangle follows the drag.
3. Release. Header is placed on those cells; its anchors in the Inspector have changed.
   - `Mode to apply: horizontal Stretch / vertical Stick top` appears in the Layout Window: touching both edges selects stretch, touching only the top selects top.
4. Place the other blocks the same way. A cell is `(column, row) width × height`, with (0, 0) at the top left.

   | Block | Cells | Horizontal | Vertical |
   |---|---|---|---|
   | Header | (0, 0) 8×1 | auto | auto |
   | Title | (0, 0) 2×1 | auto | auto |
   | Main | (1, 1) 4×3 | stretch | stretch |
   | Side | (6, 1) 2×3 | auto | stretch |
   | SubLeft | (0, 4) 2×1 | auto | auto |
   | Footer | (2, 4) 4×1 | auto | auto |
   | SubRight | (6, 4) 2×1 | auto | auto |

5. Practice changing modes with Main:
   1. Dropping Main by drag applies center.
   2. Set `Horizontal` and `Vertical` to `Stretch` in the Layout Window.
   3. Press `Apply to selected block`. Anchor Min and Max split in the Inspector.
6. The chosen modes stay for the next block, so set them back as in the table before placing the next block.

| Axis mode | Behavior on other aspect ratios |
|---|---|
| Stretch | Grows and shrinks with the cell area |
| Stick left / Stick bottom | Keeps its size, sticks to the left (bottom) edge of the cells |
| Stick right / Stick top | Keeps its size, sticks to the right (top) edge of the cells |
| Center | Keeps its size, stays centered in the cells |
| Auto | Stretch if the cells touch both screen edges, the touching side if one, otherwise center |

**Good to know**

- While a block is selected, clicks in the Scene view pick cells. Select other blocks in the Hierarchy.
- Instead of dragging, you can type `Column`, `Row`, `Width (cells)` and `Height (cells)` and press `Apply to selected block`.
- `Ctrl+Z` undoes a placement.

### Step 5. Fine-tune and read block states (5 min)

A workspace is an ordinary prefab: after grid placement you can edit it as usual, and the tool remembers which blocks you touched.

1. Select `Footer` and read the `State:` line in the Layout Window: `Grid block (2,4) 4x1, inside SafeArea`.
2. Raise Footer's `Pos Y` by 30 in the Inspector (or drag it in the Scene view).
3. Select Footer again. The state now reads grid (2,4) 4x1, inside the SafeArea, plus an adjustment (position 0,30 / size 0,0). Changing anchors or pivot themselves shows `Manually adjusted (...)` instead, and the block is excluded from templates.
4. Select Side. The Layout Window fills in the values used to place it ((6,1) 2×3, auto / stretch). Select Main and it fills in Main's values. This makes small corrections easy.
5. Try a non-grid change: uncheck `SubRight`'s active box to hide it in landscape only.
6. Set the Preview Window to `Free (window size)` and widen it into the landscape range.
   - The top right shows `FlexibleTutorial (in progress)`, and the info line shows `· workspace content`.
   - Your edits show immediately, even unsaved.
7. Narrow the window back into portrait and the original portrait layout appears. One window reviews both orientations.

Adding, deleting or renaming objects, and sprite or text changes made in a workspace are **not** committed. Make those in the original portrait prefab.

### Step 6. Commit (3 min)

`Commit` saves only the blocks that differ from the original into its landscape data and deletes the workspace.

1. Press `Commit` at the bottom of the Layout Window. Unsaved changes are saved first.
2. The result window says `Wrote N records to the 'landscape' profile.` Warnings are listed below it and in the Console.
3. Prefab mode closes and `FlexibleTutorial` is highlighted in the Project window. Select it and look at the Layout Window.
   - If you followed steps 4 and 5, the landscape row reads about `7 records (6 grid / 1 manual)`. The manual one is Footer.
4. Open `FlexibleTutorial` and confirm the portrait layout is unchanged. Close prefab mode.
5. Pick `Phone landscape (19.5:9)` in the Preview Window. The `Out of screen: Main` warning from step 1 is gone, and SubRight is hidden in landscape.
6. Press `Create workspace` on the landscape row again and confirm your layout comes back. Keep this workspace open for step 7.

**Ending without committing**: `Cancel (delete)` deletes only the workspace; the original is untouched.

**If the original changed meanwhile**: committing shows a "Source changed" confirmation first.

### Step 7. Save and apply a template (5 min)

A template stores "which role goes on which cells", so other screens with the same roles get the same layout with one click.

1. In the landscape workspace from step 6, find the `Templates (role-to-cell layouts)` section. At first it says there are no templates for this profile.
2. Type a name such as `Landscape default` and press `Save current layout as template`.
3. The confirmation lists the saved roles: Header, Main, Side, and Footer with an adjustment.
   - Footer was raised by 30 by hand in step 5, so "up 30" is saved with its cells and applied on top of them.
   - Position, size and scale changes are saved as adjustments. Blocks whose anchors or pivot were changed after grid placement cannot be expressed as cells + adjustment and are left out; the confirmation shows why.
4. Drag Main, Side and Footer somewhere wrong on purpose.
5. Pick the template in the dropdown and press `Apply`. "4 blocks placed" appears, and Footer returns to its raised spot.
6. In real use, give another screen the same roles, create its landscape workspace and apply this template. Roles the prefab lacks are listed in the result.

Templates are stored per profile in `Settings/FlexibleProfileSettings.asset`. Commit that file to share them with the team. You can delete the practice template with `Delete`.

### Step 8. Change the grid and re-place (3 min)

After a grid change, grid blocks can be re-placed on the new cells; manual blocks are left alone.

1. In the same workspace, change the grid's `Columns` from 8 to 10 in the Layout Window. The blue cells narrow immediately.
2. A notice and a `Re-place grid blocks (N)` button appear under the grid.
3. Press it. Grid blocks move to the same cell numbers on the new grid. Footer is a manual block and stays.
4. Set the columns back to 8 and press the button again. **Grid settings are project-wide, so always restore practice changes.**
5. Press `Commit` to finish.

Changing the Game view ratio (e.g. 1920x1080 to 2340x1080) can also bring up the button, because fixed-size blocks compute differently. Place blocks at the same work resolution every time.

### Step 9. Review everything (3 min)

The Review Window renders every prefab with `FlexibleLayout` on every device preset and shows them in one place.

1. Open `Tools > FlexibleUITool > Review Window` (or `Review window` at the top of the Layout Window).
2. Leave `Folder` empty to search all of Assets, or drop a folder from the Project window.
3. Keep all preset buttons and `Profile edge ratios` on, then press `Run review`.
4. One row per prefab:
   - Summary: records per profile (grid / manual), number of roles.
   - Thumbnails: preset name and the selected profile (`→ ...`).
   - Issues under each thumbnail. A red bar marks thumbnails with issues; hover for the full list.
5. Compare the `Phone landscape (19.5:9)` thumbnails of `FlexibleSample` and `FlexibleTutorial`: the sample still has `Out of screen: Main`, the tutorial copy does not.
6. Turn on `Only issues` to hide clean thumbnails. Resize them with the `Thumbnail` slider. `Select` finds the prefab in the Project window.

See [Checks](#checks) for what is reported.

### Step 10. Check in Play Mode and clean up (7 min)

In the editor an open prefab always shows the base layout; profiles switch only in Play Mode.

1. Open `SampleScene` from the sample folder (next to `FlexibleSample`). Its Canvas already has `Flexible Canvas Scaler`.
2. Drag `FlexibleTutorial` onto the Canvas in the Hierarchy. If the Flexible Layout inspector shows a yellow warning, press `Fit to parent (offsets 0, Z 0)`.
3. Set the Game view to `1080x1920` and press Play.
4. While playing, switch the Game view to `1920x1080`. The landscape layout appears.
   - Selecting `FlexibleTutorial` in the Hierarchy shows `Current profile` in the Inspector.
   - Add `1080x1080` to the resolution list to see the square profile.
5. (Optional) For the SafeArea, pick a device with a notch in `Window > General > Device Simulator` and rotate it. A full-stretch container with `Flexible Safe Area` shrinks away from the notch.
6. Stop Play. Delete `FlexibleTutorial` from the scene and close the scene without saving.

### Cleanup

- [ ] SampleScene closed without saving (no `FlexibleTutorial` left in it)
- [ ] Grid `Columns` changed in step 8 is back to 8
- [ ] Practice template kept or deleted (`Delete` in a landscape workspace)
- [ ] `FlexibleTutorial.prefab` kept or deleted
- [ ] No leftover workspace in `Assets/FlexibleUITool/Workspace/`

---

## 5. Working with an agent (CLI)

An agent (Claude Code or similar) in a terminal can read, change and render layouts through `flexui.ps1` while you keep Unity open. You talk to it in plain words.

### Start

1. Open the project in Unity. It must **not** be in Play Mode.
2. Open a terminal in the project folder and start your agent.
3. Tell it only this:

   > Read `Library/FlexibleUITool/AGENTS.md`

The agent checks the connection to Unity and shows a menu in your language:

1. **Tutorial**: try the main features on a practice copy, step by step (about 30 min)
2. **Manual**: the agent reads this file and explains it in your language
3. **Adapt a portrait UI prefab to landscape**
4. **Check or adjust an existing layout**: opens the Review Window in Unity and reviews with you

With Claude Code, questions come as buttons. You can always type your own answer instead, for example a change request.

If the agent cannot connect, run this yourself. `"ok":true` means the bridge works:

```
powershell -ExecutionPolicy Bypass -File Library/FlexibleUITool/flexui.ps1 ping
```

Good to know:

- The agent learns coordinates, commands and the spec format from AGENTS.md. Have it read the file at the start of every new conversation.
- The agent applies layouts directly and shows the result; tell it what to change afterwards.
- While a workspace for the same prefab is open or pending, or the prefab is open in prefab mode, the agent's writes are refused. Your manual work is never overwritten.
- Rendered images are saved in `Library/FlexibleUITool/renders` (not in git).

### What the agent tutorial covers

The agent makes a practice copy `FlexibleCliTutorial` (or `FlexibleCliTutorial2`, ...) with empty landscape and square data, then walks through these steps. After each step you press "Next".

| Step | What happens |
|---|---|
| 1. Practice copy | The copy is a pure portrait UI. |
| 2. Look | The agent reads the profiles and renders portrait and landscape. Landscape looks wrong because it still follows portrait. Drag the Preview Window to see the switch. |
| 3. Roles | Header, Main, Side and Footer get roles. |
| 4. Landscape layout | A default landscape layout is applied right away and rendered on phone landscape, tablet landscape and foldable, then checked on every device and profile edge ratio. |
| 5. Fine adjustments | SubRight 1.3× larger, Footer 30 up, SubLeft hidden in landscape, applied right away. |
| 6. Square (skippable) | A square layout for foldables and split screens. |
| 7. GUI hand-off | You open a workspace and tweak by hand; optionally see that the agent's writes are refused meanwhile. After you commit, the agent exports the layout as a spec, with your manual change as an adjustment. |
| 8. Real scene test (skippable) | The prefab is placed in `SampleScene`. You press Play; the agent switches the Game view through four devices and captures each in one go. |
| 9. Wrap up | Keep the practice copy, or have the agent delete it with everything it made (workspaces, scene test instance, specs, renders). The prefab goes to the OS trash. |

### Commands you can run yourself

All start with `powershell -ExecutionPolicy Bypass -File Library/FlexibleUITool/flexui.ps1`, run from the project folder. Results are JSON; error codes and messages are in English. The full reference (options, spec format, errors) is in `AGENTS.md`.

| To | Append |
|---|---|
| Check the connection | `ping` |
| See profiles, grids, presets | `profiles` |
| See where blocks are | `describe -Prefab FlexibleCliTutorial -Profile landscape` |
| Get images | `render -Prefab FlexibleCliTutorial -Preset 1,5` (or `-Aspect 1.3`, `-Preset edges`) |
| Find problems | `check -Prefab FlexibleCliTutorial` |
| Open the Review Window and review | `review` (or `-Prefab`, `-Folder`) |
| Set a role | `set-role -Prefab FlexibleCliTutorial -Path Main -Role Main` |
| Apply a spec | `apply-spec -Spec spec.json` (`-DryRun` to preview) |
| Export the current layout as a spec | `export-spec -Prefab FlexibleCliTutorial -Out spec.json` |
| Place in / remove from the test scene | `scene-place -Prefab FlexibleCliTutorial` / `scene-clear` |
| Switch (and in Play Mode, capture) the Game view | `game-view -Preset 0,3,1,5` |
| Delete a practice copy and its leftovers | `cleanup-tutorial -Prefab FlexibleCliTutorial` |
| Import the tutorial sample (package install) | `import-samples` |

Notes:

- The Unity editor must be running; it handles each command, usually within a second. While it compiles the command waits; in Play Mode only `ping` and `game-view` run.
- `export-spec` output re-applies with no change, also on a copy of the prefab or after the profile was cleared.
- If the project has `Run In Background` off (Player settings), Play Mode pauses while Unity is in the background. During `game-view` capture the tool refreshes the screen and canvas scale itself, so you do not need to bring Unity to the front.

---

## 6. Reference

### SafeArea and the grid

- In a workspace's Scene view, the red area is outside the SafeArea of the device with the largest notch and home indicator among the presets this profile covers.
- With `Place inside SafeArea only (avoid notch and home indicator)` (on by default), the grid is divided inside that area only, so **blocks placed on the grid never land outside the SafeArea**.
- Backgrounds and header bars that must reach the edge are placed with the option off. Blocks moved by hand are not guaranteed; Preview and Review check them.
- Elements that must fit each device exactly go under a runtime `Flexible Safe Area` container.

### Grid blocks, manual blocks and templates

- A block placed on the grid and not touched afterwards is a **grid block**; one changed by hand afterwards is a **manual block**. The Layout and Review windows show the counts.
- After a grid change, `Re-place grid blocks (N)` in a workspace moves grid blocks to the new cells and leaves manual blocks alone.
- Templates store blocks that have a role and were placed on the grid, as **cells + adjustment** (one per role, per profile, project-wide).
  - Position, size and scale changed by hand after placement are saved as the adjustment (canvas units) and added on top of the cells when applied.
  - Blocks whose anchors or pivot were changed cannot be expressed this way and are left out, with the reason shown.
  - Adjustments are fixed units, so they can drift on screens whose ratio is far from the work resolution.

### Checks

Preview, Review, CLI `check` and `review` report the same three issues:

| Issue | CLI code | Meaning |
|---|---|---|
| Out of screen | `OUT_OF_SCREEN` | A block leaves the screen. |
| Overlap | `OVERLAP` | Two blocks partly overlap. Full containment is ignored as intentional layering. |
| Control outside SafeArea | `OUTSIDE_SAFE_AREA` | A button or other Selectable sits under a notch or home indicator. |

- Checked blocks: blocks with a role, plus direct children of the root that do not cover the whole screen.
- **Profile edge ratios**: besides device presets, each profile is checked at the narrowest and widest ratio where it can appear, hysteresis included (e.g. landscape at 1.24 and 2.4). Fixed-size blocks (edge or center modes, or with a scale adjustment) overflow there first. The Review Window has a `Profile edge ratios` toggle; CLI `check` includes them by default.

### What is recorded

- Recorded: RectTransform (anchors, pivot, position, size, scale), active state, Layout Group settings (padding, spacing, alignment, grid cell size and column count, ...), roles.
- Objects equal to the original are not recorded.
- Made in a workspace but **not** committed (do these in the original portrait prefab):
  - adding, deleting or renaming objects
  - sprite, text and other non-layout changes
- Children positioned by a Layout Group are recorded through the group's settings, not their positions.
- For a block that has its own `FlexibleLayout`, only the block itself is recorded; its inside is handled by its own `FlexibleLayout`.

### Runtime behavior

- `FlexibleLayout` picks the profile from its own rect's width / height, with hysteresis (default 0.01) at boundaries. Targets without a record go back to their values at startup (the base layout).
- It runs in Play Mode only. In the editor an open prefab always shows the base layout; use the Preview Window for other ratios.
- Do not register objects that code or animation moves; switching overwrites them with recorded values. Register their parent container instead.
- `ProfileChanged` notifies profile switches.

### Languages

- Window and dialog text comes from `Editor/Localization/Strings.tsv`: one row per string, English (the key) / Korean / Japanese, separated by tabs. Keep `{0}`-style placeholders and write line breaks as `\n`. Open windows pick up a saved change within two seconds.
- A string missing from the table, or a cell left empty, falls back to English.
- The language is a per-user setting (`Tools > FlexibleUITool > Language`). `Auto` follows the Unity editor language and falls back to English.
- Profile and device preset names in `FlexibleProfileSettings.asset` are stored in English and translated on display. Names you add yourself show as written unless you add them to the table.
- CLI results, error codes and messages are always English, whatever the UI language.

### Preview notes

- Preview and review render a copy of the prefab in a preview scene. Editor behavior of `[ExecuteAlways]` scripts on the prefab may run too.
- SRP renderer features (CRT, vignette, ...) are turned off only while rendering, so you see the UI without screen effects.

---

## 7. FAQ

| Symptom | Cause and fix |
|---|---|
| `Create workspace` is grayed out | The original prefab has unsaved changes. Press `Ctrl+S`. |
| Yellow warning "The root ratio is outside this profile (…)" | The Game view has the wrong orientation. Press `Switch now` in the Layout Window, or pick `1920x1080` for landscape. |
| Dragging in the Scene view does nothing | No block is selected, or `Place by Scene view drag` is off. |
| Other objects cannot be clicked in the Scene view | While a block is selected, clicks pick cells. Select in the Hierarchy. |
| "Object added in the workspace" warning after commit | Objects created in a workspace are not committed. Add them to the original portrait prefab. |
| Saving a template does nothing | No block has both a role and a grid placement. The confirmation lists why each block was left out. |
| The Preview Window is empty | No prefab is selected, or its root is not a RectTransform. |
| Layout does not switch when the resolution changes in Play Mode | The prefab root is not full-stretch, or the canvas uses the default Canvas Scaler instead of `Flexible Canvas Scaler`. |
| The agent says `EDITOR_NOT_RUNNING` / `EDITOR_PLAYING` | Open Unity on this project / stop Play Mode. |
| The agent says `PREFAB_OPEN` or `WORKSPACE_EXISTS` | Close prefab mode, or commit or cancel the workspace, then ask it to retry. |
