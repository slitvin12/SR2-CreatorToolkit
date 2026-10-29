using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Il2Cpp;
using Il2CppMonomiPark.SlimeRancher;
using Il2CppTMPro;
using UnityEngine;
using SaveGamesRootUI = Il2CppMonomiPark.SlimeRancher.UI.MainMenu.SaveGamesRootUI;
using Button = UnityEngine.UI.Button;

namespace CreatorToolkit;

// «Имя сейва» (idea: Name Your Save on Nexus, written from scratch). Own names live in
// save-names.cfg (GameName → name); the save files are never touched. In the main menu the
// native slot captions («Файл 3») of the save list are replaced while the list is shown and
// handed back as soon as a name is removed. No Harmony: a twice-a-second scan, menu only.
internal sealed class SaveNameTools
{
    private sealed class Label
    {
        internal TMP_Text Text; internal string Native, Written; internal int Slot;
        internal bool AutoSize, Wrap; internal float Size, SizeMin, SizeMax; // the label's own font sizing, handed back
        internal TextOverflowModes Overflow;
        internal int MaxChars, MaxWords, MaxLines; // the label's own reveal limits, handed back
        internal float Fit; // caption width the name was fitted to (the list lays out after the first write)
        internal float FitMax; // font size the name was fitted from: the size of the untouched captions next to it
    }
    private readonly ToolkitMod _app;
    private Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private readonly Dictionary<IntPtr, Label> _labels = new();
    private readonly Dictionary<string, float> _nativeSize = new(StringComparer.Ordinal); // caption object name -> size of the native captions
    private Dictionary<int, string> _slots = new();
    private readonly List<(Transform Root, bool SlotButton)> _roots = new();
    private float _nextScan, _nextFiles, _nextGame;
    private string _savePath = "";
    internal SaveNameTools(ToolkitMod app) => _app = app;

    internal string FilePath => Path.Combine(_app.DataDirectory, "save-names.cfg");
    internal string CurrentGame { get; private set; } = "";
    internal string CurrentName => CurrentGame.Length > 0 && _names.TryGetValue(CurrentGame, out var n) ? n : "";
    internal int CurrentSlot => CapturePolicy.SlotOfGame(CurrentGame);
    internal int Count => _names.Count;
    internal int Replaced => _labels.Count;
    internal int Roots { get; private set; }
    internal int Scans { get; private set; }
    internal string Status { get; private set; } = "Имя видно в главном меню вместо «Файл N»";

    internal void Load()
    {
        try { if (File.Exists(FilePath)) _names = CapturePolicy.ParseNames(File.ReadAllText(FilePath)); }
        catch (Exception e) { _app.Fail("save names load", e); }
    }

