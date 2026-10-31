#nullable enable

using BepInEx;
using BepInEx.Logging;
using GLTFast;
using HarmonyLib;
using Newtonsoft.Json;
using Silverpine.ModdingTools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace SilverpineMods.CustomEnemyLoader;

[BepInPlugin(Guid, Name, Version)]
[BepInDependency(
    Silverpine.ModdingTools.Plugin.PluginGuid,
    "1.13.0")]
[BepInDependency(
    "renegadex.silverpine.customitemloader",
    BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "renegadex.silverpine.customenemyloader";
    public const string Name = "Custom Enemy Loader";
    public const string Version = "1.4.3";

    internal static ManualLogSource Log = null!;
    internal static string PackDirectory = "";
    internal static string AuthoringDirectory = "";
    internal static readonly Dictionary<string, LoadedEnemy> Enemies =
        new(StringComparer.OrdinalIgnoreCase);

    private async void Awake()
    {
        Log = Logger;
        CustomEnemyBossState.EnsureExists();
        PackDirectory = Path.Combine(Paths.ConfigPath, "CustomEnemyLoader");
        AuthoringDirectory = Path.Combine(
            Paths.ConfigPath, "CustomEnemyLoaderAuthoring");
        Directory.CreateDirectory(PackDirectory);
        Directory.CreateDirectory(SharedModelSources.Root);
        ModdingToolsMenu.RegisterSession(
            Guid + ".enemy-editor",
            "Custom Enemy Editor",
            (_, session) => CustomEnemyEditorUI.Open(session),
            order: 300);
        Logger.LogInfo("Registered Custom Enemy Editor with the Modding Tools main menu.");
        new Harmony(Guid).PatchAll();
        SerializationManager.OnFinishedLoadingSave +=
            CustomEnemyBossSpawner.EnsureCurrentTile;
        SerializationManager.OnFinishedLoadingSave +=
            CustomEnemyTimedSpawner.ReconcileCurrent;

        try
        {
            int count = await EnemyPackLoader.LoadAllAsync(PackDirectory);
            Log.LogInfo($"Registered {count} custom enem{(count == 1 ? "y" : "ies")}.");
            CustomEnemyWorldSpawner.PopulateCurrentGeneratedTileAfterRegistration();
        }
        catch (Exception exception)
        {
            Log.LogError("Custom enemy loading failed: " + exception);
        }
    }
}

public sealed class CustomEnemyIdentity : MonoBehaviour
{
    public string definitionId = "";
}

public sealed class CustomEnemyBossIdentity : MonoBehaviour,
    ISerializableMonoBehavior, IDeathHandler
{
    public string bossKey = "";
    public bool respawnAfterDeath;
    public string encounterMusicClipId = "";
    public int encounterMusicPriority = 500;
    public float encounterMusicVolume = 0.8f;
    public float encounterMusicFadeSeconds = 1f;
    public string deathSongClipId = "";
    public float deathSongVolume = 0.8f;
    public float deathSongMinRange;
    public float deathSongMaxRange = 25f;

    private MusicPlayback? encounterMusic;
    private bool dead;
    private bool encounterMusicErrorLogged;
    private float nextMusicCheck;

    private void OnEnable()
    {
        ModAudioEvents.PlayerTileChanged += OnPlayerTileChanged;
        RefreshEncounterMusic();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextMusicCheck)
            return;
        nextMusicCheck = Time.unscaledTime + 0.25f;
        RefreshEncounterMusic();
    }

    private void OnDisable()
    {
        ModAudioEvents.PlayerTileChanged -= OnPlayerTileChanged;
        StopEncounterMusic();
    }

    private void OnDestroy()
    {
        ModAudioEvents.PlayerTileChanged -= OnPlayerTileChanged;
        StopEncounterMusic();
    }

    private void OnPlayerTileChanged(PlayerLocationAudioContext context) =>
        RefreshEncounterMusic();

    private void RefreshEncounterMusic()
    {
        bool shouldPlay = !dead &&
                          !string.IsNullOrWhiteSpace(encounterMusicClipId) &&
                          CustomEnemyBossSpawner.IsCurrentPlayerTileAt(
                              transform.GetVector2IntPosition());
        if (!shouldPlay)
        {
            StopEncounterMusic();
            return;
        }

        if (encounterMusic?.IsActive == true)
            return;

        try
        {
            encounterMusic = ModAudio.PlayMusic(
                Plugin.Guid,
                encounterMusicClipId,
                new MusicPlaybackOptions
                {
                    Priority = encounterMusicPriority,
                    Volume = encounterMusicVolume,
                    Loop = true,
                    FadeSeconds = encounterMusicFadeSeconds
                });
            encounterMusicErrorLogged = false;
        }
        catch (Exception exception)
        {
            if (!encounterMusicErrorLogged)
            {
                encounterMusicErrorLogged = true;
                Plugin.Log.LogError(
                    $"Could not start encounter music for boss '{bossKey}': " +
                    exception.GetBaseException().Message);
            }
        }
    }

    private void StopEncounterMusic()
    {
        encounterMusic?.Stop();
        encounterMusic = null;
    }

    private void BeginDeathAudio()
    {
        if (dead)
            return;
        dead = true;
        StopEncounterMusic();

        if (string.IsNullOrWhiteSpace(deathSongClipId))
            return;
        try
        {
            _ = ModAudio.Play(
                Plugin.Guid,
                deathSongClipId,
                new AudioPlaybackOptions
                {
                    Bus = ModAudioBus.Ambient,
                    Volume = deathSongVolume,
                    Loop = false,
                    SpatialBlend = 1f,
                    Position = new Vector3(
                        transform.position.x,
                        transform.position.y,
                        0f),
                    MinDistance = deathSongMinRange,
                    MaxDistance = deathSongMaxRange,
                    RolloffMode = AudioRolloffMode.Linear
                });
        }
        catch (Exception exception)
        {
            Plugin.Log.LogError(
                $"Could not start death song for boss '{bossKey}': " +
                exception.GetBaseException().Message);
        }
    }

    public void OnDeathBeforeFade()
    {
        BeginDeathAudio();
    }

    public void OnDeath()
    {
        BeginDeathAudio();
        if (!respawnAfterDeath && !string.IsNullOrWhiteSpace(bossKey))
            CustomEnemyBossState.MarkKilled(bossKey);
    }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(1);
        writer.Write(bossKey ?? "");
        writer.Write(respawnAfterDeath);
    }

    public void Deserialize(BinaryReader reader)
    {
        int version = reader.ReadInt32();
        if (version >= 1)
        {
            bossKey = reader.ReadString();
            respawnAfterDeath = reader.ReadBoolean();
        }
    }
}

internal static class CustomEnemyBossAudio
{
    private const string ClipPrefix = "customenemyboss:";

    internal static async Task<List<AudioClipRegistration>> LoadForBossAsync(
        string enemyId,
        BossSpawnDefinition boss,
        string packFolder)
    {
        var registrations = new List<AudioClipRegistration>();
        try
        {
            if (boss.bossMusic != null)
            {
                string clipId = BuildClipId(enemyId, boss.id!, "encounter");
                registrations.Add(await LoadAsync(
                    clipId, packFolder, boss.bossMusic.file!));
                boss.EncounterMusicClipId = clipId;
            }

            if (boss.deathSong != null)
            {
                string clipId = BuildClipId(enemyId, boss.id!, "death");
                registrations.Add(await LoadAsync(
                    clipId, packFolder, boss.deathSong.file!));
                boss.DeathSongClipId = clipId;
            }

            return registrations;
        }
        catch
        {
            for (int index = registrations.Count - 1; index >= 0; index--)
                registrations[index].Unregister();
            throw;
        }
    }

    internal static bool IsSupportedPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        string extension = Path.GetExtension(value);
        return extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".aif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".aiff", StringComparison.OrdinalIgnoreCase);
    }

    internal static string Resolve(string root, string relative)
    {
        if (Path.IsPathRooted(relative))
            throw new InvalidDataException(
                "Boss audio paths must be relative to the pack JSON.");
        string basePath = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string result = Path.GetFullPath(Path.Combine(root, relative));
        if (!result.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Boss audio paths cannot leave the pack folder.");
        if (!File.Exists(result))
            throw new FileNotFoundException("Custom boss audio was not found.", result);
        return result;
    }

    private static Task<AudioClipRegistration> LoadAsync(
        string clipId,
        string packFolder,
        string relativePath) =>
        ModAudio.LoadClipAsync(
            Plugin.Guid,
            clipId,
            Resolve(packFolder, relativePath.Trim()),
            new AudioClipLoadOptions { StreamAudio = true });

    private static string BuildClipId(
        string enemyId,
        string bossId,
        string purpose) =>
        ClipPrefix + enemyId + ":" + bossId + ":" + purpose;
}

public sealed class CustomEnemyTimedIdentity : MonoBehaviour,
    ISerializableMonoBehavior
{
    public string ruleKey = "";
    public int cycleDay;

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(1);
        writer.Write(ruleKey ?? "");
        writer.Write(cycleDay);
    }

    public void Deserialize(BinaryReader reader)
    {
        int version = reader.ReadInt32();
        if (version >= 1)
        {
            ruleKey = reader.ReadString();
            cycleDay = reader.ReadInt32();
        }
    }
}

public sealed class CustomEnemyBossState : MonoBehaviour,
    IPersistentSerializableMonoBehavior
{
    private const string StateObjectName = "CustomEnemyLoader Boss State";
    private static readonly HashSet<string> KilledBosses =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> LastTimedCycles =
        new(StringComparer.OrdinalIgnoreCase);
    private static CustomEnemyBossState? instance;

    internal static void EnsureExists()
    {
        if (instance != null)
            return;
        GameObject stateObject = new(StateObjectName);
        UnityEngine.Object.DontDestroyOnLoad(stateObject);
        instance = stateObject.AddComponent<CustomEnemyBossState>();
    }

    internal static bool IsKilled(string bossKey) => KilledBosses.Contains(bossKey);

    internal static void MarkKilled(string bossKey)
    {
        if (KilledBosses.Add(bossKey))
            Plugin.Log.LogInfo($"Recorded defeated one-time boss '{bossKey}'.");
    }

    internal static void ClearKilled(string bossKey) => KilledBosses.Remove(bossKey);

    internal static void ResetForWorldInitialization()
    {
        KilledBosses.Clear();
        LastTimedCycles.Clear();
    }

    internal static bool TryStartTimedCycle(string ruleKey, int cycleDay)
    {
        if (LastTimedCycles.TryGetValue(ruleKey, out int existing) && existing == cycleDay)
            return false;
        LastTimedCycles[ruleKey] = cycleDay;
        return true;
    }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(1);
        writer.Write(KilledBosses.Count);
        foreach (string bossKey in KilledBosses.OrderBy(value => value))
            writer.Write(bossKey);
        writer.Write(LastTimedCycles.Count);
        foreach (KeyValuePair<string, int> cycle in
                 LastTimedCycles.OrderBy(value => value.Key))
        {
            writer.Write(cycle.Key);
            writer.Write(cycle.Value);
        }
    }

    public void Deserialize(BinaryReader reader)
    {
        KilledBosses.Clear();
        int version = reader.ReadInt32();
        if (version < 1)
            return;
        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
            KilledBosses.Add(reader.ReadString());
        LastTimedCycles.Clear();
        if (reader.BaseStream.Position >= reader.BaseStream.Length)
            return;
        int timedCount = reader.ReadInt32();
        for (int i = 0; i < timedCount; i++)
            LastTimedCycles[reader.ReadString()] = reader.ReadInt32();
    }
}

internal static class CustomEnemyAudio
{
    private const string KeyPrefix = "customenemyogg:";
    private static readonly Dictionary<string, AudioClip> Clips =
        new(StringComparer.OrdinalIgnoreCase);

    internal static async Task<List<string>> LoadForEnemyAsync(
        string enemyId,
        EnemyDefinition definition,
        string packFolder)
    {
        var loadedKeys = new List<string>();
        try
        {
            for (int index = 0; index < definition.abilities!.Count; index++)
            {
                AbilityDefinition ability = definition.abilities[index];
                if (!IsOggPath(ability.sound))
                    continue;

                string relative = ability.sound!.Trim();
                string path = Resolve(packFolder, relative);
                string key = KeyPrefix + enemyId + ":" +
                             relative.Replace('\\', '/');
                if (Clips.ContainsKey(key))
                {
                    ability.sound = key;
                    continue;
                }

                AudioClip clip = await LoadOggAsync(path, key);
                Clips.Add(key, clip);
                loadedKeys.Add(key);
                ability.sound = key;
                Plugin.Log.LogInfo(
                    $"Loaded custom OGG '{relative}' for '{enemyId}' ability {index + 1}.");
            }
            return loadedKeys;
        }
        catch
        {
            Release(loadedKeys);
            throw;
        }
    }

    internal static bool TryGet(string soundName, out AudioClip clip) =>
        Clips.TryGetValue(soundName, out clip!);

    internal static void Release(IEnumerable<string> keys)
    {
        foreach (string key in keys)
        {
            if (!Clips.Remove(key, out AudioClip clip))
                continue;
            UnityEngine.Object.Destroy(clip);
        }
    }

    internal static bool IsOggPath(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase);

    private static async Task<AudioClip> LoadOggAsync(string path, string name)
    {
        using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(
            new Uri(path).AbsoluteUri,
            AudioType.OGGVORBIS);
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();
        while (!operation.isDone)
            await Task.Yield();
        if (request.result != UnityWebRequest.Result.Success)
            throw new InvalidDataException(
                $"Unity could not decode OGG '{path}': {request.error}");
        AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
        if (clip == null)
            throw new InvalidDataException("Unity returned no audio clip for OGG: " + path);
        clip.name = name;
        return clip;
    }

