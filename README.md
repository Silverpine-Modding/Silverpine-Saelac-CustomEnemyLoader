# Silverpine Custom Enemy Loader

`CustomEnemyLoader` 1.4.4 adds JSON-defined enemies to Silverpine through BepInEx.
Enemy packs can reuse a base-game enemy's movement, collision, loot, shadow,
and other components while replacing its name, health, behavior, combat
abilities, visuals, sounds, and world-generation locations.

Visuals can come from one PNG, four directional PNGs, or one GLB model.
Silverpine's own model renderer generates the four directional sprites from a
GLB and this plugin caches those sprites as PNG files.

Custom enemy artwork uses Silverpine's native diffuse-lit sprite material in the
world, so it responds to darkness and nearby lights even when the cloned enemy
uses an unlit material. This is automatic for single images, directional images,
shared-library art, and generated or distributed GLB caches. Existing packs need
no JSON changes or cache rebuild; restart the game after updating the plugin.
Bosses, timed spawns, and enemies restored from saves inherit the same material.
The editor's Artwork/World previews remain image previews, not lighting simulations.
Native enemies, projectile effects, and the shared artwork files are unchanged.
If the game's diffuse material is unavailable, a warning is logged and the
cloned material is retained so the enemy can still load.

## Shared-library deletion (1.4.4)

Each shared-art card has **Delete from library**, followed by confirmation.
Escape cancels the prompt. The same framework operation is used by Item Loader
and Growables: deletion affects the shared catalog for all mods, but preserves
pack-local images, source GLBs, and images referenced by other library entries.
Mods using the deleted library entry directly may lose access.

A recovery copy is kept under `BepInEx/shared/ArtLibrary/trash`; no automatic
purge occurs, so this is not a disk-space cleanup operation. To restore, close
the game and copy its `entries/` and `images/` files back into the corresponding
library folders, preserving existing files. Invalid or unreadable records block
deletion. See the Modding Tools README for the complete API/recovery contract.

Browsers refresh cached entries and thumbnails when another mod publishes or
deletes art, using an in-memory revision token rather than filesystem polling.
Opening the library does not republish removed art; explicit indexing can.

## Installation

1. Install BepInEx for Silverpine.
2. Install the Silverpine Modding Tools Framework 1.13.1 or newer under
   `BepInEx/plugins/ModdingTools/`.
3. Build this project or obtain `CustomEnemyLoader.dll`.
4. Put the plugin files in a dedicated folder:

   ```text
   Silverpine/
   └── BepInEx/
       └── plugins/
           └── CustomEnemyLoader/
               └── CustomEnemyLoader.dll
   ```

5. Launch the game once. The plugin creates:

   ```text
   BepInEx/config/CustomEnemyLoader/
   BepInEx/shared/ArtLibrary/models/
   ```

6. Put each enemy pack's JSON and assets below `BepInEx/config/CustomEnemyLoader/`.

Do not copy `ModdingTools.dll` into the CustomEnemyLoader folder. All consumer
plugins use the framework's single installed copy. Modding Tools also supplies
the shared Newtonsoft.Json 13.0.3 assembly; do not add a private
`Newtonsoft.Json.dll` here.

## Main-menu enemy editor

From Silverpine's main menu, select:

```text
Modding Tools → Custom Enemy Editor
```

The editor provides:

- New pack creation and editing of existing pack JSON files
- Enable and disable installed packs without renaming or modifying their JSON
- Width-limited editor text inputs measured at their actual available width;
  wrapped content and blank lines expand the box and push later controls down
- A scene-description box with at least three lines of writing space, supporting
  Enter for new paragraphs; IDs, paths, and numbers remain newline-free
- Multiple enemies per pack
- Item Loader-style colors, searchable enemy sidebar, pack selector, and tabs
  for Enemy, Appearance, Combat, and Spawning
- Persistent Artwork/World preview with Down, Left, Up, and Right views
- Per-direction world offsets: drag the artwork against a fixed turf reference,
  enter X/Y values, or nudge with arrow keys (0.01 tile; Shift: 0.1 tile)
- Shared asset browser using ModdingTools' art-cache API; reuse Item Loader art,
  publish enemy views, and copy chosen images into a distributable pack
- Duplicate enemies and confirm removals; unsaved-change prompts on exit,
  switching packs, and reloading files
- Collapsible sections for general settings, visuals, abilities, loot, and each
  spawning mode
- Base-enemy clone selection
- Display name, description, HP, passive flee behavior, and pathfinding speed bonus
- Single-image, four-directional-image, and GLB visual modes
- Native file browsing; ordinary assets copy into the pack while GLB authoring
  sources are kept outside it
- Image and cached-GLB previews
- One-click distribution-cache generation, GLB separation, and 90-degree Y
  rotation controls
- Add, remove, reorder, and configure ability entries, including native
  projectile selection and custom OGG import
- Replace or extend clone loot pools with base-game and modded items, with an
  optional exact item tier per entry
- Browse loot items through a category tree, including Custom Item Loader packs
- Add and remove biome spawn rules, fixed-coordinate boss spawns, and scheduled
  Silverpine-outskirts spawn rules
- Assign tile-scoped encounter music and a positional, full-length death song
  to each fixed-coordinate boss
- Validation and formatted JSON output

Click **Save Pack** when finished. Runtime enemy templates are registered during
plugin startup, so restart Silverpine after saving a new or modified pack.
Use **Back to Mods** to return to the Modding Tools submenu. **Escape** first
cancels an active picker or offset drag, closes the art library, or prompts
about unsaved changes before leaving. A failed save preserves an editable draft.

### Enabling and disabling packs

