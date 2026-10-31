#nullable enable
using UnityEngine;

namespace SilverpineMods.CustomEnemyLoader;

internal static class EnemySpriteLighting
{
    private static Material? diffuseMaterial;
    private static bool warnedMissingMaterial;

    internal static void ApplyDiffuse(SpriteRenderer renderer)
    {
        if (renderer == null)
            return;

        // Assign only the custom template's renderer. The native shared material
        // is never modified, copied, or destroyed, and sprite/tint stay intact.
        if (diffuseMaterial == null)
            diffuseMaterial = Resources.Load<Material>("Materials/sprite-diffuse");
        if (diffuseMaterial != null)
        {
            renderer.sharedMaterial = diffuseMaterial;
            return;
        }

        // Keep packs usable if a game update removes this resource, and retry
        // for later templates without flooding the log for every enemy.
        if (!warnedMissingMaterial)
        {
            warnedMissingMaterial = true;
            Plugin.Log.LogWarning("Enemy material 'Materials/sprite-diffuse' is unavailable; " +
                "custom enemies will keep their cloned material and may not respond to lighting.");
        }
    }
}