    private static string Resolve(string root, string relative)
    {
        if (Path.IsPathRooted(relative))
            throw new InvalidDataException(
                "Custom OGG paths must be relative to the pack JSON.");
        string basePath = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string result = Path.GetFullPath(Path.Combine(root, relative));
        if (!result.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Custom OGG paths cannot leave the pack folder.");
        if (!File.Exists(result))
            throw new FileNotFoundException("Custom enemy OGG was not found.", result);
        return result;
    }
}

internal static class CustomEnemyProjectileSprites
{
    private const string KeyPrefix = "customenemyprojectile:";
    private const string NativeOozeKeyPrefix = "nativeooze:";
    private static readonly HashSet<string> OozeColors = new(
        new[] { "Green", "Blue", "Orange", "Purple", "Red" },
        StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Sprite> Sprites =
        new(StringComparer.OrdinalIgnoreCase);

    internal static List<string> LoadForEnemy(
        string enemyId,
        EnemyDefinition definition,
        string packFolder)
    {
        var loadedKeys = new List<string>();
        try
        {
            foreach (AbilityDefinition ability in definition.abilities!)
            {
                if (!IsPngPath(ability.projectileImage))
                {
                    if (TryGetNativeOozeColor(ability, out string color))
                    {
                        string resource = NativeOozeResource(color);
                        if (Resources.Load<Sprite>(resource) == null)
                            throw new InvalidDataException(
                                $"Native ooze projectile sprite '{resource}' is unavailable.");
                        ability.oozeProjectileColor = CanonicalOozeColor(color);
                        Plugin.Log.LogInfo(
                            $"Selected native {ability.oozeProjectileColor} ooze projectile " +
                            $"for '{enemyId}'.");
                    }
                    continue;
                }

                string relative = ability.projectileImage!.Trim();
                string path = Resolve(packFolder, relative);
                string key = KeyPrefix + enemyId + ":" +
                             relative.Replace('\\', '/');
                if (!Sprites.ContainsKey(key))
                {
                    Texture2D texture = new(2, 2, TextureFormat.ARGB32, false);
                    if (!texture.LoadImage(File.ReadAllBytes(path), false))
                    {
                        UnityEngine.Object.Destroy(texture);
                        throw new InvalidDataException(
                            "Unity could not decode projectile PNG: " + path);
                    }
                    texture.name = key;
                    texture.filterMode = FilterMode.Point;
                    Sprite sprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        32f);
                    sprite.name = key;
                    Sprites.Add(key, sprite);
                    loadedKeys.Add(key);
                    Plugin.Log.LogInfo(
                        $"Loaded custom projectile PNG '{relative}' for '{enemyId}'.");
                }
                ability.projectileImage = key;
            }
            return loadedKeys;
        }
        catch
        {
            Release(loadedKeys);
            throw;
        }
    }

