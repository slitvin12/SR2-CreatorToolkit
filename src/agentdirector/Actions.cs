using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CreatorToolkitAgentDirector;

/// <summary>Safety gates for actions that change the world (spawn, teleport) or press UI controls.</summary>
internal static class Gates
{
    internal static void RequireOptIn(string what)
    {
        var mod = AgentDirectorMod.I;
        if (mod != null && !mod.RequireOptIn) return;
        if (!File.Exists(Toolkit.OptInPath))
            throw new GateException(403, what + ": нужен opt-in файл " + Toolkit.OptInPath + " (пустой файл включает тестовые действия).");
    }

    internal static void RequireTestSave(string what)
    {
        var mod = AgentDirectorMod.I;
        if (mod != null && !mod.RequireTestSave) return;
        string test = Toolkit.TestGameName;
        if (test == null) throw new GateException(503, what + ": Creator Toolkit недоступен — невозможно проверить тестовый сейв.");
        string name = Toolkit.GameName();
        if (name != test)
            throw new GateException(403, what + ": загружен сейв '" + (name ?? "нет") + "', а не тестовый '" + test +
                "'. Отключение проверки: UserData\\MelonPreferences.cfg [CreatorToolkitAgent] RequireTestSave = false.");
    }

    internal static void Write(JsonWriter w)
    {
        var mod = AgentDirectorMod.I;
        string name = Toolkit.GameName(), test = Toolkit.TestGameName;
        w.Obj().P("optInRequired", mod?.RequireOptIn ?? true).P("optIn", File.Exists(Toolkit.OptInPath)).P("optInFile", Toolkit.OptInPath)
         .P("testSaveRequired", mod?.RequireTestSave ?? true).P("game", name).P("testGame", test).P("testSave", name != null && name == test)
         .EndObj();
    }
}

/// <summary>ScreenCapture.CaptureScreenshot to a fixed PNG with an explicit readiness protocol.</summary>
internal static class Shots
{
    private sealed class ShotJob
    {
        internal long Serial, LastLength = -1, Length;
        internal string Temp, Final, View, MarksMode;
        internal bool Png, Moved, Legend = true;
        internal int Width, Height, Grid, ScreenW, ScreenH;
        internal float Scale = 1f;
        internal System.Collections.Generic.List<Mark> Marks;
        internal System.Threading.Tasks.Task<ViewInfo> Render;
        internal bool Annotate => View != null;
    }
    private static readonly object Gate = new();
    internal static string Dir;
    private static string _state = "idle", _name = "screen", _path, _error = "";
    private static long _serial, _bytes;
    private static int _width, _height, _super = 1, _requestFrame = -1, _readyFrame = -1;
    private static DateTime _requestedUtc, _readyUtc;
    private static double _ms;
    private static string _view = "", _viewError = "";
    private static int _viewWidth, _viewHeight, _marksCount;

    internal static string StateName { get { lock (Gate) return _state; } }
    internal static bool Pending => StateName == "pending";