Select an installed pack and use **Disable Pack** or **Enable Pack** below the
pack selector. Disabled packs remain visible with a **(disabled)** suffix, but their
enemies are not registered the next time Silverpine starts. Restart the game
after changing a pack's state.

The editor records this state with a small sidecar marker named after the pack
JSON, for example `enemies.json.disabled`. It does not rename or modify the pack
JSON or its assets. Deleting the marker manually also re-enables the pack.

Drafts and unsaved new packs cannot be toggled. Disabling a pack needed by an
existing save has the same missing-prefab consequences as temporarily removing
that pack; re-enable the same pack and restart before loading that save again.

Incomplete new packs and failed saves are preserved as `enemies.enemy-draft`.
They appear in the pack selector with a **(draft)** suffix but are ignored by the
runtime enemy loader until they pass validation and are saved as
`enemies.json`.

The editor writes distributable packs inside
`BepInEx/config/CustomEnemyLoader/`. Ordinary imported assets are copied into
an `assets` folder beside the pack JSON. GLB source models are authoring files,
so the editor stores them separately under
`BepInEx/shared/ArtLibrary/models/` and never requires recipients to install
that directory. Existing `BepInEx/config/CustomEnemyLoaderAuthoring/` sources
remain readable. New model imports share readable filenames with content hashes
and reuse identical sources across packs. Different files with the same original
name cannot overwrite each other.

The development build command is:

```powershell
dotnet build -c Release
```

Build output is written to `bin/Release/`.

## Pack layout

The distributable pack contains JSON, ordinary assets, and generated cache
images. The GLB remains in the separate authoring tree:

```text
BepInEx/
├── config/CustomEnemyLoader/MossPack/
│   ├── enemies.json
│   ├── .cache/myname_mosspack_moss_stalker/
│   │   ├── manifest.json
│   │   ├── down.png
│   │   ├── left.png
│   │   ├── up.png
│   │   └── right.png
│   └── assets/
│       └── <image-content-hash>.png
└── shared/ArtLibrary/
    ├── models/moss_stalker__<model-hash>.glb
    ├── images/<image-content-hash>.png
    └── entries/<art-content-hash>.json
```

Only the `MossPack` directory is distributed. Generated GLB sprites are stored
under its `.cache` directory. When an authoring GLB is available, changing its
content, rotation, alpha cutoff, or renderer version regenerates the
cache. When the GLB is absent on a recipient's computer, the complete four-PNG
cache is treated as the released visual and loaded directly.

## Complete example

```json
{
  "packId": "myname.mosspack",
  "enemies": [
    {
      "id": "moss_stalker",
      "name": "Moss Stalker",
      "description": "a moss-covered stalker",
      "clone": "prefab_enemy_giantrat",
      "maxHp": 45,
      "passive": false,
      "fleeBehavior": "ProximityAndDamage",
      "pathfinding": {
        "speedBonus": 0.25
      },
      "loot": {
        "mode": "Replace",
        "min": 1,
        "max": 2,
        "entries": [
          { "item": "Iron Sword", "tier": "Superior", "weight": 3.0 },
          { "item": "Pile of Gold", "weight": 1.0 }
        ]
      },
      "visuals": {
        "model": "moss_stalker.glb",
        "rotation": [0, 0, 0],
        "alphaCutoff": 0.1,
        "cache": true,
        "scale": 1.0
      },
      "abilities": [
        {
          "type": "basicAttack",
          "cooldown": 0,
          "minPower": 4,
          "maxPower": 9,
          "damageType": "Normal",
          "sound": "sound_combat_attack_sword"
        },
        {
          "type": "applyStatusEffect",
          "cooldown": 3,
          "minPower": 2,
          "maxPower": 4,
          "statusEffect": "poison"
        }
      ],
      "spawns": [
        {
          "biome": "Forest",
          "min": 1,
          "max": 2
        }
      ],
      "bossSpawns": [
        {
          "id": "old_grove_guardian",
          "x": 125,
          "y": -42,
          "respawnAfterDeath": false,
          "bossMusic": {
            "file": "assets/grove_guardian.ogg",
            "volume": 0.8,
            "priority": 500,
            "fadeSeconds": 1.25
          },
          "deathSong": {
            "file": "assets/grove_restored.ogg",
            "volume": 0.8,
            "minRange": 0.0,
            "maxRange": 25.0
          }
        }
      ],
      "silverpineTimedSpawns": [
        {
          "id": "night_raiders",
          "startHour": 20,
          "endHour": 5,
          "min": 1,
          "max": 3,
          "edgeDepth": 8
        }
      ]
    }
  ]
}
```

One JSON file may contain multiple entries in `enemies`. Multiple pack JSON
files may also be installed together.

Unknown JSON fields are treated as errors. This is intentional: a misspelled
field is reported in `BepInEx/LogOutput.log` instead of being silently ignored.

## Enemy fields