    internal static bool TryGet(string key, out Sprite sprite)
    {
        if (Sprites.TryGetValue(key, out sprite!))
            return true;
        if (key.StartsWith(NativeOozeKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string color = key.Substring(NativeOozeKeyPrefix.Length);
            sprite = Resources.Load<Sprite>(NativeOozeResource(color));
            return sprite != null;
        }
        sprite = null!;
        return false;
    }

    internal static bool UsesOozePrefab(AbilityDefinition ability) =>
        string.Equals(
            ability.projectile ?? "MiscPrefabs/prefab_projectile_ooze",
            "MiscPrefabs/prefab_projectile_ooze",
            StringComparison.OrdinalIgnoreCase);

    internal static bool IsValidOozeColor(string? value) =>
        string.IsNullOrWhiteSpace(value) || OozeColors.Contains(value.Trim());

    internal static string GetSpriteKey(AbilityDefinition ability)
    {
        if (!string.IsNullOrWhiteSpace(ability.projectileImage))
            return ability.projectileImage!;
        return TryGetNativeOozeColor(ability, out string color)
            ? NativeOozeKeyPrefix + color.ToLowerInvariant()
            : "";
    }

    internal static void Release(IEnumerable<string> keys)
    {
        foreach (string key in keys)
        {
            if (!Sprites.Remove(key, out Sprite sprite))
                continue;
            Texture2D texture = sprite.texture;
            UnityEngine.Object.Destroy(sprite);
            UnityEngine.Object.Destroy(texture);
        }
    }

    internal static bool IsPngPath(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetNativeOozeColor(
        AbilityDefinition ability,
        out string color)
    {
        color = ability.oozeProjectileColor?.Trim() ?? "Green";
        return UsesOozePrefab(ability) &&
               OozeColors.Contains(color) &&
               !color.Equals("Green", StringComparison.OrdinalIgnoreCase);
    }

    private static string CanonicalOozeColor(string color) =>
        OozeColors.First(value => value.Equals(
            color, StringComparison.OrdinalIgnoreCase));

    private static string NativeOozeResource(string color) =>
        "sprites/projectile/sprite_projectile_ooze_" + color.ToLowerInvariant();

    private static string Resolve(string root, string relative)
    {
        if (Path.IsPathRooted(relative))
            throw new InvalidDataException(
                "Custom projectile PNG paths must be relative to the pack JSON.");
        string basePath = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string result = Path.GetFullPath(Path.Combine(root, relative));
        if (!result.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "Custom projectile PNG paths cannot leave the pack folder.");
        if (!File.Exists(result))
            throw new FileNotFoundException(
                "Custom projectile PNG was not found.", result);
        return result;
    }
}

public sealed class CustomEnemyProjectileAbility : Ability
{
    private string projectilePrefabName = "MiscPrefabs/prefab_projectile_ooze";
    private string projectileSpriteKey = "";
    private StatusEffect? statusEffect;
    private int statusEffectDuration = -1;
    private string soundName = "sound_combat_attack_sword";
    private DamageType damageType = DamageType.Normal;

    public CustomEnemyProjectileAbility()
    {
    }

    internal CustomEnemyProjectileAbility(
        float range,
        int cooldown,
        int minPower,
        int maxPower,
        string projectilePrefabName,
        string projectileSpriteKey,
        StatusEffect? statusEffect,
        int statusEffectDuration,
        string soundName,
        DamageType damageType)
        : base(range, cooldown, minPower, maxPower)
    {
        this.projectilePrefabName = projectilePrefabName;
        this.projectileSpriteKey = projectileSpriteKey;
        this.statusEffect = statusEffect;
        this.statusEffectDuration = statusEffectDuration;
        this.soundName = soundName;
        this.damageType = damageType;
    }

    public override void UseOn(GameObject target)
    {
        if (owner == null || target == null)
            return;
        ActionQueue.Instance.Enqueue(new QueueAction(
            ShootProjectile(target),
            simultaneous: false,
            ActionQueuePriority.Attack,
            owner));
    }

    private IEnumerator ShootProjectile(GameObject target)
    {
        if (owner == null || target == null)
            yield break;
        GameObject? source = Resources.Load<GameObject>(projectilePrefabName);
        if (source == null)
        {
            Plugin.Log.LogError(
                $"Custom projectile base prefab '{projectilePrefabName}' is unavailable.");
            yield break;
        }

        GameObject projectile = UnityEngine.Object.Instantiate(source);
        if (CustomEnemyProjectileSprites.TryGet(projectileSpriteKey, out Sprite sprite))
        {
            SpriteRenderer renderer = projectile.GetSpriteRendererExtended();
            if (renderer != null)
            {
                renderer.sprite = sprite;
                renderer.color = Color.white;
            }
        }
        projectile.transform.position = owner.transform.position;
        Transform shadow = projectile.transform.FindContains("shadow");
        if (shadow != null)
            shadow.parent = null;
        Vector3 vector = target.transform.position - projectile.transform.position;
        float angle = Mathf.Atan2(vector.y, vector.x) * Mathf.Rad2Deg;
        projectile.transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        if (shadow != null)
            shadow.parent = projectile.transform;

        yield return AnimationHelper.Instance.AnimateMovement(
            projectile.transform,
            owner.transform.position,
            target.transform.position,
            4f,
            forceVisible: true,
            null,
            local: false);

        if (owner == null || target == null)
        {
            UnityEngine.Object.Destroy(projectile);
            yield break;
        }

        target.GetComponent<Damagable>().TakeDamage(
            owner,
            new Damage(GetFinalDamage(), damageType),
            critical: false,
            () =>
            {
                if (target != null)
                    AudioPlayer.Instance.PlayAudioAtPosition(
                        soundName, 0.5f, target.transform.GetVector2IntPosition());
            });
        if (statusEffect != null &&
            target.TryGetComponent<StatusEffectsTarget>(out var statusTarget))
            statusTarget.AddStatusEffect(
                statusEffect.DeepClone(), statusEffectDuration, quiet: false);

        foreach (SpriteRenderer renderer in
                 projectile.GetComponentsInChildren<SpriteRenderer>())
            renderer.enabled = false;
        foreach (Light light in projectile.GetComponentsInChildren<Light>())
            light.enabled = false;
        UnityEngine.Object.Destroy(projectile, 1f);
    }

    private int GetFinalDamage()
    {
        int minimum = minPower;
        int maximum = maxPower;
        if (owner.TryGetComponent<Enemy>(out var enemy))
        {
            float modifier = enemy.GetAbilityPowerModifier();
            minimum = Mathf.RoundToInt(minPower * modifier);
            maximum = Mathf.RoundToInt(maxPower * modifier);
        }
        return UnityEngine.Random.Range(minimum, maximum + 1);
    }

    public override void Serialize(BinaryWriter writer)
    {
        base.Serialize(writer);
        writer.Write(1);
        writer.Write(projectilePrefabName ?? "");
        writer.Write(projectileSpriteKey ?? "");
        writer.Write(statusEffect == null);
        if (statusEffect != null)
            TypeSerializationUtility.SerializeWithType(statusEffect, writer);
        writer.Write(statusEffectDuration);
        writer.Write(soundName ?? "");
        writer.Write((int)damageType);
    }

    public override void Deserialize(BinaryReader reader)
    {
        base.Deserialize(reader);
        int version = reader.ReadInt32();
        if (version < 1)
            return;
        projectilePrefabName = reader.ReadString();
        projectileSpriteKey = reader.ReadString();
        if (!reader.ReadBoolean())
            statusEffect = TypeSerializationUtility.DeserializeWithType<StatusEffect>(reader);
        statusEffectDuration = reader.ReadInt32();
        soundName = reader.ReadString();
        damageType = (DamageType)reader.ReadInt32();
    }
}

public enum PassiveFleeBehavior
{
    ProximityAndDamage,
    WhenAttacked,
    Never
}

public sealed class CustomEnemyPassiveController : MonoBehaviour
{
    private static readonly MethodInfo FleeMethod =
        AccessTools.Method(typeof(Enemy), "Flee");

    public PassiveFleeBehavior fleeBehavior = PassiveFleeBehavior.ProximityAndDamage;

    private Enemy enemy = null!;
    private Damagable damageable = null!;
    private bool allowDamageFlee;

    private void Start()
    {
        enemy = GetComponent<Enemy>();
        damageable = GetComponent<Damagable>();
        damageable.OnBeforeHealthChanged += OnBeforeHealthChanged;
    }

    private void OnDestroy()
    {
        if (damageable != null)
            damageable.OnBeforeHealthChanged -= OnBeforeHealthChanged;
    }

    private void OnBeforeHealthChanged(ResourceChangeEventArgs args)
    {
        if (args.change >= 0 || fleeBehavior != PassiveFleeBehavior.WhenAttacked)
            return;

        allowDamageFlee = true;
        try
        {
            FleeMethod.Invoke(enemy, null);
        }
        finally
        {
            allowDamageFlee = false;
        }
    }

    internal bool AllowFlee() =>
        fleeBehavior == PassiveFleeBehavior.ProximityAndDamage || allowDamageFlee;
}

public sealed class CustomEnemyLootDropper : MonoBehaviour, IDeathHandler
{
    public string[] items = Array.Empty<string>();
    public string[] tiers = Array.Empty<string>();
    public float[] weights = Array.Empty<float>();
    public int minAmount;
    public int maxAmount;

    public void OnDeathBeforeFade()
    {
    }

    public void OnDeath()
    {
        if (items.Length == 0 || weights.Length != items.Length || maxAmount <= 0)
            return;

        int count = UnityEngine.Random.Range(minAmount, maxAmount + 1);
        var loot = new List<Item>();
        for (int i = 0; i < count; i++)
        {
            int selectedIndex = SelectItemIndex();
            string selected = items[selectedIndex];
            string? selectedTier = selectedIndex < tiers.Length
                ? tiers[selectedIndex]
                : null;
            if (CustomEnemyItemResolver.TryCreateItem(
                    selected, selectedTier, out Item item))
            {
                if (!CustomEnemyItemResolver.HasRequestedQuality(
                        selected, selectedTier))
                    RandomizeQuality(item);
                loot.Add(item);
            }
            else
            {
                CustomEnemyItemResolver.WarnMissing(selected);
            }
        }

        LootDropper.DropLoot(loot, transform.GetVector2IntPosition());
    }

    private int SelectItemIndex()
    {
        float total = 0f;
        for (int i = 0; i < weights.Length; i++)
            total += weights[i];

        float roll = UnityEngine.Random.Range(0f, total);
        for (int i = 0; i < weights.Length; i++)
        {
            roll -= weights[i];
            if (roll <= 0f)
                return i;
        }
        return items.Length - 1;
    }

    private void RandomizeQuality(Item item)
    {
        if (!item.TryGetItemComponent<IQualityItemComponent>(out var quality) ||
            item.name == "Green Ooze" || quality.Quality != Quality.Normal)
            return;

        if (0.33f.ToChance())
            quality.Quality = Quality.Normal;
        else if (0.33f.ToChance())
            quality.Quality = Quality.Quality;
        else if (0.33f.ToChance())
            quality.Quality = Quality.Superior;
        else if (0.33f.ToChance())
            quality.Quality = Quality.Exceptional;
        else if (0.33f.ToChance())
            quality.Quality = Quality.Pristine;
        else
            quality.Quality = Quality.Quality;

        Enemy enemy = GetComponent<Enemy>();
        if (enemy != null && enemy.enraged && quality.Quality < Quality.Legendary)
            quality.Quality++;
        if (enemy != null && enemy.sturdy && quality.Quality < Quality.Legendary)
            quality.Quality++;
    }
}

internal sealed class LootItemChoice
{
    internal readonly string Value;
    internal readonly string Label;
    internal readonly string Source;
    internal readonly Item Template;
    internal readonly string? CustomPackId;

    internal LootItemChoice(
        string value,
        string label,
        string source,
        Item template,
        string? customPackId)
    {
        Value = value;
        Label = label;
        Source = source;
        Template = template;
        CustomPackId = customPackId;
    }
}

internal static class CustomEnemyItemResolver
{
    private const string ApiTypeName =
        "SilverpineMods.CustomItemLoader.CustomItemApi";
    private static readonly HashSet<string> WarnedMissing =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> WarnedUnsupportedQuality =
        new(StringComparer.OrdinalIgnoreCase);

    internal static bool TryCreateItem(string identifier, out Item item)
    {
        return TryCreateItem(identifier, requestedTier: null, out item);
    }

    internal static bool TryCreateItem(
        string identifier,
        string? requestedTier,
        out Item item)
    {
        string value = (identifier ?? "").Trim();
        string displayName = value.Split('|').LastOrDefault() ?? "";
        if (displayName.Contains(":") && TryCreateCustomItem(displayName, out item))
        {
            ApplyRequestedQuality(value, requestedTier, item);
            return true;
        }

        if (ItemLibrary.Items.Any(candidate =>
                candidate.name.Equals(displayName, StringComparison.OrdinalIgnoreCase)))
        {
            item = ItemLibrary.GetItemByName(value, logInvalid: false);
            if (item.name == "Strange Object")
                return false;
            ApplyRequestedQuality(value, requestedTier, item);
            return true;
        }

        item = null!;
        return false;
    }

    internal static bool HasRequestedQuality(
        string identifier,
        string? requestedTier)
    {
        return TryGetRequestedQuality(identifier, requestedTier, out _);
    }

    internal static bool TryGetLegacyQuality(
        string? identifier,
        out Quality quality)
    {
        quality = Quality.Normal;
        string[] parts = (identifier ?? "").Split('|');
        return parts.Length > 1 &&
               Enum.TryParse(parts[0], ignoreCase: true, out quality) &&
               Enum.IsDefined(typeof(Quality), quality);
    }

    internal static string StripLegacyQuality(string? identifier)
    {
        string value = (identifier ?? "").Trim();
        return TryGetLegacyQuality(value, out _)
            ? value.Substring(value.IndexOf('|') + 1).Trim()
            : value;
    }

    private static bool TryGetRequestedQuality(
        string identifier,
        string? requestedTier,
        out Quality quality)
    {
        quality = Quality.Normal;
        if (!string.IsNullOrWhiteSpace(requestedTier) &&
            Enum.TryParse(requestedTier, ignoreCase: true, out quality) &&
            Enum.IsDefined(typeof(Quality), quality))
            return true;
        return TryGetLegacyQuality(identifier, out quality);
    }

    private static void ApplyRequestedQuality(
        string identifier,
        string? requestedTier,
        Item item)
    {
        if (!TryGetRequestedQuality(identifier, requestedTier, out Quality quality))
            return;
        if (item.TryGetItemComponent<IQualityItemComponent>(out var qualityComponent))
        {
            qualityComponent.Quality = quality;
            return;
        }

        string warningKey = item.name + "|" + quality;
        if (WarnedUnsupportedQuality.Add(warningKey))
            Plugin.Log.LogWarning(
                $"Loot tier '{quality}' was requested for item '{identifier}', but " +
                "that item does not support Silverpine item quality. It will drop " +
                "without a tier override.");
    }

    internal static IReadOnlyList<LootItemChoice> GetItemChoices()
    {
        var customItems = GetCustomItemMetadata()
            .GroupBy(value => value.Template)
            .Select(group => group.First())
            .ToArray();
        var customTemplates = new HashSet<Item>(customItems.Select(value => value.Template));
        IEnumerable<LootItemChoice> registeredChoices = customItems.Select(custom =>
            new LootItemChoice(
                custom.Id,
                custom.Name,
                "Custom Item Loader • " + custom.Id,
                custom.Template,
                custom.PackId));
        IEnumerable<LootItemChoice> libraryChoices = ItemLibrary.Items
            .Where(item => !customTemplates.Contains(item))
            .GroupBy(item => item.name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(template => new LootItemChoice(
                    template.name,
                    template.name,
                    template.itemCategory.ToString(),
                    template,
                    customPackId: null));

        return registeredChoices.Concat(libraryChoices)
            .OrderBy(choice => choice.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(choice => choice.Value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static void WarnMissing(string identifier)
    {
        if (WarnedMissing.Add(identifier))
            Plugin.Log.LogError(
                $"Custom enemy loot item '{identifier}' is not registered. " +
                "Install its item mod or correct the item name/qualified ID.");
    }

    private static bool TryCreateCustomItem(string identifier, out Item item)
    {
        Type? api = FindCustomItemApi();
        MethodInfo? method = api?.GetMethod(
            "TryCreateItem",
            BindingFlags.Public | BindingFlags.Static);
        if (method != null)
        {
            try
            {
                object?[] arguments = { identifier, null };
                if (method.Invoke(null, arguments) is true && arguments[1] is Item created)
                {
                    item = created;
                    return true;
                }
            }
            catch (Exception exception)
            {
                Plugin.Log.LogWarning(
                    $"Custom Item Loader lookup failed for '{identifier}': " +
                    exception.GetBaseException().Message);
            }
        }

        item = null!;
        return false;
    }

    private static IEnumerable<(string Id, string Name, string PackId, Item Template)>
        GetCustomItemMetadata()
    {
        Type? api = FindCustomItemApi();
        MethodInfo? method = api?.GetMethod(
            "GetRegisteredItems",
            BindingFlags.Public | BindingFlags.Static);
        if (method == null)
            yield break;

        System.Collections.IEnumerable? registered;
        try
        {
            registered = method.Invoke(null, null) as System.Collections.IEnumerable;
        }
        catch (Exception exception)
        {
            Plugin.Log.LogWarning(
                "Could not read Custom Item Loader registrations: " +
                exception.GetBaseException().Message);
            yield break;
        }

        if (registered == null)
            yield break;
        foreach (object info in registered)
        {
            Type type = info.GetType();
            if (type.GetProperty("QualifiedId")?.GetValue(info) is string id &&
                type.GetProperty("DisplayName")?.GetValue(info) is string name &&
                type.GetProperty("PackId")?.GetValue(info) is string packId &&
                type.GetProperty("Template")?.GetValue(info) is Item template)
                yield return (id, name, packId, template);
        }
    }

    private static Type? FindCustomItemApi() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(ApiTypeName, throwOnError: false))
            .FirstOrDefault(type => type != null);
}

internal sealed class LoadedEnemy
{
    internal readonly string Id;
    internal readonly EnemyDefinition Definition;
    internal readonly GameObject Template;
    internal readonly Dictionary<string, GameObject> BossTemplates;
    internal readonly Dictionary<string, GameObject> TimedTemplates;
    internal readonly IReadOnlyList<AudioClipRegistration> BossAudioRegistrations;

    internal LoadedEnemy(
        string id,
        EnemyDefinition definition,
        GameObject template,
        Dictionary<string, GameObject> bossTemplates,
        Dictionary<string, GameObject> timedTemplates,
        IReadOnlyList<AudioClipRegistration> bossAudioRegistrations)
    {
        Id = id;
        Definition = definition;
        Template = template;
        BossTemplates = bossTemplates;
        TimedTemplates = timedTemplates;
        BossAudioRegistrations = bossAudioRegistrations;
    }
}

internal static class EnemyPackLoader
{
    internal static async Task<int> LoadAllAsync(string root)
    {
        int loaded = 0;
        string[] discovered = FindPackJsonFiles(root, includeDisabled: true)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] enabled = discovered
            .Where(path => !IsPackDisabled(path))
            .ToArray();
        foreach (string path in enabled)
        {
            try
            {
                EnemyPackDefinition? pack = DeserializePack(path);
                if (pack == null)
                    throw new InvalidDataException("The JSON file is empty.");
                pack.Validate(path);

                string packFolder = Path.GetDirectoryName(path)!;
                foreach (EnemyDefinition definition in pack.enemies!)
                {
                    try
                    {
                        definition.Validate();
                        await RegisterAsync(pack.packId!, definition, packFolder);
                        loaded++;
                    }
                    catch (Exception exception)
                    {
                        Plugin.Log.LogError(
                            $"Skipped enemy '{definition.id ?? "<missing id>"}' in '{path}': " +
                            exception.Message);
                    }
                }
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError($"Could not load enemy pack '{path}': {exception}");
            }
        }

        if (loaded == 0 && discovered.Length > 0 && enabled.Length == 0)
            Plugin.Log.LogInfo(
                $"No custom enemies were registered because all {discovered.Length} " +
                "discovered enemy pack(s) are disabled.");
        else if (loaded == 0)
            Plugin.Log.LogWarning("No valid custom enemy definitions were found in " + root);
        return loaded;
    }

    internal static IEnumerable<string> FindPackJsonFiles(
        string root,
        bool includeDisabled = false)
    {
        return Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
            .Where(path => !path.Split(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar)
                .Any(part => part.Equals(".cache", StringComparison.OrdinalIgnoreCase)))
            .Where(path => includeDisabled || !IsPackDisabled(path));
    }

    internal static string GetDisabledMarkerPath(string packPath) =>
        packPath + ".disabled";

    internal static bool IsPackDisabled(string packPath) =>
        File.Exists(GetDisabledMarkerPath(packPath));

    internal static EnemyPackDefinition? DeserializePack(string path)
    {
        return JsonConvert.DeserializeObject<EnemyPackDefinition>(
            File.ReadAllText(path),
            new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Error
            });
    }

    private static async Task RegisterAsync(
        string packId,
        EnemyDefinition definition,
        string packFolder)
    {
        string id = packId + ":" + definition.id;
        string prefabName = "prefab_enemy_custom_" + Sanitize(packId) + "_" + Sanitize(definition.id!);
        if (Plugin.Enemies.ContainsKey(id))
            throw new InvalidDataException($"Duplicate enemy ID '{id}'.");

        GameObject? source = SerializationManager.GetPrefabFromName(definition.clone!);
        if (source == null)
            throw new InvalidDataException($"Clone prefab '{definition.clone}' does not exist.");
        if (source.GetComponent<Enemy>() == null)
            throw new InvalidDataException($"Clone prefab '{definition.clone}' is not an enemy.");

        GameObject? template = null;
        var bossTemplates = new Dictionary<string, GameObject>(
            StringComparer.OrdinalIgnoreCase);
        var timedTemplates = new Dictionary<string, GameObject>(
            StringComparer.OrdinalIgnoreCase);
        var audioKeys = new List<string>();
        var projectileSpriteKeys = new List<string>();
        var bossAudioRegistrations = new List<AudioClipRegistration>();
        IReadOnlyList<SerializablePrefabRegistration> prefabRegistrations =
            Array.Empty<SerializablePrefabRegistration>();
        try
        {
            bool sourceActive = source.activeSelf;
            source.SetActive(false);
            try
            {
                template = UnityEngine.Object.Instantiate(source);
            }
            finally
            {
                source.SetActive(sourceActive);
            }

            template.name = prefabName;
            template.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(template);
            template.hideFlags = HideFlags.HideAndDontSave;

            CustomEnemyIdentity identity = template.AddComponent<CustomEnemyIdentity>();
            identity.definitionId = id;
            audioKeys = await CustomEnemyAudio.LoadForEnemyAsync(
                id, definition, packFolder);
            projectileSpriteKeys = CustomEnemyProjectileSprites.LoadForEnemy(
                id, definition, packFolder);
            ApplyDefinition(template, definition);
            await EnemyVisualLoader.ApplyAsync(template, definition.visuals!, packFolder, id);

            foreach (BossSpawnDefinition boss in definition.bossSpawns!)
            {
                bossAudioRegistrations.AddRange(
                    await CustomEnemyBossAudio.LoadForBossAsync(
                        id, boss, packFolder));
                string bossPrefabName = prefabName + "_boss_" + Sanitize(boss.id!);
                GameObject bossTemplate = UnityEngine.Object.Instantiate(template);
                bossTemplate.name = bossPrefabName;
                bossTemplate.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(bossTemplate);
                bossTemplate.hideFlags = HideFlags.HideAndDontSave;
                var bossIdentity = bossTemplate.AddComponent<CustomEnemyBossIdentity>();
                bossIdentity.bossKey = BuildBossKey(id, boss.id!);
                bossIdentity.respawnAfterDeath = boss.respawnAfterDeath;
                bossIdentity.encounterMusicClipId = boss.EncounterMusicClipId;
                bossIdentity.encounterMusicPriority = boss.bossMusic?.priority ?? 500;
                bossIdentity.encounterMusicVolume = boss.bossMusic?.volume ?? 0.8f;
                bossIdentity.encounterMusicFadeSeconds =
                    boss.bossMusic?.fadeSeconds ?? 1f;
                bossIdentity.deathSongClipId = boss.DeathSongClipId;
                bossIdentity.deathSongVolume = boss.deathSong?.volume ?? 0.8f;
                bossIdentity.deathSongMinRange = boss.deathSong?.minRange ?? 0f;
                bossIdentity.deathSongMaxRange = boss.deathSong?.maxRange ?? 25f;
                bossTemplates.Add(boss.id!, bossTemplate);
            }

            foreach (SilverpineTimedSpawnDefinition timed in
                     definition.silverpineTimedSpawns!)
            {
                string timedPrefabName = prefabName + "_timed_" + Sanitize(timed.id!);
                GameObject timedTemplate = UnityEngine.Object.Instantiate(template);
                timedTemplate.name = timedPrefabName;
                timedTemplate.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(timedTemplate);
                timedTemplate.hideFlags = HideFlags.HideAndDontSave;
                var timedIdentity = timedTemplate.AddComponent<CustomEnemyTimedIdentity>();
                timedIdentity.ruleKey = BuildTimedRuleKey(id, timed.id!);
                timedTemplates.Add(timed.id!, timedTemplate);
            }

            using (PrefabRegistrationBatch batch =
                   SerializablePrefabs.BeginBatch(Plugin.Guid))
            {
                batch.Add(prefabName, template);
                foreach (GameObject bossTemplate in bossTemplates.Values)
                    batch.Add(bossTemplate.name, bossTemplate);
                foreach (GameObject timedTemplate in timedTemplates.Values)
                    batch.Add(timedTemplate.name, timedTemplate);
                prefabRegistrations = batch.Commit();
            }
            Plugin.Enemies.Add(id, new LoadedEnemy(
                id, definition, template, bossTemplates, timedTemplates,
                bossAudioRegistrations));
            Plugin.Log.LogInfo($"Registered custom enemy '{definition.name}' ({id}).");
        }
        catch
        {
            for (int index = prefabRegistrations.Count - 1;
                 index >= 0;
                 index--)
                prefabRegistrations[index].Unregister(
                    destroyTemplate: false);
            if (template != null)
                UnityEngine.Object.DestroyImmediate(template);
            foreach (GameObject bossTemplate in bossTemplates.Values)
                UnityEngine.Object.DestroyImmediate(bossTemplate);
            foreach (GameObject timedTemplate in timedTemplates.Values)
                UnityEngine.Object.DestroyImmediate(timedTemplate);
            CustomEnemyAudio.Release(audioKeys);
            CustomEnemyProjectileSprites.Release(projectileSpriteKeys);
            for (int index = bossAudioRegistrations.Count - 1;
                 index >= 0;
                 index--)
                bossAudioRegistrations[index].Unregister();
            throw;
        }
    }

    private static void ApplyDefinition(GameObject template, EnemyDefinition definition)
    {
        Damagable damageable = template.GetComponent<Damagable>();
        if (definition.maxHp > 0)
        {
            damageable.MaxHP = definition.maxHp;
            damageable.HP = definition.maxHp;
        }

        if (template.TryGetComponent<NPCVisibleObject>(out var visible))
            visible.description = string.IsNullOrWhiteSpace(definition.description)
                ? "a " + definition.name
                : definition.description!;

        Traverse enemy = Traverse.Create(template.GetComponent<Enemy>());
        enemy.Field("passive").SetValue(definition.passive);
        enemy.Field("abilitySet").SetValue(AbilitySet.None);

        if (definition.passive)
        {
            var passiveController = template.AddComponent<CustomEnemyPassiveController>();
            passiveController.fleeBehavior = definition.ParsedFleeBehavior;
        }

        if (definition.pathfinding != null)
        {
            PathfindingEntity? pathfinding = template.GetComponent<PathfindingEntity>();
            if (pathfinding == null)
                throw new InvalidDataException(
                    $"Clone prefab '{definition.clone}' has no PathfindingEntity component.");
            pathfinding.speedBonus = definition.pathfinding.speedBonus;
        }

        if (definition.loot != null)
        {
            if (definition.loot.ParsedMode == LootMode.Replace)
            {
                LootDropper? cloneDropper = template.GetComponent<LootDropper>();
                if (cloneDropper != null)
                    UnityEngine.Object.DestroyImmediate(cloneDropper);
            }

            List<LootEntryDefinition> entries = definition.loot.entries!;
            var customDropper = template.AddComponent<CustomEnemyLootDropper>();
            customDropper.items = entries
                .Select(entry => entry.item!)
                .ToArray();
            customDropper.tiers = entries
                .Select(entry => entry.tier ?? "")
                .ToArray();
            customDropper.weights = entries
                .Select(entry => entry.weight)
                .ToArray();
            customDropper.minAmount = definition.loot.min;
            customDropper.maxAmount = definition.loot.max;

            foreach (LootEntryDefinition entry in entries)
            {
                if (!CustomEnemyItemResolver.TryCreateItem(
                        entry.item!, entry.tier, out _))
                    Plugin.Log.LogWarning(
                        $"Loot item '{entry.item}' for custom enemy '{definition.id}' " +
                        "is not registered yet; it will be resolved again when loot drops.");
            }
        }
    }

    internal static GameObject Spawn(
        LoadedEnemy loaded,
        Vector2Int position,
        GameObject? sourceTemplate = null)
    {
        GameObject spawnTemplate = sourceTemplate ?? loaded.Template;
        GameObject instance = UnityEngine.Object.Instantiate(
            spawnTemplate,
            new Vector3(position.x, position.y, spawnTemplate.transform.position.z),
            spawnTemplate.transform.rotation);
        instance.hideFlags = HideFlags.None;
        instance.SetActive(true);
        if (instance.TryGetComponent<TurfRegistrar>(out var registrar))
            registrar.Register();
        return instance;
    }

    private static string Sanitize(string value) =>
        new(value.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_').ToArray());

    internal static string BuildBossKey(string enemyId, string bossId) =>
        enemyId + ":boss:" + bossId;

    internal static string BuildTimedRuleKey(string enemyId, string timedId) =>
        enemyId + ":timed:" + timedId;
}

internal static class EnemyVisualLoader
{
    private static readonly string[] DirectionNames =
        { "down", "left", "up", "right" };

    internal static async Task ApplyAsync(
        GameObject template,
        VisualDefinition visuals,
        string packFolder,
        string id)
    {
        EntityMover mover = template.GetComponent<EntityMover>();
        SpriteRenderer renderer = template.GetSpriteRendererExtended();
        // Covers single images, directional images, and generated/distributed
        // GLB caches before any visual-mode early return. Instances inherit it.
        EnemySpriteLighting.ApplyDiffuse(renderer);

        if (!string.IsNullOrWhiteSpace(visuals.image))
        {
            Sprite sprite = LoadSprite(Resolve(packFolder, visuals.image!), id + ":single",
                visuals, 0);
            ClearDirections(mover);
            mover.nonDirectionalSprite = sprite;
            renderer.sprite = sprite;
            return;
        }

        Sprite[] directional;
        if (visuals.directionalImages != null)
        {
            directional = LoadDirections(new[] { visuals.directionalImages.down!,
                visuals.directionalImages.left!, visuals.directionalImages.up!, visuals.directionalImages.right! }
                .Select(path => Resolve(packFolder, path)).ToArray(), visuals, id);
        }
        else
        {
            string modelPath = ResolveModelPath(
                packFolder, id, visuals.model!);
            string cache = GetCacheDirectory(packFolder, id);
            if (File.Exists(modelPath))
                directional = TryLoadCache(cache, modelPath, visuals, id)
                    ?? await RenderAndCacheAsync(cache, modelPath, visuals, id);
            else
            {
                directional = TryLoadDistributedCache(cache, visuals, id)
                    ?? throw new FileNotFoundException(
                        "The enemy GLB is absent and its distributed cache is " +
                        $"incomplete. Include down.png, left.png, up.png, and " +
                        $"right.png under '{cache}'.",
                        modelPath);
                Plugin.Log.LogInfo(
                    $"Loaded distributed directional cache for custom enemy '{id}' " +
                    "without its authoring GLB.");
            }
        }

        mover.downSprite = directional[0];
        mover.leftSprite = directional[1];
        mover.upSprite = directional[2];
        mover.rightSprite = directional[3];
        mover.nonDirectionalSprite = directional[0];
        renderer.sprite = directional[0];
    }

    internal static async Task<Sprite[]> GenerateDistributionCacheAsync(
        string packFolder,
        string id,
        VisualDefinition visuals,
        Action? ensureCurrent = null)
    {
        string modelPath = ResolveExistingModel(
            packFolder, id, visuals.model!);
        return await RenderAndCacheAsync(
            GetCacheDirectory(packFolder, id),
            modelPath,
            visuals,
            id, ensureCurrent);
    }

    internal static void ValidateDistributableModelSource(
        string packFolder,
        string id,
        VisualDefinition visuals)
    {
        string embeddedPath = ResolveContainedPath(
            packFolder, visuals.model!);
        if (File.Exists(embeddedPath) ||
            HasCompleteDirectionalCache(GetCacheDirectory(packFolder, id)))
            return;
        throw new FileNotFoundException(
            "The distributable enemy pack contains neither its GLB nor a " +
            $"complete directional cache for '{id}'. Use Build Cache + " +
            "Separate GLB before saving.",
            embeddedPath);
    }

    internal static string ImportAuthoringModel(
        string selectedPath,
        string id)
    {
        return SharedModelSources.Import(selectedPath);
    }

    internal static string PrepareModelSeparation(
        string packFolder,
        string id,
        string modelReference,
        out string? embeddedSource)
    {
        string embeddedPath = ResolveContainedPath(packFolder, modelReference);
        string source = ResolveExistingModel(packFolder, id, modelReference);
        if (!HasCompleteDirectionalCache(GetCacheDirectory(packFolder, id)))
            throw new InvalidDataException("Build all four cached views before separating the GLB.");
        embeddedSource = File.Exists(embeddedPath) ? embeddedPath : null;
        // Do not remove an embedded source until the updated JSON is saved. Other
        // enemies may still reference it, and the user can discard this edit.
        return SharedModelSources.Import(source);
    }

    internal static string GetCacheDirectory(string packFolder, string id) =>
        Path.Combine(packFolder, ".cache", EnemyPackLoaderName(id));

    internal static string GetAuthoringModelPath(
        string id,
        string modelReference) =>
        Path.Combine(
            Plugin.AuthoringDirectory,
            EnemyPackLoaderName(id),
            Path.GetFileName(modelReference));

    private static async Task<Sprite[]> RenderAndCacheAsync(
        string cache,
        string modelPath,
        VisualDefinition visuals,
        string id,
        Action? ensureCurrent = null)
    {
        string modelHash = SharedArtLibrary.FileHash(modelPath);
        Sprite[] rendered = await EnemyGlbDirectionalRenderer.RenderAsync(
            modelPath, visuals.Rotation, visuals.alphaCutoff, id);

        try
        {
            ensureCurrent?.Invoke();
            if (SharedArtLibrary.FileHash(modelPath) != modelHash)
                throw new OperationCanceledException("The GLB source changed during rendering. Build it again.");
            DirectionalCacheFiles.Write(cache, rendered.Select(sprite => sprite.texture.EncodeToPNG()).ToArray(),
                JsonConvert.SerializeObject(new RenderManifest
                {
                    modelWriteUtcTicks = File.GetLastWriteTimeUtc(modelPath).Ticks,
                    modelSha256 = modelHash,
                    rotation = visuals.rotation,
                    alphaCutoff = visuals.alphaCutoff,
                    rendererVersion = 5
                }, Formatting.Indented), ensureCurrent);
        }
        finally
        {
            foreach (Sprite sprite in rendered)
            {
                UnityEngine.Object.Destroy(sprite.texture);
                UnityEngine.Object.Destroy(sprite);
            }
        }

        // Load independent PNG-backed assets so runtime ownership is identical
        // for cached and newly rendered models.
        return LoadDirections(DirectionNames.Select(name => Path.Combine(cache, name + ".png")).ToArray(), visuals, id);
    }

    private static Sprite[]? TryLoadCache(
        string cache,
        string modelPath,
        VisualDefinition visuals,
        string id)
    {
        if (!visuals.cache)
            return null;
        string manifestPath = Path.Combine(cache, "manifest.json");
        if (!File.Exists(manifestPath))
            return null;
        RenderManifest? manifest;
        try { manifest = JsonConvert.DeserializeObject<RenderManifest>(File.ReadAllText(manifestPath)); }
        catch (JsonException) { return null; }
        if (manifest == null ||
            manifest.rendererVersion != 5 ||
            (manifest.modelSha256 != null
                ? manifest.modelSha256 != SharedArtLibrary.FileHash(modelPath)
                : manifest.modelWriteUtcTicks != File.GetLastWriteTimeUtc(modelPath).Ticks) ||
            manifest.alphaCutoff != visuals.alphaCutoff ||
            !SameRotation(manifest.rotation, visuals.rotation))
            return null;

        if (!HasCompleteDirectionalCache(cache))
            return null;
        return LoadDirections(DirectionNames.Select(name => Path.Combine(cache, name + ".png")).ToArray(), visuals, id);
    }

    private static Sprite[]? TryLoadDistributedCache(
        string cache,
        VisualDefinition visuals,
        string id)
    {
        if (!HasCompleteDirectionalCache(cache))
            return null;
        return LoadDirections(DirectionNames.Select(name => Path.Combine(cache, name + ".png")).ToArray(), visuals, id);
    }

    private static bool HasCompleteDirectionalCache(string cache) =>
        DirectionNames.All(name =>
            File.Exists(Path.Combine(cache, name + ".png")));

    private static Sprite[] LoadDirections(string[] paths, VisualDefinition visuals, string id)
    {
        var result = new List<Sprite>();
        try
        {
            for (int i = 0; i < paths.Length; i++)
                result.Add(LoadSprite(paths[i], id + ":" + DirectionNames[i], visuals, i));
            return result.ToArray();
        }
        catch
        {
            foreach (Sprite sprite in result)
            { UnityEngine.Object.Destroy(sprite.texture); UnityEngine.Object.Destroy(sprite); }
            throw;
        }
    }

    internal static Vector2 ResolvePivot(int width, int height, VisualDefinition visuals, int direction, bool includeOffset = true)
    {
        float scale = Mathf.Max(.1f, visuals.scale);
        Vector2 pivot = visuals.HasPivot ? visuals.Pivot : new Vector2(.5f, 16f / (height * scale));
        VisualOffset offset = includeOffset ? visuals.offsets?.Get(direction) ?? new VisualOffset() : new VisualOffset();
        return new Vector2(VisualOffset.ShiftPivot(pivot.x, width, scale, offset.x),
            VisualOffset.ShiftPivot(pivot.y, height, scale, offset.y));
    }

    private static Sprite LoadSprite(
        string path,
        string name,
        VisualDefinition visuals,
        int direction)
    {
        Texture2D texture = new(2, 2, TextureFormat.ARGB32, false);
        try
        {
            if (!texture.LoadImage(File.ReadAllBytes(path), false))
                throw new InvalidDataException("Unity could not decode image: " + path);
            texture.name = name;
            texture.filterMode = FilterMode.Point;
            // Turf transforms sit at tile centers. Keep the sprite's bottom edge
            // half a world unit below that center regardless of visual scale.
            Vector2 resolvedPivot = ResolvePivot(texture.width, texture.height, visuals, direction);
            Sprite sprite = Sprite.Create(texture,
                new Rect(0, 0, texture.width, texture.height),
                resolvedPivot,
                32f / visuals.scale);
            sprite.name = name;
            return sprite;
        }
        catch { UnityEngine.Object.Destroy(texture); throw; }
    }

    private static void ClearDirections(EntityMover mover)
    {
        mover.upSprite = null;
        mover.downSprite = null;
        mover.leftSprite = null;
        mover.rightSprite = null;
    }

    private static string Resolve(string root, string relative)
    {
        string result = ResolveContainedPath(root, relative);
        if (!File.Exists(result))
            throw new FileNotFoundException("Enemy asset was not found.", result);
        return result;
    }

    private static string ResolveContainedPath(string root, string relative)
    {
        if (Path.IsPathRooted(relative))
            throw new InvalidDataException("Asset paths must be relative to the pack JSON.");
        string basePath = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string result = Path.GetFullPath(Path.Combine(root, relative));
        if (!result.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Asset paths cannot leave the pack folder.");
        return result;
    }

    internal static string ResolveModelPath(
        string packFolder,
        string id,
        string modelReference)
    {
        string embeddedPath = ResolveContainedPath(packFolder, modelReference);
        if (File.Exists(embeddedPath))
            return embeddedPath;
        string authoringPath = GetAuthoringModelPath(id, modelReference);
        return File.Exists(authoringPath) ? authoringPath : SharedModelSources.Resolve(modelReference);
    }

    private static string ResolveExistingModel(
        string packFolder,
        string id,
        string modelReference)
    {
        string result = ResolveModelPath(packFolder, id, modelReference);
        if (!File.Exists(result))
            throw new FileNotFoundException(
                "Enemy GLB authoring source was not found in the separate " +
                "authoring store or the pack.",
                result);
        return result;
    }

    private static string EnemyPackLoaderName(string id) =>
        new(id.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    private static bool SameRotation(float[]? left, float[]? right)
    {
        float[] a = left is { Length: 3 } ? left : new[] { 0f, 0f, 0f };
        float[] b = right is { Length: 3 } ? right : new[] { 0f, 0f, 0f };
        return a.SequenceEqual(b);
    }

    private sealed class RenderManifest
    {
        public long modelWriteUtcTicks;
        public string? modelSha256;
        public float[]? rotation;
        public float alphaCutoff;
        public int rendererVersion;
    }
}

internal static class EnemyGlbDirectionalRenderer
{
    private const int PreviewLayer = 31;
    private const int PreviewLayerMask = 1 << PreviewLayer;
    private const int Resolution = 512;

    internal static async Task<Sprite[]> RenderAsync(
        string path,
        Vector3 rotationBias,
        float alphaCutoff,
        string name)
    {
        var import = new GltfImport();
        var temporary = new List<UnityEngine.Object>();
        var output = new List<Sprite>();
        RenderTexture? previous = RenderTexture.active;

        try
        {
            if (!await import.Load(path))
                throw new InvalidDataException("glTFast could not import the GLB.");

            GameObject pivot = new("CustomEnemyDirectionalPivot");
            temporary.Add(pivot);
            GameObject model = new("CustomEnemyDirectionalModel");
            model.transform.SetParent(pivot.transform, false);
            GameObject scene = new("CustomEnemyDirectionalScene");
            scene.transform.SetParent(model.transform, false);
            if (!await import.InstantiateMainSceneAsync(scene.transform))
                throw new InvalidDataException(
                    "glTFast could not instantiate the GLB's main scene.");

            Renderer[] renderers =
                scene.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers.Length == 0)
                throw new InvalidDataException(
                    "The GLB main scene contains no renderers.");
            foreach (Renderer renderer in renderers)
                renderer.enabled = true;
            RemapMaterials(import, renderers, temporary);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            float largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (largest <= 0.0001f)
                throw new InvalidDataException(
                    "The GLB scene has zero-size bounds.");

            Vector3 stage = new(400, 400, 400);
            scene.transform.localPosition = -bounds.center;
            pivot.transform.position = stage;
            model.transform.localEulerAngles = rotationBias;
            model.transform.localScale = Vector3.one / largest;
            SetLayerRecursively(pivot, PreviewLayer);

            GameObject cameraObject = new("CustomEnemyDirectionalCamera");
            temporary.Add(cameraObject);
            cameraObject.transform.position = stage + new Vector3(0, 0, 2);
            cameraObject.transform.eulerAngles = new Vector3(0, 180, 0);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 0.65f;
            camera.cullingMask = PreviewLayerMask;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;

            AddLight(temporary, stage + new Vector3(2, 2, 2), 1.4f);
            AddLight(temporary, stage + new Vector3(-2, 1, 2), 0.8f);
            AddLight(temporary, stage + new Vector3(0, -2, 1), 0.35f);

            RenderTexture renderTexture = new(
                Resolution,
                Resolution,
                24,
                RenderTextureFormat.ARGB32);
            temporary.Add(renderTexture);
            renderTexture.filterMode = FilterMode.Point;
            renderTexture.Create();
            camera.targetTexture = renderTexture;

            string[] directions = { "down", "left", "up", "right" };
            for (int i = 0; i < directions.Length; i++)
            {
                // Silverpine's established ordering rotates the parent 90 degrees
                // before each capture: down, left, up, then right.
                pivot.transform.localRotation =
                    Quaternion.Euler(0, 90f * (i + 1), 0);
                camera.Render();

                RenderTexture.active = renderTexture;
                Texture2D full = new(
                    Resolution,
                    Resolution,
                    TextureFormat.ARGB32,
                    false);
                temporary.Add(full);
                full.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
                full.Apply();
                if (!HasVisiblePixels(full))
                    throw new InvalidDataException(
                        $"The GLB {directions[i]} render was fully transparent.");

                Texture2D cropped = TextureCropper.CropToAlpha(full);
                if (cropped == null || cropped.width <= 1 || cropped.height <= 1)
                    throw new InvalidDataException(
                        $"The GLB {directions[i]} render was empty.");
                temporary.Add(cropped);

                Texture2D final = FantResize.DownsampleImage(
                    cropped,
                    (float)cropped.height / 32f,
                    alphaCutoff);
                if (final == null || final.width <= 1 || final.height <= 1)
                    throw new InvalidDataException(
                        $"The GLB {directions[i]} render could not be downsampled.");
                final.name = name + ":" + directions[i];
                final.filterMode = FilterMode.Point;
                Sprite sprite = Sprite.Create(
                    final,
                    new Rect(0, 0, final.width, final.height),
                    new Vector2(0.5f, 16f / final.height),
                    32f);
                sprite.name = final.name;
                output.Add(sprite);
            }

            return output.ToArray();
        }
        catch
        {
            foreach (Sprite sprite in output)
            {
                if (sprite != null)
                {
                    UnityEngine.Object.Destroy(sprite.texture);
                    UnityEngine.Object.Destroy(sprite);
                }
            }
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            foreach (UnityEngine.Object value in temporary)
                if (value != null)
                    UnityEngine.Object.Destroy(value);
            import.Dispose();
        }
    }

    private static bool HasVisiblePixels(Texture2D texture) =>
        texture.GetPixels32().Any(pixel => pixel.a > 0);

    private static void RemapMaterials(
        GltfImport import,
        IEnumerable<Renderer> renderers,
        ICollection<UnityEngine.Object> temporary)
    {
        Material fallbackMaterial =
            Resources.Load<Material>("Materials/material_glbimport");
        Shader standardShader = Shader.Find("Standard");
        if (standardShader == null && fallbackMaterial == null)
            throw new InvalidDataException(
                "Neither Unity's Standard shader nor Silverpine's runtime GLB " +
                "material is available.");

        List<Texture2D> fallbackTextures = new();
        for (int i = 0; i < import.TextureCount; i++)
        {
            Texture2D texture = import.GetTexture(i);
            if (texture != null &&
                texture.width > 2 &&
                !texture.name.ContainsAnyIgnoreCase(
                    "normal",
                    "metal",
                    "rough",
                    "specular",
                    "smooth",
                    "emission"))
                fallbackTextures.Add(texture);
        }

        int fallbackIndex = 0;
        int sourceSlotIndex = 0;
        foreach (Renderer renderer in renderers)
        {
            Material[] imported = renderer.sharedMaterials;
            Material[] safe = new Material[imported.Length];
            for (int slot = 0; slot < imported.Length; slot++)
            {
                Material original = imported[slot];
                int sourceMaterialIndex =
                    FindSourceMaterialIndex(
                        import,
                        original,
                        sourceSlotIndex);
                sourceSlotIndex++;
                Texture? texture = GetBaseColorTexture(
                    import,
                    sourceMaterialIndex,
                    original);
                if (texture == null && fallbackTextures.Count > 0)
                {
                    texture = fallbackTextures[
                        Mathf.Min(fallbackIndex, fallbackTextures.Count - 1)];
                    fallbackIndex++;
                }

                Material replacement = standardShader != null
                    ? new Material(standardShader)
                    : new Material(fallbackMaterial);
                replacement.name = "CustomEnemy_" +
                    (original != null ? original.name : $"Material_{slot}");
                temporary.Add(replacement);
                replacement.mainTexture = texture;

                Color color = GetBaseColor(
                    import,
                    original,
                    sourceMaterialIndex);
                ApplyBaseColor(replacement, color);

                if (sourceMaterialIndex >= 0)
                {
                    var source = import.GetSourceMaterial(sourceMaterialIndex);
                    var pbr = source?.PbrMetallicRoughness;
                    if (pbr != null)
                    {
                        if (replacement.HasProperty("_Metallic"))
                            replacement.SetFloat(
                                "_Metallic",
                                Mathf.Clamp01(pbr.metallicFactor));
                        if (replacement.HasProperty("_Glossiness"))
                            replacement.SetFloat(
                                "_Glossiness",
                                1f - Mathf.Clamp01(pbr.roughnessFactor));
                    }
                    if (source != null)
                        ConfigureAlpha(replacement, source);
                }

                if (original != null)
                    CopyTextureTransform(original, replacement);
                safe[slot] = replacement;
            }
            renderer.sharedMaterials = safe;
        }
    }

    private static int FindSourceMaterialIndex(
        GltfImport import,
        Material? material,
        int sourceSlotIndex)
    {
        if (!ReferenceEquals(material, null))
        {
            for (int i = 0; i < import.MaterialCount; i++)
                if (ReferenceEquals(import.GetMaterial(i), material) ||
                    import.GetMaterial(i) == material)
                    return i;

            for (int i = 0; i < import.MaterialCount; i++)
            {
                Material imported = import.GetMaterial(i);
                if (!ReferenceEquals(imported, null) &&
                    string.Equals(
                        imported.name,
                        material.name,
                        StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            const string generatedPrefix = "Material_";
            int marker = material.name.LastIndexOf(
                generatedPrefix,
                StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
            {
                string suffix = new(
                    material.name
                        .Substring(marker + generatedPrefix.Length)
                        .TakeWhile(char.IsDigit)
                        .ToArray());
                if (int.TryParse(suffix, out int generatedIndex) &&
                    generatedIndex >= 0 &&
                    generatedIndex < import.MaterialCount)
                    return generatedIndex;
            }
        }

        return sourceSlotIndex >= 0 && sourceSlotIndex < import.MaterialCount
            ? sourceSlotIndex
            : -1;
    }

    private static Texture? GetBaseColorTexture(
        GltfImport import,
        int sourceMaterialIndex,
        Material? material)
    {
        if (sourceMaterialIndex >= 0)
        {
            var textureInfo = import
                .GetSourceMaterial(sourceMaterialIndex)?
                .PbrMetallicRoughness?
                .BaseColorTexture;
            if (textureInfo != null && textureInfo.index >= 0)
            {
                Texture2D sourceTexture = import.GetTexture(textureInfo.index);
                if (sourceTexture != null)
                    return sourceTexture;
            }
        }

        if (material == null)
            return null;

        string[] properties =
            { "baseColorTexture", "diffuseTexture", "_BaseMap", "_MainTex" };
        foreach (string property in properties)
            if (material.HasProperty(property))
            {
                Texture texture = material.GetTexture(property);
                if (texture != null)
                    return texture;
            }
        return material.mainTexture;
    }

    private static Color GetBaseColor(
        GltfImport import,
        Material? material,
        int sourceMaterialIndex)
    {
        if (sourceMaterialIndex >= 0)
        {
            var source = import.GetSourceMaterial(sourceMaterialIndex);
            if (source?.PbrMetallicRoughness != null)
                return source.PbrMetallicRoughness.BaseColor;
        }

        if (ReferenceEquals(material, null))
            return Color.white;

        string[] properties =
            { "baseColorFactor", "diffuseFactor", "_BaseColor", "_Color" };
        foreach (string property in properties)
            if (material.HasProperty(property))
                return material.GetColor(property);
        return Color.white;
    }

    private static void ApplyBaseColor(Material material, Color color)
    {
        string[] properties =
            { "_Color", "_BaseColor", "baseColorFactor", "diffuseFactor" };
        foreach (string property in properties)
            if (material.HasProperty(property))
                material.SetColor(property, color);
    }

    private static void ConfigureAlpha(
        Material material,
        GLTFast.Schema.MaterialBase source)
    {
        if (source.doubleSided && material.HasProperty("_Cull"))
            material.SetInt("_Cull", (int)CullMode.Off);

        switch (source.GetAlphaMode())
        {
            case GLTFast.Schema.MaterialBase.AlphaMode.Mask:
                if (material.HasProperty("_Mode"))
                    material.SetFloat("_Mode", 1f);
                if (material.HasProperty("_Cutoff"))
                    material.SetFloat("_Cutoff", source.alphaCutoff);
                if (material.HasProperty("_SrcBlend"))
                    material.SetInt("_SrcBlend", (int)BlendMode.One);
                if (material.HasProperty("_DstBlend"))
                    material.SetInt("_DstBlend", (int)BlendMode.Zero);
                if (material.HasProperty("_ZWrite"))
                    material.SetInt("_ZWrite", 1);
                material.EnableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = (int)RenderQueue.AlphaTest;
                break;

            case GLTFast.Schema.MaterialBase.AlphaMode.Blend:
                if (material.HasProperty("_Mode"))
                    material.SetFloat("_Mode", 2f);
                if (material.HasProperty("_SrcBlend"))
                    material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                if (material.HasProperty("_DstBlend"))
                    material.SetInt(
                        "_DstBlend",
                        (int)BlendMode.OneMinusSrcAlpha);
                if (material.HasProperty("_ZWrite"))
                    material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = (int)RenderQueue.Transparent;
                break;

            default:
                if (material.HasProperty("_Mode"))
                    material.SetFloat("_Mode", 0f);
                if (material.HasProperty("_SrcBlend"))
                    material.SetInt("_SrcBlend", (int)BlendMode.One);
                if (material.HasProperty("_DstBlend"))
                    material.SetInt("_DstBlend", (int)BlendMode.Zero);
                if (material.HasProperty("_ZWrite"))
                    material.SetInt("_ZWrite", 1);
                material.DisableKeyword("_ALPHATEST_ON");
                material.DisableKeyword("_ALPHABLEND_ON");
                material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                material.renderQueue = -1;
                break;
        }
    }

    private static void CopyTextureTransform(
        Material original,
        Material replacement)
    {
        if (original.HasProperty("baseColorTexture_ST"))
        {
            Vector4 transform = original.GetVector("baseColorTexture_ST");
            replacement.mainTextureScale =
                new Vector2(transform.x, transform.y);
            replacement.mainTextureOffset =
                new Vector2(transform.z, transform.w);
        }
        else if (original.HasProperty("_MainTex"))
        {
            replacement.mainTextureScale =
                original.GetTextureScale("_MainTex");
            replacement.mainTextureOffset =
                original.GetTextureOffset("_MainTex");
        }
    }

    private static void AddLight(
        ICollection<UnityEngine.Object> temporary,
        Vector3 position,
        float intensity)
    {
        GameObject value = new("CustomEnemyDirectionalLight");
        temporary.Add(value);
        value.transform.position = position;
        Light light = value.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 10f;
        light.intensity = intensity;
        light.cullingMask = PreviewLayerMask;
    }

    private static void SetLayerRecursively(GameObject value, int layer)
    {
        value.layer = layer;
        foreach (Transform child in value.transform)
            SetLayerRecursively(child.gameObject, layer);
    }
}

[HarmonyPatch(typeof(SubmergeVisualsHandler), nameof(SubmergeVisualsHandler.OnSpriteSet))]
internal static class CustomEnemySubmergeVisualsPatch
{
    private static readonly HashSet<int> WarnedInstances = new();

    private static bool Prefix(SubmergeVisualsHandler __instance, ref Sprite sprite)
    {
        if (sprite != null ||
            !__instance.TryGetComponent<CustomEnemyIdentity>(out _))
            return true;

        EntityMover mover = __instance.GetComponent<EntityMover>();
        Sprite? fallback = FirstUsable(
            mover.nonDirectionalSprite,
            mover.downSprite,
            mover.leftSprite,
            mover.upSprite,
            mover.rightSprite);

        int instanceId = __instance.GetInstanceID();
        if (fallback != null)
        {
            sprite = fallback;
            if (WarnedInstances.Add(instanceId))
                Plugin.Log.LogWarning(
                    "Recovered a missing directional sprite for custom enemy '" +
                    __instance.name + "' before applying submerged visuals.");
            return true;
        }

        if (WarnedInstances.Add(instanceId))
            Plugin.Log.LogError(
                "Skipped submerged visual processing for custom enemy '" +
                __instance.name + "' because it has no usable sprites.");
        return false;
    }

    private static Sprite? FirstUsable(params Sprite[] sprites)
    {
        foreach (Sprite candidate in sprites)
        {
            if (candidate != null)
                return candidate;
        }
        return null;
    }
}

[HarmonyPatch(typeof(Enemy), "Awake")]
internal static class CustomEnemyAbilityPatch
{
    private static void Postfix(Enemy __instance)
    {
        if (!__instance.TryGetComponent<CustomEnemyIdentity>(out var identity) ||
            !Plugin.Enemies.TryGetValue(identity.definitionId, out var loaded))
            return;

        List<Ability> abilities = loaded.Definition.abilities!
            .Select(AbilityFactory.Create)
            .Reverse()
            .ToList();
        foreach (Ability ability in abilities)
            ability.SetOwner(__instance.gameObject);
        Traverse.Create(__instance).Field("abilities").SetValue(abilities);
    }
}

[HarmonyPatch(typeof(EnemyHPBar), nameof(EnemyHPBar.SetTarget))]
internal static class CustomEnemyHealthBarPatch
{
    private static readonly FieldInfo NameLabelField =
        AccessTools.Field(typeof(EnemyHPBar), "nameLabel");

    private static void Postfix(EnemyHPBar __instance, Damagable target)
    {
        if (!target.TryGetComponent<CustomEnemyIdentity>(out var identity) ||
            !Plugin.Enemies.TryGetValue(identity.definitionId, out var loaded))
            return;

        if (NameLabelField.GetValue(__instance) is TextMeshProUGUI nameLabel)
            nameLabel.text = loaded.Definition.name!;
    }
}

[HarmonyPatch(typeof(WorldTileGenerator), nameof(WorldTileGenerator.Generate))]
internal static class CustomEnemyWorldSpawnPatch
{
    private static void Prefix(WorldTileGenerator __instance)
    {
        CustomEnemyWorldSpawner.BeginGeneration(__instance.worldTile);
    }

    private static void Postfix(WorldTileGenerator __instance)
    {
        CustomEnemyWorldSpawner.Populate(__instance.worldTile);
        CustomEnemyBossSpawner.EnsureForTile(__instance.worldTile);
        CustomEnemyTimedSpawner.ReconcileForTile(__instance.worldTile);
    }
}

internal static class CustomEnemyWorldSpawner
{
    private static readonly Dictionary<WorldTile, HashSet<string>> AppliedRules = new();

    internal static void BeginGeneration(WorldTile worldTile)
    {
        AppliedRules[worldTile] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    internal static void PopulateCurrentGeneratedTileAfterRegistration()
    {
        if (DungeonGenerationManager.Instance == null)
            return;
        if (!DungeonGenerationManager.Instance.worldTiles.TryGetValue(
                DungeonGenerationManager.Instance.currentPlayerWorldTilePosition,
                out WorldTile tile))
            return;
        if (!AppliedRules.ContainsKey(tile))
            return;
        Populate(tile);
        CustomEnemyBossSpawner.EnsureForTile(tile);
        CustomEnemyTimedSpawner.ReconcileForTile(tile);
    }

    internal static void Populate(WorldTile worldTile)
    {
        if (!AppliedRules.TryGetValue(worldTile, out HashSet<string> applied))
        {
            applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AppliedRules.Add(worldTile, applied);
        }

        List<Vector2Int> positions = Turfs.GetWorldTileTurfs(worldTile)
            .Where(position => Turfs.IsValidTurf(position))
            .Where(position => !TurfCollider.HasImpassableCollider(position))
            .Where(position => !Turfs.IsOccupiedByCharacter(position))
            .OrderBy(_ => UnityEngine.Random.value)
            .ToList();

        foreach (LoadedEnemy loaded in Plugin.Enemies.Values)
        {
            for (int spawnIndex = 0; spawnIndex < loaded.Definition.spawns!.Count; spawnIndex++)
            {
                SpawnDefinition spawn = loaded.Definition.spawns[spawnIndex];
                if (!spawn.Matches(worldTile.generator))
                    continue;
                string ruleKey = loaded.Id + ":spawn:" + spawnIndex;
                if (!applied.Add(ruleKey))
                    continue;
                int count = UnityEngine.Random.Range(spawn.min, spawn.max + 1);
                for (int i = 0; i < count && positions.Count > 0; i++)
                {
                    int index = UnityEngine.Random.Range(0, positions.Count);
                    Vector2Int position = positions[index];
                    positions.RemoveAt(index);
                    EnemyPackLoader.Spawn(loaded, position);
                }
            }
        }
    }
}

[HarmonyPatch(typeof(WorldTile), nameof(WorldTile.TryLoadFromMemory))]
internal static class CustomEnemyBossTileLoadPatch
{
    private static void Postfix(WorldTile __instance, bool __result)
    {
        if (__result && !SerializationManager.loadingSave)
        {
            CustomEnemyBossSpawner.EnsureForTile(__instance);
            CustomEnemyTimedSpawner.ReconcileForTile(__instance);
        }
    }
}

[HarmonyPatch(typeof(DungeonGenerationManager), "InitializeWorld")]
internal static class CustomEnemyBossNewWorldPatch
{
    private static void Prefix()
    {
        CustomEnemyBossState.ResetForWorldInitialization();
    }
}

internal static class CustomEnemyBossSpawner
{
    private static readonly HashSet<string> WarnedInvalidSpawns =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> WarnedRelocatedSpawns =
        new(StringComparer.OrdinalIgnoreCase);

    internal static void EnsureCurrentTile()
    {
        if (DungeonGenerationManager.Instance != null &&
            DungeonGenerationManager.Instance.worldTiles.TryGetValue(
                DungeonGenerationManager.Instance.currentPlayerWorldTilePosition,
                out WorldTile tile))
            EnsureForTile(tile);
    }

    internal static void EnsureForTile(WorldTile worldTile)
    {
        if (SerializationManager.loadingSave)
            return;

        Dictionary<string, CustomEnemyBossIdentity> activeBosses =
            UnityEngine.Object.FindObjectsOfType<CustomEnemyBossIdentity>()
                .Where(identity => !string.IsNullOrWhiteSpace(identity.bossKey))
                .GroupBy(identity => identity.bossKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(),
                    StringComparer.OrdinalIgnoreCase);

        foreach (LoadedEnemy loaded in Plugin.Enemies.Values)
        {
            foreach (BossSpawnDefinition boss in loaded.Definition.bossSpawns!)
            {
                Vector2Int requestedPosition = new(boss.x, boss.y);
                if (!ContainsPosition(worldTile, requestedPosition))
                    continue;

                string bossKey = EnemyPackLoader.BuildBossKey(loaded.Id, boss.id!);
                if (boss.respawnAfterDeath)
                    CustomEnemyBossState.ClearKilled(bossKey);
                else if (CustomEnemyBossState.IsKilled(bossKey))
                    continue;

                if (activeBosses.TryGetValue(bossKey, out CustomEnemyBossIdentity existing))
                {
                    existing.respawnAfterDeath = boss.respawnAfterDeath;
                    continue;
                }

                if (!TryFindSpawnPosition(
                        worldTile,
                        requestedPosition,
                        out Vector2Int position))
                {
                    if (WarnedInvalidSpawns.Add(bossKey))
                        Plugin.Log.LogError(
                            $"Boss spawn '{bossKey}' found no passable, unoccupied " +
                            $"turf on the world tile containing its requested coordinate " +
                            $"({requestedPosition.x}, {requestedPosition.y}).");
                    continue;
                }

                if (position != requestedPosition &&
                    WarnedRelocatedSpawns.Add(bossKey))
                    Plugin.Log.LogWarning(
                        $"Boss spawn '{bossKey}' could not use requested coordinate " +
                        $"({requestedPosition.x}, {requestedPosition.y}); using nearest " +
                        $"valid turf ({position.x}, {position.y}).");

                if (!loaded.BossTemplates.TryGetValue(boss.id!, out GameObject bossTemplate))
                {
                    Plugin.Log.LogError($"Boss template '{bossKey}' is unavailable.");
                    continue;
                }

                GameObject instance = EnemyPackLoader.Spawn(loaded, position, bossTemplate);
                CustomEnemyBossIdentity identity =
                    instance.GetComponent<CustomEnemyBossIdentity>();
                identity.bossKey = bossKey;
                identity.respawnAfterDeath = boss.respawnAfterDeath;
                activeBosses[bossKey] = identity;
                Plugin.Log.LogInfo(
                    $"Spawned custom boss '{bossKey}' at ({position.x}, {position.y}).");
            }
        }
    }

    internal static bool IsCurrentPlayerTileAt(Vector2Int position)
    {
        DungeonGenerationManager? manager = DungeonGenerationManager.Instance;
        return manager != null &&
               manager.worldTiles.TryGetValue(
                   manager.currentPlayerWorldTilePosition,
                   out WorldTile tile) &&
               ContainsPosition(tile, position);
    }

    internal static bool ContainsPosition(WorldTile tile, Vector2Int position)
    {
        if (tile.IsDungeonInstance())
            return Turfs.GetWorldTileTurfs(tile).Contains(position);
        int minX = tile.position.x * 100 - 50;
        int minY = tile.position.y * 100 - 50;
        return position.x >= minX && position.x < minX + 100 &&
               position.y >= minY && position.y < minY + 100;
    }

    private static bool TryFindSpawnPosition(
        WorldTile worldTile,
        Vector2Int requested,
        out Vector2Int position)
    {
        if (IsValidSpawnPosition(requested))
        {
            position = requested;
            return true;
        }

        bool found = false;
        long bestDistanceSquared = long.MaxValue;
        Vector2Int best = default;
        foreach (Vector2Int candidate in Turfs.GetWorldTileTurfs(worldTile))
        {
            if (!IsValidSpawnPosition(candidate))
                continue;

            long deltaX = (long)candidate.x - requested.x;
            long deltaY = (long)candidate.y - requested.y;
            long distanceSquared = deltaX * deltaX + deltaY * deltaY;
            if (found && (distanceSquared > bestDistanceSquared ||
                          distanceSquared == bestDistanceSquared &&
                          (candidate.y > best.y ||
                           candidate.y == best.y && candidate.x >= best.x)))
                continue;

            found = true;
            bestDistanceSquared = distanceSquared;
            best = candidate;
        }

        position = best;
        return found;
    }

    private static bool IsValidSpawnPosition(Vector2Int position) =>
        Turfs.IsValidTurf(position) &&
        !TurfCollider.HasImpassableCollider(position) &&
        !Turfs.IsOccupiedByCharacter(position);
}

[HarmonyPatch(typeof(WorldInfoManager), "Start")]
internal static class CustomEnemyTimedClockPatch
{
    private static void Postfix(WorldInfoManager __instance)
    {
        CustomEnemyTimedSpawner.AttachClock(__instance);
    }
}

internal static class CustomEnemyTimedSpawner
{
    private static WorldInfoManager? attachedClock;

    internal static void AttachClock(WorldInfoManager clock)
    {
        if (attachedClock == clock)
            return;
        if (attachedClock != null)
            attachedClock.OnNewHour -= OnNewHour;
        attachedClock = clock;
        attachedClock.OnNewHour += OnNewHour;
        ReconcileCurrent();
    }

    internal static void ReconcileCurrent()
    {
        if (DungeonGenerationManager.Instance == null)
            return;
        if (DungeonGenerationManager.Instance.worldTiles.TryGetValue(
                DungeonGenerationManager.Instance.currentPlayerWorldTilePosition,
                out WorldTile tile))
            ReconcileForTile(tile);
    }

    internal static void ReconcileForTile(WorldTile worldTile)
    {
        if (SerializationManager.loadingSave || WorldInfoManager.Instance == null ||
            !string.Equals(worldTile.name, "Silverpine", StringComparison.OrdinalIgnoreCase))
            return;

        int hour = WorldInfoManager.Instance.GetCurrentHourOfDay();
        int day = WorldInfoManager.Instance.GetCurrentDay();
        var activeRules = new List<(
            LoadedEnemy Loaded,
            SilverpineTimedSpawnDefinition Rule,
            string RuleKey,
            int CycleDay)>();
        var activeCycles = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (LoadedEnemy loaded in Plugin.Enemies.Values)
        {
            foreach (SilverpineTimedSpawnDefinition rule in
                     loaded.Definition.silverpineTimedSpawns!)
            {
                string ruleKey = EnemyPackLoader.BuildTimedRuleKey(loaded.Id, rule.id!);
                if (!rule.IsActiveAt(hour))
                    continue;
                int cycleDay = rule.GetCycleDay(day, hour);
                activeRules.Add((loaded, rule, ruleKey, cycleDay));
                activeCycles[ruleKey] = cycleDay;
            }
        }

        foreach (CustomEnemyTimedIdentity identity in
                 UnityEngine.Object.FindObjectsOfType<CustomEnemyTimedIdentity>())
        {
            if (activeCycles.TryGetValue(identity.ruleKey, out int activeCycle) &&
                identity.cycleDay == activeCycle)
                continue;
            GameObject target = identity.gameObject;
            target.SetActive(false);
            UnityEngine.Object.Destroy(target);
            Plugin.Log.LogInfo(
                $"Despawned timed Silverpine enemy for inactive rule '{identity.ruleKey}'.");
        }

        foreach (var active in activeRules)
        {
            if (CustomEnemyBossState.TryStartTimedCycle(
                    active.RuleKey, active.CycleDay))
                SpawnRule(
                    worldTile,
                    active.Loaded,
                    active.Rule,
                    active.RuleKey,
                    active.CycleDay);
        }
    }

    private static void OnNewHour(int _) => ReconcileCurrent();

    private static void SpawnRule(
        WorldTile tile,
        LoadedEnemy loaded,
        SilverpineTimedSpawnDefinition rule,
        string ruleKey,
        int cycleDay)
    {
        if (!loaded.TimedTemplates.TryGetValue(rule.id!, out GameObject template))
        {
            Plugin.Log.LogError($"Timed enemy template '{ruleKey}' is unavailable.");
            return;
        }

        List<Vector2Int> outskirts = GetOutskirts(tile, rule.edgeDepth)
            .OrderBy(_ => UnityEngine.Random.value)
            .ToList();
        int count = UnityEngine.Random.Range(rule.min, rule.max + 1);
        int spawned = 0;
        for (int i = 0; i < count && outskirts.Count > 0; i++)
        {
            int index = UnityEngine.Random.Range(0, outskirts.Count);
            Vector2Int position = outskirts[index];
            outskirts.RemoveAt(index);
            GameObject instance = EnemyPackLoader.Spawn(loaded, position, template);
            CustomEnemyTimedIdentity identity =
                instance.GetComponent<CustomEnemyTimedIdentity>();
            identity.ruleKey = ruleKey;
            identity.cycleDay = cycleDay;
            spawned++;
        }
        Plugin.Log.LogInfo(
            $"Spawned {spawned} timed Silverpine enem{(spawned == 1 ? "y" : "ies")} " +
            $"for '{ruleKey}' during hours {rule.startHour}:00-{rule.endHour}:00.");
    }

    private static IEnumerable<Vector2Int> GetOutskirts(WorldTile tile, int edgeDepth)
    {
        int minX = tile.position.x * 100 - 50;
        int minY = tile.position.y * 100 - 50;
        int maxX = minX + 99;
        int maxY = minY + 99;
        return Turfs.GetWorldTileTurfs(tile)
            .Where(position =>
                position.x - minX < edgeDepth || maxX - position.x < edgeDepth ||
                position.y - minY < edgeDepth || maxY - position.y < edgeDepth)
            .Where(position => Turfs.IsValidTurf(position))
            .Where(position => !TurfCollider.HasImpassableCollider(position))
            .Where(position => !Turfs.IsOccupiedByCharacter(position));
    }
}

[HarmonyPatch(typeof(Enemy), "Flee")]
internal static class CustomEnemyPassiveFleePatch
{
    private static bool Prefix(Enemy __instance)
    {
        CustomEnemyPassiveController? controller =
            __instance.GetComponent<CustomEnemyPassiveController>();
        return controller == null || controller.AllowFlee();
    }
}

internal static class AbilityFactory
{
    internal static Ability Create(AbilityDefinition value)
    {
        string type = Normalize(value.type);
        return type switch
        {
            "basicattack" => new Ability_BasicAttack(
                value.cooldown, value.minPower, value.maxPower,
                value.sound ?? "sound_combat_attack_sword", ParseDamage(value.damageType)),
            "projectile" => string.IsNullOrWhiteSpace(
                                CustomEnemyProjectileSprites.GetSpriteKey(value))
                ? new Ability_Projectile(
                    value.range, value.cooldown, value.minPower, value.maxPower,
                    value.projectile ?? "MiscPrefabs/prefab_projectile_ooze",
                    CreateStatus(value.statusEffect), value.statusDuration,
                    value.sound ?? "sound_combat_attack_sword", ParseDamage(value.damageType))
                : new CustomEnemyProjectileAbility(
                    value.range, value.cooldown, value.minPower, value.maxPower,
                    value.projectile ?? "MiscPrefabs/prefab_projectile_ooze",
                    CustomEnemyProjectileSprites.GetSpriteKey(value),
                    CreateStatus(value.statusEffect), value.statusDuration,
                    value.sound ?? "sound_combat_attack_sword", ParseDamage(value.damageType)),
            "applystatuseffect" => new Ability_ApplyStatusEffect(
                CreateStatus(value.statusEffect) ??
                    throw new InvalidDataException("applyStatusEffect requires statusEffect."),
                value.cooldown, value.minPower, value.maxPower),
            "harmsanity" => new Ability_HarmSanity(
                value.cooldown, value.minPower, value.maxPower),
            "ice" => new Ability_Ice(value.range, value.cooldown, value.chance),
            "lines" => new Ability_Lines(
                value.range, value.cooldown, value.minPower, value.maxPower),
            "screech" => new Ability_Screech(
                value.range, value.cooldown, value.minPower, value.maxPower),
            "sermon" => new Ability_Sermon(
                value.range, value.cooldown, value.minPower, value.maxPower),
            "spawnstunbubbles" => new Ability_SpawnStunBubbles(
                value.range, value.cooldown, value.minPower, value.maxPower,
                value.projectile ?? "prefab_enemy_stunbubble"),
            "avatar" => new Ability_Avatar(
                value.range, value.cooldown, value.minPower, value.maxPower),
            _ => throw new InvalidDataException($"Unknown ability type '{value.type}'.")
        };
    }

    private static StatusEffect? CreateStatus(string? value) =>
        Normalize(value) switch
        {
            "" => null,
            "poison" => new StatusEffect_Poison(),
            "frostpoison" => new StatusEffect_FrostPoison(),
            "burning" => new StatusEffect_Burning(),
            "stunned" => new StatusEffect_Stunned(),
            "confused" => new StatusEffect_Confused(),
            "slowed" => new StatusEffect_Slowed(0.3f),
            _ => throw new InvalidDataException($"Unknown status effect '{value}'.")
        };

    private static DamageType ParseDamage(string? value) =>
        Enum.TryParse(value ?? "Normal", true, out DamageType result) &&
        Enum.IsDefined(typeof(DamageType), result)
            ? result
            : throw new InvalidDataException($"Unknown damage type '{value}'.");

    internal static string Normalize(string? value) =>
        (value ?? "").Replace("_", "").Replace("-", "").Replace(" ", "").ToLowerInvariant();
}

[HarmonyPatch(
    typeof(AudioPlayer),
    nameof(AudioPlayer.PlayAudioAtPosition),
    new[] { typeof(string), typeof(float), typeof(Vector2Int), typeof(bool) })]
internal static class CustomEnemyAudioPlaybackPatch
{
    private static bool Prefix(
        AudioPlayer __instance,
        string soundName,
        float volume,
        Vector2Int position,
        bool visibleOnly)
    {
        if (!CustomEnemyAudio.TryGet(soundName, out AudioClip clip))
            return true;
        __instance.PlayAudioAtPosition(clip, volume, position, visibleOnly);
        return false;
    }
}

[HarmonyPatch(
    typeof(TypeSerializationUtility),
    nameof(TypeSerializationUtility.SerializeWithType))]
internal static class CustomEnemySerializableTypeNamePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo fullNameGetter = AccessTools.PropertyGetter(
            typeof(Type), nameof(Type.FullName));
        MethodInfo replacement = AccessTools.Method(
            typeof(CustomEnemySerializableTypeNamePatch),
            nameof(GetSerializableTypeName));
        int patched = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Call ||
                 instruction.opcode == OpCodes.Callvirt) &&
                Equals(instruction.operand, fullNameGetter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                patched++;
            }
            yield return instruction;
        }
        if (patched != 1)
            Plugin.Log.LogError(
                $"Expected one serialized type-name call, patched {patched}. " +
                "Custom projectile abilities may not restore from saves.");
    }

    private static string GetSerializableTypeName(Type type)
    {
        if (type == typeof(CustomEnemyProjectileAbility))
            return type.FullName + ", " + type.Assembly.GetName().Name;
        return type.FullName;
    }
}

internal sealed class EnemyPackDefinition
{
    public string? packId;
    public List<EnemyDefinition>? enemies;

    internal void Validate(string path)
    {
        if (string.IsNullOrWhiteSpace(packId))
            throw new InvalidDataException($"Pack '{path}' has no packId.");
        packId = packId.Trim();
        ValidateIdentifier(packId, "packId", allowDot: true);
        if (enemies == null || enemies.Count == 0)
            throw new InvalidDataException($"Pack '{packId}' contains no enemies.");
        string? duplicateId = enemies
            .Where(enemy => !string.IsNullOrWhiteSpace(enemy.id))
            .GroupBy(enemy => enemy.id!.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateId != null)
            throw new InvalidDataException(
                $"Pack '{packId}' contains duplicate enemy ID '{duplicateId}'.");
    }

    internal static void ValidateIdentifier(string value, string field, bool allowDot)
    {
        if (value.Any(character =>
                !(char.IsLetterOrDigit(character) || character is '_' or '-' ||
                  (allowDot && character == '.'))))
            throw new InvalidDataException(
                $"{field} may contain only letters, numbers, underscores, hyphens" +
                (allowDot ? ", and dots." : "."));
    }
}

internal sealed class EnemyDefinition
{
    public string? id;
    public string? name;
    public string? description;
    public string? clone;
    public int maxHp;
    public bool passive;
    public string fleeBehavior = "ProximityAndDamage";
    public PathfindingDefinition? pathfinding;
    public LootDefinition? loot;
    public VisualDefinition? visuals;
    public List<AbilityDefinition>? abilities;
    public List<SpawnDefinition>? spawns;
    public List<BossSpawnDefinition>? bossSpawns;
    public List<SilverpineTimedSpawnDefinition>? silverpineTimedSpawns;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(clone))
            throw new InvalidDataException("Enemy id, name, and clone are required.");
        id = id.Trim();
        name = name.Trim();
        clone = clone.Trim();
        EnemyPackDefinition.ValidateIdentifier(id, "Enemy id", allowDot: false);
        if (maxHp < 0)
            throw new InvalidDataException("maxHp cannot be negative.");
        if (!Enum.TryParse(fleeBehavior, ignoreCase: true, out PassiveFleeBehavior parsedFlee))
            throw new InvalidDataException(
                "fleeBehavior must be ProximityAndDamage, WhenAttacked, or Never.");
        fleeBehavior = parsedFlee.ToString();
        pathfinding?.Validate();
        loot?.Validate();
        visuals ??= new VisualDefinition();
        visuals.Validate();
        abilities ??= new List<AbilityDefinition>();
        foreach (AbilityDefinition ability in abilities)
        {
            ability.Validate();
            _ = AbilityFactory.Create(ability);
        }
        spawns ??= new List<SpawnDefinition>();
        foreach (SpawnDefinition spawn in spawns)
            spawn.Validate();
        bossSpawns ??= new List<BossSpawnDefinition>();
        foreach (BossSpawnDefinition boss in bossSpawns)
            boss.Validate();
        string? duplicateBossId = bossSpawns
            .GroupBy(boss => boss.id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateBossId != null)
            throw new InvalidDataException(
                $"Enemy '{id}' contains duplicate boss spawn ID '{duplicateBossId}'.");
        silverpineTimedSpawns ??= new List<SilverpineTimedSpawnDefinition>();
        foreach (SilverpineTimedSpawnDefinition timed in silverpineTimedSpawns)
            timed.Validate();
        string? duplicateTimedId = silverpineTimedSpawns
            .GroupBy(timed => timed.id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateTimedId != null)
            throw new InvalidDataException(
                $"Enemy '{id}' contains duplicate timed spawn ID '{duplicateTimedId}'.");
    }

    [JsonIgnore]
    internal PassiveFleeBehavior ParsedFleeBehavior =>
        Enum.TryParse(fleeBehavior, ignoreCase: true, out PassiveFleeBehavior behavior)
            ? behavior
            : PassiveFleeBehavior.ProximityAndDamage;
}

internal enum LootMode
{
    Replace,
    AddToClone
}

internal sealed class LootDefinition
{
    public string mode = "Replace";
    public int min;
    public int max;
    public List<LootEntryDefinition>? entries;

    [JsonIgnore]
    internal LootMode ParsedMode =>
        Enum.TryParse(mode, ignoreCase: true, out LootMode parsed)
            ? parsed
            : LootMode.Replace;

    internal void Validate()
    {
        if (!Enum.TryParse(mode, ignoreCase: true, out LootMode parsed))
            throw new InvalidDataException(
                "loot.mode must be Replace or AddToClone.");
        mode = parsed.ToString();
        if (min < 0 || max < min)
            throw new InvalidDataException("loot min/max values are invalid.");
        entries ??= new List<LootEntryDefinition>();
        foreach (LootEntryDefinition entry in entries)
            entry.Validate();
        if (entries.Count == 0 && max > 0)
            throw new InvalidDataException(
                "loot.entries cannot be empty when loot.max is greater than zero.");
    }
}

internal sealed class LootEntryDefinition
{
    public string? item;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? tier;
    public float weight = 1f;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(item))
            throw new InvalidDataException("Every loot entry requires an item.");
        item = item.Trim();
        if (string.IsNullOrWhiteSpace(tier) ||
            tier.Equals("Automatic", StringComparison.OrdinalIgnoreCase))
        {
            tier = null;
        }
        else
        {
            if (!Enum.TryParse(tier, ignoreCase: true, out Quality parsedTier) ||
                !Enum.IsDefined(typeof(Quality), parsedTier))
                throw new InvalidDataException(
                    "Loot entry tier must be Automatic, Normal, Quality, Superior, " +
                    "Exceptional, Pristine, Blessed, or Legendary.");
            tier = parsedTier.ToString();
        }
        if (float.IsNaN(weight) || float.IsInfinity(weight) || weight <= 0f)
            throw new InvalidDataException("Loot entry weight must be finite and greater than zero.");
    }
}

internal sealed class PathfindingDefinition
{
    public float speedBonus;

    internal void Validate()
    {
        if (float.IsNaN(speedBonus) || float.IsInfinity(speedBonus) ||
            speedBonus < 0f || speedBonus > 1f)
            throw new InvalidDataException(
                "pathfinding.speedBonus must be between 0 and 1.");
    }
}

internal sealed class VisualDefinition
{
    public string? image;
    public DirectionalImages? directionalImages;
    public string? model;
    public float[]? rotation;
    public float alphaCutoff = 0.1f;
    public bool cache = true;
    public float[]? pivot;
    public float scale = 1f;
    public VisualOffsets? offsets;
    // Provenance only; runtime visuals always use pack-local images/cache.
    public string? artAssetId;
    public string? artOriginalName;

    internal Vector3 Rotation => rotation is { Length: 3 }
        ? new Vector3(rotation[0], rotation[1], rotation[2])
        : Vector3.zero;
    internal Vector2 Pivot => pivot is { Length: 2 }
        ? new Vector2(pivot[0], pivot[1])
        : new Vector2(0.5f, 0f);
    internal bool HasPivot => pivot is { Length: 2 };

    internal void Validate()
    {
        int sources = (string.IsNullOrWhiteSpace(image) ? 0 : 1) +
                      (directionalImages == null ? 0 : 1) +
                      (string.IsNullOrWhiteSpace(model) ? 0 : 1);
        if (sources != 1)
            throw new InvalidDataException(
                "visuals must specify exactly one of image, directionalImages, or model.");
        directionalImages?.Validate();
        offsets?.Validate();
        if (rotation != null && rotation.Length != 3)
            throw new InvalidDataException("visuals.rotation must contain [x, y, z].");
        if (rotation != null && rotation.Any(value => !IsFinite(value)))
            throw new InvalidDataException("visuals.rotation values must be finite.");
        if (!IsFinite(alphaCutoff) || alphaCutoff < 0f || alphaCutoff > 1f)
            throw new InvalidDataException("visuals.alphaCutoff must be between 0 and 1.");
        if (!IsFinite(scale) || scale < 0.1f || scale > 10f)
            throw new InvalidDataException(
                "visuals.scale must be between 0.1 and 10.");
        if (pivot != null &&
            (pivot.Length != 2 || pivot.Any(value => !IsFinite(value) || value < 0f || value > 1f)))
            throw new InvalidDataException(
                "visuals.pivot must contain two normalized values between 0 and 1.");
    }

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);
}

internal sealed class DirectionalImages
{
    public string? down;
    public string? left;
    public string? up;
    public string? right;

