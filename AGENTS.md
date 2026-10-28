# FlexibleUITool — guide for coding agents

FlexibleUITool lets one UI prefab serve portrait, square and landscape screens.
Use the CLI below to read and change layouts. Do not edit prefab YAML by hand.
This file is the only document the user needs to give you. Everything else is below.

> **Where this file lives.** The Unity editor copies it to `Library/FlexibleUITool/AGENTS.md` when the project opens and fills in the tool folder in every path below (`Assets/FlexibleUITool` when the tool sits in Assets, or a `Library/PackageCache/com.raccooncoder7.flexibleuitool@...` folder when it is installed as a package). If you are reading the original file and a path starts with a placeholder in double curly braces, that placeholder means the folder that contains this file.

The human manual is `{{ToolPath}}/MANUAL.md` (English). Read it when the user picks "Manual" or asks how the GUI works.

People who do not use an agent do not need you: Unity has a built-in tutorial (`Tools > FlexibleUITool > Tutorial`) and the GUI works with buttons only. In short: open a UI prefab in Prefab Mode, press **Portrait / Square / Landscape** (Scene view overlay or the Flexible UI window), move blocks, press Ctrl+S.

## Start here

When the user asks you to read this file, do this before anything else.

1. Run `ping` (see Running). Do not show raw JSON.
   - `ok`: continue to step 2.
   - `EDITOR_NOT_RUNNING`: tell the user to open this project in the Unity editor, then stop and wait.
   - `EDITOR_PLAYING`: ask the user to exit Play Mode, then stop and wait.
2. Reply **in the user's language** (the language they wrote to you in). Show only this menu, translated, with the numbers kept:

   ```
   Flexible UI Tool is ready. What would you like to do?
   1. Tutorial: try the main features on a practice copy, step by step (about 30 min)
   2. Manual: explain the tool and its manual in your language
   3. Adapt a portrait UI prefab to landscape
   4. Check or adjust an existing layout
   ```

   With a choice tool (see Rules), show these four as the choices instead of a text list.

3. Run the playbook for the chosen item. If the user writes a free request instead, pick the closest playbook.

### Rules for every playbook

