#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Silverpine.ModdingTools;

namespace SilverpineMods.CustomEnemyLoader;

internal sealed partial class CustomEnemyEditorUI
{
    private string FinishModelSeparations()
    {
        if (document == null || document.PendingModelSeparations.Count == 0) return "";
        try
        {
            // Inspect saved packs (including disabled packs and recovery drafts),
            // not only the selected enemy. A shared embedded source must remain
            // available until every referencing definition has been migrated.
            var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var files = EnemyPackLoader.FindPackJsonFiles(Plugin.PackDirectory, includeDisabled: true)
                .Concat(Directory.GetFiles(Plugin.PackDirectory, "*.enemy-draft", SearchOption.AllDirectories));
            foreach (string path in files)
            {
                EnemyPackDefinition pack = EnemyPackLoader.DeserializePack(path)
                    ?? throw new InvalidDataException("Could not check model references in " + path);
                foreach (EnemyDefinition enemy in pack.enemies ?? new List<EnemyDefinition>())
                    if (!string.IsNullOrWhiteSpace(enemy.visuals?.model))
                        references.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, enemy.visuals!.model!)));
            }
            int retained = 0;
            foreach (var pair in document.PendingModelSeparations)
                if (!SharedModelSources.RemoveEmbeddedCopy(pair.Key, pair.Value, references.Contains(pair.Key))) retained++;
            document.PendingModelSeparations.Clear();
            return retained == 0 ? " Embedded GLB separation finished."
                : " Some embedded GLBs were retained because they are still referenced or changed; the shared copies remain available.";
        }
        catch (Exception error)
        {
            Plugin.Log.LogWarning("Pack saved, but embedded model cleanup was skipped: " + error.Message);
            return " Embedded GLB cleanup was skipped: " + error.Message;
        }
    }

    private static string ImportAudioAsset(string source, string pack)
    {
        string hash = SharedArtLibrary.FileHash(source);
        string relative = "assets/" + hash + Path.GetExtension(source).ToLowerInvariant();
        string target = Path.Combine(pack, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            if (SharedArtLibrary.FileHash(target) != hash)
                throw new IOException("Existing audio has different contents: " + target);
        }
        else File.Copy(source, target, overwrite: false);
        return relative;
    }
}