    internal void Validate()
    {
        if (new[] { down, left, up, right }.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException(
                "directionalImages requires down, left, up, and right.");
    }
}

internal sealed class AbilityDefinition
{
    public string? type;
    public float range;
    public int cooldown;
    public int minPower;
    public int maxPower;
    public string? damageType = "Normal";
    public string? sound;
    public string? projectile;
    public string? projectileImage;
    public string? oozeProjectileColor;
    public string? statusEffect;
    public int statusDuration = -1;
    public float chance;

    internal void Validate()
    {
        string normalized = AbilityFactory.Normalize(type);
        if (normalized.Length == 0)
            throw new InvalidDataException("Ability type is required.");
        if (cooldown < 0)
            throw new InvalidDataException($"Ability '{type}' cooldown cannot be negative.");
        if (float.IsNaN(range) || float.IsInfinity(range) || range < 0f)
            throw new InvalidDataException($"Ability '{type}' range must be finite and non-negative.");
        if (minPower < 0 || maxPower < minPower)
            throw new InvalidDataException($"Ability '{type}' minPower/maxPower values are invalid.");
        if (float.IsNaN(chance) || float.IsInfinity(chance) || chance < 0f || chance > 1f)
            throw new InvalidDataException($"Ability '{type}' chance must be between 0 and 1.");

        if (normalized == "projectile")
        {
            string path = projectile ?? "MiscPrefabs/prefab_projectile_ooze";
            if (Resources.Load<GameObject>(path) == null)
                throw new InvalidDataException(
                    $"Projectile resource '{path}' does not exist.");
            if (!string.IsNullOrWhiteSpace(statusEffect) && statusDuration <= 0)
                throw new InvalidDataException(
                    "Projectile statusDuration must be greater than zero when statusEffect is set.");
            if (!string.IsNullOrWhiteSpace(projectileImage) &&
                !projectileImage.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Projectile projectileImage must be a pack-relative PNG file.");
            if (!CustomEnemyProjectileSprites.IsValidOozeColor(oozeProjectileColor))
                throw new InvalidDataException(
                    "oozeProjectileColor must be Green, Blue, Orange, Purple, or Red.");
            if (!string.IsNullOrWhiteSpace(oozeProjectileColor) &&
                !CustomEnemyProjectileSprites.UsesOozePrefab(this))
                throw new InvalidDataException(
                    "oozeProjectileColor can only be used with prefab_projectile_ooze.");
        }
        else if (normalized == "spawnstunbubbles")
        {
            string prefab = projectile ?? "prefab_enemy_stunbubble";
            if (Resources.Load<GameObject>("Prefabs/" + prefab) == null)
                throw new InvalidDataException(
                    $"Stun-bubble prefab '{prefab}' does not exist.");
        }
    }
}

internal sealed class SpawnDefinition
{
    private static readonly HashSet<string> ValidBiomes =
        new(StringComparer.OrdinalIgnoreCase)
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