    internal static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "screen";
        var sb = new StringBuilder();
        foreach (char c in name.Trim())
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-') sb.Append(c);
            if (sb.Length >= 48) break;
        }
        return sb.Length == 0 ? "screen" : sb.ToString();
    }
    internal static string PathFor(string name) => Path.Combine(Dir, Sanitize(name) + ".png");
    internal static string ViewPathFor(string name) => Path.Combine(Dir, Sanitize(name) + ".view.png");
    private static string LegendPathFor(string view) => Path.ChangeExtension(view, ".json");

    internal static Job Capture(AgentRequest r)
    {
        string name = Sanitize(r.Args.Str("name"));
        int super = Math.Clamp(r.Args.Int("super", 1), 1, 4);
        bool wait = r.Args.Bool("wait", true), png = r.Args.Bool("png", false);
        string marks = ParseMarks(r.Args.Str("marks"));
        int grid = ParseGrid(r.Args.Str("grid"));
        float scale = Math.Clamp(r.Args.Float("scale", 1f), 0.1f, 1f);
        bool annotate = marks != null || grid > 0 || scale < 0.999f || r.Args.Bool("view", false);
        long serial;
        lock (Gate)
        {
            if (_state == "pending") throw new GateException(409, "Снимок #" + _serial + " ещё пишется. Ждите /screenshot/status или /wait?until=screenshot.");
            serial = ++_serial;
        }
        Directory.CreateDirectory(Dir);
        string final = PathFor(name), temp = Path.Combine(Dir, name + ".capturing-" + serial + ".png");
        TryDelete(temp);
        TryDelete(ViewPathFor(name));
        TryDelete(LegendPathFor(ViewPathFor(name)));
        ScreenCapture.CaptureScreenshot(temp, super);
        lock (Gate)
        {
            _state = "pending"; _name = name; _path = final; _error = ""; _super = super;
            _requestFrame = FrameStats.Frame; _readyFrame = -1; _requestedUtc = DateTime.UtcNow;
            _width = _height = 0; _bytes = 0; _ms = 0;
            _view = ""; _viewError = ""; _viewWidth = _viewHeight = 0; _marksCount = 0;
        }
        WriteStatusFile();
        var st = new ShotJob
        {
            Serial = serial, Temp = temp, Final = final, Png = png, View = annotate ? ViewPathFor(name) : null,
            MarksMode = marks, Grid = grid, Scale = scale, Legend = r.Args.Bool("legend", true),
            ScreenW = UnityEngine.Screen.width, ScreenH = UnityEngine.Screen.height
        };
        var job = Commands.NewJob(r, "screenshot", Step, false);
        job.State = st;
        job.Cleanup = j =>
        {
            var s = (ShotJob)j.State;
            bool failed = false;
            lock (Gate)
            {
                if (_serial == s.Serial && _state == "pending")
                {
                    _state = "failed";
                    _error = AgentDirectorMod.I == null || AgentDirectorMod.I.ShuttingDown ? "shutdown" : "тайм-аут: файл не записан (окно свёрнуто или кадры не рисуются?)";
                    failed = true;
                }
            }
            if (failed) { TryDelete(s.Temp); WriteStatusFile(); }
        };
        if (!wait)
        {
            job.Detached = true;
            var w = Commands.Begin(r);
            WriteFields(w);
            Commands.Done(r, w, 202);
        }
        return job;
    }

    private static bool Step(Job j)
    {
        var st = (ShotJob)j.State;
        if (j.Frames < 1) return false;
        if (st.Annotate && st.Marks == null) st.Marks = CollectMarks(st.MarksMode);
        if (!st.Moved)
        {
            var info = new FileInfo(st.Temp);
            if (!info.Exists || info.Length < 64) return false;
            long length = info.Length;
            if (length != st.LastLength) { st.LastLength = length; return false; } // size must be stable for one frame
            if (!CompletePng(st.Temp, out int width, out int height)) return false;
            try { File.Move(st.Temp, st.Final, true); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            st.Moved = true; st.Length = length; st.Width = width; st.Height = height;
            if (st.Annotate)
            {
                // Decode + draw + encode off the main thread; the job polls for completion each frame.
                string source = st.Final, target = st.View;
                var marks = st.Marks;
                int screenW = st.ScreenW, screenH = st.ScreenH, grid = st.Grid;
                float scale = st.Scale;
                st.Render = System.Threading.Tasks.Task.Run(() => Annotator.Render(source, target, marks, screenW, screenH, scale, grid));
            }
        }
        if (st.Render != null && !st.Render.IsCompleted) return false;
        ViewInfo view = null;
        string viewError = "";
        if (st.Render != null)
        {
            if (st.Render.IsFaulted) viewError = st.Render.Exception?.GetBaseException().Message ?? "render failed";
            else view = st.Render.Result;
            if (view != null) WriteLegend(st, view);
        }
        lock (Gate)
        {
            _state = "ready"; _width = st.Width; _height = st.Height; _bytes = st.Length;
            _readyFrame = FrameStats.Frame; _readyUtc = DateTime.UtcNow; _ms = (_readyUtc - _requestedUtc).TotalMilliseconds;
            _view = view?.Path ?? ""; _viewWidth = view?.Width ?? 0; _viewHeight = view?.Height ?? 0;
            _marksCount = view?.Drawn ?? 0; _viewError = viewError;
        }
        WriteStatusFile();
        if (!j.Detached)
        {
            if (st.Png) j.Request.Complete(200, "image/png", File.ReadAllBytes(view?.Path ?? st.Final));
            else
            {
                var w = Commands.Begin(j.Request);
                WriteFields(w);
                if (view != null && st.Legend) WriteLegendArray(w, st.Marks);
                Commands.Done(j.Request, w);
            }
        }
        return true;
    }

    private static string ParseMarks(string value)
    {
        switch ((value ?? "").Trim().ToLowerInvariant())
        {
            case "": case "0": case "off": case "none": case "false": case "no": return null;
            case "1": case "on": case "true": case "yes": case "all": return "all";
            case "imgui": case "toolkit": case "ct": return "imgui";
            case "canvas": case "ugui": case "native": case "game": return "canvas";
            default: throw new ArgumentException("marks = imgui | canvas | all | off");
        }
    }

    private static int ParseGrid(string value)
    {
        string v = (value ?? "").Trim().ToLowerInvariant();
        switch (v)
        {
            case "": case "0": case "off": case "none": case "false": case "no": return 0;
            case "1": case "on": case "true": case "yes": return 100;
        }
        if (int.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int step) && step >= 20)
            return Math.Min(step, 2000);
        throw new ArgumentException("grid = 1 (шаг 100 px) | N (шаг от 20 px) | off");
    }

    /// <summary>Main thread: Creator Toolkit IMGUI elements (tag = id) and interactable native uGUI selectables (tag = cN).</summary>
    private static System.Collections.Generic.List<Mark> CollectMarks(string mode)
    {
        var list = new System.Collections.Generic.List<Mark>();
        if (mode == null) return list;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        try
        {
            if (mode != "canvas" && UiRecorder.PublishedFrame >= 0 && FrameStats.Frame - UiRecorder.PublishedFrame <= 3)
                foreach (var v in UiRecorder.Snapshot())
                    if (v.Visible && v.W >= 2 && v.H >= 2)
                        list.Add(new Mark { Tag = v.Id.ToString(inv), Kind = v.Kind, Label = v.Label, X = v.X, Y = v.Y, W = v.W, H = v.H });
            if (mode != "imgui")
                foreach (var h in CanvasTree.Selectables(false))
                    if (h.Interactable)
                        list.Add(new Mark
                        {
                            Tag = "c" + h.Id.ToString(inv), Kind = "canvas", Label = string.IsNullOrEmpty(h.Text) ? h.Name : h.Text,
                            X = h.Screen.x, Y = h.Screen.y, W = h.Screen.width, H = h.Screen.height
                        });
        }
        catch (Exception e) { AgentDirectorMod.Log?.Warning("screenshot marks: " + e.Message); }
        return list;
    }

    private static void WriteLegendArray(JsonWriter w, System.Collections.Generic.List<Mark> marks)
    {
        w.Arr("legend");
        if (marks != null)
            foreach (var m in marks)
                w.Obj().P("tag", m.Tag).P("kind", m.Kind).P("label", m.Label)
                 .P("x", m.X, 0).P("y", m.Y, 0).P("w", m.W, 0).P("h", m.H, 0)
                 .P("click", m.Tag.StartsWith("c", StringComparison.Ordinal) ? "cid=" + m.Tag.Substring(1) : "id=" + m.Tag).EndObj();
        w.EndArr();
    }

    /// <summary>Writes &lt;name&gt;.view.json next to the annotated image (atomic replace).</summary>
    private static void WriteLegend(ShotJob st, ViewInfo view)
    {
        try
        {
            var w = new JsonWriter(4096);
            w.Obj().P("image", view.Path).P("source", st.Final).P("width", view.Width).P("height", view.Height)
             .P("factor", view.Factor, 4).P("screenWidth", st.ScreenW).P("screenHeight", st.ScreenH)
             .P("grid", st.Grid).P("marks", st.MarksMode).P("drawn", view.Drawn);
            WriteLegendArray(w, st.Marks);
            w.EndObj();
            string path = LegendPathFor(view.Path), temp = path + ".tmp";
            File.WriteAllText(temp, w.ToString(), new UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        catch (Exception e) { AgentDirectorMod.Log?.Warning("screenshot legend: " + e.Message); }
    }

    /// <summary>PNG signature + IHDR + trailing IEND, opened without write sharing (writer must be done).</summary>
    internal static bool CompletePng(string path, out int width, out int height)
    {
        width = height = 0;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 57) return false;
            var head = new byte[24];
            if (fs.Read(head, 0, 24) != 24) return false;
            byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            for (int i = 0; i < 8; i++) if (head[i] != signature[i]) return false;
            if (head[12] != (byte)'I' || head[13] != (byte)'H' || head[14] != (byte)'D' || head[15] != (byte)'R') return false;
            width = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
            fs.Seek(-12, SeekOrigin.End);
            var tail = new byte[12];
            if (fs.Read(tail, 0, 12) != 12) return false;
            return tail[4] == (byte)'I' && tail[5] == (byte)'E' && tail[6] == (byte)'N' && tail[7] == (byte)'D';
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { } }

    /// <summary>Removes temp captures left by a crash (startup only).</summary>
    internal static void CleanTemp()
    {
        try { foreach (var f in Directory.GetFiles(Dir, "*.capturing-*.png")) TryDelete(f); } catch (Exception) { }
    }

    internal static void WriteFields(JsonWriter w)
    {
        lock (Gate)
        {
            w.P("state", _state).P("ready", _state == "ready").P("serial", _serial).P("name", _name).P("path", _path)
             .P("width", _width).P("height", _height).P("bytes", _bytes).P("superSize", _super)
             .P("requestFrame", _requestFrame).P("readyFrame", _readyFrame)
             .P("requestedUtc", _serial == 0 ? null : _requestedUtc.ToString("O"))
             .P("readyUtc", _state == "ready" ? _readyUtc.ToString("O") : null)
             .P("captureMs", _ms, 0).P("error", _error)
             .P("view", _view.Length == 0 ? null : _view).P("viewWidth", _viewWidth).P("viewHeight", _viewHeight)
             .P("marks", _marksCount).P("viewError", _viewError.Length == 0 ? null : _viewError);
        }
    }

    private static void WriteStatusFile()
    {
        try
        {
            var w = new JsonWriter(512);
            w.Obj(); WriteFields(w); w.EndObj();
            string path = Path.Combine(Dir, "screenshot.json"), temp = path + ".tmp";
            File.WriteAllText(temp, w.ToString(), new UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        catch (Exception) { }
    }

    internal static void Status(AgentRequest r)
    {
        var w = Commands.Begin(r);
        WriteFields(w);
        Commands.Done(r, w);
    }

    internal static void Png(AgentRequest r)
    {
        string path = r.Args.Has("name") ? PathFor(r.Args.Str("name")) : (_path ?? PathFor("screen"));
        if (r.Args.Bool("view", false)) path = ViewPathFor(Path.GetFileNameWithoutExtension(path));
        if (!File.Exists(path)) throw new GateException(404, "Снимок ещё не создан: " + path + ". Сначала POST /screenshot.");
        byte[] bytes;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            bytes = new byte[fs.Length];
            int read = 0;
            while (read < bytes.Length) { int n = fs.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
        }
        r.Complete(200, "image/png", bytes);
    }
}

/// <summary>Test spawning through the toolkit's own catalog and ActorTools.Spawn (same code path as the UI).</summary>
internal static class Spawner
{
    private static object Actors()
    {
        var app = Toolkit.RequireReady();
        return Reflect.Get<object>(app, "Actors") ?? throw new MissingMemberException("ToolkitMod", "Actors");
    }

    private static IList Catalog(object actors, bool refresh)
    {
        var list = Reflect.Get<IList>(actors, "Catalog") ?? throw new MissingMemberException("ActorTools", "Catalog");
        if (refresh || list.Count == 0)
        {
            Reflect.Call(actors, "RefreshCatalog");
            list = Reflect.Get<IList>(actors, "Catalog") ?? list;
        }
        return list;
    }

    private static string S(object entry, string field) => Reflect.Get<string>(entry, field) ?? "";

    private static void WriteEntry(JsonWriter w, object e, int index)
    {
        bool can = Reflect.Get(e, "CanSpawn", false);
        w.Obj().P("index", index).P("name", S(e, "Name")).P("reference", S(e, "Reference")).P("category", S(e, "Category")).P("canSpawn", can);
        if (!can) w.P("unavailable", S(e, "UnavailableReason"));
        w.EndObj();
    }

    internal static void List(AgentRequest r)
    {
        var actors = Actors();
        var list = Catalog(actors, r.Args.Bool("refresh", false));
        string filter = Commands.Norm(r.Args.First("filter", "q", "item"));
        string category = Commands.Norm(r.Args.Str("category"));
        bool onlySpawnable = r.Args.Bool("spawnable", false);
        int limit = Math.Clamp(r.Args.Int("limit", 200), 1, 10000), matched = 0, shown = 0;
        var categories = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var w = Commands.Begin(r);
        w.P("total", list.Count).P("generation", Reflect.Get(actors, "Generation", 0));
        w.Arr("items");
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e == null) continue;
            string cat = S(e, "Category");
            categories[cat] = categories.TryGetValue(cat, out int c) ? c + 1 : 1;
            if (category.Length > 0 && Commands.Norm(cat) != category) continue;
            if (filter.Length > 0 && !Commands.Norm(S(e, "Name")).Contains(filter, StringComparison.Ordinal)
                && !Commands.Norm(S(e, "Reference")).Contains(filter, StringComparison.Ordinal)) continue;
            if (onlySpawnable && !Reflect.Get(e, "CanSpawn", false)) continue;
            matched++;
            if (shown >= limit) continue;
            shown++;
            WriteEntry(w, e, i);
        }
        w.EndArr();
        w.P("matched", matched).P("shown", shown).P("truncated", matched > shown);
        w.Obj("categories");
        foreach (var kv in categories) w.P(kv.Key.Length == 0 ? "(none)" : kv.Key, kv.Value);
        w.EndObj();
        Commands.Done(r, w);
    }

    private static object Find(IList list, string item, int index, out int found, out List<string> alternatives)
    {
        found = -1; alternatives = new List<string>();
        if (index >= 0)
        {
            if (index >= list.Count) throw new GateException(404, "index " + index + " вне каталога (0.." + (list.Count - 1) + ")");
            found = index; return list[index];
        }
        if (string.IsNullOrWhiteSpace(item)) throw new ArgumentException("Укажите item=<имя или reference> или index=N (см. /spawn/list).");
        if (item.StartsWith("#", StringComparison.Ordinal) && int.TryParse(item.Substring(1), out int hash)) return Find(list, null, hash, out found, out alternatives);
        string norm = Commands.Norm(item);
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null && S(list[i], "Reference").Equals(item.Trim(), StringComparison.OrdinalIgnoreCase)) { found = i; return list[i]; }
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null && Commands.Norm(S(list[i], "Name")) == norm) { found = i; return list[i]; }
        object first = null;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e == null) continue;
            if (!Commands.Norm(S(e, "Name")).Contains(norm, StringComparison.Ordinal) && !Commands.Norm(S(e, "Reference")).Contains(norm, StringComparison.Ordinal)) continue;
            if (first == null) { first = e; found = i; }
            else if (alternatives.Count < 10) alternatives.Add(S(e, "Name") + " (#" + i + ")");
        }
        return first ?? throw new GateException(404, "В каталоге нет '" + item + "'. См. /spawn/list?filter=...");
    }

    internal static void Spawn(AgentRequest r)
    {
        Gates.RequireOptIn("spawn");
        Gates.RequireTestSave("spawn");
        var actors = Actors();
        var list = Catalog(actors, false);
        var entry = Find(list, r.Args.First("item", "name", "reference", "ref", "body"), r.Args.Int("index", -1), out int found, out var alternatives);
        if (!Reflect.Get(entry, "CanSpawn", false))
            throw new GateException(409, "'" + S(entry, "Name") + "' недоступен для создания: " + S(entry, "UnavailableReason"));
        int count = Math.Clamp(r.Args.Int("count", 1), 1, 50);
        float distance = Math.Clamp(r.Args.Float("distance", 2.5f), 0.5f, 30f);
        float lateral = Math.Clamp(r.Args.Float("lateral", 0f), -30f, 30f);
        Reflect.Call(actors, "Spawn", entry, count, lateral, distance);
        var w = Commands.Begin(r);
        w.Name("item"); WriteEntry(w, entry, found);
        w.P("count", count).P("distance", distance, 2).P("lateral", lateral, 2).Strings("alternatives", alternatives);
        Commands.Section(w, "toolkit", Commands.WriteToolkitBrief);
        Commands.Done(r, w);
    }
}