| Field | Required | Meaning |
| --- | --- | --- |
| `packId` | Yes | Namespace for every enemy in the file. Keep it stable after releasing a pack. |
| `enemies` | Yes | Array containing one or more enemy definitions. |
| `id` | Yes | Enemy identifier within the pack. Keep it stable for save compatibility. |
| `name` | Yes | Short player-facing name shown on the enemy health bar. |
| `description` | No | Rich description exposed to the LLM scene-visibility system and kill narration. Defaults to `a NAME`. |
| `clone` | Yes | Base-game enemy prefab whose components, loot, armor, movement, and physical configuration are copied. |
| `maxHp` | No | Maximum and initial HP. A value greater than zero replaces the clone's HP. |
| `passive` | No | If `true`, the enemy wanders without initiating combat. Its fleeing behavior is selected by `fleeBehavior`. Defaults to `false`. |
| `fleeBehavior` | No | Passive-only behavior: `ProximityAndDamage`, `WhenAttacked`, or `Never`. Defaults to `ProximityAndDamage`, preserving the base-game passive behavior used by older packs. |
| `pathfinding` | No | Optional movement overrides. Omit it to retain the selected clone's settings. |
| `loot` | No | Optional weighted loot pool. Omit it to retain the clone's loot unchanged. |
| `visuals` | Yes | Exactly one of the three visual-source formats below. |
| `abilities` | No | Ordered list of combat abilities. An empty list creates an enemy with no attacks. |
| `spawns` | No | Locations and counts used when new world tiles are generated. |
| `bossSpawns` | No | Fixed absolute world coordinates for unique or respawning boss-style instances. |
| `silverpineTimedSpawns` | No | Scheduled groups that appear along Silverpine's outskirts and despawn outside their configured hours. |

The registered save prefab name is derived from `packId` and `id`. Changing
either value makes existing saved copies of that enemy unavailable.

### Passive flee behavior

`fleeBehavior` is used only when `passive` is `true`:

| Value | Behavior |
| --- | --- |
| `ProximityAndDamage` | Wanders, flees from a nearby visible player, and flees when damaged. This is Silverpine's base passive behavior. |
| `WhenAttacked` | Wanders and ignores nearby players, but flees after taking damage. |
| `Never` | Wanders, never attacks, and never flees. |

The Custom Enemy Editor exposes these values under **Flee behavior** whenever
**Passive** is enabled.

### Pathfinding

```json
"pathfinding": {
  "speedBonus": 0.25
}
```

`speedBonus` is Silverpine's native `PathfindingEntity.speedBonus` value. It
must be between `0` and `1` and is the chance each turn that the creature takes
one extra pathfinding step. `0` gives no extra step; `1` always gives one.
Omit the complete `pathfinding` object to preserve the clone prefab's original
value. The editor provides an **Override clone pathfinding speed bonus** toggle
for this distinction.

## Loot pools

```json
"loot": {
  "mode": "AddToClone",
  "min": 1,
  "max": 2,
  "entries": [
    { "item": "Pile of Gold", "weight": 4.0 },
    { "item": "Iron Sword", "tier": "Pristine", "weight": 1.0 }
  ]
}
```

`min` and `max` are inclusive numbers of weighted rolls. Each roll selects one
entry; entries can be selected more than once. Every `weight` must be greater
than zero. Relative weights matter, so weights `4` and `1` give the first item
an 80% selection chance.

Each entry may include an optional `tier`. Valid named tiers are `Normal`,
`Quality`, `Superior`, `Exceptional`, `Pristine`, `Blessed`, and `Legendary`.
Omitting `tier`, setting it to `null`, or writing `"Automatic"` keeps the
existing Silverpine randomized-quality behavior. A named tier forces that exact
quality, including `Normal`. Items without an `IQualityItemComponent` still
drop, but the tier cannot be applied and the loader writes a warning once.

| `mode` | Behavior |
| --- | --- |
| `Replace` | Removes the selected clone's `LootDropper` and uses only this pool. An empty pool with `min` and `max` set to `0` produces no loot. |
| `AddToClone` | Keeps the clone's original loot and performs these additional rolls afterward. |

Loot `item` values support:

- Any base-game item display name, such as `Pile of Gold`.
- Display names inserted into `ItemLibrary.Items` by other mods.
- Stable Custom Item Loader qualified IDs in `packId:itemId` or
  `custom:packId:itemId` form.
- Silverpine's native `Quality|Item Name` form for items that support quality.
  This remains supported for older packs; the separate `tier` field is clearer
  and takes precedence when both forms are present.

The editor provides an **Item tier** selector on every loot entry. **Automatic**
keeps randomized quality; choosing a named tier forces it. When an older
`Quality|Item Name` entry is opened, the editor separates it into the item and
tier fields automatically.

The editor's **Choose item...** browser reads the live `ItemLibrary`, so it
includes items already inserted by other mods. Its tree groups normal items by
category. If Custom Item Loader is installed, a **Custom Item Loader Packs**
branch groups those items by pack; item leaves show their display names but
save stable qualified IDs. Custom Enemy Loader does not require Custom Item
Loader to be installed.

Item resolution is repeated when loot drops. This allows an item registered
later by another mod to work without rebuilding the enemy template. A missing
item is skipped and reported once in `BepInEx/LogOutput.log`.

`name` and `description` intentionally serve different audiences. For example,
`"name": "Skeleton"` keeps the health bar concise, while
`"description": "a walking, living skeleton carrying a rusted sword"` gives
nearby neural NPCs useful scene context. Version 1.1.4 overrides only the
health-bar label for custom enemies; base-game enemy labels are unchanged.

## Visuals

Specify exactly one of `image`, `directionalImages`, or `model`.

### One PNG

```json
"visuals": {
  "image": "sprites/moss_blob.png"
}
```

The same sprite is used in every direction. Silverpine retains its normal
left/right horizontal flipping behavior for non-directional sprites.

By default, the sprite's bottom-middle is placed on the lower edge of its
occupied turf. Silverpine stores the enemy transform at the turf center, so the
loader calculates a scale-aware half-tile vertical pivot offset.

PNG, JPG, and JPEG files supported by Unity's image decoder may be used,
although PNG is recommended when transparency and pixel edges matter.

### Four directional images

