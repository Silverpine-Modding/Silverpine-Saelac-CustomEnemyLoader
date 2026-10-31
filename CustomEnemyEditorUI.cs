#nullable enable

using Newtonsoft.Json;
using Silverpine.ModdingTools;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SilverpineMods.CustomEnemyLoader;

internal sealed partial class CustomEnemyEditorUI : ModToolBehaviour
{
    private const float DesignWidth = 1920f;
    private const float DesignHeight = 1080f;
    private const float SidebarInputMaxWidth = 255f;
    private const float DetailsInputMaxWidth = 1040f;
    private const float AssetInputMaxWidth = 920f;
    private const float InlineNumberInputMaxWidth = 260f;
    private const float FieldLabelWidth = 230f;

    private static readonly string[] ClonePrefabs =
    {
        "prefab_enemy_animal_bear_brown",
        "prefab_enemy_animal_serpent_ice",
        "prefab_enemy_avatar",
        "prefab_enemy_cultist",
        "prefab_enemy_elemental_ice",
        "prefab_enemy_elemental_ice_fleeting",
        "prefab_enemy_elemental_ice_lesser",
        "prefab_enemy_firefly",
        "prefab_enemy_giantbat",
        "prefab_enemy_giantfrog",
        "prefab_enemy_giantfrog_swamp",
        "prefab_enemy_giantrat",
        "prefab_enemy_giantspider",
        "prefab_enemy_golem_iron",
        "prefab_enemy_golem_rock",
        "prefab_enemy_ooze_black",
        "prefab_enemy_ooze_black_cult",
        "prefab_enemy_ooze_black_sentinel",
        "prefab_enemy_ooze_blue",
        "prefab_enemy_ooze_frozen",
        "prefab_enemy_ooze_green",
        "prefab_enemy_ooze_orange",
        "prefab_enemy_ooze_purple",
        "prefab_enemy_ooze_red"
    };

    private static readonly string[] AbilityTypes =
    {
        "basicAttack",
        "projectile",
        "applyStatusEffect",
        "harmSanity",
        "ice",
        "lines",
        "screech",
        "sermon",
        "spawnStunBubbles",
        "avatar"
    };

    private static readonly string[] DamageTypes =
        { "Normal", "True", "Fire", "Frost" };
    private static readonly string[] StatusEffects =
        { "None", "poison", "frostPoison", "burning", "stunned", "confused", "slowed" };
    private const string CustomProjectileOption = "Custom / modded resource";
    private static readonly string[] ProjectileChoices =
    {
        "MiscPrefabs/prefab_projectile_fireball",
        "MiscPrefabs/prefab_projectile_ooze",
        "MiscPrefabs/prefab_projectile_stinger",
        "MiscPrefabs/prefab_projectile_stinger_frozen",
        CustomProjectileOption
    };
    private static readonly string[] OozeProjectileColors =
        { "Green", "Blue", "Orange", "Purple", "Red" };
    private static readonly string[] FleeBehaviors =
        { "ProximityAndDamage", "WhenAttacked", "Never" };
    private static readonly string[] LootModes = { "Replace", "AddToClone" };
    private static readonly string[] LootTiers =
    {
        "Automatic",
        "Normal",
        "Quality",
        "Superior",
        "Exceptional",
        "Pristine",
        "Blessed",
        "Legendary"
    };
    private static readonly string[] Biomes =
    {
        "Forest",
        "DeepForest",
        "Mountain",
        "HighMountain",
        "Swamp",
        "FloodedCaverns",
        "FrozenFortress",
        "SwampTemple",
        "Dungeon",
        "Silverpine"
    };

    private static CustomEnemyEditorUI? instance;

    private readonly List<PackDocument> documents = new();
    private readonly Dictionary<string, string> numberText = new();
    private readonly List<UnityEngine.Object> ownedPreviewObjects = new();
    private readonly Dictionary<string, bool> expandedSections = new()
    {
        ["general"] = true,
        ["visuals"] = true,
        ["abilities"] = true,
        ["loot"] = false,
        ["spawns"] = true,
        ["bosses"] = false,
        ["timed"] = false
    };
    private PackDocument? document;
    private int selectedEnemy;
    private string status = "";
    private Vector2 fileScroll;
    private Vector2 enemyScroll;
    private Vector2 detailsScroll;
    private Sprite[] previewSprites = Array.Empty<Sprite>();
    private bool open;
    private bool generatingPreview;
    private bool pickerOpen;
    private GUIStyle? wrappedInputStyle;
    private readonly List<float> wrappedInputWidths = new();
    private int wrappedInputIndex;

    private sealed class PackDocument
    {
        internal string Path = "";
        internal EnemyPackDefinition Pack = new();
        internal readonly Dictionary<string, string> PendingModelSeparations = new(StringComparer.OrdinalIgnoreCase);
        internal bool IsDraft =>
            Path.EndsWith(".enemy-draft", StringComparison.OrdinalIgnoreCase);
        internal bool IsEnabled =>
            !IsDraft && !EnemyPackLoader.IsPackDisabled(Path);
    }

    internal static void Open(ModToolSession session)
    {
        try
        {
            if (instance == null)
            {
                GameObject root = new("Custom Enemy Editor IMGUI");
                instance = root.AddComponent<CustomEnemyEditorUI>();
            }
            else
            {
                instance.gameObject.SetActive(true);
            }

            instance.AttachSession(session);
            session.EscapeHandler = instance.HandleEscape;
            instance.open = true;
            instance.terrainSprite = null;
            instance.terrainLookedUp = false;
            instance.RefreshDocuments();
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError("Custom Enemy Editor could not open: " + exception);
            if (instance != null)
            {
                instance.open = false;
                instance.pickerOpen = false;
                instance.ClearPreview();
                instance.ReleaseSession();
                instance.gameObject.SetActive(false);
            }
            session.Close();
        }
    }

    private void Update()
    {
        if (!open)
            return;

        if (pickerOpen)
        {
            if (GenericListUI.Instance == null ||
                !GenericListUI.Instance.gameObject.activeSelf)
                pickerOpen = false;
        }
        if (Input.GetKeyDown(KeyCode.Escape)) HandleEscape();
    }

