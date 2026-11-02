#nullable enable
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Silverpine.ModdingTools;

namespace SilverpineMods.CustomEnemyLoader;

internal sealed partial class CustomEnemyEditorUI
{
    private static readonly string[] ViewNames = { "Down", "Left", "Up", "Right" };
    private int editorTab, previewTab = 1, previewDirection, previewGeneration;
    private bool packPickerOpen, packSettingsOpen, assetLibraryOpen, removeConfirmation;
    private SharedArtLibrary.Entry? artDeleteConfirmation;
    private long libraryRevision = -1;
    private string librarySearch = "", enemySearch = "";
    private int libraryPage;
    private Vector2 libraryScroll, previewScroll, statusScroll;
    private GUISkin? editorSkin;
    private GUIStyle? editorHeading, editorMuted, editorSelected;
    private GUIStyle? editorActionButton, editorCompactLabel;
    private readonly List<Texture2D> skinTextures = new();
    private readonly List<SharedArtLibrary.Entry> libraryEntries = new();
    private readonly Dictionary<string, Sprite> libraryThumbnails = new();
    private readonly HashSet<string> failedLibraryThumbnails = new();
    private VisualDefinition? draggedVisual;
    private int draggedDirection, offsetControl;
    private Vector2 dragMouse, dragStart;
    private float dragTileSize;
    private Sprite? terrainSprite;
    private bool terrainLookedUp;
    private string savedDocumentJson = "";
    private Action? pendingAction;
    private int escapeHandledFrame = -1;

    private bool HasUnsavedChanges => document != null &&
        JsonConvert.SerializeObject(document.Pack) != savedDocumentJson;

    private void RequestAction(Action action)
    {
        CancelOffsetDrag();
        if (HasUnsavedChanges) pendingAction = action;
        else action();
    }

    private bool HandleEscape()
    {
        if (escapeHandledFrame == Time.frameCount) return true;
        escapeHandledFrame = Time.frameCount;
        if (pickerOpen)
        {
            if (GenericListUI.Instance != null) GenericListUI.Instance.Close();
            pickerOpen = false; status = "Selection cancelled.";
        }
        else if (draggedVisual != null) CancelOffsetDrag();
        else if (artDeleteConfirmation != null) artDeleteConfirmation = null;
        else if (pendingAction != null) pendingAction = null;
        else if (assetLibraryOpen) assetLibraryOpen = false;
        else RequestAction(Close);
        return true;
    }