```json
"visuals": {
  "directionalImages": {
    "down": "sprites/stalker_down.png",
    "left": "sprites/stalker_left.png",
    "up": "sprites/stalker_up.png",
    "right": "sprites/stalker_right.png"
  }
}
```

All four fields are required.

All image and GLB modes use the scale-aware tile-edge anchor by default. A
custom normalized pivot may be provided instead:

```json
"pivot": [0.5, 0.1]
```

`[0, 0]` is the bottom-left of the image and `[1, 1]` is the top-right. A custom
pivot replaces the automatic half-tile offset. The editor exposes this through
**Override tile-edge sprite anchor** and **Origin X / Y**.

All three visual modes support an in-game sprite scale:

```json
"scale": 2.0
```

`1` is normal size, `2` is twice as large, and `0.5` is half size. Accepted
values are `0.1` through `10`. Scaling changes the sprite's pixels-per-unit;
the automatic pivot compensates so the art's lower edge remains fixed to the
turf's lower edge while the art grows upward and outward. It does not enlarge
the inherited collider or movement footprint.

### GLB directional model

```json
"visuals": {
  "model": "moss_stalker.glb",
  "rotation": [0, 90, 0],
  "alphaCutoff": 0.1,
  "cache": true,
  "scale": 1.0
}
```

| Field | Default | Meaning |
| --- | --- | --- |
| `model` | — | GLB source filename/reference. Authors resolve it from the separate authoring store; recipients use the matching cached PNGs. |
| `rotation` | `[0, 0, 0]` | Initial Euler rotation in degrees as `[x, y, z]`. |
| `alphaCutoff` | `0.1` | Transparency threshold used while downsampling the render. |
| `cache` | `true` | Reuse generated PNGs when the rendering inputs have not changed. |
| `pivot` | Automatic tile-edge anchor | Optional normalized origin override used for every visual mode. |
| `scale` | `1` | In-game visual-size multiplier from `0.1` through `10`; does not change collision. |
| `offsets` | Zero in every direction | Optional `down`, `left`, `up`, `right` objects containing `x` and `y` in tiles. Single-image visuals use `down` for all facing directions. |
| `artAssetId` | — | Shared-library provenance; does not create a runtime dependency on the library. |
| `artOriginalName` | — | Original artwork/object name shown by the editor and preserved when publishing. |

Offsets move the artwork relative to its existing pivot. Positive X moves right;
positive Y moves up. They are independent of sprite scale and render resolution,
do not move the occupied turf, and do not alter pathfinding or collision. Missing
directions mean zero. Values must be finite and within -100 to 100 tiles.

```json
"offsets": {
  "down": { "x": 0.0, "y": 0.1 },
  "left": { "x": -0.05, "y": 0.0 },
  "up": { "x": 0.0, "y": 0.0 },
  "right": { "x": 0.05, "y": 0.0 }
}
```

Offsets and scale update the World preview immediately. Use **Save Pack** and
restart the game to apply them to runtime enemy templates. Escape restores the
offset at the beginning of an unfinished drag; mouse release commits the edit
to the current unsaved document.

#### Shared art browser

Open **Asset library** or choose **Cached art** in Appearance. This uses
`Silverpine.ModdingTools.SharedArtLibrary` and `SharedArtApi`; Item Loader is
optional. Both editors see the same framework-owned library. **Use cached art**
copies all views into the pack's `assets/` folder and selects single-image or
four-direction image mode. Distribute these images with the JSON; recipients
need neither a GLB nor your art library.

**Add current art** publishes the selected source images. **Index existing enemy
art** publishes available images/caches from loaded editor documents without
changing those packs. Successful GLB builds also publish all four views. Shared
entries retain the original object name and deduplicate matching image content.
Scale, pivot, and offsets belong to each enemy and are not baked into shared art.
The editor maps library Front/Right/Back/Left to enemy Down/Right/Up/Left.

The generated sprite order is:

1. Down
2. Left
3. Up
4. Right

If the enemy displays its down view while moving left or otherwise faces the
wrong horizontal direction, adjust the Y rotation by `90` or `-90` degrees.
Use X for forward/back tilt and Z for roll.

The full GLB scene is instantiated for conversion, including all nodes, meshes,
and material primitives. This allows separate equipment such as swords,
shields, and clothing to appear in the directional sprites. Diffuse, albedo,
or color textures are preferred. glTF PBR base-color factors are preserved for
solid-color and texture-tinted materials; the renderer otherwise selects
plausible non-normal/non-metal textures. If built-player shader stripping
exposes renderer materials as empty Unity "fake null" objects, the loader maps
their primitive/material-slot order back to the source glTF material indices.

#### Preparing a GLB enemy pack for distribution

1. In **GLB Model** mode, use **Browse**. The editor copies the selected source
   into `BepInEx/shared/ArtLibrary/models/` outside the pack.
2. Set rotation, alpha cutoff, scale, pivot, and directional offsets as desired.
3. Select **Build Cache + Separate GLB**. This renders `down.png`, `left.png`,
   `up.png`, and `right.png` into the pack's derived `.cache` directory. For an
   older pack that still embeds its GLB, the same action copies that source to
   the authoring store. The embedded copy is removed only after a successful
   **Save Pack**, and only if no other saved enemy or draft still references it.
   Discarding edits or a failed save never removes the source GLB.
4. Save the pack and distribute only its folder under `CustomEnemyLoader`.

Recipients do not need the GLB or shared art library. At startup the
loader sees that the model is absent and automatically loads the four cached
images. A non-empty `model` value remains in JSON to select GLB/cached-model
visual mode and identify the author's source filename. If neither an authoring
GLB nor all four cached PNGs exist, validation reports the incomplete pack.

