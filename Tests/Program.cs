using Newtonsoft.Json;
using Silverpine.ModdingTools;
using SilverpineMods.CustomEnemyLoader;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    checks++; Console.WriteLine("PASS: " + message);
}
void Reject(Action action, string message)
{
    bool rejected = false;
    try { action(); } catch (InvalidDataException) { rejected = true; }
    Check(rejected, message);
}

foreach (string name in new[] { "prefab_tile_grass", "PREFAB_TILE_GRASS", "prefab_tile_grass(Clone)",
    "prefab_tile_grass (Clone)", "prefab_tile_grass (1)" })
    Check(EditorTerrainReference.IsGrassGround(name), "terrain reference accepts only base grass or its Unity copies: " + name);
foreach (string? name in new[] { "prefab_tile_grass_overgrowth", "prefab_tile_grass_overgrowth(Clone)",
    "prefab_tile_grass_overgrowth_swamp", "prefab_tile_grass_swamp", "prefab_plant_grass", "grass",
    "prefab_tile_grass (overgrowth)", "prefab_tile_grass (1) extra", "", null })
    Check(!EditorTerrainReference.IsGrassGround(name), "terrain reference rejects vegetation, variants, and ambiguous names: " + name);

Check(EditorTextLayout.Width(0, 920, 0) == 920, "text inputs have a bounded first-layout width");
Check(EditorTextLayout.Width(710, 920, 0) == 710, "text height uses the allocated width after adjacent controls");
Check(EditorTextLayout.Width(1040, 255, 0) == 255, "changing panels cannot reuse an oversized input width");
Check(EditorTextLayout.Width(20, 260, 55) == 55, "numeric inputs retain their minimum width");
Check(EditorTextLayout.Height(20, 20) == 44, "short fields retain a readable single-line box");
Check(EditorTextLayout.Height(140.2f, 80) == 143, "wrapped text grows the box to its measured content height");
Check(EditorTextLayout.Height(20, 80) == 82, "multiline fields retain their minimum writing space");
Check(EditorTextLayout.Height(500, 80) > EditorTextLayout.Height(90, 80), "longer input expands downward instead of being height-capped");
Check(EditorTextLayout.Measurement("") == "M", "empty input has a measurable line");
Check(EditorTextLayout.Measurement("first\n\n") == "first\n\nM", "trailing blank lines are counted when sizing the box");
Check(EditorTextLayout.Measurement("first\r\nsecond\r") == "first\nsecond\nM", "Windows line endings measure as single line breaks");
Check(EditorTextLayout.Measurement("unchanged") == "unchanged", "ordinary text is measured without extra characters");
Check(EditorTextLayout.InputValue("first\r\n\nlast\n", true) == "first\r\n\nlast\n", "multiline content and blank lines are preserved without measurement placeholders");
Check(EditorTextLayout.InputValue("asset\r\nname\n", false) == "assetname", "IDs and paths do not acquire stored line breaks");

var offsets = new VisualOffsets();
offsets.Set(1, .25f, -.4f);
var copy = JsonConvert.DeserializeObject<VisualOffsets>(JsonConvert.SerializeObject(offsets))!;
Check(copy.Get(1).x == .25f && copy.Get(1).y == -.4f && copy.Get(0).x == 0 && copy.Get(3).y == 0,
    "per-direction offsets survive JSON and do not leak to other directions");
copy.Get(2).x = 4;
Check(copy.Get(2).x == 0, "missing directions return independent zero offsets");
Reject(() => offsets.Set(1, float.NaN, 0), "NaN cannot corrupt an existing offset");
Reject(() => offsets.Set(1, 0, float.PositiveInfinity), "infinite offsets are rejected");
Reject(() => offsets.Set(1, 101, 0), "offsets outside the supported range are rejected");
Check(offsets.Get(1).x == .25f, "invalid edits preserve the previous valid value");
foreach (float scale in new[] { .1f, .5f, 1f, 3f, 10f })
{
    const float pixels = 128, offset = .35f;
    float original = 16 / (pixels * scale);
    float shifted = VisualOffset.ShiftPivot(original, pixels, scale, offset);
    float lowerEdge = -shifted * pixels * scale / 32;
    Check(Math.Abs(lowerEdge - (-.5f + offset)) < .00001f, "tile-edge offset stays in world units at scale " + scale);
}
string[] library = { "front", "right", "back", "left" };
Check(ArtDirections.SwapLibrarySides(library).SequenceEqual(new[] { "front", "left", "back", "right" }),
    "shared Front/Right/Back/Left maps to enemy Down/Left/Up/Right");