    private void OnGUI()
    {
        if (!open || pickerOpen)
            return;

        using ModGuiScope scope = ModGui.BeginScaled(DesignWidth, DesignHeight);
        GUI.enabled = true;
        GUI.color = Color.white;
        GUI.backgroundColor = Color.white;
        GUI.depth = -1000;

        EnsureEditorSkin();
        wrappedInputIndex = 0;
        GUISkin previousSkin = GUI.skin;
        GUI.skin = editorSkin;
        try
        {
            Color previous = GUI.color;
            GUI.color = new Color(0.055f, 0.075f, 0.095f, 0.99f);
            GUI.DrawTexture(new Rect(0, 0, DesignWidth, DesignHeight), Texture2D.whiteTexture);
            GUI.color = previous;

            GUILayout.BeginArea(new Rect(12, 10, DesignWidth - 24, DesignHeight - 20));
            DrawHeader();
            if (pendingAction != null) DrawUnsavedPrompt();
            else
            {
                GUILayout.BeginHorizontal(GUILayout.Height(880));
                DrawLibrarySidebar();
                if (assetLibraryOpen) DrawArtLibrary();
                else { DrawDetailsPanel(); DrawPersistentPreview(); }
                GUILayout.EndHorizontal();
            }
            if (!string.IsNullOrWhiteSpace(status))
            {
                statusScroll = GUILayout.BeginScrollView(statusScroll, GUILayout.MaxHeight(80));
                GUILayout.Label(status, GUI.skin!.box);
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }
        finally { GUI.skin = previousSkin; }
    }

    private void DrawHeader()
    {
        GUILayout.BeginHorizontal(GUI.skin.box);
        GUILayout.Label("Custom Enemy Editor", GUILayout.Width(220));
        GUILayout.Label(document == null
            ? "Create or select an enemy pack."
            : (string.IsNullOrWhiteSpace(document.Path)
                ? "New unsaved pack"
                : Path.GetFileName(document.Path)));
        if (ActionButton("Asset library", 150))
        { OpenArtLibrary(); GUIUtility.ExitGUI(); }
        GUI.enabled = document != null && !generatingPreview;
        if (ActionButton("Save Pack", 130))
        { SaveCurrent(); GUIUtility.ExitGUI(); }
        GUI.enabled = true;
        if (ActionButton("Back to Mods", 150))
        { RequestAction(Close); GUIUtility.ExitGUI(); }
        GUILayout.EndHorizontal();
    }


    private void DrawDetailsPanel()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(DesignWidth - 24 - 290 - 420 - 20));
        EnemyDefinition? enemy = CurrentEnemy;
        if (enemy == null)
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("No enemy selected.");
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            return;
        }

        GUILayout.Label(enemy.name ?? "Unnamed enemy", editorHeading!);
        GUILayout.Label(enemy.id ?? "", editorMuted!);
        int nextTab = GUILayout.Toolbar(editorTab, new[] { "Enemy", "Appearance", "Combat", "Spawning" });
        if (nextTab != editorTab)
        { CancelOffsetDrag(); editorTab = nextTab; detailsScroll = Vector2.zero; GUIUtility.ExitGUI(); }
        GUILayout.Space(14);
        detailsScroll = GUILayout.BeginScrollView(detailsScroll);
        if (editorTab == 0 && DrawSectionHeader("general", "Identity and behavior"))
            DrawGeneral(enemy);
        if (editorTab == 1 && DrawSectionHeader("visuals", "Appearance and authoring"))
            DrawVisuals(enemy);
        if (editorTab == 2 && DrawSectionHeader(
                "abilities", $"Abilities ({enemy.abilities?.Count ?? 0})"))
            DrawAbilities(enemy);
        if (editorTab == 2 && DrawSectionHeader(
                "loot", enemy.loot == null ? "Loot (clone default)" : "Loot (custom)"))
            DrawLoot(enemy);
        if (editorTab == 3 && DrawSectionHeader(
                "spawns", $"World spawns ({enemy.spawns?.Count ?? 0})"))
            DrawSpawns(enemy);
        if (editorTab == 3 && DrawSectionHeader(
                "bosses", $"Coordinate bosses ({enemy.bossSpawns?.Count ?? 0})"))
            DrawBossSpawns(enemy);
        if (editorTab == 3 && DrawSectionHeader(
                "timed",
                $"Timed Silverpine outskirts ({enemy.silverpineTimedSpawns?.Count ?? 0})"))
            DrawSilverpineTimedSpawns(enemy);
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private bool DrawSectionHeader(string key, string label)
    {
        bool expanded = expandedSections.TryGetValue(key, out bool value) && value;
        Color previous = GUI.backgroundColor;
        if (expanded)
            GUI.backgroundColor = new Color(0.45f, 0.75f, 0.9f);
        if (GUILayout.Button(
                (expanded ? "▼  " : "▶  ") + label,
                GUI.skin.box))
        {
            expanded = !expanded;
            expandedSections[key] = expanded;
        }
        GUI.backgroundColor = previous;
        return expanded;
    }

    private void DrawGeneral(EnemyDefinition enemy)
    {
        GUILayout.BeginVertical(GUI.skin.box);
        DrawText("Enemy ID", enemy.id ?? "", value => enemy.id = value);
        DrawText("Health-bar name", enemy.name ?? "", value => enemy.name = value);
        DrawText("LLM scene description", enemy.description ?? "",
            value => enemy.description = value,
            allowNewlines: true);
        DrawChoice("Clone prefab", enemy.clone ?? ClonePrefabs[0], ClonePrefabs,
            value => enemy.clone = value);
        DrawInt("Maximum HP", "enemy.maxHp", enemy.maxHp, value => enemy.maxHp = value);
        enemy.passive = GUILayout.Toggle(enemy.passive, "Passive (wanders without attacking)");
        if (enemy.passive)
        {
            DrawChoice("Flee behavior", enemy.fleeBehavior, FleeBehaviors,
                value => enemy.fleeBehavior = value);
            GUILayout.Label(
                "ProximityAndDamage flees from nearby players and damage; " +
                "WhenAttacked flees only after damage; Never keeps wandering.");
        }

        bool overrideSpeed = enemy.pathfinding != null;
        bool nextOverrideSpeed = GUILayout.Toggle(
            overrideSpeed, "Override clone pathfinding speed bonus");
        if (nextOverrideSpeed && enemy.pathfinding == null)
            enemy.pathfinding = new PathfindingDefinition();
        else if (!nextOverrideSpeed && enemy.pathfinding != null)
            enemy.pathfinding = null;
        if (enemy.pathfinding != null)
        {
            DrawFloat("Speed bonus (0-1)", "enemy.pathfinding.speedBonus",
                enemy.pathfinding.speedBonus,
                value => enemy.pathfinding.speedBonus = value);
            GUILayout.Label(
                "Chance per turn to take one extra pathfinding step (1 = always). " +
                "Disable the override to retain the clone's value.");
        }
        GUILayout.EndVertical();
    }

    private void DrawVisuals(EnemyDefinition enemy)
    {
        enemy.visuals ??= new VisualDefinition { image = "" };
        VisualDefinition visuals = enemy.visuals;
        GUILayout.BeginVertical(GUI.skin.box);

        int currentMode = !string.IsNullOrWhiteSpace(visuals.artAssetId) ? 3 : visuals.model != null
            ? 2
            : visuals.directionalImages != null ? 1 : 0;
        int nextMode = GUILayout.Toolbar(
            currentMode, new[] { "Single Image", "4 Images", "GLB Model", "Cached art" });
        if (nextMode != currentMode)
        {
            if (nextMode == 3) { OpenArtLibrary(); GUIUtility.ExitGUI(); return; }
            CancelOffsetDrag();
            SetVisualMode(visuals, nextMode);
            RefreshPreview();
            GUIUtility.ExitGUI();
        }

        DrawFloat(
            "In-game sprite scale",
            "visual.scale",
            visuals.scale,
            value => { if (value >= .1f && value <= 10f) visuals.scale = value; });
        GUILayout.Label(
            "1 = normal size; 2 = twice as large. Collision is unchanged.");

        bool customPivot = visuals.pivot is { Length: 2 };
        bool nextCustomPivot = GUILayout.Toggle(
            customPivot, "Override tile-edge sprite anchor");
        if (nextCustomPivot && !customPivot)
        {
            visuals.pivot = new[] { 0.5f, 0f };
            numberText.Remove("visual.pivot.x");
            numberText.Remove("visual.pivot.y");
        }
        else if (!nextCustomPivot && customPivot)
        {
            visuals.pivot = null;
        }
        if (visuals.pivot is { Length: 2 })
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Origin X / Y", GUILayout.Width(FieldLabelWidth));
            DrawFloatInline(
                "visual.pivot.x",
                visuals.pivot[0],
                value => visuals.pivot[0] = Mathf.Clamp01(value));
            DrawFloatInline(
                "visual.pivot.y",
                visuals.pivot[1],
                value => visuals.pivot[1] = Mathf.Clamp01(value));
            GUILayout.EndHorizontal();
        }
        GUILayout.Label(
            "Default keeps the art's bottom-middle on the occupied tile's lower edge.");

        if (nextMode == 0)
        {
            DrawAssetPath("Image", visuals.image ?? "",
                value => visuals.image = value,
                path => BrowseAsset(
                    path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase),
                    value => visuals.image = value));
        }
        else if (nextMode == 1)
        {
            visuals.directionalImages ??= new DirectionalImages();
            DirectionalImages images = visuals.directionalImages;
            DrawDirectionalPath("Down", images.down ?? "", value => images.down = value);
            DrawDirectionalPath("Left", images.left ?? "", value => images.left = value);
            DrawDirectionalPath("Up", images.up ?? "", value => images.up = value);
            DrawDirectionalPath("Right", images.right ?? "", value => images.right = value);
        }
        else if (nextMode == 3)
        {
            GUILayout.Label(visuals.artOriginalName ?? "Cached art", editorHeading!);
            GUILayout.Label("Images are included in this pack. Recipients do not need the original model or your shared library.");
            if (GUILayout.Button("Choose cached art")) { OpenArtLibrary(); GUIUtility.ExitGUI(); }
        }
        else
        {
            DrawAssetPath("GLB model", visuals.model ?? "",
                value => visuals.model = value,
                path => BrowseGlbAuthoringSource(enemy, visuals));
            if (!string.IsNullOrWhiteSpace(visuals.model))
                GUILayout.Label(
                    "Authoring source is stored outside the distributable pack under " +
                    "BepInEx/shared/ArtLibrary/models. Legacy authoring folders remain readable.");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Rotation X / Y / Z", GUILayout.Width(FieldLabelWidth));
            visuals.rotation ??= new[] { 0f, 0f, 0f };
            for (int axis = 0; axis < 3; axis++)
            {
                int captured = axis;
                DrawFloatInline(
                    $"visual.rotation.{axis}",
                    visuals.rotation[axis],
                    value => visuals.rotation[captured] = value);
            }
            GUILayout.EndHorizontal();
            DrawFloat(
                "Alpha cutoff",
                "visual.alpha",
                visuals.alphaCutoff,
                value => visuals.alphaCutoff = value);
            visuals.cache = GUILayout.Toggle(visuals.cache, "Cache generated sprites");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rotate Y -90°"))
            {
                visuals.rotation[1] -= 90f;
                numberText.Remove("visual.rotation.1");
            }
            GUI.enabled = !generatingPreview;
            if (GUILayout.Button(
                    generatingPreview
                        ? "Building…"
                        : "Build Cache + Separate GLB"))
                GenerateGlbPreview();
            GUI.enabled = true;
            if (GUILayout.Button("Rotate Y +90°"))
            {
                visuals.rotation[1] += 90f;
                numberText.Remove("visual.rotation.1");
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh Preview"))
            RefreshPreview();
        if (GUILayout.Button("Clear Preview"))
            ClearPreview();
        GUILayout.EndHorizontal();
        if (GUILayout.Button("Add current art to shared library")) PublishCurrentArt();
        GUILayout.EndVertical();
    }

    private void DrawDirectionalPath(
        string label,
        string value,
        Action<string> assign)
    {
        DrawAssetPath(label, value, assign,
            ignored => BrowseAsset(
                path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase),
                assign));
    }

    private void DrawAssetPath(
        string label,
        string value,
        Action<string> assign,
        Action<string> browse)
    {
        GUILayout.BeginVertical();
        GUILayout.Label(label);
        GUILayout.BeginHorizontal();
        string next = DrawWrappedInput(value, AssetInputMaxWidth);
        if (next != value)
            assign(next);
        if (ActionButton("Browse", 100))
            browse(value);
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }


    private void DrawAbilities(EnemyDefinition enemy)
    {
        enemy.abilities ??= new List<AbilityDefinition>();
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Entries ({enemy.abilities.Count})");
        if (ActionButton("Add Ability", 110))
        {
            enemy.abilities.Add(NewBasicAttack());
            numberText.Clear();
        }
        GUILayout.EndHorizontal();

        for (int i = 0; i < enemy.abilities.Count; i++)
        {
            AbilityDefinition ability = enemy.abilities[i];
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            CompactLabel($"#{i + 1}", 28);
            DrawChoiceInline(
                ability.type ?? AbilityTypes[0],
                AbilityTypes,
                value =>
                {
                    ability.type = value;
                    ApplyAbilityDefaults(ability);
                    numberText.Clear();
                });
            if (ActionButton("↑", 32) && i > 0)
            {
                Swap(enemy.abilities, i, i - 1);
                numberText.Clear();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                break;
            }
            if (ActionButton("↓", 32) && i + 1 < enemy.abilities.Count)
            {
                Swap(enemy.abilities, i, i + 1);
                numberText.Clear();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                break;
            }
            if (ActionButton("Remove", 70))
            {
                enemy.abilities.RemoveAt(i);
                numberText.Clear();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                break;
            }
            GUILayout.EndHorizontal();

            DrawAbilityFields(ability, i);
            GUILayout.EndVertical();
        }
        GUILayout.Label("Abilities are attempted from bottom to top; place highest priority last.");
    }

    private void DrawAbilityFields(AbilityDefinition ability, int index)
    {
        string type = AbilityFactory.Normalize(ability.type);
        string key = "ability." + index + ".";

        if (type != "ice")
        {
            DrawInt("Cooldown", key + "cooldown", ability.cooldown,
                value => ability.cooldown = value);
        }

        if (new[]
            {
                "basicattack", "projectile", "applystatuseffect", "harmsanity",
                "lines", "spawnstunbubbles"
            }.Contains(type))
        {
            string unit = type == "applystatuseffect" ? "turns" : type == "spawnstunbubbles" ? "bubbles" :
                type == "harmsanity" ? "sanity loss" : "damage";
            DrawInt("Minimum " + unit, key + "min", ability.minPower,
                value => ability.minPower = value);
            DrawInt("Maximum " + unit, key + "max", ability.maxPower,
                value => ability.maxPower = value);
        }

        if (new[]
            {
                "projectile", "ice", "lines", "screech", "sermon",
                "spawnstunbubbles", "avatar"
            }.Contains(type))
        {
            DrawFloat("Range", key + "range", ability.range,
                value => ability.range = value);
        }

        if (type == "ice")
        {
            DrawInt("Cooldown", key + "cooldown", ability.cooldown,
                value => ability.cooldown = value);
            DrawFloat("Chance", key + "chance", ability.chance,
                value => ability.chance = value);
        }

        if (type is "basicattack" or "projectile")
        {
            DrawChoice("Damage type", ability.damageType ?? "Normal", DamageTypes,
                value => ability.damageType = value);
            DrawAssetPath(
                "Sound resource / OGG",
                ability.sound ?? "",
                value => ability.sound = value,
                ignored => BrowseAsset(
                    path => path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase),
                    value => ability.sound = value));
            GUILayout.Label(
                "Use a built-in SFX name, or Browse to copy a pack-relative .ogg file.");
        }

        if (type == "projectile")
        {
            string currentProjectile = ability.projectile ??
                                       "MiscPrefabs/prefab_projectile_ooze";
            string selectedProjectile = ProjectileChoices
                .Take(ProjectileChoices.Length - 1)
                .Any(value => value.Equals(
                    currentProjectile, StringComparison.OrdinalIgnoreCase))
                    ? currentProjectile
                    : CustomProjectileOption;
            DrawChoice(
                "Projectile prefab",
                selectedProjectile,
                ProjectileChoices,
                value =>
                {
                    ability.projectile = value == CustomProjectileOption ? "" : value;
                    if (!value.Equals(
                            "MiscPrefabs/prefab_projectile_ooze",
                            StringComparison.OrdinalIgnoreCase))
                        ability.oozeProjectileColor = null;
                });
            if (selectedProjectile == CustomProjectileOption)
                DrawText("Custom resource", currentProjectile,
                    value => ability.projectile = value);
            GUILayout.Label(
                "Select a native projectile, or use Custom for a prefab added by another mod.");
            if (currentProjectile.Equals(
                    "MiscPrefabs/prefab_projectile_ooze",
                    StringComparison.OrdinalIgnoreCase))
            {
                DrawChoice(
                    "Ooze color",
                    ability.oozeProjectileColor ?? "Green",
                    OozeProjectileColors,
                    value => ability.oozeProjectileColor = value);
                GUILayout.Label(
                    "Uses Silverpine's native colored ooze projectile sprites.");
            }
            DrawAssetPath(
                "Projectile PNG",
                ability.projectileImage ?? "",
                value => ability.projectileImage =
                    string.IsNullOrWhiteSpace(value) ? null : value,
                ignored => BrowseAsset(
                    path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase),
                    value => ability.projectileImage = value));
            GUILayout.Label(
                "Optional: replaces the selected prefab's visible sprite; blank keeps its native art.");
        }
        else if (type == "spawnstunbubbles")
        {
            DrawText("Bubble prefab", ability.projectile ?? "",
                value => ability.projectile = value);
        }

        if (type is "projectile" or "applystatuseffect")
        {
            string current = string.IsNullOrWhiteSpace(ability.statusEffect)
                ? "None"
                : ability.statusEffect!;
            string[] choices = type == "applystatuseffect"
                ? StatusEffects.Skip(1).ToArray()
                : StatusEffects;
            if (type == "applystatuseffect" && current == "None")
                current = "poison";
            DrawChoice("Status effect", current, choices,
                value => ability.statusEffect = value == "None" ? null : value);
        }

        if (type == "projectile" && !string.IsNullOrWhiteSpace(ability.statusEffect))
        {
            DrawInt("Status duration", key + "duration", ability.statusDuration,
                value => ability.statusDuration = value);
        }
    }

    private void DrawSpawns(EnemyDefinition enemy)
    {
        enemy.spawns ??= new List<SpawnDefinition>();
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Rules ({enemy.spawns.Count})");
        if (ActionButton("Add Spawn Rule", 130))
        {
            enemy.spawns.Add(new SpawnDefinition
                { biome = "Forest", min = 1, max = 1 });
            numberText.Clear();
        }
        GUILayout.EndHorizontal();

        for (int i = 0; i < enemy.spawns.Count; i++)
        {
            SpawnDefinition spawn = enemy.spawns[i];
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            CompactLabel($"Rule #{i + 1}", 65);
            DrawChoiceInline(spawn.biome ?? "Forest", Biomes,
                value => spawn.biome = value);
            if (ActionButton("Remove", 70))
            {
                enemy.spawns.RemoveAt(i);
                numberText.Clear();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                break;
            }
            GUILayout.EndHorizontal();
            DrawInt("Minimum per tile", $"spawn.{i}.min", spawn.min,
                value => spawn.min = value);
            DrawInt("Maximum per tile", $"spawn.{i}.max", spawn.max,
                value => spawn.max = value);
            GUILayout.EndVertical();
        }
    }

    private void DrawBossSpawns(EnemyDefinition enemy)
    {
        enemy.bossSpawns ??= new List<BossSpawnDefinition>();
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Entries ({enemy.bossSpawns.Count})");
        if (ActionButton("Add Boss Spawn", 130))
        {
            enemy.bossSpawns.Add(new BossSpawnDefinition
            {
                id = "boss_" + (enemy.bossSpawns.Count + 1),
                x = 0,
                y = 0,
                respawnAfterDeath = false
            });
            numberText.Clear();
        }
        GUILayout.EndHorizontal();

        for (int i = 0; i < enemy.bossSpawns.Count; i++)
        {
            BossSpawnDefinition boss = enemy.bossSpawns[i];
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            CompactLabel($"Boss #{i + 1}", 65);
            if (ActionButton("Remove", 70))
            {
                enemy.bossSpawns.RemoveAt(i);
                numberText.Clear();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                break;
            }
            GUILayout.EndHorizontal();
            DrawText("Stable spawn ID", boss.id ?? "", value => boss.id = value);
            DrawInt("World X", $"boss.{i}.x", boss.x, value => boss.x = value);
            DrawInt("World Y", $"boss.{i}.y", boss.y, value => boss.y = value);
            boss.respawnAfterDeath = GUILayout.Toggle(
                boss.respawnAfterDeath, "Respawn after death when the tile regenerates");

            bool hasBossMusic = boss.bossMusic != null;
            bool enableBossMusic = GUILayout.Toggle(
                hasBossMusic,
                "Play custom encounter music while the player is on this boss's tile");
            if (enableBossMusic != hasBossMusic)
            {
                boss.bossMusic = enableBossMusic
                    ? new BossMusicDefinition()
                    : null;
                numberText.Clear();
            }
            if (boss.bossMusic != null)
            {
                BossMusicDefinition music = boss.bossMusic;
                DrawAssetPath(
                    "Boss music file (OGG/WAV/MP3/AIFF)",
                    music.file ?? "",
                    value => music.file = value,
                    ignored => BrowseAsset(
                        path => CustomEnemyBossAudio.IsSupportedPath(path),
                        value => music.file = value));
                DrawFloat(
                    "Music volume (0-1)",
                    $"boss.{i}.music.volume",
                    music.volume,
                    value => music.volume = value);
                DrawInt(
                    "Music priority",
                    $"boss.{i}.music.priority",
                    music.priority,
                    value => music.priority = value);
                DrawFloat(
                    "Fade seconds",
                    $"boss.{i}.music.fade",
                    music.fadeSeconds,
                    value => music.fadeSeconds = value);
            }

            bool hasDeathSong = boss.deathSong != null;
            bool enableDeathSong = GUILayout.Toggle(
                hasDeathSong,
                "Play a full positional song near the boss's death");
            if (enableDeathSong != hasDeathSong)
            {
                boss.deathSong = enableDeathSong
                    ? new BossDeathSongDefinition()
                    : null;
                numberText.Clear();
            }
            if (boss.deathSong != null)
            {
                BossDeathSongDefinition deathSong = boss.deathSong;
                DrawAssetPath(
                    "Death song file (OGG/WAV/MP3/AIFF)",
                    deathSong.file ?? "",
                    value => deathSong.file = value,
                    ignored => BrowseAsset(
                        path => CustomEnemyBossAudio.IsSupportedPath(path),
                        value => deathSong.file = value));
                DrawFloat(
                    "Death song volume (0-1)",
                    $"boss.{i}.death.volume",
                    deathSong.volume,
                    value => deathSong.volume = value);
                DrawFloat(
                    "Full-volume radius",
                    $"boss.{i}.death.minRange",
                    deathSong.minRange,
                    value => deathSong.minRange = value);
                DrawFloat(
                    "Silent beyond radius",
                    $"boss.{i}.death.maxRange",
                    deathSong.maxRange,
                    value => deathSong.maxRange = value);
                GUILayout.Label(
                    "Ranges are measured in turfs from the corpse. At 0, " +
                    "the song begins fading immediately away from the corpse.");
            }

            int tileX = Mathf.FloorToInt((boss.x + 50) / 100f);
            int tileY = Mathf.FloorToInt((boss.y + 50) / 100f);
            GUILayout.Label(
                $"World tile ({tileX}, {tileY}); local turf " +
                $"({boss.x - (tileX * 100 - 50)}, {boss.y - (tileY * 100 - 50)}). " +
                "This is the preferred turf. If unavailable, the closest " +
                "passable, unoccupied turf on this world tile is used.");
            GUILayout.EndVertical();
        }
        GUILayout.Label(
            "With respawning disabled, a defeat is stored in the save and survives " +
            "Silverpine's daily inactive-tile regeneration. Encounter music loops " +
            "only on the boss's world tile and stops at death; a death song is " +
            "positional and continues to its natural end.");
    }

    private void DrawSilverpineTimedSpawns(EnemyDefinition enemy)
    {
        enemy.silverpineTimedSpawns ??= new List<SilverpineTimedSpawnDefinition>();
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Rules ({enemy.silverpineTimedSpawns.Count})");
        if (ActionButton("Add Timed Rule", 130))
        {
            enemy.silverpineTimedSpawns.Add(new SilverpineTimedSpawnDefinition
            {
                id = "night_outskirts_" + (enemy.silverpineTimedSpawns.Count + 1),
                startHour = 20,
                endHour = 5,
                min = 1,
                max = 1,
                edgeDepth = 8
            });
            numberText.Clear();
        }
        GUILayout.EndHorizontal();

        for (int i = 0; i < enemy.silverpineTimedSpawns.Count; i++)
        {
            SilverpineTimedSpawnDefinition timed = enemy.silverpineTimedSpawns[i];
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            CompactLabel($"Rule #{i + 1}", 65);
            if (ActionButton("Remove", 70))
            {
                enemy.silverpineTimedSpawns.RemoveAt(i);
                numberText.Clear();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                break;
            }
            GUILayout.EndHorizontal();
            DrawText("Stable rule ID", timed.id ?? "", value => timed.id = value);
            DrawInt("Start hour (0-23)", $"timed.{i}.start", timed.startHour,
                value => timed.startHour = value);
            DrawInt("End hour (exclusive)", $"timed.{i}.end", timed.endHour,
                value => timed.endHour = value);
            DrawInt("Minimum enemies", $"timed.{i}.min", timed.min,
                value => timed.min = value);
            DrawInt("Maximum enemies", $"timed.{i}.max", timed.max,
                value => timed.max = value);
            DrawInt("Outskirts edge depth", $"timed.{i}.edge", timed.edgeDepth,
                value => timed.edgeDepth = value);
            GUILayout.Label(
                "Overnight windows are supported (for example 20 to 5). " +
                "Enemies spawn once per daily window and are removed when it ends.");
            GUILayout.EndVertical();
        }
    }

    private void DrawLoot(EnemyDefinition enemy)
    {
        bool configured = enemy.loot != null;
        bool nextConfigured = GUILayout.Toggle(configured, "Configure custom loot pool");
        if (nextConfigured && enemy.loot == null)
            enemy.loot = new LootDefinition();
        else if (!nextConfigured && enemy.loot != null)
            enemy.loot = null;
        if (enemy.loot == null)
        {
            GUILayout.Label("Disabled: the selected clone's original loot is unchanged.");
            return;
        }

        LootDefinition loot = enemy.loot;
        loot.entries ??= new List<LootEntryDefinition>();
        DrawChoice("Loot mode", loot.mode, LootModes, value => loot.mode = value);
        GUILayout.Label(
            loot.mode.Equals("AddToClone", StringComparison.OrdinalIgnoreCase)
                ? "Adds these rolls after the clone's original loot drops."
                : "Replaces the clone's original loot; an empty pool drops nothing.");
        DrawInt("Minimum rolls", "loot.min", loot.min, value => loot.min = value);
        DrawInt("Maximum rolls", "loot.max", loot.max, value => loot.max = value);

        GUILayout.BeginHorizontal();
        GUILayout.Label($"Loot entries ({loot.entries.Count})");
        if (ActionButton("Add Loot Entry", 120))
        {
            loot.entries.Add(new LootEntryDefinition
                { item = "Pile of Gold", weight = 1f });
            if (loot.max == 0)
                loot.max = loot.min = 1;
            numberText.Clear();
        }
        GUILayout.EndHorizontal();

        for (int i = 0; i < loot.entries.Count; i++)
        {
            LootEntryDefinition entry = loot.entries[i];
            if (string.IsNullOrWhiteSpace(entry.tier) &&
                CustomEnemyItemResolver.TryGetLegacyQuality(
                    entry.item, out Quality legacyQuality))
            {
                entry.tier = legacyQuality.ToString();
                entry.item = CustomEnemyItemResolver.StripLegacyQuality(entry.item);
            }
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            CompactLabel($"#{i + 1}", 28);
            GUILayout.FlexibleSpace();
            if (ActionButton("Choose item...", 105))
                OpenLootItemPicker(entry);
            if (ActionButton("Remove", 70))
            {
                loot.entries.RemoveAt(i);
                numberText.Clear();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                break;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("Item name / qualified ID");
            entry.item = DrawWrappedInput(
                entry.item ?? "",
                DetailsInputMaxWidth);
            DrawChoice(
                "Item tier",
                entry.tier ?? "Automatic",
                LootTiers,
                value => entry.tier = value == "Automatic" ? null : value);
            DrawFloat("Selection weight", $"loot.{i}.weight", entry.weight,
                value => entry.weight = value);
            GUILayout.EndVertical();
        }
        GUILayout.Label(
            "The picker includes base items, ItemLibrary additions from other mods, " +
            "and stable qualified IDs from Custom Item Loader.");
        GUILayout.Label(
            "Automatic keeps Silverpine's normal randomized quality behavior. " +
            "A named tier forces that exact quality when the item supports it.");
    }

    private void OpenLootItemPicker(LootEntryDefinition entry)
    {
        try
        {
            if (GenericListUI.Instance == null)
                throw new InvalidOperationException(
                    "Silverpine's list picker has not initialized.");

            IReadOnlyList<LootItemChoice> choices =
                CustomEnemyItemResolver.GetItemChoices();
            pickerOpen = true;
            ShowLootPickerRoot(entry, choices);
            if (!GenericListUI.Instance.gameObject.activeSelf)
            {
                pickerOpen = false;
                status = "Silverpine's item picker could not be opened.";
            }
        }
        catch (Exception exception)
        {
            pickerOpen = false;
            status = "Item picker failed: " + exception.Message;
            Plugin.Log.LogError("Enemy editor item picker failed: " + exception);
        }
    }

    private void ShowLootPickerRoot(
        LootEntryDefinition entry,
        IReadOnlyList<LootItemChoice> choices)
    {
        var rows = new List<ListUIItem> { CreateLootCancelRow() };
        foreach (IGrouping<ItemCategory, LootItemChoice> category in
                 choices.Where(choice => choice.CustomPackId == null)
                     .GroupBy(choice => choice.Template.itemCategory)
                     .OrderBy(group => group.Key.ToString()))
        {
            List<LootItemChoice> categoryChoices = category
                .OrderBy(choice => choice.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            rows.Add(new ListUIItem_Generic(
                category.Key.ToString(),
                categoryChoices[0].Template.Sprite,
                $"{categoryChoices.Count} item(s) →",
                () => ShowLootItemLeaves(
                    entry,
                    categoryChoices,
                    () => ShowLootPickerRoot(entry, choices))));
        }

        List<LootItemChoice> customChoices = choices
            .Where(choice => choice.CustomPackId != null)
            .ToList();
        if (customChoices.Count > 0)
        {
            rows.Insert(1, new ListUIItem_Generic(
                "Custom Item Loader packs",
                customChoices[0].Template.Sprite,
                $"{customChoices.Select(choice => choice.CustomPackId).Distinct().Count()} pack(s) →",
                () => ShowCustomItemPacks(entry, choices, customChoices)));
        }
        ShowLootPickerRows(rows);
    }

    private void ShowCustomItemPacks(
        LootEntryDefinition entry,
        IReadOnlyList<LootItemChoice> allChoices,
        IReadOnlyList<LootItemChoice> customChoices)
    {
        var rows = new List<ListUIItem>
        {
            CreateLootBackRow(() => ShowLootPickerRoot(entry, allChoices))
        };
        foreach (IGrouping<string?, LootItemChoice> pack in customChoices
                     .GroupBy(choice => choice.CustomPackId)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            List<LootItemChoice> packChoices = pack
                .OrderBy(choice => choice.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            rows.Add(new ListUIItem_Generic(
                pack.Key ?? "Unknown pack",
                packChoices[0].Template.Sprite,
                $"{packChoices.Count} item(s) →",
                () => ShowLootItemLeaves(
                    entry,
                    packChoices,
                    () => ShowCustomItemPacks(entry, allChoices, customChoices))));
        }
        ShowLootPickerRows(rows);
    }

    private void ShowLootItemLeaves(
        LootEntryDefinition entry,
        IReadOnlyList<LootItemChoice> choices,
        Action goBack)
    {
        var rows = new List<ListUIItem> { CreateLootBackRow(goBack) };
        rows.AddRange(choices.Select(choice => new ListUIItem_Generic(
            choice.Label,
            choice.Template.Sprite,
            choice.Source,
            () =>
            {
                entry.item = choice.Value;
                GenericListUI.Instance.Close();
                pickerOpen = false;
                status = $"Selected loot item {choice.Label}.";
            })));
        ShowLootPickerRows(rows);
    }

    private void ShowLootPickerRows(IEnumerable<ListUIItem> rows)
    {
        GenericListUI.Instance.Draw(rows);
        GenericListUI.Instance.Open();
    }

    private ListUIItem CreateLootBackRow(Action goBack) =>
        new ListUIItem_Generic(
            "← Back",
            Resources.Load<Sprite>(
                "Sprites/StatusEffect/sprite_statuseffect_interacting"),
            "Return to the previous item group",
            goBack);

    private ListUIItem CreateLootCancelRow() =>
        new ListUIItem_Generic(
            "Cancel",
            Resources.Load<Sprite>(
                "Sprites/StatusEffect/sprite_statuseffect_interacting"),
            "Return to the enemy editor",
            () =>
            {
                GenericListUI.Instance.Close();
                pickerOpen = false;
                status = "Loot item selection cancelled.";
            });

    private void DrawText(
        string label,
        string value,
        Action<string> assign,
        bool allowNewlines = false)
    {
        GUILayout.BeginVertical();
        GUILayout.Label(label);
        string next = DrawWrappedInput(
            value,
            DetailsInputMaxWidth,
            allowNewlines);
        if (next != value)
            assign(next);
        GUILayout.EndVertical();
    }

    private void DrawInt(
        string label,
        string key,
        int value,
        Action<int> assign)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(FieldLabelWidth));
        string text = NumberText(key, value.ToString(CultureInfo.InvariantCulture));
        string next = DrawWrappedInput(text, DetailsInputMaxWidth);
        numberText[key] = next;
        if (int.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed != value)
            assign(parsed);
        GUILayout.EndHorizontal();
    }

    private void DrawFloat(
        string label,
        string key,
        float value,
        Action<float> assign,
        float labelWidth = FieldLabelWidth)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(labelWidth));
        DrawFloatInline(key, value, assign);
        GUILayout.EndHorizontal();
    }

    private void DrawFloatInline(
        string key,
        float value,
        Action<float> assign)
    {
        string text = NumberText(key, value.ToString("0.###", CultureInfo.InvariantCulture));
        string next = DrawWrappedInput(
            text,
            InlineNumberInputMaxWidth,
            minWidth: 55f);
        numberText[key] = next;
        if (float.TryParse(
                next, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) &&
            !float.IsNaN(parsed) && !float.IsInfinity(parsed) && parsed != value)
            assign(parsed);
    }

    private string DrawWrappedInput(
        string value,
        float maxWidth,
        bool allowNewlines = false,
        float minWidth = 0f)
    {
        wrappedInputStyle ??= new GUIStyle(GUI.skin.textArea)
        {
            wordWrap = true,
            clipping = TextClipping.Clip,
            fixedWidth = 0f,
            fixedHeight = 0f,
            stretchWidth = true,
            stretchHeight = false,
            alignment = TextAnchor.UpperLeft,
            contentOffset = Vector2.zero
        };

        // Reserve width independently of the text, so long values cannot widen
        // a row. Repaint supplies the actual width after adjacent buttons, box
        // padding and scrollbars; the next Layout measures at that same width.
        int index = wrappedInputIndex++;
        float width = EditorTextLayout.Width(
            index < wrappedInputWidths.Count ? wrappedInputWidths[index] : 0f, maxWidth, minWidth);
        float minimumHeight = wrappedInputStyle.CalcHeight(
            new GUIContent(allowNewlines ? "M\nM\nM" : "M"), width);
        float height = EditorTextLayout.Height(wrappedInputStyle.CalcHeight(
            new GUIContent(EditorTextLayout.Measurement(value)), width), minimumHeight);
        Rect rect = GUILayoutUtility.GetRect(
            GUIContent.none,
            wrappedInputStyle,
            GUILayout.MinWidth(minWidth),
            GUILayout.MaxWidth(maxWidth),
            GUILayout.ExpandWidth(true),
            GUILayout.Height(height),
            GUILayout.ExpandHeight(false));
        if (Event.current.type == EventType.Repaint && rect.width > 0f)
        {
            while (wrappedInputWidths.Count <= index) wrappedInputWidths.Add(0f);
            wrappedInputWidths[index] = rect.width;
        }
        return EditorTextLayout.InputValue(GUI.TextArea(rect, value, wrappedInputStyle), allowNewlines);
    }

    private string NumberText(string key, string fallback)
    {
        if (!numberText.TryGetValue(key, out string value))
        {
            value = fallback;
            numberText.Add(key, value);
        }
        return value;
    }

    private void DrawChoice(
        string label,
        string current,
        IReadOnlyList<string> values,
        Action<string> assign)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(FieldLabelWidth));
        DrawChoiceInline(current, values, assign);
        GUILayout.EndHorizontal();
    }

    private void DrawChoiceInline(
        string current,
        IReadOnlyList<string> values,
        Action<string> assign)
    {
        int index = IndexOf(values, current);
        if (ActionButton("◀", 32))
            assign(values[(index - 1 + values.Count) % values.Count]);
        GUILayout.Label(values[index], GUILayout.MinWidth(80), GUILayout.MaxWidth(650), GUILayout.ExpandWidth(true));
        if (ActionButton("▶", 32))
            assign(values[(index + 1) % values.Count]);
    }

    private static int IndexOf(IReadOnlyList<string> values, string current)
    {
        for (int i = 0; i < values.Count; i++)
            if (values[i].Equals(current, StringComparison.OrdinalIgnoreCase))
                return i;
        return 0;
    }

    private EnemyDefinition? CurrentEnemy =>
        document?.Pack.enemies != null &&
        selectedEnemy >= 0 &&
        selectedEnemy < document.Pack.enemies.Count
            ? document.Pack.enemies[selectedEnemy]
            : null;

    private void CreateNewPack()
    {
        document = new PackDocument
        {
            Pack = new EnemyPackDefinition
            {
                packId = "my.enemy.pack",
                enemies = new List<EnemyDefinition> { NewEnemy() }
            }
        };
        selectedEnemy = 0;
        SelectionChanged();
        status = "New pack created. Choose a visual asset before saving.";
    }

    private void AddEnemy()
    {
        if (document == null)
            return;
        document.Pack.enemies ??= new List<EnemyDefinition>();
        EnemyDefinition added = NewEnemy();
        int number = 2;
        while (document.Pack.enemies.Any(enemy => string.Equals(enemy.id, added.id, StringComparison.OrdinalIgnoreCase)))
            added.id = "new_enemy_" + number++;
        document.Pack.enemies.Add(added);
        selectedEnemy = document.Pack.enemies.Count - 1;
        SelectionChanged();
    }

    private static EnemyDefinition NewEnemy()
    {
        return new EnemyDefinition
        {
            id = "new_enemy",
            name = "New Enemy",
            description = "a new enemy",
            clone = "prefab_enemy_giantrat",
            maxHp = 25,
            visuals = new VisualDefinition { image = "" },
            abilities = new List<AbilityDefinition> { NewBasicAttack() },
            spawns = new List<SpawnDefinition>()
        };
    }

    private static AbilityDefinition NewBasicAttack()
    {
        return new AbilityDefinition
        {
            type = "basicAttack",
            cooldown = 0,
            minPower = 1,
            maxPower = 3,
            damageType = "Normal",
            sound = "sound_combat_attack_sword"
        };
    }

    private static void ApplyAbilityDefaults(AbilityDefinition ability)
    {
        string type = AbilityFactory.Normalize(ability.type);
        ability.range = type == "basicattack" || type == "applystatuseffect" ? 0f : 6f;
        ability.cooldown = type == "basicattack" ? 0 : 4;
        ability.minPower = type == "ice" ? 0 : 1;
        ability.maxPower = type == "ice" ? 0 : 3;
        ability.damageType = "Normal";
        ability.sound = type == "basicattack"
            ? "sound_combat_attack_sword"
            : type == "projectile" ? "sound_combat_attack_ooze" : null;
        ability.projectile = type switch
        {
            "projectile" => "MiscPrefabs/prefab_projectile_ooze",
            "spawnstunbubbles" => "prefab_enemy_stunbubble",
            _ => null
        };
        ability.projectileImage = null;
        ability.oozeProjectileColor = null;
        ability.statusEffect = type == "applystatuseffect" ? "poison" : null;
        ability.statusDuration = type == "projectile" ? 5 : -1;
        ability.chance = type == "ice" ? 0.25f : 0f;
    }

    private void SetVisualMode(VisualDefinition visuals, int mode)
    {
        visuals.artAssetId = null;
        visuals.artOriginalName = null;
        visuals.image = mode == 0 ? "" : null;
        visuals.directionalImages = mode == 1 ? new DirectionalImages() : null;
        visuals.model = mode == 2 ? "" : null;
        if (mode == 2)
            visuals.rotation = new[] { 0f, 0f, 0f };
        numberText.Clear();
    }

    private void RefreshDocuments()
    {
        string previouslySelected = document?.Path ?? "";
        documents.Clear();
        IEnumerable<string> editorFiles =
            EnemyPackLoader.FindPackJsonFiles(
                    Plugin.PackDirectory, includeDisabled: true)
                .Concat(Directory.GetFiles(
                    Plugin.PackDirectory,
                    "*.enemy-draft",
                    SearchOption.AllDirectories));
        foreach (string path in editorFiles
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                EnemyPackDefinition? pack = EnemyPackLoader.DeserializePack(path);
                if (pack != null)
                    documents.Add(new PackDocument { Path = path, Pack = pack });
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError($"Enemy editor could not read '{path}': {exception}");
                status = $"Could not read {Path.GetFileName(path)}: {exception.Message}";
            }
        }

        PackDocument? restored = documents.FirstOrDefault(value =>
            value.Path.Equals(previouslySelected, StringComparison.OrdinalIgnoreCase));
        if (restored != null)
            SelectDocument(restored);
        else if (documents.Count > 0)
            SelectDocument(documents[0]);
        else if (document != null && string.IsNullOrWhiteSpace(document.Path))
            SelectionChanged();
        else
        {
            document = null;
            selectedEnemy = 0;
            SelectionChanged();
        }
    }

    private void SelectDocument(PackDocument value)
    {
        document = value;
        selectedEnemy = 0;
        SelectionChanged();
        savedDocumentJson = JsonConvert.SerializeObject(document.Pack);
        status = "Loaded " + Path.GetFileName(value.Path);
    }

    private void SetCurrentPackEnabled(bool enabled)
    {
        if (document == null || document.IsDraft ||
            string.IsNullOrWhiteSpace(document.Path) ||
            !File.Exists(document.Path))
            return;

        try
        {
            string marker = EnemyPackLoader.GetDisabledMarkerPath(document.Path);
            if (!enabled)
            {
                File.WriteAllText(
                    marker,
                    "Disabled by the Custom Enemy Editor. Delete this marker or " +
                    "use Enable Pack to load the pack again.");
                status = $"Disabled {Path.GetFileName(document.Path)}. " +
                         "Restart Silverpine to unload it.";
            }
            else
            {
                File.Delete(marker);
                status = $"Enabled {Path.GetFileName(document.Path)}. " +
                         "Restart Silverpine to load it.";
            }
        }
        catch (Exception exception)
        {
            status = "Could not change pack state: " + exception.Message;
            Plugin.Log.LogError("Enemy editor pack toggle failed: " + exception);
        }
    }

    private void SelectEnemy(int index)
    {
        selectedEnemy = index;
        SelectionChanged();
    }

    private void SelectionChanged()
    {
        CancelOffsetDrag();
        removeConfirmation = false;
        previewDirection = 0;
        numberText.Clear();
        detailsScroll = Vector2.zero;
        RefreshPreview();
    }

    private bool SaveCurrent()
    {
        if (document == null)
            return false;

        try
        {
            CancelOffsetDrag();
            string? selectedId = CurrentEnemy?.id;
            document.Pack.Validate(
                string.IsNullOrWhiteSpace(document.Path) ? "new pack" : document.Path);
            foreach (EnemyDefinition enemy in document.Pack.enemies!)
            {
                enemy.Validate();
                ValidateAssets(enemy);
            }

            string originalPath = document.Path;
            bool replacingDraft = document.IsDraft;
            if (string.IsNullOrWhiteSpace(document.Path) || replacingDraft)
            {
                string folder = Path.Combine(
                    Plugin.PackDirectory, SafeFileName(document.Pack.packId!));
                if (replacingDraft)
                    folder = Path.GetDirectoryName(document.Path)!;
                Directory.CreateDirectory(folder);
                document.Path = Path.Combine(folder, "enemies.json");
            }

            string json = JsonConvert.SerializeObject(document.Pack, Formatting.Indented);
            string temporary = document.Path + ".tmp";
            File.WriteAllText(temporary, json);
            File.Copy(temporary, document.Path, overwrite: true);
            File.Delete(temporary);
            if (replacingDraft &&
                !string.IsNullOrWhiteSpace(originalPath) &&
                File.Exists(originalPath))
                File.Delete(originalPath);
            string savedName = Path.GetFileName(document.Path);
            string separationStatus = FinishModelSeparations();
            RefreshDocuments();
            int restoredIndex = document?.Pack.enemies?.FindIndex(e => e.id == selectedId) ?? -1;
            if (restoredIndex >= 0) SelectEnemy(restoredIndex);
            status = $"Saved {savedName}. Restart Silverpine to load changes." + separationStatus;
            return true;
        }
        catch (Exception exception)
        {
            string draftMessage = TrySaveDraft();
            status = "Save failed: " + exception.Message + draftMessage;
            Plugin.Log.LogError("Enemy editor save failed: " + exception);
            return false;
        }
    }

    private string TrySaveDraft()
    {
        if (document == null)
            return "";
        try
        {
            string folder = GetPackFolder();
            string draftPath = document.IsDraft
                ? document.Path
                : Path.Combine(folder, "enemies.enemy-draft");
            File.WriteAllText(
                draftPath,
                JsonConvert.SerializeObject(document.Pack, Formatting.Indented));
            if (string.IsNullOrWhiteSpace(document.Path))
                document.Path = draftPath;
            return $" Draft preserved as {Path.GetFileName(draftPath)}.";
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError("Enemy editor could not preserve its draft: " + exception);
            return " The draft could not be preserved: " + exception.Message;
        }
    }

    private void ValidateAssets(EnemyDefinition enemy)
    {
        if (document == null)
            return;
        string folder = GetPackFolder();
        VisualDefinition visuals = enemy.visuals!;
        if (!string.IsNullOrWhiteSpace(visuals.image))
            ResolveAsset(folder, visuals.image!);
        else if (visuals.directionalImages != null)
        {
            foreach (string? relative in new[]
                     {
                         visuals.directionalImages.down,
                         visuals.directionalImages.left,
                         visuals.directionalImages.up,
                         visuals.directionalImages.right
                     })
            {
                if (string.IsNullOrWhiteSpace(relative))
                    throw new InvalidDataException(
                        $"Enemy '{enemy.id}' has an empty visual asset path.");
                ResolveAsset(folder, relative);
            }
        }
        else
            EnemyVisualLoader.ValidateDistributableModelSource(
                folder,
                GetQualifiedEnemyId(enemy),
                visuals);

        foreach (AbilityDefinition ability in enemy.abilities!)
        {
            if (CustomEnemyAudio.IsOggPath(ability.sound))
                ResolveAsset(folder, ability.sound!);
            if (CustomEnemyProjectileSprites.IsPngPath(ability.projectileImage))
                ResolveAsset(folder, ability.projectileImage!);
        }

        foreach (BossSpawnDefinition boss in enemy.bossSpawns!)
        {
            if (boss.bossMusic != null)
                CustomEnemyBossAudio.Resolve(folder, boss.bossMusic.file!);
            if (boss.deathSong != null)
                CustomEnemyBossAudio.Resolve(folder, boss.deathSong.file!);
        }
    }

    private void BrowseAsset(Predicate<string> predicate, Action<string> assign)
    {
        try
        {
            if (GenericListUI.Instance == null)
                throw new InvalidOperationException(
                    "Silverpine's file picker has not initialized.");

            pickerOpen = true;
            FilePickerUI.GetFileSelection(predicate, selected =>
            {
                pickerOpen = false;
                if (selected == "Exit")
                {
                    status = "File selection cancelled.";
                    return;
                }
                try
                {
                    string folder = GetPackFolder();
                    string extension = Path.GetExtension(selected).ToLowerInvariant();
                    string relative = extension is ".png" or ".jpg" or ".jpeg"
                        ? SharedArtApi.ImportImage(selected, folder)
                        : ImportAudioAsset(selected, folder);
                    assign(relative);
                    RefreshPreview();
                    status = "Copied asset to " + relative;
                }
                catch (Exception exception)
                {
                    status = "Asset import failed: " + exception.Message;
                    Plugin.Log.LogError("Enemy editor asset import failed: " + exception);
                }
            });

            if (!GenericListUI.Instance.gameObject.activeSelf)
            {
                pickerOpen = false;
                status = "Silverpine's file picker could not be opened.";
            }
        }
        catch (Exception exception)
        {
            pickerOpen = false;
            status = "File picker failed: " + exception.Message;
            Plugin.Log.LogError("Enemy editor file picker failed: " + exception);
        }
    }

    private void BrowseGlbAuthoringSource(
        EnemyDefinition enemy,
        VisualDefinition visuals)
    {
        try
        {
            if (GenericListUI.Instance == null)
                throw new InvalidOperationException(
                    "Silverpine's file picker has not initialized.");

            pickerOpen = true;
            FilePickerUI.GetFileSelection(
                path => path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase),
                selected =>
                {
                    pickerOpen = false;
                    if (selected == "Exit")
                    {
                        status = "File selection cancelled.";
                        return;
                    }
                    try
                    {
                        string qualifiedId = GetQualifiedEnemyId(enemy);
                        string destination = EnemyVisualLoader.ImportAuthoringModel(
                            selected, qualifiedId);
                        visuals.model = Path.GetFileName(destination);
                        visuals.artOriginalName = SharedArtLibrary.OriginalModelName(selected);
                        visuals.artAssetId = null;
                        visuals.cache = true;
                        ClearPreview();
                        status =
                            "Imported GLB into the shared model library. Build the " +
                            "distribution cache before releasing the pack.";
                    }
                    catch (Exception exception)
                    {
                        status = "GLB authoring import failed: " + exception.Message;
                        Plugin.Log.LogError(
                            "Enemy editor GLB authoring import failed: " + exception);
                    }
                });

            if (!GenericListUI.Instance.gameObject.activeSelf)
            {
                pickerOpen = false;
                status = "Silverpine's file picker could not be opened.";
            }
        }
        catch (Exception exception)
        {
            pickerOpen = false;
            status = "File picker failed: " + exception.Message;
            Plugin.Log.LogError("Enemy editor file picker failed: " + exception);
        }
    }

    private string GetPackFolder()
    {
        if (document == null)
            throw new InvalidOperationException("No pack is selected.");
        if (!string.IsNullOrWhiteSpace(document.Path))
            return Path.GetDirectoryName(document.Path)!;
        string id = string.IsNullOrWhiteSpace(document.Pack.packId)
            ? "new-enemy-pack"
            : document.Pack.packId!;
        string stem = Path.Combine(Plugin.PackDirectory, SafeFileName(id));
        string folder = stem;
        int suffix = 2;
        while (Directory.Exists(folder)) folder = stem + "_" + suffix++;
        Directory.CreateDirectory(folder);
        // Fix a draft's asset location before imports. Renaming its ID must not orphan its assets.
        document.Path = Path.Combine(folder, "enemies.enemy-draft");
        return folder;
    }

    private string GetQualifiedEnemyId(EnemyDefinition enemy)
    {
        if (document == null)
            throw new InvalidOperationException("No pack is selected.");
        return (document.Pack.packId ?? "") + ":" + (enemy.id ?? "");
    }

    private void RefreshPreview()
    {
        ClearPreview();
        EnemyDefinition? enemy = CurrentEnemy;
        if (enemy?.visuals == null)
            return;

        try
        {
            string folder = GetPackFolder();
            VisualDefinition visuals = enemy.visuals;
            if (!string.IsNullOrWhiteSpace(visuals.image))
            {
                previewSprites = new[]
                    { LoadPreviewSprite(ResolveAsset(folder, visuals.image!), visuals) };
            }
            else if (visuals.directionalImages != null)
            {
                string?[] paths =
                {
                    visuals.directionalImages.down,
                    visuals.directionalImages.left,
                    visuals.directionalImages.up,
                    visuals.directionalImages.right
                };
                if (paths.All(path => !string.IsNullOrWhiteSpace(path)))
                    previewSprites = paths
                        .Select(path => LoadPreviewSprite(
                            ResolveAsset(folder, path!), visuals))
                        .ToArray();
            }
            else if (!string.IsNullOrWhiteSpace(visuals.model))
            {
                string cache = EnemyVisualLoader.GetCacheDirectory(
                    folder, GetQualifiedEnemyId(enemy));
                string[] names = { "down.png", "left.png", "up.png", "right.png" };
                if (names.All(name => File.Exists(Path.Combine(cache, name))))
                    previewSprites = names.Select(name =>
                        LoadPreviewSprite(Path.Combine(cache, name), visuals)).ToArray();
            }
        }
        catch (Exception exception)
        {
            ClearPreview();
            status = "Preview unavailable: " + exception.Message;
        }
    }

    private async void GenerateGlbPreview()
    {
        if (generatingPreview) return;
        EnemyDefinition? enemy = CurrentEnemy;
        if (enemy?.visuals == null || string.IsNullOrWhiteSpace(enemy.visuals.model))
        {
            status = "Choose a GLB model first.";
            return;
        }

        CancelOffsetDrag();
        PackDocument sourceDocument = document!;
        string sourceJson = JsonConvert.SerializeObject(enemy.visuals);
        VisualDefinition snapshot = JsonConvert.DeserializeObject<VisualDefinition>(sourceJson)!;
        string folder = GetPackFolder();
        string qualifiedId = GetQualifiedEnemyId(enemy);
        int generation = ++previewGeneration;
        void EnsureCurrent()
        {
            if (this == null || !open || generation != previewGeneration ||
                !ReferenceEquals(document, sourceDocument) || !ReferenceEquals(CurrentEnemy, enemy) ||
                GetQualifiedEnemyId(enemy) != qualifiedId || GetPackFolder() != folder ||
                JsonConvert.SerializeObject(enemy.visuals) != sourceJson)
                throw new OperationCanceledException("Enemy selection or visual settings changed. Build again when ready.");
        }
        generatingPreview = true;
        status = "Building four directional distribution sprites…";
        Sprite[] rendered = Array.Empty<Sprite>();
        try
        {
            snapshot.Validate();
            rendered = await EnemyVisualLoader.GenerateDistributionCacheAsync(
                    folder,
                    qualifiedId,
                    snapshot, EnsureCurrent);
            EnsureCurrent();
            string authoringPath = EnemyVisualLoader.PrepareModelSeparation(
                    folder,
                    qualifiedId,
                    snapshot.model!, out string? embeddedSource);
            if (embeddedSource != null)
                sourceDocument.PendingModelSeparations[embeddedSource] = authoringPath;
            enemy.visuals.model = Path.GetFileName(authoringPath);
            enemy.visuals.cache = true;
            enemy.visuals.artOriginalName ??= SharedArtLibrary.OriginalModelName(authoringPath);
            ClearPreview(invalidateRender: false);
            previewSprites = rendered;
            foreach (Sprite sprite in rendered)
            {
                ownedPreviewObjects.Add(sprite);
                ownedPreviewObjects.Add(sprite.texture);
            }
            rendered = Array.Empty<Sprite>(); // Preview now owns the results.
            status =
                "Distribution cache ready. The GLB is stored separately under " +
                "BepInEx/shared/ArtLibrary/models. Save Pack to finish removing any unused embedded GLB.";
            try { PublishEnemyArt(enemy, folder, sourceDocument.Pack.packId ?? ""); }
            catch (Exception error) { status += " Shared-library publish failed: " + error.Message; }
        }
        catch (OperationCanceledException exception)
        {
            if (this != null && open) status = exception.Message;
        }
        catch (Exception exception)
        {
            status = "GLB preview failed: " + exception.Message;
            Plugin.Log.LogError("Enemy editor GLB preview failed: " + exception);
        }
        finally
        {
            foreach (Sprite sprite in rendered) { Destroy(sprite.texture); Destroy(sprite); }
            generatingPreview = false;
        }
    }

    private Sprite LoadPreviewSprite(string path, VisualDefinition visuals)
    {
        Texture2D texture = new(2, 2, TextureFormat.ARGB32, false);
        try
        {
            if (!texture.LoadImage(File.ReadAllBytes(path), false))
                throw new InvalidDataException("Unity could not decode " + path);
            texture.filterMode = FilterMode.Point;
            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f),
                32f);
            ownedPreviewObjects.Add(sprite);
            ownedPreviewObjects.Add(texture);
            return sprite;
        }
        catch { Destroy(texture); throw; }
    }

    private void ClearPreview(bool invalidateRender = true)
    {
        if (invalidateRender) previewGeneration++;
        previewSprites = Array.Empty<Sprite>();
        foreach (UnityEngine.Object value in ownedPreviewObjects)
            if (value != null)
                Destroy(value);
        ownedPreviewObjects.Clear();
    }

    private void Close()
    {
        CancelOffsetDrag();
        pendingAction = null;
        open = false;
        pickerOpen = false;
        if (document != null &&
            (string.IsNullOrWhiteSpace(document.Path) || document.IsDraft))
            TrySaveDraft();
        ClearPreview();
        numberText.Clear();
        document = null;
        documents.Clear();
        ReleaseSession();
        gameObject.SetActive(false);
    }

    private static string ResolveAsset(string folder, string relative)
    {
        if (Path.IsPathRooted(relative))
            throw new InvalidDataException("Asset paths must be relative to the pack.");
        string root = Path.GetFullPath(folder).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(folder, relative));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Asset path leaves the pack folder.");
        if (!File.Exists(path))
            throw new FileNotFoundException("Asset was not found.", path);
        return path;
    }

    private static string SafeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value.Replace(':', '_').Replace('.', '_');
    }

    private static void Swap<T>(IList<T> values, int left, int right)
    {
        T temporary = values[left];
        values[left] = values[right];
        values[right] = temporary;
    }
}