## Spawning

```json
"spawns": [
  {
    "biome": "DeepForest",
    "min": 1,
    "max": 3
  }
]
```

`min` and `max` specify how many copies are added after a newly generated
matching world tile finishes. Both are inclusive. Use `0` as `min` if a tile
may receive no copies.

This does not retroactively populate world tiles that were already generated.
An enemy can contain multiple spawn entries.

Silverpine is a special case in the base game: its generator has ordinary enemy
spawning disabled. Custom Enemy Loader bypasses that restriction for custom
rules. It also waits for asynchronous pack registration to finish before
populating the already-generated starting Silverpine tile, so `Silverpine`
rules work when starting a new game as well as on later tile loads.

### Valid base-game biome entries

Use these exact `biome` values:

| JSON value | Base-game generator |
| --- | --- |
| `Forest` | `ForestTileGenerator` |
| `DeepForest` | `DeepForestTileGenerator` |
| `Mountain` | `MountainTileGenerator` |
| `HighMountain` | `HighMountainTileGenerator` |
| `Swamp` | `SwampTileGenerator` |
| `FloodedCaverns` | `FloodedCavernsTileGenerator` |
| `FrozenFortress` | `FrozenFortressTileGenerator` |
| `SwampTemple` | `SwampTempleTileGenerator` |
| `Dungeon` | `DungeonTileGenerator` |
| `Silverpine` | Only the world tile named `Silverpine` (matched by tile name) |

`Silverpine` is deliberately matched by the world tile's name. Although the
town uses a normal terrain generator, selecting this value does not add the
enemy to every tile that uses that generator.

### Fixed-coordinate boss spawns

```json
"bossSpawns": [
  {
    "id": "old_grove_guardian",
    "x": 125,
    "y": -42,
    "respawnAfterDeath": false,
    "bossMusic": {
      "file": "assets/grove_guardian.ogg",
      "volume": 0.8,
      "priority": 500,
      "fadeSeconds": 1.25
    },
    "deathSong": {
      "file": "assets/grove_restored.ogg",
      "volume": 0.8,
      "minRange": 0.0,
      "maxRange": 25.0
    }
  }
]
```

Each entry creates one boss-style instance near the absolute world turf
coordinate `x`, `y` when the player loads the containing world tile. `id` must
be unique within that enemy definition and should remain stable after release.
The configured coordinate is preferred. If it is invalid, impassable, or
occupied, the loader searches that same world tile and uses the passable,
unoccupied turf with the shortest straight-line distance. Equal-distance ties
are resolved deterministically. If the entire tile has no usable turf, the
loader reports an error and does not spawn the boss.

When `respawnAfterDeath` is `false`, the boss's death is recorded in the save's
persistent Custom Enemy Loader state and it remains dead after leaving the
tile, saving, or reloading. A new world starts with a fresh ledger. When it is
`true`, the instance can return the next time its tile is loaded after it dies.

World tiles are 100 by 100 turfs. The editor displays the world-tile and local
tile coordinates calculated from the absolute values to make placement easier.

`bossMusic` is optional. When present, its song loops while the player is on
the same world tile as the living boss. It stops immediately when the boss's
death begins or when the player leaves that tile, allowing Modding Tools to
restore the next eligible custom track or Silverpine's normal music. Higher
`priority` values win over other active custom-music requests; `500` is the
default. `volume` accepts `0` through `1`, and `fadeSeconds` must be zero or
greater.

`deathSong` is also optional. It starts once at the boss's exact world position
when death begins, does not loop, and is not tied to the boss object afterward,
so the complete song can continue after the boss fades away. It uses Modding
Tools' positional Ambient channel: it is heard in the vicinity of the death
and follows Silverpine's Master and Ambient volume controls. Its `volume`
accepts `0` through `1`.

The death song uses a corpse-centered circular range measured in turfs, not a
rectangular map-music area. `minRange` is the radius that remains at full
volume; it defaults to `0`, so the song is at full volume exactly at the corpse
and begins fading immediately with distance. `maxRange` defaults to `25`; the
linear fade reaches silence at that radius and remains silent beyond it.
`maxRange` must be greater than `minRange`.

Both `file` values are pack-relative and accept `.ogg`, `.wav`, `.mp3`, `.aif`,
or `.aiff`. Paths cannot leave the pack directory. The shared Modding Tools
audio registry streams these tracks, and the editor's **Browse** buttons copy
selected files into the pack's `assets` directory. Restart Silverpine after
saving the pack so the clips can be registered.

### Scheduled Silverpine outskirts spawns

```json
"silverpineTimedSpawns": [
  {
    "id": "night_raiders",
    "startHour": 20,
    "endHour": 5,
    "min": 1,
    "max": 3,
    "edgeDepth": 8
  }
]
```

These rules apply only to the world tile named `Silverpine`. At the start of
the configured time window, the loader places a random inclusive number from
`min` through `max` on passable, unoccupied turfs near the tile border. The
first `edgeDepth` rows and columns on each side count as the outskirts;
`edgeDepth` accepts values from `1` through `50`.

`startHour` is inclusive and `endHour` is exclusive, using the game's 24-hour
clock (`0` through `23`). A start later than the end crosses midnight, so `20`
through `5` is active from 20:00 until just before 05:00. The two hours must be
different.

The game clock's hourly event drives reconciliation. Instances are created at
most once per rule per daily window and are removed when the window closes.
Entering or loading Silverpine also reconciles the current hour. Scheduled
instances retain their rule and cycle identity in saves, preventing duplicate
spawns after save/load and removing stale instances if Silverpine was unloaded
across days. Killing a scheduled enemy does not replace it during the same
window; the rule becomes eligible again in its next daily window.