Check(ArtDirections.SwapLibrarySides(ArtDirections.SwapLibrarySides(library)).SequenceEqual(library),
    "publishing and importing four views round-trips their orientation");

string root = Path.Combine(Path.GetTempPath(), "cel-regression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    SharedArtLibrary.Root = Path.Combine(root, "shared", "ArtLibrary");
    byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aE3sAAAAASUVORK5CYII=");
    var entry = SharedArtLibrary.Publish("Skeleton", "skeleton.glb", new[] { png, png, png, png });
    var same = SharedArtLibrary.Publish("Different name", "other.glb", new[] { png, png, png, png });
    Check(entry.id == same.id && same.originalName == "Skeleton", "framework deduplication retains the original art identity");
    string pack = Path.Combine(root, "pack");
    string[] local = SharedArtLibrary.CopyToPack(entry, pack);
    Check(local.Length == 4 && local.All(p => File.Exists(Path.Combine(pack, p))), "all cached views materialize in the distribution pack");
    Check(SharedArtLibrary.CopyToPack(entry, pack).SequenceEqual(local), "reusing art keeps stable local filenames");
    // Recipients have no authoring library; the copied files must remain usable.
    string authoredRoot = SharedArtLibrary.Root;
    SharedArtLibrary.Root = Path.Combine(root, "empty-recipient-library");
    Check(local.All(p => File.ReadAllBytes(Path.Combine(pack, p)).SequenceEqual(png)), "distributed images remain intact without the shared library or GLB");
    SharedArtLibrary.Root = authoredRoot;
    Reject(() => SharedArtLibrary.Resolve("../../escape.png"), "shared art prevents traversal outside its root");
    Reject(() => SharedModelSources.Resolve("../../escape.glb"), "model references prevent traversal outside their root");

    string first = Path.Combine(root, "one"), second = Path.Combine(root, "two");
    Directory.CreateDirectory(first); Directory.CreateDirectory(second);
    string a = Path.Combine(first, "Creature.glb"), b = Path.Combine(second, "Creature.glb");
    File.WriteAllBytes(a, new byte[] { 1, 2, 3 }); File.WriteAllBytes(b, new byte[] { 4, 5, 6 });
    string importedA = SharedModelSources.Import(a), importedB = SharedModelSources.Import(b);
    Check(importedA != importedB && File.ReadAllBytes(importedA).SequenceEqual(new byte[] { 1, 2, 3 }), "same-name GLBs cannot overwrite each other");
    Check(SharedModelSources.Import(importedA) == importedA, "shared models are reused without repeated hash suffixes");
    string renamed = Path.Combine(root, "renamed.glb"); File.Copy(a, renamed);
    Check(SharedModelSources.Import(renamed) == importedA, "identical models from different packs share one authoring file");
    Check(!SharedModelSources.RemoveEmbeddedCopy(renamed, importedA, true) && File.Exists(renamed),
        "GLB separation preserves a source still referenced by another enemy");
    Check(!SharedModelSources.RemoveEmbeddedCopy(renamed, importedB, false) && File.Exists(renamed),
        "GLB separation preserves a source whose contents changed after import");
    Check(!SharedModelSources.RemoveEmbeddedCopy(renamed, Path.Combine(root, "missing.glb"), false) && File.Exists(renamed),
        "GLB separation requires an intact authoring copy");
    Check(SharedModelSources.RemoveEmbeddedCopy(renamed, importedA, false) && !File.Exists(renamed) && File.Exists(importedA),
        "committed separation removes only the redundant embedded copy");

    string cache = Path.Combine(pack, ".cache", "test_enemy");
    DirectionalCacheFiles.Write(cache, new[] { png, png, png, png }, "old manifest");
    bool cancelled = false;
    try { DirectionalCacheFiles.Write(cache, new[] { png, png, png, png }, "new manifest", () => throw new OperationCanceledException()); }
    catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled && File.ReadAllText(Path.Combine(cache, "manifest.json")) == "old manifest", "cancelled/stale render preserves the previous complete cache");
    Check(Directory.GetDirectories(Path.GetDirectoryName(cache)!).Length == 1, "cancelled render cleans its staging files");
    DirectionalCacheFiles.Write(cache, new[] { png, png, png, png }, "new manifest");
    Check(File.ReadAllText(Path.Combine(cache, "manifest.json")) == "new manifest" && Directory.GetFiles(cache).Length == 5,
        "successful render replaces all views and the manifest together");
    Reject(() => DirectionalCacheFiles.Write(cache, new[] { png }, "bad"), "incomplete directional output cannot replace a cache");
}
finally { Directory.Delete(root, recursive: true); } // Only the fresh GUID test directory above.
Console.WriteLine($"{checks} regression checks passed.");
