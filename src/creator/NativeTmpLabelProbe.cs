// Isolated-copy rendering experiment only. Not a production typography backend.
// Borrow genuine static native TMP assets; visual layering must be inspected.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;
using UObj = UnityEngine.Object;

namespace CreatorToolkit.NativeFontResearch;

// Managed owner of existing IL2CPP components; NOT an injected MonoBehaviour.
// No input listeners, native hierarchy changes, global measurement objects,
// Addressables operations, font creation, or writes to borrowed assets.
internal sealed class NativeTmpLabelProbe : IDisposable
{
    internal const string ProbeText = "Розовый слайм Ёё Йй 0123456789";
    private GameObject _root;
    private readonly GameObject _nativeOwner;
    private readonly TMP_Text _donor;
    private readonly TMP_FontAsset _font;
    private readonly Material _sharedMaterial;
    private readonly Canvas _canvas;
    private readonly TextMeshProUGUI _label;
    private readonly List<FontStamp> _fonts;
    private bool _disposed, _meshChecked;

    private sealed class FontStamp
    {
        internal TMP_FontAsset Asset;
        internal int Characters, Glyphs;
        internal IntPtr[] Atlases;
    }

    private NativeTmpLabelProbe(GameObject root, GameObject nativeOwner, TMP_Text donor,
        TMP_FontAsset font, Material sharedMaterial, Canvas canvas,
        TextMeshProUGUI label, List<FontStamp> fonts)
    {
        _root = root; _nativeOwner = nativeOwner; _donor = donor;
        _font = font; _sharedMaterial = sharedMaterial;
        _canvas = canvas; _label = label; _fonts = fonts;
    }

    private static bool Alive(UObj value) => value != null && !value.WasCollected;

    private static GameObject RectObject(string name)
    {
        // Exact constructor/Il2CppType.Of surface must be confirmed by API audit.
        // Supplying RectTransform at construction avoids replacing a Transform.
        var types = new Il2CppReferenceArray<Il2CppSystem.Type>(1);
        types[0] = Il2CppType.Of<RectTransform>();
        return new GameObject(name, types);
    }

    private static List<FontStamp> CaptureStaticChain(TMP_FontAsset primary)
    {
        var result = new List<FontStamp>();
        var pending = new Queue<(TMP_FontAsset Asset, int Depth)>();
        var seen = new HashSet<IntPtr>();
        pending.Enqueue((primary, 0));
        while (pending.Count != 0)
        {
            var item = pending.Dequeue();
            var asset = item.Asset;
            if (!Alive(asset)) throw new InvalidOperationException("Native font chain is unavailable.");
            if (!seen.Add(asset.Pointer)) continue;
            if (item.Depth > 4 || seen.Count > 32)
                throw new InvalidOperationException("Native fallback chain exceeds the probe's bound.");
            if (asset.atlasPopulationMode != AtlasPopulationMode.Static)
                throw new InvalidOperationException("Probe accepts only already-populated static native assets.");
            var atlas = asset.atlasTextures;
            if (atlas == null || atlas.Length == 0 || asset.characterTable == null || asset.glyphTable == null)
                throw new InvalidOperationException("Static native font has no readable tables/atlas.");
            var pointers = new IntPtr[atlas.Length];
            for (int i = 0; i < atlas.Length; i++)
            {
                if (!Alive(atlas[i])) throw new InvalidOperationException("Native atlas is unavailable.");
                pointers[i] = atlas[i].Pointer;
            }
            result.Add(new FontStamp { Asset = asset, Characters = asset.characterTable.Count,
                Glyphs = asset.glyphTable.Count, Atlases = pointers });
            var fallback = asset.fallbackFontAssetTable;
            if (fallback == null) continue;
            if (fallback.Count > 32)
                throw new InvalidOperationException("Native fallback list exceeds the probe's bound.");
            for (int i = 0; i < fallback.Count; i++)
                if (Alive(fallback[i])) pending.Enqueue((fallback[i], item.Depth + 1));
        }
        return result;
    }