## Abilities

Abilities are attempted in reverse JSON order, matching the priority reversal
used by base-game `Enemy.Awake`. Put the ability that should receive the
highest priority last.

`cooldown` follows Silverpine's native turn-counter behavior. An ability is
available when its internal counter is greater than the configured cooldown.

### Ability field reference

| Ability `type` | Used fields |
| --- | --- |
| `basicAttack` | `cooldown`, `minPower`, `maxPower`, `damageType`, `sound` |
| `projectile` | `range`, `cooldown`, `minPower`, `maxPower`, `damageType`, `sound`, `projectile`, optional `oozeProjectileColor`, optional `projectileImage`, optional `statusEffect`, `statusDuration` |
| `applyStatusEffect` | `cooldown`, `minPower`, `maxPower`, `statusEffect` |
| `harmSanity` | `cooldown`, `minPower`, `maxPower` |
| `ice` | `range`, `cooldown`, `chance` |
| `lines` | `range`, `cooldown`, `minPower`, `maxPower` |
| `screech` | `range`, `cooldown`, `minPower`, `maxPower` |
| `sermon` | `range`, `cooldown`, `minPower`, `maxPower` |
| `spawnStunBubbles` | `range`, `cooldown`, `minPower`, `maxPower`, `projectile` |
| `avatar` | `range`, `cooldown`, `minPower`, `maxPower` |

`minPower` and `maxPower` mean damage for attacks, but the native
`applyStatusEffect` ability interprets them as the minimum and maximum status
duration in turns.

### Custom OGG sounds

The `sound` field on `basicAttack` and `projectile` accepts either a built-in
SFX resource name or a pack-relative `.ogg` file:

```json
{
  "type": "basicAttack",
  "cooldown": 0,
  "minPower": 3,
  "maxPower": 6,
  "damageType": "Normal",
  "sound": "assets/moss_hit.ogg"
}
```

OGG paths follow the same rules as visual assets: they must be relative to the
pack JSON, cannot leave the pack folder, and must exist when the pack loads.
The loader decodes each referenced file at startup and routes only its own
registered sound keys through Silverpine's normal positional effects-audio
player. Built-in SFX names and sounds used by base-game enemies are unchanged.

In the editor, enter a native sound name directly or use **Browse** beside
**Sound resource / OGG**. The Modding Tools file picker accepts `.ogg` files
and copies the selected file into the pack's `assets` directory. Restart the
game after saving so the new clip can be registered.

### Basic attack

```json
{
  "type": "basicAttack",
  "cooldown": 0,
  "minPower": 3,
  "maxPower": 6,
  "damageType": "Normal",
  "sound": "sound_combat_attack_ooze"
}
```

### Projectile

```json
{
  "type": "projectile",
  "range": 6,
  "cooldown": 5,
  "minPower": 2,
  "maxPower": 5,
  "damageType": "Fire",
  "projectile": "MiscPrefabs/prefab_projectile_fireball",
  "sound": "sound_statuseffect_burning",
  "statusEffect": "burning",
  "statusDuration": 5
}
```

Omit `statusEffect` when the projectile should only deal damage.

#### Ooze projectile color

When `projectile` is `MiscPrefabs/prefab_projectile_ooze`, select one of the
native color sprites with `oozeProjectileColor`:

```json
{
  "type": "projectile",
  "range": 6,
  "cooldown": 6,
  "minPower": 0,
  "maxPower": 1,
  "projectile": "MiscPrefabs/prefab_projectile_ooze",
  "oozeProjectileColor": "Purple",
  "sound": "sound_combat_attack_ooze"
}
```

Accepted values are `Green`, `Blue`, `Orange`, `Purple`, and `Red`. Green is
the base prefab's default. The editor displays **Ooze color** only while the
ooze projectile prefab is selected. A custom `projectileImage` takes visual
precedence over the color selection.

#### Custom projectile PNG

```json
{
  "type": "projectile",
  "range": 6,
  "cooldown": 5,
  "minPower": 2,
  "maxPower": 5,
  "damageType": "Fire",
  "projectile": "MiscPrefabs/prefab_projectile_fireball",
  "projectileImage": "assets/moss_bolt.png",
  "sound": "assets/moss_bolt_hit.ogg"
}
```

`projectileImage` is an optional pack-relative PNG. When present, the selected
`projectile` prefab remains the behavior template: its movement, rotation,
shadow, light, impact timing, damage, and status-effect handling are retained,
while its primary visible sprite is replaced with the PNG. The image uses a
center pivot and the game's native 32 pixels-per-unit projectile scale. PNG is
the only custom projectile-image format.

The editor's **Projectile prefab** selector lists every native projectile below
and retains a **Custom / modded resource** entry for prefab paths supplied by
other mods. **Projectile PNG** uses the Modding Tools file picker and copies the
selected image into the pack's `assets` folder. Leave it blank to keep the
selected prefab's native art.

### Apply status effect

```json
{
  "type": "applyStatusEffect",
  "cooldown": 2,
  "minPower": 1,
  "maxPower": 4,
  "statusEffect": "poison"
}
```

### Ice

```json
{
  "type": "ice",
  "range": 6,
  "cooldown": 5,
  "chance": 0.25
}
```

`chance` is normally between `0` and `1`.

### Spawn stun bubbles

```json
{
  "type": "spawnStunBubbles",
  "range": 8,
  "cooldown": 6,
  "minPower": 4,
  "maxPower": 6,
  "projectile": "prefab_enemy_stunbubble_toxic"
}
```

