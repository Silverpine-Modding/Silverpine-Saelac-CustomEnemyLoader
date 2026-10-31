using Mono.Cecil;
using Mono.Cecil.Cil;
using SilverpineMods.CustomEnemyLoader;
using UnityEngine;

internal static class EnemyLightingChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var unlit = new Material();
        var nativeRenderer = new SpriteRenderer { sharedMaterial = unlit };
        var customRenderer = new SpriteRenderer { sharedMaterial = unlit, color = "red" };
        var diffuse = new Material();
        Resources.Diffuse = diffuse;
        EnemySpriteLighting.ApplyDiffuse(customRenderer);
        check(ReferenceEquals(customRenderer.sharedMaterial, diffuse) && Resources.LastPath == "Materials/sprite-diffuse",
            "custom enemy replaces inherited unlit material with the game's native diffuse material");
        check(ReferenceEquals(nativeRenderer.sharedMaterial, unlit), "native enemy renderer is not modified");
        check(customRenderer.color == "red" && customRenderer.sprite == "custom-art",
            "diffuse assignment preserves sprite artwork and native tint");

        var second = new SpriteRenderer();
        EnemySpriteLighting.ApplyDiffuse(second);
        check(ReferenceEquals(second.sharedMaterial, diffuse) && Resources.LoadCalls == 1,
            "custom enemies reuse one native material without per-enemy copies");

        diffuse.Destroyed = true;
        Resources.Diffuse = new Material();
        EnemySpriteLighting.ApplyDiffuse(second);
        check(ReferenceEquals(second.sharedMaterial, Resources.Diffuse) && Resources.LoadCalls == 2,
            "a Unity-destroyed cached diffuse material is reloaded");

        Resources.Diffuse.Destroyed = true;
        Resources.Diffuse = null;
        second.sharedMaterial = unlit;
        EnemySpriteLighting.ApplyDiffuse(second);
        EnemySpriteLighting.ApplyDiffuse(second);
        check(ReferenceEquals(second.sharedMaterial, unlit) && Plugin.Log.Messages.Count == 1,
            "missing diffuse resource preserves the cloned material and warns only once");
        Resources.Diffuse = new Material();
        EnemySpriteLighting.ApplyDiffuse(second);
        check(ReferenceEquals(second.sharedMaterial, Resources.Diffuse),
            "a missing diffuse resource can recover on later template initialization");

        int loadsBefore = Resources.LoadCalls;
        EnemySpriteLighting.ApplyDiffuse(null!);
        second.Destroyed = true;
        EnemySpriteLighting.ApplyDiffuse(second);
        check(Resources.LoadCalls == loadsBefore, "missing or destroyed sprite renderers are ignored safely");
    }

    internal static void CheckCompiledIntegration(Action<bool, string> check, string workspace)
    {
        using var plugin = AssemblyDefinition.ReadAssembly(Path.Combine(workspace,
            "Plugin Development", "CustomEnemyLoader", "bin", "Release", "CustomEnemyLoader.dll"));
        var visuals = plugin.MainModule.Types.Single(t => t.Name == "EnemyVisualLoader");
        var apply = visuals.NestedTypes.Single(t => t.Name.StartsWith("<ApplyAsync>"))
            .Methods.Single(m => m.Name == "MoveNext");
        var calls = apply.Body.Instructions.Where(i => i.Operand is MethodReference)
            .Select(i => (MethodReference)i.Operand).ToList();
        int lighting = calls.FindIndex(m => m.DeclaringType.Name == "EnemySpriteLighting" && m.Name == "ApplyDiffuse");
        check(lighting >= 0 && new[] { "LoadSprite", "LoadDirections", "ResolveModelPath" }
                .All(name => calls.FindIndex(m => m.Name == name) > lighting),
            "compiled visual initialization applies diffuse before single, directional, and GLB/cache branches");

        var helper = plugin.MainModule.Types.Single(t => t.Name == "EnemySpriteLighting")
            .Methods.Single(m => m.Name == "ApplyDiffuse");
        var helperCalls = helper.Body.Instructions.Where(i => i.Operand is MethodReference)
            .Select(i => ((MethodReference)i.Operand).Name).ToList();
        check(helperCalls.Contains("set_sharedMaterial") && !helperCalls.Contains("set_material") &&
              !helperCalls.Contains("set_color") && !helperCalls.Contains("set_sprite") &&
              !helper.Body.Instructions.Any(i => i.OpCode == OpCodes.Newobj),
            "compiled lighting changes only the shared material reference without allocating material copies");

        using var game = AssemblyDefinition.ReadAssembly(Path.Combine(workspace,
            "Silverpine_Data", "Managed", "Assembly-CSharp.dll"));
        var pickup = game.MainModule.Types.Single(t => t.Name == "Pickupable")
            .Methods.Single(m => m.Name == "SetEnabled");
        check(pickup.Body.Instructions.Any(i => Equals(i.Operand, "Materials/sprite-diffuse")),
            "installed game restores normal sprite lighting using the same native diffuse resource");
    }
}