    internal static bool TryCreate(TMP_Text donor, GameObject authoritativeNativeOwner,
        int sortingOrder, out NativeTmpLabelProbe probe, out string failure)
    {
        probe = null; failure = ""; GameObject owned = null, unparentedChild = null;
        try
        {
            ToolkitMod.I.Audit.GuardStressCopy();
            // The CALLER must supply the real current display root, not a name-search result.
            if (!Alive(authoritativeNativeOwner) || !authoritativeNativeOwner.activeInHierarchy ||
                !Alive(donor) || !donor.isActiveAndEnabled ||
                donor.TryCast<TextMeshProUGUI>() == null)
                throw new InvalidOperationException("A visible real native UGUI donor is required.");
            var texts = authoritativeNativeOwner.GetComponentsInChildren<TMP_Text>(true);
            if (texts.Length > 256 || !texts.Any(t => Alive(t) && t.Pointer == donor.Pointer))
                throw new InvalidOperationException("Donor is not in the supplied live native display.");
            var font = donor.font; var material = donor.fontSharedMaterial;
            if (!Alive(font) || !Alive(material))
                throw new InvalidOperationException("Native font/shared material is unavailable.");
            if (font.name != "NotoSans-Medium (Cyrillic)" &&
                font.name != "NotoSans-Bold (Cyrillic)" &&
                font.name != "NotoSans-Black (Cyrillic)")
                throw new InvalidOperationException("No exact native Noto Cyrillic donor was supplied.");
            var donorCanvas = donor.canvas;
            if (!Alive(donorCanvas) || donorCanvas.targetDisplay != 0)
                throw new InvalidOperationException("This small prototype supports display 0 only.");
            if (sortingOrder < -30000 || sortingOrder > 30000)
                throw new ArgumentOutOfRangeException(nameof(sortingOrder));
            var fonts = CaptureStaticChain(font);

            owned = RectObject("CreatorToolkit.NativeFontResearch.OwnedCanvas");
            owned.SetActive(false); // Set assets before TMP is enabled; do not adopt a default font.
            var canvas = owned.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.targetDisplay = 0;
            canvas.scaleFactor = 1f; // All presentation coordinates below are physical screen pixels.
            canvas.additionalShaderChannels = donorCanvas.additionalShaderChannels;
            canvas.sortingLayerID = donorCanvas.sortingLayerID;
            canvas.sortingOrder = sortingOrder; // Canvas order only; NOT an IMGUI ordering guarantee.
            var group = owned.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            unparentedChild = RectObject("CreatorToolkit.NativeFontResearch.OwnedLabel");
            unparentedChild.transform.SetParent(owned.transform, false);
            var child = unparentedChild;
            unparentedChild = null; // Now owned by the new root, including on later setup failure.
            child.AddComponent<CanvasRenderer>();
            var label = child.AddComponent<TextMeshProUGUI>();
            label.font = font; // Genuine loaded native asset; sourceFontFile is not needed.
            label.fontWeight = donor.fontWeight;
            label.fontStyle = donor.fontStyle; // Copy actual native style; do not synthesize a new face.
            label.fontSharedMaterial = material; // Never use the instantiating fontMaterial getter.
            label.color = donor.color; // No IMGUI TextTint/gamma compensation here.
            label.enableVertexGradient = donor.enableVertexGradient;
            label.colorGradient = donor.colorGradient;
            label.characterSpacing = donor.characterSpacing;
            label.wordSpacing = donor.wordSpacing;
            label.lineSpacing = donor.lineSpacing;
            label.paragraphSpacing = donor.paragraphSpacing;
            label.enableKerning = donor.enableKerning;
            label.margin = donor.margin;
            label.alignment = donor.alignment;
            label.enableAutoSizing = false;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.richText = false;
            label.raycastTarget = false;
            label.text = "";
            var rt = label.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);

            probe = new NativeTmpLabelProbe(owned, authoritativeNativeOwner, donor,
                font, material, canvas, label, fonts);
            owned = null; // Ownership transferred; only the probe will destroy this root.
            return true;
        }
        catch (Exception e)
        {
            if (Alive(unparentedChild)) UObj.Destroy(unparentedChild);
            if (Alive(owned)) UObj.Destroy(owned);
            failure = e.GetType().Name + ": " + e.Message;
            return false;
        }
    }

    private bool SourceStillValid() =>
        !_disposed && Alive(_root) && Alive(_nativeOwner) && _nativeOwner.activeInHierarchy &&
        Alive(_donor) && _donor.isActiveAndEnabled && Alive(_font) && Alive(_sharedMaterial) &&
        Alive(_donor.font) && _donor.font.Pointer == _font.Pointer &&
        Alive(_donor.fontSharedMaterial) && _donor.fontSharedMaterial.Pointer == _sharedMaterial.Pointer;

    // Call on text/geometry changes only, not on each OnGUI Layout/Repaint event.
    internal bool TryPresent(string plainText, Rect topLeftScreenPixels, float fontSizePixels,
        out string failure)
    {
        failure = ""; _meshChecked = false; Hide();
        try
        {
            ToolkitMod.I.Audit.GuardStressCopy();
            if (!SourceStillValid()) throw new InvalidOperationException("Native owner/assets are no longer live.");
            if (string.IsNullOrEmpty(plainText) || plainText.Length > 192)
                throw new ArgumentException("Use one short plain BMP line (1–192 UTF-16 units).");
            foreach (char c in plainText.Distinct())
            {
                if (char.IsControl(c) || char.IsSurrogate(c))
                    throw new ArgumentException("Control characters and surrogate pairs are outside this probe.");
                if (char.IsWhiteSpace(c)) continue;
                // Explicit native fallback graph only. No global default, OS substitute, or dynamic addition.
                if (!_fonts.Any(f => f.Asset.HasCharacter(c, false, false)))
                    throw new InvalidOperationException("Explicit native chain lacks U+" + ((int)c).ToString("X4"));
            }
            var r = topLeftScreenPixels;
            if (!float.IsFinite(r.x) || !float.IsFinite(r.y) || !float.IsFinite(r.width) ||
                !float.IsFinite(r.height) || r.width <= 0 || r.height <= 0 ||
                r.x < 0 || r.y < 0 || r.xMax > Screen.width || r.yMax > Screen.height ||
                !float.IsFinite(fontSizePixels) || fontSizePixels < 14 || fontSizePixels > 64)
                throw new ArgumentOutOfRangeException(nameof(topLeftScreenPixels));
            _label.rectTransform.anchoredPosition = new Vector2(r.x, -r.y);
            _label.rectTransform.sizeDelta = new Vector2(r.width, r.height);
            _label.fontSize = fontSizePixels;
            _label.text = plainText;
            _root.SetActive(true);
            _label.ForceMeshUpdate(false, false); // Owned label only; no global canvas update.
            if (!Alive(_label.font) || _label.font.Pointer != _font.Pointer ||
                !Alive(_label.fontSharedMaterial) ||
                _label.fontSharedMaterial.Pointer != _sharedMaterial.Pointer)
                throw new InvalidOperationException("Owned TMP changed the requested primary font/shared material.");
            float measuredWidth = _label.preferredWidth, measuredHeight = _label.preferredHeight;
            if (!float.IsFinite(measuredWidth) || !float.IsFinite(measuredHeight) ||
                measuredWidth > r.width + .5f || measuredHeight > r.height + .5f)
                throw new InvalidOperationException("Owned TMP preferred size exceeds the reserved rectangle.");
            foreach (var graphic in _root.GetComponentsInChildren<Graphic>(true))
                if (Alive(graphic)) graphic.raycastTarget = false; // Includes owned TMP fallback submeshes.
            if (_root.GetComponentsInChildren<GraphicRaycaster>(true).Length != 0)
                throw new InvalidOperationException("Unexpected raycaster on owned probe.");
            var rows = ResolvedGlyphs(); // Reject unproven/default/weight-table substitutions.
            if (rows.Count == 0) throw new InvalidOperationException("No owned TMP glyph geometry was produced.");
            if (!BorrowedTablesUnchanged())
                throw new InvalidOperationException("Borrowed static tables/atlas references changed.");
            _meshChecked = true;
            return true;
        }
        catch (Exception e)
        {
            Hide();
            failure = e.GetType().Name + ": " + e.Message;
            return false;
        }
    }

    private List<object> ResolvedGlyphs()
    {
        var info = _label.textInfo;
        if (info == null || info.characterInfo == null || info.characterCount <= 0 ||
            info.characterCount > info.characterInfo.Length || info.characterCount > 192)
            throw new InvalidOperationException("Owned TMP character info is not ready/readable.");
        var rows = new List<object>();
        for (int i = 0; i < info.characterCount; i++)
        {
            var c = info.characterInfo[i];
            if (char.IsWhiteSpace(c.character)) continue;
            var effective = c.fontAsset;
            if (!Alive(effective) || !_fonts.Any(f => f.Asset.Pointer == effective.Pointer))
                throw new InvalidOperationException("Rendered glyph resolved outside the verified native fallback graph.");
            if (!c.isVisible)
                throw new InvalidOperationException("A non-space glyph has no visible geometry.");
            rows.Add(new { Character = c.character.ToString(), Unicode = "U+" + ((int)c.character).ToString("X4"),
                Font = effective.name, FontPointer = effective.Pointer.ToString(),
                MaterialSlot = c.materialReferenceIndex });
        }
        return rows;
    }

    private bool BorrowedTablesUnchanged()
    {
        foreach (var stamp in _fonts)
        {
            var a = stamp.Asset;
            if (!Alive(a) || a.atlasPopulationMode != AtlasPopulationMode.Static ||
                a.characterTable == null || a.glyphTable == null ||
                a.characterTable.Count != stamp.Characters || a.glyphTable.Count != stamp.Glyphs)
                return false;
            var atlas = a.atlasTextures;
            if (atlas == null || atlas.Length != stamp.Atlases.Length) return false;
            for (int i = 0; i < atlas.Length; i++)
                if (!Alive(atlas[i]) || atlas[i].Pointer != stamp.Atlases[i]) return false;
        }
        return true; // Count/pointer guard, NOT a byte-level material/atlas-content audit.
    }

    // Once per Unity frame. Caller must also pass false for modal/native handoff/closing.
    // This never scans resources or creates/updates glyph meshes.
    internal bool Maintain(bool requestedVisible)
    {
        if (_disposed || !Alive(_root)) return false;
        try
        {
            bool show = requestedVisible && _meshChecked && SourceStillValid();
            if (_root.activeSelf != show) _root.SetActive(show);
            return show;
        }
        catch
        {
            _meshChecked = false;
            Hide(); // Fail closed on a native-lifetime/read failure; harness should record diagnostics.
            return false;
        }
    }

    internal void Hide()
    {
        if (!_disposed && Alive(_root) && _root.activeSelf) _root.SetActive(false);
    }

    // Explicit, occasional diagnostics only. No file I/O or per-frame serialization.
    internal string SnapshotJson()
    {
        try
        {
            return JsonSerializer.Serialize(new {
                Backend = "genuine borrowed native TMP / owned UGUI Canvas",
                OwnerBound = true, SourceLive = SourceStillValid(),
                OwnedRootVisible = Alive(_root) && _root.activeInHierarchy,
                MeshProvenanceChecked = _meshChecked, ScreenshotProofStillRequired = true,
                Font = Alive(_font) ? _font.name : "", FontPointer = Alive(_font) ? _font.Pointer.ToString() : "",
                SharedMaterial = Alive(_sharedMaterial) ? _sharedMaterial.name : "",
                SharedMaterialPointer = Alive(_sharedMaterial) ? _sharedMaterial.Pointer.ToString() : "",
                Shader = Alive(_sharedMaterial) && Alive(_sharedMaterial.shader) ? _sharedMaterial.shader.name : "",
                CanvasOrder = Alive(_canvas) ? _canvas.sortingOrder : 0,
                FontSizePixels = Alive(_label) ? _label.fontSize : 0,
                BorrowedTableCountsAndAtlasPointersUnchanged = SourceStillValid() && BorrowedTablesUnchanged(),
                Glyphs = SourceStillValid() && _meshChecked ? ResolvedGlyphs() : new List<object>()
            }, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception e) { return JsonSerializer.Serialize(new { DiagnosticError = e.Message }); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Hide(); _disposed = true; _meshChecked = false;
        if (Alive(_root)) UObj.Destroy(_root); // Only our root and its owned components/submeshes.
        _root = null;
        _fonts.Clear();
        // Never destroy/release a borrowed font, atlas, material, donor, or native display.
        // Caller must discard this managed owner after Dispose. Destroy is deferred by Unity.
    }
}