For this ability, `projectile` is the stun-bubble prefab name despite the
field's general name.

## Base-game value reference

The following values were found in Silverpine 1.7.3 and are accepted by the
current plugin implementation.

### Clone prefab entries

Use one of these values for `clone`:

```text
prefab_enemy_animal_bear_brown
prefab_enemy_animal_serpent_ice
prefab_enemy_avatar
prefab_enemy_cultist
prefab_enemy_elemental_ice
prefab_enemy_elemental_ice_fleeting
prefab_enemy_elemental_ice_lesser
prefab_enemy_firefly
prefab_enemy_giantbat
prefab_enemy_giantfrog
prefab_enemy_giantfrog_swamp
prefab_enemy_giantrat
prefab_enemy_giantspider
prefab_enemy_golem_iron
prefab_enemy_golem_rock
prefab_enemy_ooze_black
prefab_enemy_ooze_black_cult
prefab_enemy_ooze_black_sentinel
prefab_enemy_ooze_blue
prefab_enemy_ooze_frozen
prefab_enemy_ooze_green
prefab_enemy_ooze_orange
prefab_enemy_ooze_purple
prefab_enemy_ooze_red
```

The clone controls more than appearance. It supplies the original prefab's
loot table, armor, movement flags, shadow structure, collider configuration,
death handlers, and any specialized components. The loader clears the clone's
native `AbilitySet` and installs the JSON abilities.

The game also contains `prefab_enemy_stunbubble` and
`prefab_enemy_stunbubble_toxic`, but those are ability-spawned helper enemies
and should be used as `spawnStunBubbles.projectile` values rather than general
custom-enemy clone templates.

### Damage type entries

```text
Normal
True
Fire
Frost
```

### Status-effect entries

```text
poison
frostPoison
burning
stunned
confused
slowed
```

The plugin currently constructs `slowed` with the base-game `0.3` skip-move
chance.

### Projectile prefab entries

These include the `MiscPrefabs/` resource prefix required by the native
projectile ability:

```text
MiscPrefabs/prefab_projectile_fireball
MiscPrefabs/prefab_projectile_ooze
MiscPrefabs/prefab_projectile_stinger
MiscPrefabs/prefab_projectile_stinger_frozen
```

The editor exposes all four through **Projectile prefab**. Choose
**Custom / modded resource** to enter a projectile prefab path registered by
another mod. The custom resource must already be available through Unity's
resource system when the pack is validated and loaded.

Stun-bubble helper prefab entries:

```text
prefab_enemy_stunbubble
prefab_enemy_stunbubble_toxic
```

### Enemy-combat sound entries

These sound names are used by native enemy abilities in this game build:

```text
sound_combat_ability_screech
sound_combat_ability_sermon
sound_combat_attack_bigpunch
sound_combat_attack_iceimpact
sound_combat_attack_ooze
sound_combat_attack_punch
sound_combat_attack_sizzle
sound_combat_attack_sword
sound_combat_attack_whispers
sound_interaction_rockcrumble
sound_statuseffect_burning
```

`sound` is only read by ability types that expose a sound parameter:
`basicAttack` and `projectile`. The other native ability types select their
sounds internally.

### Base-game ability loadout examples

The following examples mirror useful native loadouts and can be copied into an
enemy's `abilities` array.

Green ooze:

```json
[
  {
    "type": "basicAttack",
    "cooldown": 0,
    "minPower": 2,
    "maxPower": 4,
    "sound": "sound_combat_attack_ooze",
    "damageType": "Normal"
  },
  {
    "type": "projectile",
    "range": 6,
    "cooldown": 6,
    "minPower": 0,
    "maxPower": 1,
    "projectile": "MiscPrefabs/prefab_projectile_ooze",
    "statusEffect": "slowed",
    "statusDuration": 5,
    "sound": "sound_combat_attack_ooze",
    "damageType": "Normal"
  }
]
```

Giant bat:

```json
[
  {
    "type": "basicAttack",
    "cooldown": 0,
    "minPower": 3,
    "maxPower": 6,
    "sound": "sound_combat_attack_sword",
    "damageType": "Normal"
  },
  {
    "type": "screech",
    "range": 4,
    "cooldown": 6,
    "minPower": 0,
    "maxPower": 0
  }
]
```

Cultist:

```json
[
  {
    "type": "basicAttack",
    "cooldown": 0,
    "minPower": 8,
    "maxPower": 16,
    "sound": "sound_combat_attack_bigpunch",
    "damageType": "Normal"
  },
  {
    "type": "projectile",
    "range": 6,
    "cooldown": 10,
    "minPower": 0,
    "maxPower": 1,
    "projectile": "MiscPrefabs/prefab_projectile_fireball",
    "statusEffect": "burning",
    "statusDuration": 5,
    "sound": "sound_statuseffect_burning",
    "damageType": "Fire"
  },
  {
    "type": "sermon",
    "range": 6,
    "cooldown": 4,
    "minPower": 1,
    "maxPower": 1
  }
]
```

Frozen ooze:

```json
[
  {
    "type": "basicAttack",
    "cooldown": 0,
    "minPower": 3,
    "maxPower": 6,
    "sound": "sound_combat_attack_ooze",
    "damageType": "Normal"
  },
  {
    "type": "projectile",
    "range": 4,
    "cooldown": 2,
    "minPower": 3,
    "maxPower": 6,
    "projectile": "MiscPrefabs/prefab_projectile_stinger_frozen",
    "statusEffect": "stunned",
    "statusDuration": 1,
    "sound": "sound_combat_attack_iceimpact",
    "damageType": "Normal"
  }
]
```

## Save compatibility

