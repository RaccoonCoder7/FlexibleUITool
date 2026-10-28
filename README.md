# Flexible UI Tool

One UI prefab for portrait, square and landscape screens. The prefab itself is the portrait layout; other layouts are stored as data in its `FlexibleLayout` component and picked at runtime from the screen's aspect ratio.

- **Install**: Package Manager > `+` > `Add package from git URL...` and paste `https://github.com/RaccoonCoder7/FlexibleUITool.git#v1.0.5`. Details in [`MANUAL.md`](MANUAL.md#0-installation).
- **Manual**: [`MANUAL.md`](MANUAL.md) (setup, workflow, GUI tutorial, agent usage, reference, FAQ)
- **Coding agents**: tell your agent "Read `Library/FlexibleUITool/AGENTS.md`" (the editor writes it there when the project opens) and pick from its menu.
- **Window**: `Tools > FlexibleUITool > Flexible UI Window` (Edit / Preview / Check tabs, built-in tutorial)
- **Auto-layout with AI**: one button in the Edit tab makes the landscape and square layouts with Claude Code on your PC (your Claude subscription, no API key). See [`MANUAL.md`](MANUAL.md).
- **Languages**: English, Korean, Japanese (`Tools > FlexibleUITool > Language`)
- No dependency on project code (`com.unity.ugui` only). Assemblies: `FlexibleUITool.Runtime`, `FlexibleUITool.Editor`. Tested on Unity 6.3; the agent CLI needs Windows PowerShell.
- License: MIT ([`LICENSE.md`](LICENSE.md))