    private void Save()
    {
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, CapturePolicy.SerializeNames(_names), new UTF8Encoding(false));
        if (File.Exists(FilePath)) File.Replace(temp, FilePath, null); else File.Move(temp, FilePath);
    }

    private static string ReadGame()
    {
        var metadata = NativeSaveMetadata.Read(GameContext.Instance?.AutoSaveDirector);
        return metadata != null && metadata.HasValue ? metadata.Value.GameName ?? "" : "";
    }

    // In game only (UI and the diagnostic relay go through Queue): names the loaded save.
    internal void SetCurrentName(string value)
    {
        string game = ReadGame();
        if (!CapturePolicy.ValidGameName(game)) throw new InvalidOperationException("Имя текущего сейва недоступно.");
        string name = CapturePolicy.CleanName(value);
        if (name.Length == 0) _names.Remove(game); else _names[game] = name;
        CurrentGame = game;
        Save();
        int slot = CapturePolicy.SlotOfGame(game);
        Status = name.Length == 0 ? "Имя убрано: в меню снова «Файл " + slot + "»" : "Сейв «" + name + "» · так он будет подписан в главном меню";
        _app.Notify(Status);
    }

    internal void Tick()
    {
        float now = Time.unscaledTime;
        if (_app.Ready)
        {
            if (_labels.Count > 0) _labels.Clear(); // the menu scene and its labels are gone
            if (now >= _nextGame) { _nextGame = now + 2f; try { CurrentGame = ReadGame(); } catch { CurrentGame = ""; } }
            return;
        }
        CurrentGame = "";
        if (now < _nextScan) return;
        _nextScan = now + .5f;
        if (_names.Count == 0 && _labels.Count == 0) return;
        var loader = SystemContext.Instance?.SceneLoader;
        if (loader == null || loader.WasCollected || loader.IsSceneLoadInProgress) return;
        Scan(now);
    }

    private void Scan(float now)
    {
        Scans++;
        FindRoots();
        if (_roots.Count > 0)
        {
            if (now >= _nextFiles) { _nextFiles = now + 3f; RefreshSlots(); }
            NativeSizes();
            foreach (var (root, slotButton) in _roots)
            {
                if (root == null || root.WasCollected) continue;
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(false))
                {
                    if (text == null || text.WasCollected) continue;
                    string current = text.text ?? "";
                    if (_labels.TryGetValue(text.Pointer, out var label))
                    {
                        if (current == label.Written)
                        {
                            string want = NameForSlot(label.Slot);
                            if (want == null) { RestoreLabel(label, true); _labels.Remove(text.Pointer); }
                            else if (want != label.Written) { text.text = want; label.Written = want; Refit(label, want); }
                            else
                            {
                                Reveal(text);
                                if (Math.Abs(Available(text) - label.Fit) > 2f || Math.Abs(MaxFor(label) - label.FitMax) > .5f) Refit(label, want);
                            }
                            continue;
                        }
                        RestoreLabel(label, false); _labels.Remove(text.Pointer); // the game rebound this label
                    }
                    int slot = CaptionSlot(text, current, root, slotButton);
                    string name = slot > 0 ? NameForSlot(slot) : null;
                    if (name == null) continue;
                    var fresh = new Label { Text = text, Native = current, Written = name, Slot = slot, AutoSize = text.enableAutoSizing, Size = text.fontSize, SizeMin = text.fontSizeMin, SizeMax = text.fontSizeMax, Wrap = text.enableWordWrapping, Overflow = text.overflowMode,
                        MaxChars = text.maxVisibleCharacters, MaxWords = text.maxVisibleWords, MaxLines = text.maxVisibleLines };
                    text.text = name;
                    Refit(fresh, name);
                    _labels[text.Pointer] = fresh;
                }
            }
        }
        Prune();
    }

    // Save list containers: the old root component and the slot buttons of the current list
    // (MainMenu_SaveGames → SaveGameSlotButton(Clone) → «Файл N»).
    private void FindRoots()
    {
        _roots.Clear();
        foreach (var root in UnityEngine.Object.FindObjectsOfType<SaveGamesRootUI>())
            if (root != null && !root.WasCollected) _roots.Add((root.transform, false));
        foreach (var button in UnityEngine.Object.FindObjectsOfType<Button>())
            if (button != null && !button.WasCollected && CapturePolicy.SlotButtonObject(button.gameObject.name)) _roots.Add((button.transform, true));
        Roots = _roots.Count;
    }

    // Only the slot title of a list item: «Файл 3» (any language), never «День 3» and the like.
    private static int CaptionSlot(TMP_Text text, string current, Transform root, bool slotButton)
    {
        int slot = CapturePolicy.SlotOfCaption(current);
        if (slot <= 0) return 0;
        string path;
        if (slotButton)
        {
            var parts = new List<string>();
            for (var t = text.transform; t != null && t.Pointer != root.Pointer && parts.Count < 6; t = t.parent) parts.Add(t.gameObject.name);
            path = string.Join("/", parts);
        }
        else path = text.gameObject.name + "/" + (text.transform.parent != null ? text.transform.parent.gameObject.name : "");
        return CapturePolicy.CaptionObject(path, slotButton) ? slot : 0;
    }

    private string NameForSlot(int slot) =>
        slot > 0 && _slots.TryGetValue(slot, out var game) && _names.TryGetValue(game, out var name) ? name : null;

    private void RefreshSlots()
    {
        var files = new List<(string Leaf, DateTime Time)>();
        try
        {
            var storage = GameContext.Instance?.AutoSaveDirector?._storageProvider?.TryCast<FileStorageProvider>();
            string path = storage != null && !storage.WasCollected ? storage.savePath : null;
            _savePath = path ?? "";
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                foreach (string file in Directory.GetFiles(path, "*.sav"))
                    files.Add((Path.GetFileNameWithoutExtension(file), File.GetLastWriteTimeUtc(file)));
        }
        catch (Exception e) { _app.Fail("save names files", e); }
        _slots = CapturePolicy.SlotGames(files);
    }

    private void Prune()
    {
        if (_labels.Count == 0) return;
        var dead = new List<IntPtr>();
        foreach (var pair in _labels)
        {
            bool alive;
            try { alive = pair.Value.Text != null && !pair.Value.Text.WasCollected; } catch { alive = false; }
            if (!alive) dead.Add(pair.Key);
        }
        foreach (var key in dead) _labels.Remove(key);
    }

    private static void RestoreLabel(Label label, bool text)
    {
        try
        {
            var t = label.Text;
            if (t == null || t.WasCollected) return;
            if (text && t.text == label.Written) t.text = label.Native;
            t.enableWordWrapping = label.Wrap; t.overflowMode = label.Overflow;
            t.enableAutoSizing = label.AutoSize; t.fontSizeMin = label.SizeMin; t.fontSizeMax = label.SizeMax;
            if (!label.AutoSize) t.fontSize = label.Size;
            t.maxVisibleCharacters = label.MaxChars; t.maxVisibleWords = label.MaxWords; t.maxVisibleLines = label.MaxLines;
        }
        catch { }
    }

    // The native slot caption caps its visible glyphs at 13 (maxVisibleCharacters, SR2 1.3.0): a longer
    // own name would lose its tail. Lifted only on our own label; RestoreLabel hands the cap back.
    private static void Reveal(TMP_Text text)
    {
        try
        {
            if (text.maxVisibleCharacters < FullReveal) text.maxVisibleCharacters = FullReveal;
            if (text.maxVisibleWords < FullReveal) text.maxVisibleWords = FullReveal;
        }
        catch { }
    }
    // The size a name starts from: the native captions of the same list («Новая игра», «Файл N»), so a
    // short name looks like its neighbours. The label's own sizing is the fallback; it can be read
    // before the game finishes styling the list.
    private void NativeSizes()
    {
        _nativeSize.Clear();
        foreach (var (root, slotButton) in _roots)
        {
            if (!slotButton || root == null || root.WasCollected) continue;
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (text == null || text.WasCollected || _labels.ContainsKey(text.Pointer)) continue;
                float size = text.fontSize;
                if (!(size >= 8f && size <= 200f)) continue;
                string key = text.gameObject.name;
                if (!_nativeSize.TryGetValue(key, out var old) || size > old) _nativeSize[key] = size;
            }
        }
    }

    private float MaxFor(Label label)
    {
        float own = label.AutoSize ? label.SizeMax : label.Size;
        try { if (_nativeSize.TryGetValue(label.Text.gameObject.name, out var size)) return size; } catch { }
        return own;
    }

    private void Refit(Label label, string name)
    {
        label.FitMax = MaxFor(label);
        label.Fit = FitLabel(label.Text, name, label.FitMax, label.Overflow);
    }

    private const int FullReveal = 99999; // TMP's own "no limit"

    // A long name shrinks to the caption width (down to half the native size) on one line; only a
    // name still too long at half size ends with «…». Measured once per write, no per-frame auto-size.
    private static float FitLabel(TMP_Text text, string name, float max, TextOverflowModes native)
    {
        float avail = 0f;
        try
        {
            text.enableAutoSizing = false; text.enableWordWrapping = false; text.fontSize = max;
            Reveal(text);
            avail = Available(text);
            // Measures the label's own (already written) text. TMP adds positive margins to the
            // preferred width; Available() already took them off.
            float need = text.GetPreferredValues(float.PositiveInfinity, float.PositiveInfinity).x - Math.Max(0f, text.margin.x) - Math.Max(0f, text.margin.z);
            float size = max;
            if (avail > 1f && need > avail) size = Math.Max(Math.Max(8f, max * .5f), max * avail / need * .97f);
            text.fontSize = size;
            text.overflowMode = avail > 1f && need * size / max > avail ? TextOverflowModes.Ellipsis : native;
            text.ForceMeshUpdate(false, true); // one re-parse of the final text, no global canvas update
        }
        catch { }
        return avail;
    }

    // Width the caption may use: its own rect, narrowed by a clipping parent (RectMask2D) if any.
    private static float Available(TMP_Text text)
    {
        var rt = text.rectTransform;
        float w = rt.rect.width - text.margin.x - text.margin.z;
        var mask = text.GetComponentInParent<UnityEngine.UI.RectMask2D>();
        if (mask != null)
        {
            var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            ((RectTransform)mask.transform).GetWorldCorners(corners);
            var mine = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            rt.GetWorldCorners(mine);
            float scale = rt.lossyScale.x;
            if (scale > 0.0001f)
            {
                float left = Math.Max(corners[0].x, mine[0].x + text.margin.x * scale);
                float right = Math.Min(corners[2].x, mine[2].x - text.margin.z * scale);
                w = Math.Min(w, (right - left) / scale);
            }
        }
        return w;
    }

    // Hands every replaced caption back (quit, diagnostics).
    internal void Restore()
    {
        foreach (var label in _labels.Values) RestoreLabel(label, true);
        _labels.Clear();
    }

    // Read-only dump of the save list labels, for the layout check of a new game version.
    private string Dump()
    {
        var sb = new StringBuilder();
        FindRoots();
        sb.Append("roots=").Append(_roots.Count).Append('\n');
        foreach (var (root, slotButton) in _roots)
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                var parts = new List<string>();
                for (var t = text.transform; t != null && parts.Count < 8; t = t.parent) parts.Insert(0, t.gameObject.name);
                sb.Append(slotButton ? "slot\t" : "root\t").Append(string.Join("/", parts)).Append('\t').Append(text.gameObject.activeInHierarchy ? "on" : "off").Append('\t')
                  .Append((text.text ?? "").Replace('\n', ' ')).Append('\t').Append(CaptionSlot(text, text.text ?? "", root.transform, slotButton))
                  .Append('\t').Append(text.enableAutoSizing ? "auto" : "fixed").Append(' ').Append(text.fontSize.ToString("0.#")).Append(' ').Append(text.fontSizeMin.ToString("0.#")).Append('-').Append(text.fontSizeMax.ToString("0.#"))
                  .Append("\tw=").Append(text.rectTransform.rect.width.ToString("0")).Append(" avail=").Append(Available(text).ToString("0"))
                  .Append(" pref=").Append(text.preferredWidth.ToString("0")).Append(" wrap=").Append(text.enableWordWrapping ? 1 : 0)
                  .Append(" ovf=").Append(text.overflowMode.ToString()).Append(" trunc=").Append(text.isTextTruncated ? 1 : 0)
                  .Append(" mvc=").Append(text.maxVisibleCharacters).Append(" mvw=").Append(text.maxVisibleWords).Append(" mvl=").Append(text.maxVisibleLines)
                  .Append(" margin=").Append(text.margin.x.ToString("0")).Append(',').Append(text.margin.z.ToString("0"))
                  .Append(" glyphs=").Append(Glyphs(text)).Append(" mask=").Append(MaskInfo(text))
                  .Append(" comps=").Append(Components(text.transform, 3)).Append('\n');
            }
        File.WriteAllText(Path.Combine(_app.DataDirectory, "save-names-dump.txt"), sb.ToString(), new UTF8Encoding(false));
        return sb.Length + " chars";
    }

    // Dump helpers: visible glyphs of the last mesh, the clipping parent and the components near the label.
    private static string Glyphs(TMP_Text text)
    {
        try
        {
            var info = text.textInfo;
            if (info == null || info.characterInfo == null) return "-";
            int n = info.characterCount, visible = 0; float right = float.MinValue; string last = "";
            for (int i = 0; i < n && i < info.characterInfo.Length; i++)
            {
                var c = info.characterInfo[i];
                if (!c.isVisible) continue;
                visible++; right = Math.Max(right, c.topRight.x); last = c.character.ToString();
            }
            int sub = 0; foreach (var s in text.GetComponentsInChildren<TMP_SubMeshUI>(true)) if (s != null) sub++;
            return visible + "/" + n + " last=" + last + " right=" + (visible > 0 ? right.ToString("0") : "-") + " xmax=" + text.rectTransform.rect.xMax.ToString("0") + " sub=" + sub;
        }
        catch (Exception e) { return "err:" + e.GetType().Name; }
    }

    private static string MaskInfo(TMP_Text text)
    {
        try
        {
            var mask = text.GetComponentInParent<UnityEngine.UI.RectMask2D>();
            if (mask == null) return "-";
            var p = mask.padding;
            return mask.gameObject.name + " w=" + ((RectTransform)mask.transform).rect.width.ToString("0") + " pad=" + p.x.ToString("0") + "," + p.z.ToString("0");
        }
        catch (Exception e) { return "err:" + e.GetType().Name; }
    }

    private static string Components(Transform t, int depth)
    {
        var parts = new List<string>();
        try
        {
            for (int d = 0; t != null && d < depth; d++, t = t.parent)
            {
                var names = new List<string>();
                foreach (var c in t.GetComponents<Component>()) if (c != null) names.Add(c.GetIl2CppType().Name);
                parts.Add(t.gameObject.name + "[" + string.Join(",", names) + "]");
            }
        }
        catch (Exception e) { parts.Add("err:" + e.GetType().Name); }
        return string.Join(" < ", parts);
    }

    internal string Diagnostics() =>
        "SaveNames: names=" + _names.Count + " current=" + (CurrentGame.Length > 0 ? CurrentGame : "-") + " name=" + (CurrentName.Length > 0 ? CurrentName : "-") +
        " roots=" + Roots + " replaced=" + _labels.Count + " scans=" + Scans + " slots=" + _slots.Count + " path=" + (_savePath.Length > 0 ? "ok" : "-") + "\n";

    // Local diagnostic relay (commands arrive lowercased): "savename set <name>", "savename clear", "savename dump".
    internal bool Handle(string command)
    {
        if (!command.StartsWith("savename ", StringComparison.Ordinal)) return false;
        string rest = command.Substring(9).Trim();
        if (rest == "clear") _app.Queue("save name", () => SetCurrentName(""));
        else if (rest.StartsWith("set ", StringComparison.Ordinal)) { string name = rest.Substring(4); _app.Queue("save name", () => SetCurrentName(name)); }
        else if (rest == "dump") _app.Notify("Save list dump: " + Dump());
        else if (rest == "restore") Restore();
        return true;
    }
}