Custom enemies are serialized by Silverpine under a stable generated prefab
name. Keep the defining pack installed whenever loading a save that contains
its enemies.

Version 1.3.11 treats each boss coordinate as a preferred origin and falls back
to the closest usable turf on the same world tile. It does not change saved
enemy prefab names. Existing packs and saves remain compatible. Boss audio
requires Modding Tools 1.7.1 for its shared audio registry, music-priority API,
and radial falloff controls.

Version 1.3.12 separates GLB authoring sources from distributable enemy packs.
Complete cached directional images now load automatically when a recipient has
no GLB. Existing packs with embedded GLBs remain supported and can be converted
with **Build Cache + Separate GLB**.

Version 1.4.2 explicitly sizes text boxes to their wrapped content at the width
allocated by the editor. Scene descriptions start with three lines of writing
space and grow for additional text, new paragraphs, and trailing blank lines.
Other long values wrap visually without adding newlines to IDs or asset paths.
Deleting text shrinks the field again; later controls move with its height.

Version 1.4.1 fixes the World preview selecting grass overgrowth instead of the
base grass ground tile. Only the exact base prefab and its Unity copies are used;
if unavailable, a plain ground reference is shown. Short action buttons now size
to their text, and wider field labels prevent split damage/rotation labels.
Long user-entered text continues to wrap within the editor.

Version 1.4.0 adopts ModdingTools 1.13.0's shared art API and adds the tabbed
editor, persistent world preview, and per-direction offsets. Existing JSON,
legacy authoring folders, cached PNGs, disabled markers, and saved prefab names
remain compatible. New caches record the source content hash and are staged as
a complete set before replacement. Cancelled/stale renders preserve the old
cache. Image and audio imports use content-addressed filenames to avoid
same-name overwrites. Editor-owned previews are released on close or disable.
Embedded GLBs are removed only after saving the new reference and checking that
no saved pack or recovery draft still uses them. Unsaved edits are guarded by a
Save/Discard/Keep editing prompt. Numeric inputs reject non-finite values, and
text fields grow only as needed for wrapping rather than filling spare height.

Version 1.3.7 registers each enemy's base, boss, and timed variants atomically
through Modding Tools' shared `SerializablePrefabs` registry. A pack either
registers all of its serializer prefabs or none of them. The loader no longer
reflects Silverpine's private prefab dictionary or patches serializer
instantiation itself.

Removing a pack causes Silverpine to log a missing-prefab message and skip
saved enemies from that pack. Reinstalling the same pack with the same
`packId` and enemy `id` restores the prefab mapping for future loads.

Avoid changing an enemy's clone prefab after releasing it. The clone determines
the collection and ordering of serializable components, so changing it can
produce component-count or missing-component warnings when older enemies load.

## Troubleshooting

Check `BepInEx/LogOutput.log` for messages from `Custom Enemy Loader`.

- **Clone prefab does not exist:** Verify `clone` exactly matches an entry in
  the clone list.
- **Clone prefab is not an enemy:** Do not use stun-bubble helper prefabs as
  the clone.
- **Exactly one visual source:** Remove extra `image`, `directionalImages`, or
  `model` fields.
- **GLB produces empty sprites:** Ensure the main scene has visible renderers
  and usable material textures.
- **GLB equipment is missing:** Version 1.1.5 renders the complete scene,
  including every mesh primitive. Delete an old `.cache` directory after
  upgrading if another tool copied a stale manifest into the pack.
- **GLB renders without its material colors:** Version 1.1.9 builds Standard
  PBR-compatible Unity materials and applies source `baseColorFactor`,
  base-color textures, metallic, roughness, opacity mode, and double-sided
  state. It also recovers source indices from fake-null built-player material
  slots. Its renderer-version change automatically invalidates colorless
  caches from earlier releases.
- **Enemy sprite is too small or large:** Set `visuals.scale`; `1` is normal,
  `2` doubles visual size, and `0.5` halves it without changing collision.
- **GLB faces the wrong direction:** Adjust the Y value of `rotation` in
  90-degree increments.
- **Old GLB image remains:** Restore or select the authoring GLB and use
  **Build Cache + Separate GLB** again. Recipients intentionally treat the
  distributed four-PNG cache as authoritative when no GLB is installed.
- **GLB is absent and distributed cache is incomplete:** The released pack
  must include all four files `down.png`, `left.png`, `up.png`, and `right.png`
  under its generated `.cache/<pack-and-enemy-id>/` directory.
- **`SubmergeVisualsHandler.GenerateSubmergedSprite` null error:** Version 1.1.3
  keeps independent runtime copies of GLB sprites and safely recovers a missing
  directional sprite for custom enemies. Restart Silverpine after updating the
  plugin.
- **Enemy never appears:** It only spawns when a new matching tile is generated;
  also check the biome spelling and that `max` is greater than zero.
- **Enemy has no attacks:** Confirm `abilities` is non-empty and ability names
  match the supported list.
- **Configured loot does not appear:** Check the item name or qualified ID in
  the log. For Custom Item Loader entries, keep that item pack installed and
  prefer the ID selected by **Choose item...** over a manually typed display
  name.
- **Could not find member:** The JSON contains an unsupported or misspelled
  field. Compare it with the field tables in this README.
- **Boss music does not play:** Install Modding Tools 1.7.1 or newer, confirm
  the file is inside the pack folder and uses a supported extension, then
  restart the game after saving the pack.

The included `example-pack.json` is an authoring template. Select its source
GLB in the editor and run **Build Cache + Separate GLB** before distributing or
installing it as a cache-only pack.

## Credits

Created by **Saelac and ChatGPT**.