    private void DrawUnsavedPrompt()
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.Label("Unsaved pack changes", editorHeading!);
        GUILayout.Label("Save your changes before continuing, discard them, or return to the editor.");
        GUILayout.BeginHorizontal();
        GUI.enabled = !generatingPreview;
        if (GUILayout.Button("Save and continue"))
        {
            Action? action = pendingAction;
            if (SaveCurrent()) { pendingAction = null; action?.Invoke(); }
            GUIUtility.ExitGUI();
        }
        GUI.enabled = true;
        if (GUILayout.Button("Discard changes"))
        {
            Action? action = pendingAction; pendingAction = null;
            if (document != null && !string.IsNullOrEmpty(savedDocumentJson))
                document.Pack = JsonConvert.DeserializeObject<EnemyPackDefinition>(savedDocumentJson)!;
            else document = null;
            action?.Invoke(); GUIUtility.ExitGUI();
        }
        if (GUILayout.Button("Keep editing")) { pendingAction = null; GUIUtility.ExitGUI(); }
        GUILayout.EndHorizontal(); GUILayout.EndVertical();
    }

    private void EnsureEditorSkin()
    {
        if (editorSkin != null) return;
        editorSkin = Instantiate(GUI.skin);
        Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1); texture.SetPixel(0, 0, color); texture.Apply();
            skinTextures.Add(texture); return texture;
        }
        Color text = new(.90f, .92f, .90f);
        Texture2D panel = Solid(new Color(.098f, .129f, .145f));
        Texture2D input = Solid(new Color(.063f, .086f, .098f));
        Texture2D button = Solid(new Color(.153f, .196f, .22f));
        Texture2D selected = Solid(new Color(.188f, .251f, .224f));
        Texture2D hover = Solid(new Color(.204f, .259f, .286f));
        foreach (GUIStyle style in new[] { editorSkin.label, editorSkin.button, editorSkin.box,
                     editorSkin.textArea, editorSkin.textField, editorSkin.toggle })
        {
            style.fontSize = 20; style.wordWrap = true;
            style.normal.textColor = text; style.hover.textColor = text;
            style.active.textColor = text; style.focused.textColor = text;
            style.onNormal.textColor = text; style.onHover.textColor = text;
            style.onActive.textColor = text; style.onFocused.textColor = text;
        }
        editorSkin.box.normal.background = panel;
        editorSkin.box.border = new RectOffset();
        editorSkin.box.padding = new RectOffset(18, 18, 18, 18);
        editorSkin.label.padding = new RectOffset(2, 2, 6, 6);
        editorSkin.button.normal.background = button;
        editorSkin.button.hover.background = hover;
        editorSkin.button.active.background = selected;
        editorSkin.button.focused.background = hover;
        editorSkin.button.onNormal.background = selected;
        editorSkin.button.onHover.background = selected;
        editorSkin.button.onActive.background = selected;
        editorSkin.button.onFocused.background = selected;
        editorSkin.button.padding = new RectOffset(12, 12, 10, 10);
        editorSkin.button.border = new RectOffset();
        foreach (GUIStyle style in new[] { editorSkin.textArea, editorSkin.textField })
        {
            style.normal.background = input; style.focused.background = input;
            style.hover.background = input; style.active.background = input;
            style.padding = new RectOffset(10, 10, 9, 9);
            style.border = new RectOffset();
        }
        editorHeading = new GUIStyle(editorSkin.label) { fontSize = 27 };
        editorMuted = new GUIStyle(editorSkin.label); editorMuted.normal.textColor = new Color(.65f, .69f, .68f);
        editorSelected = new GUIStyle(editorSkin.button); editorSelected.normal.background = selected;
        editorActionButton = new GUIStyle(editorSkin.button)
        {
            wordWrap = false, stretchWidth = false, stretchHeight = false, fixedHeight = 0
        };
        editorCompactLabel = new GUIStyle(editorSkin.label) { wordWrap = false, stretchWidth = false };
        wrappedInputStyle = null;
    }

    // Short actions use their measured text width, including the current font
    // and padding. Long user-authored text keeps the separate wrapping styles.
    private bool ActionButton(string text, float minimumWidth = 0)
    {
        var content = new GUIContent(text);
        Vector2 size = editorActionButton!.CalcSize(content);
        return GUILayout.Button(content, editorActionButton,
            GUILayout.Width(Mathf.Ceil(Mathf.Max(minimumWidth, size.x + 2))),
            GUILayout.Height(Mathf.Ceil(size.y)), GUILayout.ExpandHeight(false));
    }

    private void CompactLabel(string text, float minimumWidth = 0)
    {
        var content = new GUIContent(text);
        GUILayout.Label(content, editorCompactLabel!,
            GUILayout.Width(Mathf.Ceil(Mathf.Max(minimumWidth, editorCompactLabel!.CalcSize(content).x + 2))));
    }

    private void DrawLibrarySidebar()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(290));
        GUILayout.Label("CURRENT PACK", editorMuted!);
        if (GUILayout.Button((document?.Pack.packId ?? "Choose enemy pack") + "  …"))
        { packPickerOpen = !packPickerOpen; GUIUtility.ExitGUI(); }
        if (packPickerOpen)
        {
            fileScroll = GUILayout.BeginScrollView(fileScroll, GUILayout.MaxHeight(220));
            foreach (PackDocument candidate in documents.ToArray())
                if (GUILayout.Button((candidate.Pack.packId ?? Path.GetFileName(candidate.Path)) +
                    (candidate.IsDraft ? " (draft)" : candidate.IsEnabled ? "" : " (disabled)")))
                { RequestAction(() => { SelectDocument(candidate); packPickerOpen = false; assetLibraryOpen = false; }); GUIUtility.ExitGUI(); }
            GUILayout.EndScrollView();
            if (GUILayout.Button("New pack"))
            { RequestAction(() => { CreateNewPack(); savedDocumentJson = ""; packPickerOpen = false; assetLibraryOpen = false; }); GUIUtility.ExitGUI(); }
            if (GUILayout.Button("Reload pack files")) { RequestAction(RefreshDocuments); GUIUtility.ExitGUI(); }
        }
        if (document != null)
        {
            GUILayout.Label(document.IsDraft ? "Draft · not loaded by the game" : document.IsEnabled ? "Enabled at startup" : "Disabled at startup", editorMuted!);
            GUI.enabled = !document.IsDraft && File.Exists(document.Path);
            if (GUILayout.Button(document.IsEnabled ? "Disable pack" : "Enable pack"))
            { SetCurrentPackEnabled(!document.IsEnabled); GUIUtility.ExitGUI(); }
            GUI.enabled = true;
            if (GUILayout.Button((packSettingsOpen ? "▼  " : "▶  ") + "Pack settings", GUI.skin.box))
            { packSettingsOpen = !packSettingsOpen; GUIUtility.ExitGUI(); }
            if (packSettingsOpen)
            {
                GUILayout.Label("Pack ID");
                document.Pack.packId = DrawWrappedInput(document.Pack.packId ?? "", SidebarInputMaxWidth);
                GUILayout.Label("Keep released IDs stable. Save pack to apply edits.", editorMuted!);
            }
        }
        GUILayout.Space(10);
        GUILayout.Label("ENEMIES  " + (document?.Pack.enemies?.Count ?? 0), editorMuted!);
        enemySearch = DrawWrappedInput(enemySearch, SidebarInputMaxWidth);
        enemyScroll = GUILayout.BeginScrollView(enemyScroll);
        if (document?.Pack.enemies != null)
            for (int i = 0; i < document.Pack.enemies.Count; i++)
            {
                EnemyDefinition value = document.Pack.enemies[i];
                string title = value.name ?? value.id ?? "Unnamed enemy";
                if ((title + " " + value.id).IndexOf(enemySearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string subtitle = value.passive ? "Passive" : "Hostile";
                if ((value.bossSpawns?.Count ?? 0) > 0) subtitle += " · Boss";
                if (GUILayout.Button(title + "\n" + subtitle, i == selectedEnemy ? editorSelected! : GUI.skin.button))
                { SelectEnemy(i); assetLibraryOpen = false; GUIUtility.ExitGUI(); }
            }
        GUILayout.EndScrollView();
        GUI.enabled = document != null;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Add")) { AddEnemy(); enemySearch = ""; GUIUtility.ExitGUI(); }
        GUI.enabled = CurrentEnemy != null;
        if (GUILayout.Button("Duplicate")) { DuplicateEnemy(); GUIUtility.ExitGUI(); }
        GUILayout.EndHorizontal();
        GUI.enabled = CurrentEnemy != null && document!.Pack.enemies!.Count > 1;
        if (GUILayout.Button("Remove selected")) { removeConfirmation = true; GUIUtility.ExitGUI(); }
        GUI.enabled = true;
        if (removeConfirmation)
        {
            GUILayout.Label("Remove this enemy from the pack?", editorMuted!);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Remove") && CurrentEnemy != null)
            {
                CancelOffsetDrag();
                document!.Pack.enemies!.RemoveAt(selectedEnemy);
                selectedEnemy = Mathf.Clamp(selectedEnemy, 0, document.Pack.enemies.Count - 1);
                SelectionChanged(); GUIUtility.ExitGUI();
            }
            if (GUILayout.Button("Cancel")) { removeConfirmation = false; GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndVertical();
    }

    private void DuplicateEnemy()
    {
        EnemyDefinition? source = CurrentEnemy;
        if (source == null || document == null) return;
        try
        {
            var copy = JsonConvert.DeserializeObject<EnemyDefinition>(JsonConvert.SerializeObject(source))!;
            string stem = (source.id ?? "enemy") + "_copy";
            copy.id = stem; int number = 2;
            while (document.Pack.enemies!.Any(e => string.Equals(e.id, copy.id, StringComparison.OrdinalIgnoreCase)))
                copy.id = stem + number++;
            copy.name = (source.name ?? source.id) + " (copy)";
            // A cache-only GLB is keyed by enemy ID. Materialize its images for the new ID.
            if (!string.IsNullOrWhiteSpace(source.visuals?.model))
            {
                SharedArtLibrary.Entry entry = PublishEnemyArt(source, GetPackFolder(), document.Pack.packId ?? "");
                ApplyLibraryEntry(copy.visuals!, entry, GetPackFolder());
            }
            // Copying boss coordinates would create a second boss at the same place.
            copy.bossSpawns = new List<BossSpawnDefinition>();
            document.Pack.enemies!.Add(copy); selectedEnemy = document.Pack.enemies.Count - 1;
            enemySearch = ""; SelectionChanged();
            status = "Enemy duplicated with independent settings. Coordinate boss spawns were cleared; add a location for the copy if needed.";
        }
        catch (Exception error) { status = "Cannot duplicate enemy: " + error.Message; }
    }

    private void DrawPersistentPreview()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(420));
        previewScroll = GUILayout.BeginScrollView(previewScroll);
        GUILayout.Label("Preview", editorHeading!);
        int next = GUILayout.Toolbar(previewTab, new[] { "Artwork", "World" });
        if (next != previewTab) { CancelOffsetDrag(); previewTab = next; GUIUtility.ExitGUI(); }
        if (previewSprites.Length > 1)
        {
            int direction = GUILayout.Toolbar(previewDirection, ViewNames);
            if (direction != previewDirection) { CancelOffsetDrag(); previewDirection = direction; GUIUtility.ExitGUI(); }
        }
        int active = previewSprites.Length > 1 ? previewDirection : 0;
        Sprite? sprite = previewSprites.Length > active ? previewSprites[active] : null;
        Rect rect = GUILayoutUtility.GetRect(100, 360, GUILayout.ExpandWidth(true), GUILayout.Height(360));
        Fill(rect, new Color(.045f, .06f, .07f));
        if (sprite == null) GUI.Label(rect, "Choose an image or build the GLB cache to preview this enemy.");
        else if (previewTab == 0) DrawSpriteTexture(sprite, FitRect(rect, sprite.rect.width, sprite.rect.height));
        else DrawWorldPreview(sprite, rect, active);
        VisualDefinition? visuals = CurrentEnemy?.visuals;
        if (visuals != null && previewTab == 1)
        {
            VisualOffset offset = visuals.offsets?.Get(active) ?? new VisualOffset();
            string label = previewSprites.Length > 1 ? ViewNames[active] : "All directions";
            GUILayout.Label(label + " offset (tiles)", editorHeading!);
            DrawFloat("X", "offset." + active + ".x", offset.x,
                value => SetOffset(visuals, active, value, visuals.offsets?.Get(active).y ?? 0), labelWidth: 36);
            DrawFloat("Y", "offset." + active + ".y", offset.y,
                value => SetOffset(visuals, active, visuals.offsets?.Get(active).x ?? 0, value), labelWidth: 36);
            if (GUILayout.Button("Reset " + label.ToLowerInvariant() + " offset"))
            { CancelOffsetDrag(); SetOffset(visuals, active, 0, 0, clearNumbers: true); }
            GUILayout.Label("Drag the art. Arrow keys nudge by 0.01 tile; Shift by 0.1. Escape cancels a drag.", editorMuted!);
            GUILayout.Label("Red marker: occupied turf center. Offsets move artwork only. The default anchor is the bottom edge of this turf.", editorMuted!);
            if (sprite != null)
                GUILayout.Label($"Terrain: 1 × 1 tile\nSprite: {sprite.rect.width / 32f * visuals.scale:0.###} × {sprite.rect.height / 32f * visuals.scale:0.###} tiles\nScale: {visuals.scale:0.###}×", editorMuted!);
        }
        if (sprite != null) GUILayout.Label($"Source: {sprite.rect.width:0} × {sprite.rect.height:0} px", editorMuted!);
        if (GUILayout.Button("Refresh preview")) RefreshPreview();
        GUILayout.EndScrollView(); GUILayout.EndVertical();
    }

    private void DrawWorldPreview(Sprite sprite, Rect rect, int direction)
    {
        VisualDefinition? visuals = CurrentEnemy?.visuals;
        if (visuals == null) return;
        float scale = Mathf.Clamp(visuals.scale, .1f, 10f);
        float width = sprite.rect.width / 32f * scale, height = sprite.rect.height / 32f * scale;
        float tile = 96f * Mathf.Min(1f, rect.width * .86f / (Mathf.Max(1, width) * 96f),
            rect.height * .72f / (Mathf.Max(1, height) * 96f));
        Vector2 center = new(rect.center.x, rect.yMax - rect.height * .15f - tile * .5f);
        Vector2 pivot = EnemyVisualLoader.ResolvePivot((int)sprite.rect.width, (int)sprite.rect.height, visuals, direction);
        Rect art = new(center.x - pivot.x * width * tile, center.y - (1 - pivot.y) * height * tile, width * tile, height * tile);
        GUI.BeginGroup(rect);
        DrawTerrain(new Rect(center.x - rect.x - tile / 2, center.y - rect.y - tile / 2, tile, tile));
        DrawSpriteTexture(sprite, new Rect(art.x - rect.x, art.y - rect.y, art.width, art.height));
        Fill(new Rect(center.x - rect.x - 5, center.y - rect.y - 5, 10, 10), new Color(1, .34f, .34f));
        GUI.EndGroup();
        int control = GUIUtility.GetControlID("cel-offset-drag".GetHashCode(), FocusType.Keyboard, rect);
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
        {
            GUIUtility.keyboardControl = control;
            if (art.Contains(e.mousePosition))
            {
                VisualOffset offset = visuals.offsets?.Get(direction) ?? new VisualOffset();
                draggedVisual = visuals; draggedDirection = direction; dragMouse = e.mousePosition;
                dragStart = new Vector2(offset.x, offset.y); dragTileSize = tile;
                offsetControl = control; GUIUtility.hotControl = control;
            }
            e.Use();
        }
        if (draggedVisual != null && GUIUtility.hotControl == control)
        {
            if (e.type == EventType.MouseDrag)
            {
                Vector2 delta = (e.mousePosition - dragMouse) / dragTileSize;
                SetOffset(draggedVisual, draggedDirection, dragStart.x + delta.x, dragStart.y - delta.y, true); e.Use();
            }
            if (e.type == EventType.MouseUp && e.button == 0) { draggedVisual = null; GUIUtility.hotControl = 0; e.Use(); }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { CancelOffsetDrag(); e.Use(); }
        }
        if (GUIUtility.keyboardControl == control && e.type == EventType.KeyDown)
        {
            Vector2 delta = e.keyCode switch { KeyCode.LeftArrow => Vector2.left, KeyCode.RightArrow => Vector2.right,
                KeyCode.UpArrow => Vector2.up, KeyCode.DownArrow => Vector2.down, _ => Vector2.zero };
            if (delta != Vector2.zero)
            {
                VisualOffset offset = visuals.offsets?.Get(direction) ?? new VisualOffset();
                float step = e.shift ? .1f : .01f;
                SetOffset(visuals, direction, offset.x + delta.x * step, offset.y + delta.y * step, true); e.Use();
            }
        }
    }

    private void SetOffset(VisualDefinition visuals, int direction, float x, float y, bool clearNumbers = false)
    {
        if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y)) return;
        visuals.offsets ??= new VisualOffsets();
        visuals.offsets.Set(direction, (float)Math.Round(Mathf.Clamp(x, -100, 100), 3),
            (float)Math.Round(Mathf.Clamp(y, -100, 100), 3));
        if (clearNumbers) { numberText.Remove("offset." + direction + ".x"); numberText.Remove("offset." + direction + ".y"); }
    }

    private void CancelOffsetDrag()
    {
        if (draggedVisual == null) return;
        SetOffset(draggedVisual, draggedDirection, dragStart.x, dragStart.y, true);
        draggedVisual = null;
        if (GUIUtility.hotControl == offsetControl) GUIUtility.hotControl = 0;
    }

    private void DrawTerrain(Rect rect)
    {
        if (!terrainLookedUp)
        {
            terrainLookedUp = true;
            terrainSprite = Resources.FindObjectsOfTypeAll<SpriteRenderer>()
                .Where(r => r != null && r.sprite != null && EditorTerrainReference.IsGrassGround(r.gameObject.name))
                .OrderBy(r => r.gameObject.name.Equals(EditorTerrainReference.GrassPrefab, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .FirstOrDefault()?.sprite;
            if (terrainSprite != null)
                Plugin.Log.LogInfo($"Enemy editor uses grass ground sprite '{terrainSprite.name}' as its one-tile reference.");
            else
                Plugin.Log.LogWarning("Enemy editor could not find prefab_tile_grass; using a plain one-tile ground reference.");
        }
        Fill(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), new Color(.1f, .2f, .1f));
        if (terrainSprite != null) DrawSpriteTexture(terrainSprite, rect);
        else Fill(rect, new Color(.24f, .42f, .19f));
    }

    private static void Fill(Rect rect, Color color)
    { Color old = GUI.color; GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old; }

    private static Rect FitRect(Rect bounds, float width, float height)
    {
        float scale = Mathf.Min(bounds.width / width, bounds.height / height);
        return new Rect(bounds.center.x - width * scale / 2, bounds.center.y - height * scale / 2, width * scale, height * scale);
    }

    private static void DrawSpriteTexture(Sprite sprite, Rect rect)
    {
        Rect source = sprite.textureRect; Texture texture = sprite.texture;
        GUI.DrawTextureWithTexCoords(rect, texture, new Rect(source.x / texture.width, source.y / texture.height,
            source.width / texture.width, source.height / texture.height), true);
    }

    private void OpenArtLibrary()
    {
        CancelOffsetDrag(); assetLibraryOpen = true; libraryPage = 0; artDeleteConfirmation = null; ReloadArtLibrary();
    }

    private void ReloadArtLibrary()
    {
        libraryRevision = SharedArtApi.Revision;
        ClearLibraryThumbnails(); libraryEntries.Clear();
        try { libraryEntries.AddRange(SharedArtLibrary.ReadEntries(message => Plugin.Log.LogWarning("Art library: " + message))); }
        catch (Exception error) { status = "Cannot read shared art: " + error.Message; }
        if (artDeleteConfirmation != null && !libraryEntries.Any(e => e.id == artDeleteConfirmation.id)) artDeleteConfirmation = null;
    }

    private void DrawArtLibrary()
    {
        if (Event.current.type == EventType.Layout && libraryRevision != SharedArtApi.Revision) ReloadArtLibrary();
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.BeginHorizontal(); GUILayout.Label("Shared asset library", editorHeading!);
        if (ActionButton("Back to editor", 190)) { assetLibraryOpen = false; artDeleteConfirmation = null; GUIUtility.ExitGUI(); }
        GUILayout.EndHorizontal();
        GUILayout.Label("BepInEx/shared/ArtLibrary · reusable cached views · shared with Item Loader", editorMuted!);
        if (artDeleteConfirmation != null)
        {
            GUILayout.Label("Delete \"" + artDeleteConfirmation.originalName + "\" from the shared library?", editorHeading!);
            GUILayout.Label("This affects every mod's library. Pack copies and images used by other entries stay intact. " +
                "Mods reading the shared asset directly may lose access. A recovery copy is kept in ArtLibrary/trash.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Confirm delete")) { DeleteLibraryEntry(artDeleteConfirmation.id); GUIUtility.ExitGUI(); }
            if (GUILayout.Button("Cancel")) { artDeleteConfirmation = null; GUIUtility.ExitGUI(); }
            GUILayout.EndHorizontal(); GUILayout.EndVertical(); return;
        }
        string search = DrawWrappedInput(librarySearch, 1450);
        if (search != librarySearch) { librarySearch = search; libraryPage = 0; ClearLibraryThumbnails(); }
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Index existing enemy art")) IndexExistingArt();
        GUI.enabled = CurrentEnemy != null;
        if (GUILayout.Button("Add current art")) PublishCurrentArt();
        GUI.enabled = true;
        if (GUILayout.Button("Refresh library")) ReloadArtLibrary();
        GUILayout.EndHorizontal();
        SharedArtLibrary.Entry[] entries = libraryEntries.Where(e => (e.originalName + " " + e.sourceFileName)
            .IndexOf(librarySearch, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        int pages = Math.Max(1, (entries.Length + 7) / 8); libraryPage = Mathf.Clamp(libraryPage, 0, pages - 1);
        libraryScroll = GUILayout.BeginScrollView(libraryScroll);
        if (entries.Length == 0) GUILayout.Label("No matching art. Index existing enemy packs or render/import an asset and add it here.");
        for (int row = libraryPage * 8; row < Math.Min(entries.Length, libraryPage * 8 + 8); row += 2)
        {
            GUILayout.BeginHorizontal();
            for (int i = row; i < Math.Min(entries.Length, row + 2); i++) DrawArtCard(entries[i]);
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
        GUILayout.BeginHorizontal();
        if (ActionButton("◀", 40)) { libraryPage = (libraryPage - 1 + pages) % pages; ClearLibraryThumbnails(); GUIUtility.ExitGUI(); }
        GUILayout.Label($"Page {libraryPage + 1} / {pages} · {entries.Length} assets");
        if (ActionButton("▶", 40)) { libraryPage = (libraryPage + 1) % pages; ClearLibraryThumbnails(); GUIUtility.ExitGUI(); }
        GUILayout.EndHorizontal(); GUILayout.EndVertical();
    }

    private void DrawArtCard(SharedArtLibrary.Entry entry)
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width((DesignWidth - 24 - 290 - 80) / 2));
        GUILayout.Label(entry.originalName, editorHeading!);
        GUILayout.Label(entry.sourceFileName + " · " + entry.views.Length + " view(s)", editorMuted!);
        Rect rect = GUILayoutUtility.GetRect(180, 180, GUILayout.ExpandWidth(true));
        try
        {
            if (failedLibraryThumbnails.Contains(entry.id)) throw new InvalidDataException("Refresh after repairing this image.");
            if (!libraryThumbnails.TryGetValue(entry.id, out Sprite thumbnail))
            {
                var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                try
                {
                    if (!texture.LoadImage(File.ReadAllBytes(SharedArtLibrary.Resolve(entry.views[0])), false))
                        throw new InvalidDataException("Image cannot be decoded.");
                    texture.filterMode = FilterMode.Point;
                    thumbnail = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 32);
                    libraryThumbnails.Add(entry.id, thumbnail);
                }
                catch { Destroy(texture); throw; }
            }
            DrawSpriteTexture(thumbnail, FitRect(rect, thumbnail.rect.width, thumbnail.rect.height));
        }
        catch (Exception error) { failedLibraryThumbnails.Add(entry.id); GUI.Label(rect, "Preview unavailable: " + error.Message); }
        GUI.enabled = CurrentEnemy != null && !failedLibraryThumbnails.Contains(entry.id);
        if (GUILayout.Button("Use cached art")) { UseLibraryEntry(entry); GUIUtility.ExitGUI(); }
        GUI.enabled = true;
        if (GUILayout.Button("Delete from library")) { artDeleteConfirmation = entry; GUIUtility.ExitGUI(); }
        GUILayout.EndVertical();
    }

    private void DeleteLibraryEntry(string id)
    {
        artDeleteConfirmation = null;
        try
        {
            var result = SharedArtApi.Delete(id);
            ReloadArtLibrary();
            status = result.Deleted
                ? $"Art removed; {result.DeletedImageCount} unused image(s) removed, {result.SharedImageCount} shared image(s) retained. " +
                  "Recovery copy: " + result.RecoveryDirectory
                : "This art was already removed from the library.";
        }
        catch (Exception error) { ReloadArtLibrary(); status = "Cannot delete shared art: " + error.Message; }
    }

    private static void ApplyLibraryEntry(VisualDefinition visuals, SharedArtLibrary.Entry entry, string folder)
    {
        string[] paths = ArtDirections.SwapLibrarySides(SharedArtLibrary.CopyToPack(entry, folder));
        visuals.image = paths.Length == 1 ? paths[0] : null;
        visuals.directionalImages = paths.Length == 4
            ? new DirectionalImages { down = paths[0], left = paths[1], up = paths[2], right = paths[3] } : null;
        visuals.model = null; visuals.artAssetId = entry.id; visuals.artOriginalName = entry.originalName;
    }

    private void UseLibraryEntry(SharedArtLibrary.Entry entry)
    {
        if (CurrentEnemy == null) return;
        try
        {
            CancelOffsetDrag(); CurrentEnemy.visuals ??= new VisualDefinition();
            ApplyLibraryEntry(CurrentEnemy.visuals, entry, GetPackFolder());
            assetLibraryOpen = false; editorTab = 1; previewDirection = 0; RefreshPreview();
            status = "Cached art copied into this pack. Save to keep it; no GLB or shared library is required by recipients.";
        }
        catch (Exception error) { status = "Cannot use cached art: " + error.Message; }
    }

    private SharedArtLibrary.Entry PublishEnemyArt(EnemyDefinition enemy, string folder, string packId)
    {
        VisualDefinition visuals = enemy.visuals ?? throw new InvalidDataException("No visual source.");
        string id = packId + ":" + enemy.id;
        bool model = !string.IsNullOrWhiteSpace(visuals.model);
        string? modelPath = model ? EnemyVisualLoader.ResolveModelPath(folder, id, visuals.model!) : null;
        string source = model ? visuals.model! : visuals.image ?? visuals.directionalImages?.down ?? "";
        string[] paths = model ? ViewNames.Select(n => Path.Combine(EnemyVisualLoader.GetCacheDirectory(folder, id), n.ToLowerInvariant() + ".png")).ToArray()
            : visuals.directionalImages != null ? new[] { visuals.directionalImages.down!, visuals.directionalImages.left!, visuals.directionalImages.up!, visuals.directionalImages.right! }.Select(p => ResolveAsset(folder, p)).ToArray()
            : new[] { ResolveAsset(folder, source) };
        var images = new List<byte[]>();
        foreach (string path in ArtDirections.SwapLibrarySides(paths))
        {
            var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            try
            {
                if (!texture.LoadImage(File.ReadAllBytes(path), false)) throw new InvalidDataException("Cannot decode " + path);
                images.Add(texture.EncodeToPNG());
            }
            finally { Destroy(texture); }
        }
        return SharedArtLibrary.Publish(visuals.artOriginalName ?? (model ? SharedArtLibrary.OriginalModelName(modelPath) : Path.GetFileNameWithoutExtension(source)),
            Path.GetFileName(source), images, modelPath != null && File.Exists(modelPath) ? SharedArtLibrary.FileHash(modelPath) : null);
    }

    private void PublishCurrentArt()
    {
        if (CurrentEnemy == null) return;
        try
        {
            PublishEnemyArt(CurrentEnemy, GetPackFolder(), document!.Pack.packId ?? "");
            ReloadArtLibrary(); status = "Art added to the shared library. Offsets and scale remain specific to this enemy.";
        }
        catch (Exception error) { status = "Cannot publish art: " + error.Message; }
    }

    private void IndexExistingArt()
    {
        int count = 0, skipped = 0;
        foreach (PackDocument pack in documents)
            foreach (EnemyDefinition enemy in pack.Pack.enemies ?? new List<EnemyDefinition>())
                try { PublishEnemyArt(enemy, Path.GetDirectoryName(pack.Path)!, pack.Pack.packId ?? ""); count++; }
                catch (Exception error) { skipped++; Plugin.Log.LogWarning("Art index skipped '" + enemy.id + "': " + error.Message); }
        ReloadArtLibrary(); status = $"Indexed {count} enemy asset(s); {skipped} incomplete sources skipped. Pack files were not changed.";
    }

    private void ClearLibraryThumbnails()
    {
        foreach (Sprite sprite in libraryThumbnails.Values)
            if (sprite != null) { Destroy(sprite.texture); Destroy(sprite); }
        libraryThumbnails.Clear(); failedLibraryThumbnails.Clear();
    }

    protected override void OnDisable()
    {
        artDeleteConfirmation = null;
        if (open && HasUnsavedChanges) TrySaveDraft();
        CancelOffsetDrag(); ClearPreview(); ClearLibraryThumbnails();
        open = false; pickerOpen = false;
        base.OnDisable();
    }

    protected override void OnDestroy()
    {
        ClearPreview(); ClearLibraryThumbnails();
        foreach (Texture2D texture in skinTextures) if (texture != null) Destroy(texture);
        if (editorSkin != null) Destroy(editorSkin);
        base.OnDestroy();
    }
}
