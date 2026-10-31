# Custom Enemy Loader 1.4.2

Created by **Saelac and ChatGPT**.

## Requirements

- Silverpine 1.7.3 and BepInEx 5.
- **[ModdingTools 1.13.0 or newer](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases) is required.** Version 1.12.1 is not compatible; this release uses the shared art-cache API. ModdingTools is distributed separately.
- Custom Item Loader is optional, for selecting its registered items in enemy loot pools.

## Changes since 1.4.1

- Scene descriptions now start with three lines of writing space and expand downward for wrapped text, new paragraphs, and trailing blank lines.
- Text-box heights are explicitly measured at the width actually allocated by the editor, including space taken by adjacent Browse buttons, padding, and scrollbars.
- Growing fields push subsequent controls down. Shortening the text shrinks them again.
- Long asset paths and other values wrap visually without widening the form or obscuring adjacent controls. IDs, paths, and numeric fields remain free of stored line breaks.
- Added 14 text-layout and newline regression checks.

The tabbed editor, shared art library, per-direction offsets, corrected grass-ground preview, and readable action buttons from 1.4.1 are retained.

Existing JSON packs, distributed directional PNG caches, disabled markers, and saved enemy prefab names remain compatible. Recipients do not need an authoring GLB when the pack includes its complete four-view cache.

## Installation

Close Silverpine, install the required ModdingTools version, then extract the ZIP into `BepInEx/plugins/CustomEnemyLoader/`. Alternatively, replace only `CustomEnemyLoader.dll` in that folder.

The ZIP contains the plugin DLL, README, release notes, and an example JSON definition. It does not include ModdingTools, game assemblies, personal packs, configuration files, or debug symbols. The example requires its own artwork/audio and is not an installed enemy pack.

Keep your existing packs under `BepInEx/config/CustomEnemyLoader/` and restart the game after updating.

## Verification

- Release build passed with zero warnings and zero errors.
- All 59 regression checks passed.
- In-game verification covered a six-line paragraph, trailing blank lines, shortening text, and a long asset path beside its Browse button.
- Installed pack JSON files were unchanged by testing.