    public string? biome;
    public int min = 1;
    public int max = 1;

    internal bool Matches(WorldTileGenerator generator)
    {
        string expected = (biome ?? "").Replace(" ", "").Replace("TileGenerator", "");
        if (expected.Equals("Silverpine", StringComparison.OrdinalIgnoreCase))
            return generator.worldTile != null &&
                   string.Equals(generator.worldTile.name, "Silverpine",
                       StringComparison.OrdinalIgnoreCase);

        string actual = generator.GetType().Name.Replace("TileGenerator", "");
        return expected.Equals(actual, StringComparison.OrdinalIgnoreCase);
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(biome))
            throw new InvalidDataException("Spawn biome is required.");
        biome = biome.Trim();
        if (!ValidBiomes.Contains(biome))
            throw new InvalidDataException($"Unknown spawn biome '{biome}'.");
        if (min < 0 || max < min)
            throw new InvalidDataException("Spawn min/max values are invalid.");
    }
}

internal sealed class BossSpawnDefinition
{
    public string? id;
    public int x;
    public int y;
    public bool respawnAfterDeath;
    public BossMusicDefinition? bossMusic;
    public BossDeathSongDefinition? deathSong;

    [JsonIgnore]
    internal string EncounterMusicClipId = "";
    [JsonIgnore]
    internal string DeathSongClipId = "";

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException("Every boss spawn requires an id.");
        id = id.Trim();
        EnemyPackDefinition.ValidateIdentifier(id, "Boss spawn id", allowDot: false);
        bossMusic?.Validate();
        deathSong?.Validate();
    }
}

internal sealed class BossMusicDefinition
{
    public string? file;
    public float volume = 0.8f;
    public int priority = 500;
    public float fadeSeconds = 1f;

