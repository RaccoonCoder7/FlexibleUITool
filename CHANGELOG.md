# Changelog

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