internal sealed class TestPoint
{
    internal string Name, Group;
    internal Vector3 Position;
    internal float Yaw = float.NaN;
    internal DateTime SavedUtc;
}

/// <summary>Named player positions persisted in UserData\CreatorToolkitAgent\testpoints.txt.</summary>
internal static class TestPoints
{
    private static readonly Dictionary<string, TestPoint> Points = new(StringComparer.OrdinalIgnoreCase);
    internal static string FilePath;
    internal static TestPoint Start; // captured automatically when the world becomes ready
    private static bool _loaded;

    private static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (FilePath == null || !File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split('|');
                if (p.Length < 4) continue;
                var point = new TestPoint
                {
                    Name = p[0].Trim(),
                    Position = new Vector3(F(p[1]), F(p[2]), F(p[3])),
                    Yaw = p.Length > 4 && p[4].Length > 0 ? F(p[4]) : float.NaN,
                    Group = p.Length > 5 ? p[5] : null,
                    SavedUtc = p.Length > 6 && DateTime.TryParse(p[6], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : DateTime.MinValue
                };
                if (point.Name.Length > 0) Points[point.Name] = point;
            }
        }
        catch (Exception e) { AgentDirectorMod.Log.Warning("testpoints.txt: " + e.Message); }
    }
    private static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
    private static string F(float f) => float.IsNaN(f) ? "" : f.ToString("R", CultureInfo.InvariantCulture);

    private static void Save()
    {
        var sb = new StringBuilder("# name|x|y|z|yaw|sceneGroup|savedUtc  (Creator Toolkit Agent Director)\n");
        foreach (var p in Points.Values)
            sb.Append(p.Name).Append('|').Append(F(p.Position.x)).Append('|').Append(F(p.Position.y)).Append('|').Append(F(p.Position.z))
              .Append('|').Append(F(p.Yaw)).Append('|').Append(p.Group ?? "").Append('|').Append(p.SavedUtc.ToString("O")).Append('\n');
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
        File.Move(temp, FilePath, true);
    }

    internal static TestPoint Get(string name)
    {
        Load();
        if (name.Equals("start", StringComparison.OrdinalIgnoreCase) && !Points.ContainsKey("start")) return Start;
        return Points.TryGetValue(name, out var p) ? p : null;
    }

    internal static TestPoint CaptureCurrent(string name)
    {
        var player = Commands.PlayerObject() ?? throw new GateException(409, "Игрок не найден (мир не загружен).");
        return new TestPoint
        {
            Name = name, Position = Commands.PlayerPosition(player), Yaw = player.transform.eulerAngles.y,
            Group = Commands.SceneGroupName(), SavedUtc = DateTime.UtcNow
        };
    }

    /// <summary>Main thread: remember where the player stood when the world became ready.</summary>
    internal static void CaptureStart()
    {
        try { Start = CaptureCurrent("start"); } catch (Exception) { }
    }

    internal static void WritePoint(JsonWriter w, TestPoint p)
    {
        if (p == null) { w.Null(); return; }
        w.Obj().P("name", p.Name).P("x", p.Position.x, 3).P("y", p.Position.y, 3).P("z", p.Position.z, 3);
        if (!float.IsNaN(p.Yaw)) w.P("yaw", p.Yaw, 1);
        w.P("group", p.Group).P("saved", p.SavedUtc == DateTime.MinValue ? null : p.SavedUtc.ToString("O")).EndObj();
    }

    internal static void WriteNames(JsonWriter w)
    {
        Load();
        w.Arr();
        foreach (var k in Points.Keys) w.Val(k);
        if (Start != null && !Points.ContainsKey("start")) w.Val("start");
        w.EndArr();
    }

    internal static void List(AgentRequest r)
    {
        Load();
        var w = Commands.Begin(r);
        w.P("file", FilePath).Arr("points");
        foreach (var p in Points.Values) WritePoint(w, p);
        w.EndArr();
        w.Name("start"); WritePoint(w, Start);
        Commands.Done(r, w);
    }

    /// <summary>/testpoint?name=test [&amp;set=1 | &amp;x=&amp;y=&amp;z=[&amp;yaw=] | &amp;delete=1]</summary>
    internal static void Handle(AgentRequest r)
    {
        Load();
        string name = (r.Args.First("name", "point") ?? "test").Trim();
        if (name.Length == 0 || name.Length > 64 || name.IndexOf('|') >= 0 || name.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            throw new ArgumentException("Недопустимое имя точки.");
        string action = "get";
        if (r.Args.Bool("delete", false))
        {
            action = Points.Remove(name) ? "deleted" : "absent";
            Save();
        }
        else if (r.Args.Has("x") && r.Args.Has("y") && r.Args.Has("z"))
        {
            Points[name] = new TestPoint
            {
                Name = name, Position = new Vector3(r.Args.Float("x", 0), r.Args.Float("y", 0), r.Args.Float("z", 0)),
                Yaw = r.Args.Has("yaw") ? r.Args.Float("yaw", 0) : float.NaN, Group = r.Args.Str("group") ?? Commands.SceneGroupName(), SavedUtc = DateTime.UtcNow
            };
            action = "set-coords"; Save();
        }
        else if (r.Args.Bool("set", false) || r.Args.Bool("save", false) || (r.Method == "POST" && !r.Args.Has("get")))
        {
            Points[name] = CaptureCurrent(name);
            action = "set-current"; Save();
        }
        var point = Get(name);
        if (action == "get" && point == null)
            throw new GateException(404, "Точка '" + name + "' не сохранена. Сохранить текущую позицию: /testpoint?name=" + name + "&set=1");
        var w = Commands.Begin(r);
        w.P("action", action).Name("point"); WritePoint(w, point);
        w.P("file", FilePath);
        Commands.Done(r, w);
    }
}