    internal void Validate()
    {
        file = ValidateAudioFile(file, "Boss music");
        ValidateVolume(volume, "Boss music volume");
        if (float.IsNaN(fadeSeconds) || float.IsInfinity(fadeSeconds) ||
            fadeSeconds < 0f)
            throw new InvalidDataException(
                "Boss music fadeSeconds must be finite and non-negative.");
    }

    internal static string ValidateAudioFile(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException(label + " requires a file.");
        value = value.Trim();
        if (!CustomEnemyBossAudio.IsSupportedPath(value))
            throw new InvalidDataException(
                label + " file must be OGG, WAV, MP3, AIF, or AIFF.");
        return value;
    }

    internal static void ValidateVolume(float value, string label)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) ||
            value < 0f || value > 1f)
            throw new InvalidDataException(label + " must be between 0 and 1.");
    }
}

internal sealed class BossDeathSongDefinition
{
    public string? file;
    public float volume = 0.8f;
    public float minRange;
    public float maxRange = 25f;

    internal void Validate()
    {
        file = BossMusicDefinition.ValidateAudioFile(file, "Boss death song");
        BossMusicDefinition.ValidateVolume(volume, "Boss death song volume");
        if (float.IsNaN(minRange) || float.IsInfinity(minRange) || minRange < 0f)
            throw new InvalidDataException(
                "Boss death song minRange must be finite and non-negative.");
        if (float.IsNaN(maxRange) || float.IsInfinity(maxRange) ||
            maxRange <= minRange)
            throw new InvalidDataException(
                "Boss death song maxRange must be finite and greater than minRange.");
    }
}