- Write short replies in the user's language. Summarize results. Never paste raw JSON.
- **Ask with buttons.** If your environment has a tool that shows the user clickable choices (for example `AskUserQuestion` in Claude Code), use it for **every** question with fixed answers, every time, for the whole session: the start menu, "next" after each tutorial step, skip/show-me, keep/delete. Put the usual choice first. Keep labels short, in the user's language. The user can still type a free answer (such as a change request) through the tool's "other" field. Ask in plain text only if no such tool exists.
- When you render, open the PNG yourself to judge it, then give the user its file path.
- **Apply directly.** Do not run `-DryRun` first and do not ask "shall I apply this?". Apply, render, then report in this shape (in the user's language): "I applied this layout. Tell me if you want to change anything; otherwise I'll move on." Changes are cheap to redo with another spec. Use `-DryRun` only when the user asks to preview.
- **Explain layouts in plain words.** Never show cell numbers, axis modes or a table of columns/rows/width/height/stretch. Show the rendered images and give one short sentence per block, e.g. "Main fills the left half and grows with the screen", "SubRight stays the same size in the bottom-right corner".
- **Optional sub-steps are separate questions.** Finish the main action first, then ask about the optional part as its own yes/no question, with "skip" as the default. Each choice is exactly one action: the plain "done" choice never includes an optional extra. Run an optional part only when the user explicitly picks it.
- Save spec files as `Library/FlexibleUITool/specs/<PrefabName>_<profile>.json`.
- If a write is refused (`PREFAB_OPEN`, `WORKSPACE_EXISTS`), tell the user exactly what to do in Unity and wait. Do not retry in a loop.
- The GUI is one window, `Tools > FlexibleUITool > Flexible UI Window`, with three tabs: **Edit** (screen buttons, grid, templates), **Preview** (live preview at the window's aspect ratio) and **Check** (thumbnails of every device). When a prefab with `FlexibleLayout` is open in Prefab Mode, the Scene view also shows the screen buttons (overlay "Flexible UI").
- **Unity labels.** The tool's windows show English, Korean or Japanese (the user's choice in `Tools > FlexibleUITool > Language`). When you name a button or label, quote it in the user's language as Unity shows it: look up the English label in `{{ToolPath}}/Editor/Localization/Strings.tsv` (columns: English, Korean, Japanese). If unsure which language their Unity shows, give the English label too.

### Playbook 1: Tutorial

Run it one step at a time. **Each step is one reply**: explain it in 2 to 4 sentences, run its commands, and show the result, all in that same reply. Then ask with buttons (see Rules): "Next" first, and "Skip the next step" second. On a skippable step, label the skip choice clearly. If the user types a change request instead, do it, show the result, and ask again. Skip any step they ask to skip.

- Never end a reply by announcing or proposing the next step ("Step 4: here is the default plan, say next to continue"). Start the next step only after the user says "next", and then do it completely in that reply.
- Steps 4, 5 and 6 apply their default layout **without asking first**. The user changes things after seeing the result.

1. **Practice copy.** Run `duplicate -Prefab FlexibleSample -Out FlexibleCliTutorial` (the copy is created next to `FlexibleSample`).
   - If you get `PREFAB_NOT_FOUND` for `FlexibleSample`, the tutorial sample is not in the project yet (package install). Run `import-samples`, then retry.
   - If you get `TARGET_EXISTS`, ask whether to reuse the existing copy or make a new one with a suffix (`FlexibleCliTutorial2`, ...). Recommend a new copy: an older copy may carry an outdated portrait layout from before `FlexibleSample` was fixed.
   - Run `clear-profile` for `landscape` and for `square`, so the copy is a pure portrait UI.
   - Explain: the prefab itself is the portrait (base) layout. Other profiles only store differences.
2. **Look.** Run `profiles`, then `describe -Profile landscape`, then `render` for `portrait,landscape`.
   - Explain why the landscape image looks wrong: every block is `layout: base`, so it follows the portrait layout.
   - Ask the user to open the Flexible UI window, select the prefab, open the **Preview** tab, and drag the window wider and narrower. The layout switches when the aspect ratio crosses a profile boundary.
3. **Roles.** Set roles `Header`, `Main`, `Side`, `Footer` on the blocks with the same names. Explain that roles let you say "put Main on the left" and reuse layouts across screens.
4. **Landscape layout.** Do not ask how they want landscape. Apply this default right away:
   - header: full width at the top, `safeArea: false`
   - main: large, on the left
   - side: tall, on the right
   - footer: bottom center
   - SubLeft: bottom left
   - SubRight: bottom right

   Then write the spec and apply it right away (no dry-run, no "shall I apply?").
   - Render presets `1,5,3` (phone landscape, tablet landscape, foldable) and run `check`. Show the images and describe the result in plain words (see Rules).
   - Mention in one sentence that the layout is stored as grid cells, so it follows other landscape sizes. Do not explain cell numbers or modes unless asked.
   - If `check` reports issues, explain them in plain words and fix them with another spec.
   - Note that the foldable uses the square profile, which still follows portrait. Step 6 handles it.
   - End with: "I applied this layout. Tell me if you want to change anything; otherwise we'll go to the next step."
5. **Fine adjustments.** Do not ask first. Apply these three changes right away (the user can ask for others afterwards):
   - SubRight 1.3x larger
   - footer 30 up
   - hide SubLeft in landscape

   Apply them right away with `scale`, `offset` and `visible`, render preset `1`, and describe the change in plain words. End with the same "applied; tell me if you want changes" line.
6. **Square (recommended, skippable).** Explain why it matters:
   - The `square` profile covers aspect ratios between portrait and landscape (see `profiles`, about 0.8 to 1.25).
   - Foldable inner screens, tablet split view and resizable windows land here.
   - Each profile is independent. Until square gets its own spec, it follows the portrait layout, which is usually cramped or stretched there.

   Render preset `3` (foldable) to show the current result, then, in the same reply, apply this layout on the square grid (fewer columns than landscape, about 6×6) without asking:
   - header: full width at the top
   - main: large, top-left
   - side: right, shorter than in landscape
   - SubLeft / SubRight: a row above the footer
   - footer: bottom

   Render `3` again and run `check`. Show the before and after images. End with the "applied; tell me if you want changes" line.
   Ask the user to drag the Preview tab's window width through the three ranges (portrait, square, landscape) and watch the layout switch.
7. **GUI hand-off.** Ask the user to double-click the practice copy in the Project window (it opens in Prefab Mode) and press the `Landscape` screen button (Scene view overlay or the Edit tab of the Flexible UI window; quote it in the user's UI language, see Rules). Wait until they say it is open.
   - Then ask one separate yes/no question: "Want to see how the CLI protects your manual work? (optional)". Offer exactly two choices: "Skip" and "Show me". Treat "done", "opened" or anything unclear as Skip.
     - Only on "Show me": try a small apply; it is refused with `PREFAB_OPEN`. Explain that this keeps the agent from overwriting their manual work.
   - Ask them to move or resize something by hand on the landscape screen, press Ctrl+S, then close Prefab Mode. (Switching screens with the buttons also saves.)
   - After they close it, run `export-spec` and explain in plain words how their manual change came back (as an adjustment on top of the cells, or as raw values).
8. **Real scene test (optional, skippable).** Explain: so far everything was a preview. Now the same prefab runs in a real scene, where `FlexibleLayout` picks the profile at runtime, with no code.
   - The user must close Prefab Mode and save the open scene first (otherwise `PREFAB_OPEN` / `SCENE_UNSAVED`).
   - Run `scene-place -Prefab FlexibleCliTutorial` (the copy from step 1). It opens the sample's `SampleScene` (next to `FlexibleSample`), places the prefab full-stretch under its Canvas (which has `FlexibleCanvasScaler`) and saves the scene. Tell the user their previous scene comes back at the end.
   - Ask the user to press Play in Unity. Wait until they say it is running.
   - Run **one** command: `game-view -Preset 0,3,1,5` (portrait phone, foldable, landscape phone, landscape tablet). It switches the Game view to each resolution, waits about a second, and captures it, all in one go (about 5 seconds). Do not stop between resolutions or wait for the user.
   - Open every captured image (`shots[].path`) yourself, then show them all together with one line each: device, profile (`shots[].layouts[].profile`), and what changed.
   - Explain: on a device the same switch happens when the screen rotates or a foldable opens.
   - Ask the user to stop Play. Then run `scene-clear`: it removes the test instance, saves the scene and reopens the previous scene.
9. **Wrap up.** Summarize what they learned in 3 to 5 bullets. Then ask whether to keep the practice prefab. Offer exactly two choices: "Keep it" and "Delete it".
   - "Keep it": do nothing.
   - "Delete it": run `cleanup-tutorial -Prefab <the copy from step 1>`. It moves the prefab to the OS trash (recoverable), deletes old-version work copies of it, removes its test instance from open scenes, and deletes its spec files and rendered images in `Library/FlexibleUITool`. Report what was removed in one or two lines. If it is refused with `PREFAB_OPEN`, ask the user to close Prefab Mode and retry once.
   - Then show the menu again.

### Playbook 2: Manual

The manual is written in English for everyone; you explain it in the user's language.

1. Read `{{ToolPath}}/MANUAL.md` in full.
2. Give an overview in about 8 short bullets, in the user's language: what the tool does, the screens (profiles), switching screens with buttons in Prefab Mode, the grid and axis modes, the window's three tabs, the built-in tutorial, and that an agent can do most of the work.
3. Ask which part to open, with buttons (see Rules), exactly these four:
   - GUI tutorial (section 4)
   - Working with an agent (section 5)
   - Reference and FAQ (sections 6 and 7)
   - Back to the menu
4. Explain the chosen part in the user's language. Summarize it; do not paste it. For the tutorial, go step by step and ask "Next" (button) after each step, like Playbook 1.
   - Unity labels: the manual quotes the English labels. Quote them in the user's UI language as described in Rules.
   - Answer follow-up questions from the manual. If it does not cover something, say so; do not guess.
5. Ask again with the same four buttons until the user picks "Back to the menu", then show the menu.

### Playbook 3: Adapt a portrait UI prefab to landscape

1. Ask which prefab. Offer `list` results, or accept a name.
   - Check that the root is full-stretch. If `describe` shows the root size is not the canvas size, warn the user.
2. Run `profiles`, then `describe` for the base profile, then `render -Preset portrait`. Learn the structure: the root's direct children and their roles.
   - If the main blocks have no roles, propose roles and set them after the user agrees.
3. Design the landscape layout on that profile's grid:
   - Keep top and bottom bars as full-width bars. Header bars that touch the edge use `safeArea: false`.
   - Turn a vertical stack into columns: primary content on the left and large, secondary content on the right.
   - Keep the reading order. Use `stretch` for content areas. Use fixed modes (`left`, `right`, `top`, `bottom`, `center`) for icons and buttons.
   - Keep buttons inside the safe area (`safeArea: true`).
   - Fixed-size blocks (`left`, `right`, `top`, `bottom`, `center`, or with `scale`) keep their size, so they overflow first at the narrowest edge of the profile (e.g. `landscape@1.24`). `check` covers these edges.
4. Write the spec and apply it right away. Render `1,5` and run `check`. Show the images, the issues, and a one-line plain description per block. End with "applied; tell me if you want changes".
5. Iterate on the user's feedback with small spec patches; unlisted blocks keep their state. Run `check` after each change.
6. Ask whether to do `square` (foldables) too. If yes, repeat steps 3 to 5 for `square`.
7. Summarize. Mention that they can fine-tune by hand: open the prefab, press the screen button, move blocks, Ctrl+S. `export-spec` captures the final layout.

### Playbook 4: Check or adjust an existing layout

The user reviews in Unity, where the thumbnails are easier to read than images in chat.

1. Run `review` (`-Prefab` if they named one, `-Folder` for a folder, nothing for every prefab). It opens the **Check** tab of the Flexible UI window and runs the check: one row per prefab, one thumbnail per device and per screen edge ratio, a red bar on thumbnails with problems.
   - Tell the user to switch to Unity and look at the Check tab. Mention the `Only issues` toggle (in their UI language) to hide clean thumbnails.
2. Summarize the `review` result in plain words, grouped by prefab and device (it lists only screens with problems). Do not render images in chat unless the user asks.
3. Turn their requests into a minimal spec patch and apply it right away. Run `review` again so the window refreshes, then report what changed in plain words.

## Model

- **Profiles**: `portrait` (base), `square`, `landscape`. Run `profiles` for the real list and aspect ranges.
  - The **base profile is the prefab itself**. You cannot change it through a spec. Edit the prefab normally.
  - Every other profile stores per-block overrides in the `FlexibleLayout` component on the prefab root.
  - At runtime the profile is chosen from the root's aspect ratio (width / height).
- **Blocks** are RectTransforms identified by **path** from the prefab root, e.g. `Header` or `Main/Title`.
  - Same-named siblings are written `Name[1]`.
- **Roles** are optional labels on blocks (`Header`, `Main`, `Side`, `Footer`, `Sub`, `Overlay`, ...).
  - A spec block with a role and no path applies to **every** block with that role.
- **Grid**: each profile has a grid (`columns × rows`, `margin`, `gutter`, in canvas units).
  - A cell is `[column, row, width, height]`. `[0,0]` is the **top-left** cell.
  - With `safeArea: true` (the default), the grid fills only the area inside the largest notch / home-indicator insets of that profile's devices. Grid-placed blocks then never overlap the unsafe area.
- **Axis modes** (`modeX`, `modeY`) decide how a block follows other aspect ratios inside its cells.

  | Mode | Behavior |
  |---|---|
  | `stretch` | Scales with the cell area. |
  | `start` / `left` / `bottom` | Keeps its size and sticks to the left (x) or bottom (y) edge of the cells. |
  | `end` / `right` / `top` | Keeps its size and sticks to the right (x) or top (y) edge. |
  | `center` | Keeps its size and stays centered in the cells. |
  | `auto` (default) | `stretch` if the cells touch both screen edges on that axis, the touching side if one, otherwise `center`. |

- **Adjustments** are added on top of the grid result, in canvas units:
  - `offset` [x, y]: **y is up** (Unity anchoredPosition).
  - `sizeOffset` [w, h]
  - `scale` [s] or [x, y, z]
- **Canvas units**: the short screen side is always 1080, e.g. landscape 16:9 = 1920×1080.

## Running

```
powershell -ExecutionPolicy Bypass -File Library/FlexibleUITool/flexui.ps1 <command> [options]
```

- The script prints one JSON object. Exit code 0 means `"ok": true`.
- The Unity editor must be open on this project. The running editor executes the command, usually in under a second.
  - If the editor is closed, you get `EDITOR_NOT_RUNNING`. Ask the user to open it.
  - If the editor is in Play Mode, you get `EDITOR_PLAYING`. Ask the user to exit Play Mode. Only `ping` and `game-view` work during Play Mode.
  - While the editor is compiling, the script waits.
- Options: `-Prefab <name or Assets/...prefab>`, `-Profile <id>`, `-Preset <index|name|profile id|edges>` (several: comma-separated, e.g. `-Preset 1,portrait`; `edges` = each profile's edge ratios), `-Aspect <width/height>` (any ratio, e.g. `-Aspect 1.3`; several: comma-separated), `-Spec <file>`, `-Out <file or folder>`, `-Folder <Assets/...>`, `-Path <block path>`, `-Role <role>`, `-Scene <Assets/...unity>`, `-DryRun`, `-All`, `-Timeout <sec>`, `-Project <path>` (only if not run from the project folder).

## Commands

| Command | Use it to | Notes |
|---|---|---|
| `ping` | check the connection | |
| `profiles` | read profiles, grids, safe insets, device presets, roles, templates | read first |
| `list` | find prefabs that have FlexibleLayout | `-Folder` |
| `describe` | see where every block is, per profile | `-Prefab`, optional `-Profile`, `-All` for every RectTransform |
| `render` | get PNG images | `-Prefab`, `-Preset` (default: one per profile), `-Aspect`. Look at the returned `path`. |
| `check` | find layout problems | `-Prefab` (or all prefabs), `-Preset`, `-Aspect`. Default: all device presets **plus each profile's edge ratios** (targets named like `landscape@1.24`). |
| `review` | open the Check tab of the Flexible UI window and run the check, so the user sees thumbnails | optional `-Prefab` or `-Folder`. Same checks and screens as `check`. Returns `results[]` (only screens with problems; `issues` are English sentences), `prefabCount`, `issueCount`. |
| `export-spec` | dump current profile data as a spec | `-Prefab`, optional `-Profile`, `-Out file.json` |
| `apply-spec` | change layouts | `-Spec file.json`, `-DryRun` to preview changes |
| `set-role` | label a block | `-Prefab -Path -Role` (`-Role ''` clears; `-Role` is required) |
| `duplicate` | copy a prefab, e.g. a practice copy | `-Prefab <source> -Out <Name>` (next to the source) or `-Out Assets/.../Name.prefab` |
| `import-samples` | bring the tutorial sample (`FlexibleSample`, `SampleScene`) into `Assets/FlexibleUITool/Samples/` | Needed once when the tool is installed as a package. Moves a sample from an older import location. Returns the sample `folder`. |
| `clear-profile` | delete every record of one profile, so it follows the base layout | `-Prefab -Profile` (not the base profile) |
| `cleanup-tutorial` | remove a tutorial practice copy and what the tutorial made for it | `-Prefab`. Only `Samples/*Tutorial*.prefab` (never `FlexibleSample`). Prefab goes to the OS trash; old work copies, test instances, specs and renders are deleted. |
| `scene-place` | put a prefab in a test scene to try it at runtime | `-Prefab`, optional `-Scene` (default: the `SampleScene` next to `FlexibleSample`). Opens and saves the scene; replaces an earlier test instance. |
| `scene-clear` | remove the test instance and go back to the scene open before `scene-place` | optional `-Scene` |
| `game-view` | change the Game view resolution; in Play Mode, also capture it | `-Preset` (several: comma-separated). In Play Mode it goes through every preset, waits about 1 s each, and returns `shots[]` with `path` (PNG) and `layouts[]` (each `FlexibleLayout`'s current profile and size). Outside Play Mode it only selects the last preset. |

### `describe` output

`profiles[].blocks[]` contains:

| Field | Meaning |
|---|---|
| `path`, `role`, `active` | Block identity and visibility. |
| `rect` [x, y, w, h] | Canvas units. **Top-left origin, y down.** |
| `normalized` [x0, y0, x1, y1] | 0..1, top-left origin. |
| `layout` | `base`: no override in this profile.<br>`grid`: placed on cells.<br>`adjusted`: cells + `offset` / `sizeOffset` / `scale`.<br>`manual`: free RectTransform values. |
| `cell`, `modeX`, `modeY`, `safeArea` | Present for `grid` and `adjusted`. |

`issues[]` contains `{code, blocks}` objects:

| Code | Meaning |
|---|---|
| `OUT_OF_SCREEN` | The block leaves the screen. |
| `OVERLAP` | Two blocks partially overlap. Full containment is ignored, because it is intentional layering. |
| `OUTSIDE_SAFE_AREA` | A button or other Selectable is under a notch or home indicator. |

## Spec format

```json
{
  "prefab": "Assets/UI/Home.prefab",
  "profiles": [
    { "profile": "landscape",
      "blocks": [
        { "role": "Main",  "cell": [1,1,4,3], "modeX": "stretch", "modeY": "stretch" },
        { "path": "Icon",  "cell": [8,0,2,2], "modeX": "right", "modeY": "top", "scale": [1.3] },
        { "path": "Footer","cell": [2,7,4,1], "offset": [0,30], "sizeOffset": [40,0] },
        { "path": "Banner","cell": [0,0,10,1], "safeArea": false },
        { "path": "Promo", "visible": "hide" },
        { "path": "Title", "reset": true },
        { "path": "Badge", "rect": { "anchorMin":[1,1], "anchorMax":[1,1], "pivot":[1,1], "position":[-20,-20], "size":[120,120] } }
      ] }
  ]
}
```

- Each block needs `path` or `role`. If both are given, `path` wins.
- Each block needs at least one of `cell`, `rect`, `visible` or `reset`. Do not use `cell` and `rect` together.
- **Merge semantics**: blocks not listed keep their current state in that profile. `reset: true` removes the override, so the block falls back to the base layout.
- The spec is validated as a whole. On `SPEC_INVALID` nothing was changed, and the message lists every problem.
- `export-spec` output is a valid spec. Re-applying it changes nothing, also on a copy of the prefab or after `clear-profile` (adjustments such as `scale` are kept).

## Recommended workflow

1. `profiles`: learn the grid sizes and the cell count per profile.
2. `describe -Prefab X -Profile landscape`, plus `render -Prefab X` and look at the images.
3. Write a spec that changes only what was asked.
4. `apply-spec -Spec s.json` (the result lists `changes`: added / updated / removed), then `render` and `check` again.
5. Show the user the rendered images and the remaining issues in plain words. If they want changes, patch the spec and apply again. Fine-tuning by hand happens in Prefab Mode with the screen buttons (`Tools > FlexibleUITool > Flexible UI Window`).

## Errors

| Code | Meaning / what to do |
|---|---|
| `EDITOR_NOT_RUNNING` | Open the Unity editor on this project. |
| `PROJECT_NOT_FOUND` | Run the script from the Unity project folder, or pass `-Project <path>`. |
| `SAMPLE_NOT_FOUND` | The tutorial sample is missing and could not be imported. Ask the user to reinstall the package. |
| `EDITOR_PLAYING` | The editor is in Play Mode. Ask the user to stop it. |
| `PREFAB_OPEN` | The user has this prefab (or an old-version work copy of it) open in Prefab Mode (for `scene-place`: any prefab). Ask them to save (Ctrl+S) and close it. |
| `SCENE_UNSAVED` | An open scene has unsaved changes. Ask the user to save it. |
| `SCENE_NOT_FOUND`, `CANVAS_NOT_FOUND` | The test scene is missing, or has no root Canvas. |
| `GAME_VIEW_FAILED` | The Game view could not be resized. Ask the user to open the Game view. |
| `SPEC_INVALID` | Fix every item in the message. Includes `WORKSPACE_EXISTS`: a work copy made by an older version of the tool is pending. Ask the user to open the prefab in Unity and press `Apply it` or `Discard it` in the Flexible UI window. |
| `PREFAB_NOT_FOUND`, `AMBIGUOUS_PREFAB` | Pass the full `Assets/...prefab` path. |
| `TARGET_EXISTS` | The `duplicate` target already exists. Reuse it or choose another name. |
| `NOT_PRACTICE_COPY` | `cleanup-tutorial` only removes tutorial practice copies. Never retry it on another prefab. |
| `BASE_PROFILE` | The base profile is the prefab itself. Edit the prefab directly. |
| `PROFILE_NOT_FOUND`, `PRESET_NOT_FOUND`, `TARGET_NOT_FOUND` | Check the name with `profiles` / `describe`. |
| `BRIDGE_NOT_RESPONDING` | The editor is open but has compile errors or is stuck. |
| `TIMEOUT` | The editor is busy. |

## Do not

- Do not hand-edit `FlexibleLayout` data or files in `Assets/FlexibleUITool/Workspace/`.
- Do not apply specs while the user is editing the same prefab.
- Do not leave test instances (`[FlexibleTest] ...`) in a scene. Run `scene-clear` when the test ends.
- Do not change grid settings in `Settings/FlexibleProfileSettings.asset` without asking. They are shared by the whole project.