/// <summary>Reset/teleport of the player to a test point (toolkit's capsule-checked placement by default).</summary>
internal static class Teleporter
{
    private sealed class TeleportState { internal TestPoint Target; internal Vector3 From; internal string Via; }

    internal static Job Teleport(AgentRequest r, string defaultPoint)
    {
        Gates.RequireOptIn("teleport");
        Gates.RequireTestSave("teleport");
        var app = Toolkit.RequireReady();
        TestPoint target;
        string pointName = r.Args.First("point", "name", "body");
        if (pointName == null && r.Args.Has("x") && r.Args.Has("y") && r.Args.Has("z"))
            target = new TestPoint { Name = "coords", Position = new Vector3(r.Args.Float("x", 0), r.Args.Float("y", 0), r.Args.Float("z", 0)) };
        else
        {
            pointName = (pointName ?? defaultPoint ?? "test").Trim();
            target = TestPoints.Get(pointName);
            if (target == null && pointName.Equals("test", StringComparison.OrdinalIgnoreCase)) target = TestPoints.Start;
            if (target == null)
                throw new GateException(404, "Точка '" + pointName + "' не сохранена (и нет авто-точки start). Сохраните: /testpoint?name=" + pointName + "&set=1");
        }
        float yaw = r.Args.Has("yaw") ? r.Args.Float("yaw", 0) : target.Yaw;
        string group = Commands.SceneGroupName();
        if (!string.IsNullOrEmpty(target.Group) && group != null && target.Group != group && !r.Args.Bool("anygroup", false))
            throw new GateException(409, "Точка '" + target.Name + "' записана в группе сцен '" + target.Group + "', сейчас '" + group +
                "'. Сначала перейдите туда или добавьте anygroup=1.");
        var player = Commands.PlayerObject() ?? throw new GateException(409, "Игрок не найден.");
        var st = new TeleportState { Target = target, From = Commands.PlayerPosition(player) };
        bool force = r.Args.Bool("force", false);
        var tools = Reflect.Get<object>(app, "Player");
        if (!force && tools != null && Reflect.Method(tools.GetType(), "PlaceForMap", 1) != null)
        {
            try { Reflect.Call(tools, "PlaceForMap", target.Position); st.Via = "toolkit.PlaceForMap"; }
            catch (InvalidOperationException e) { throw new GateException(409, e.Message + " Для прямой установки позиции добавьте force=1."); }
        }
        else { Direct(player, target.Position); st.Via = force ? "motor(force)" : "motor"; }
        if (!float.IsNaN(yaw))
        {
            try
            {
                var motor = Commands.Controller(player)?.Motor;
                if (motor != null) motor.SetRotation(Quaternion.Euler(0f, yaw, 0f), true);
            }
            catch (Exception) { }
        }
        var job = Commands.NewJob(r, "teleport", Step, true);
        job.State = st;
        return job;
    }

    private static void Direct(GameObject player, Vector3 position)
    {
        var controller = Commands.Controller(player) ?? throw new GateException(409, "SRCharacterController игрока не найден.");
        var motor = controller.Motor ?? throw new GateException(409, "KinematicCharacterMotor игрока не найден.");
        motor.SetPosition(position, true);
        motor.BaseVelocity = Vector3.zero;
        controller.ResetVelocity(false);
    }

    private static bool Step(Job j)
    {
        if (j.Frames < 3) return false;
        var st = (TeleportState)j.State;
        var player = Commands.PlayerObject();
        var w = Commands.Begin(j.Request);
        w.Name("point"); TestPoints.WritePoint(w, st.Target);
        w.P("via", st.Via);
        w.Obj("from").P("x", st.From.x, 3).P("y", st.From.y, 3).P("z", st.From.z, 3).EndObj();
        if (player != null)
        {
            var now = Commands.PlayerPosition(player);
            float distance = Vector3.Distance(now, st.Target.Position);
            w.Obj("final").P("x", now.x, 3).P("y", now.y, 3).P("z", now.z, 3).EndObj()
             .P("distance", distance, 3).P("arrived", distance < 1.5f);
        }
        Commands.Done(j.Request, w);
        return true;
    }
}
