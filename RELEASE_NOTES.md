# Custom Enemy Loader 1.4.1

Created by **Saelac and ChatGPT**.

## Requirements

- Silverpine 1.7.3 and BepInEx 5.
- **[ModdingTools 1.13.0 or newer](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases) is required.** Version 1.12.1 is not compatible; this release uses the new shared art-cache API. ModdingTools is distributed separately. If 1.13.0 is not yet available, wait for that release before upgrading Enemy Loader.
- Custom Item Loader is optional, for selecting its registered items in enemy loot pools.

## Changes since 1.3.12

- Redesigned the editor with a searchable enemy sidebar, Enemy/Appearance/Combat/Spawning tabs, and a persistent directional Artwork/World preview.
- Added per-direction artwork offsets, with drag editing, numeric controls, keyboard nudging, and reset. Offsets affect artwork, not collision or pathfinding.
- Integrated ModdingTools' shared art library, including reuse of Item Loader artwork and copying selected images into distributable enemy packs.
- Moved imported GLB authoring sources into the shared model store, with content-based deduplication. Existing packs and legacy authoring locations remain supported.
- Hardened cache generation and asset imports: complete-set replacement, cancellation protection, source-content checks, and content-addressed imported filenames.
- Added unsaved-change prompts, safer embedded-GLB separation, preview cleanup, and validation against non-finite numeric inputs.
- Fixed World preview terrain selection: use the grass ground tile, not grass vegetation or overgrowth.
- Fixed cramped action buttons and split damage/rotation labels. Long text inputs still wrap and expand downward within the editor.

Existing JSON packs, distributed directional PNG caches, disabled markers, and saved enemy prefab names remain compatible. Recipients do not need an authoring GLB when the pack includes its complete four-view cache.

## Installation

Close Silverpine, install the required ModdingTools version, then extract the ZIP into `BepInEx/plugins/CustomEnemyLoader/`. Alternatively, replace only `CustomEnemyLoader.dll` in that folder.

The ZIP contains the plugin DLL, README, release notes, and an example JSON definition. It does not include ModdingTools, game assemblies, personal packs, configuration files, or debug symbols. The example requires its own artwork/audio and is not an installed enemy pack.

Keep your existing packs under `BepInEx/config/CustomEnemyLoader/` and restart the game after updating.

## Verification

- Release build passed with zero warnings and zero errors.
- All 45 regression checks passed.
- In-game verification confirmed the grass ground reference and readable Combat-panel labels/buttons.
- Installed pack JSON files were unchanged by testing.
