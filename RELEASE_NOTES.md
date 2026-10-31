# Custom Enemy Loader 1.4.3

Created by **Saelac and ChatGPT**.

## Requirements

- Silverpine 1.7.3 and BepInEx 5.
- **[ModdingTools 1.13.0 or newer](https://github.com/Silverpine-Modding/Silverpine-Saelac-Modding-Tools/releases) is required.** Version 1.12.1 is not compatible; this release uses the shared art-cache API. ModdingTools is distributed separately.
- Custom Item Loader is optional, for selecting its registered items in enemy loot pools.

## Changes since 1.4.2

- Custom enemy sprites now explicitly use Silverpine's native diffuse-lit material, including enemies cloned from unlit base prefabs.
- Applies automatically to single-image, directional-image, shared-library, and GLB/cached-image visuals. Boss and timed templates and saved enemies inherit the same lighting material.
- Existing artwork and distributed caches need no regeneration or JSON changes. Restart the game after updating.
- Only the custom enemy renderer's material reference changes; native enemies, sprite tints, projectile effects, and shared image files are untouched. Editor previews remain unlit image previews.
- If the game's diffuse material is unavailable, the loader warns once and preserves the cloned material so enemies remain loadable.
- Added 11 lighting-helper and compiled-integration regression checks.

The auto-expanding multiline fields, tabbed editor, shared art library, per-direction offsets, corrected grass-ground preview, and readable action buttons are retained.

Existing JSON packs, distributed directional PNG caches, disabled markers, and saved enemy prefab names remain compatible. Recipients do not need an authoring GLB when the pack includes its complete four-view cache.

## Installation

Close Silverpine, install the required ModdingTools version, then extract the ZIP into `BepInEx/plugins/CustomEnemyLoader/`. Alternatively, replace only `CustomEnemyLoader.dll` in that folder.

The ZIP contains the plugin DLL, README, release notes, and an example JSON definition. It does not include ModdingTools, game assemblies, personal packs, configuration files, or debug symbols. The example requires its own artwork/audio and is not an installed enemy pack.

Keep your existing packs under `BepInEx/config/CustomEnemyLoader/` and restart the game after updating.

## Verification

- Release build passed with zero warnings and zero errors.
- All 70 regression checks passed, including native-material assignment, clone isolation, tint/art preservation, missing-resource recovery, and compiled coverage of every enemy visual mode.
- Live day/night and lantern rendering has not been visually tested for this build.
- No installed pack JSON or artwork files were edited.
