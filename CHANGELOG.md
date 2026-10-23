# Changelog

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