internal sealed class SilverpineTimedSpawnDefinition
{
    public string? id;
    public int startHour = 20;
    public int endHour = 5;
    public int min = 1;
    public int max = 1;
    public int edgeDepth = 8;

    internal bool IsActiveAt(int hour) =>
        startHour < endHour
            ? hour >= startHour && hour < endHour
            : hour >= startHour || hour < endHour;

    internal int GetCycleDay(int currentDay, int hour) =>
        startHour > endHour && hour < endHour
            ? currentDay - 1
            : currentDay;

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new InvalidDataException(
                "Every Silverpine timed spawn requires an id.");
        id = id.Trim();
        EnemyPackDefinition.ValidateIdentifier(
            id, "Silverpine timed spawn id", allowDot: false);
        if (startHour < 0 || startHour > 23 || endHour < 0 || endHour > 23 ||
            startHour == endHour)
            throw new InvalidDataException(
                "Timed spawn startHour/endHour must be different values from 0 through 23.");
        if (min < 0 || max < min)
            throw new InvalidDataException(
                "Silverpine timed spawn min/max values are invalid.");
        if (edgeDepth < 1 || edgeDepth > 50)
            throw new InvalidDataException(
                "Silverpine timed spawn edgeDepth must be between 1 and 50.");
    }
}
