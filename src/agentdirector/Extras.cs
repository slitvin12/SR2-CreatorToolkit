using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CreatorToolkitAgentDirector;

/// <summary>
/// After-hooks, applied on the transport thread once a main-thread command has answered:
/// "during" lists MelonLoader/Unity problems logged while the command ran; shot=NAME takes a
/// screenshot right after the action (marks=/grid=/scale= are passed through).
/// </summary>
internal static class After
{
    private static readonly HashSet<string> ErrorLevels = new(StringComparer.Ordinal) { "error", "unity-error", "unity-exception", "unity-assert" };
    private static readonly HashSet<string> Truthy = new(StringComparer.OrdinalIgnoreCase) { "", "1", "true", "yes", "on" };
    private static readonly string[] ShotKeys = { "marks", "grid", "scale", "super" };

    internal static bool IsError(string level) => ErrorLevels.Contains(level ?? "");

    internal static void Apply(AgentRequest r, long logSerial, Action<AgentRequest> execute)
    {
        try { ApplyCore(r, logSerial, execute); }
        catch (Exception e) { AgentDirectorMod.Log?.Warning("after-hook " + r.Path + ": " + e.Message); }
    }

    private static void ApplyCore(AgentRequest r, long logSerial, Action<AgentRequest> execute)
    {
        if (!r.IsCompleted || r.Body == null) return;
        if (!(r.ContentType ?? "").StartsWith("application/json", StringComparison.Ordinal)) return;
        var extra = new List<string>();
        var problems = ErrorWatch.Since(logSerial, "problems", null, 0);
        if (problems.Count > 0)
        {
            int errors = 0;
            foreach (var e in problems) if (IsError(e.Level)) errors++;
            var w = new JsonWriter(512);
            w.Obj().P("errors", errors).P("warnings", problems.Count - errors).Name("last");
            ErrorWatch.WriteEntries(w, problems.Count > 3 ? problems.GetRange(problems.Count - 3, 3) : problems);
            w.EndObj();
            extra.Add("\"during\":" + w.ToString());
        }
        string shot = r.Args.Str("shot");
        bool stopping = AgentDirectorMod.I == null || AgentDirectorMod.I.ShuttingDown;
        if (shot != null && r.Path != "/screenshot" && !stopping) extra.Add("\"shot\":" + Shot(r, shot, execute));
        if (extra.Count == 0) return;
        string body = Encoding.UTF8.GetString(r.Body).TrimEnd();
        if (!body.EndsWith("}", StringComparison.Ordinal)) return;
        string head = body.Substring(0, body.Length - 1).TrimEnd();
        string separator = head.EndsWith("{", StringComparison.Ordinal) ? "" : ",";
        r.Amend(Encoding.UTF8.GetBytes(head + separator + string.Join(",", extra) + "}"));
    }

    private static string Shot(AgentRequest r, string shot, Action<AgentRequest> execute)
    {
        string name = Truthy.Contains(shot.Trim()) ? "after" : shot;
        var target = new StringBuilder("/screenshot?name=").Append(Uri.EscapeDataString(name));
        foreach (var key in ShotKeys)
        {
            string value = r.Args.Str(key);
            if (value != null) target.Append("&").Append(key).Append("=").Append(Uri.EscapeDataString(value));
        }
        var sub = RequestFactory.Create("POST", target.ToString(), null, null, "after", "shot after #" + r.Id);
        try
        {
            execute(sub);
            if (!sub.IsCompleted) return "{\"ok\":false,\"error\":\"no reply\"}";
            return Scenario.BodyJson(sub);
        }
        finally { sub.Release(); }
    }
}

/// <summary>
/// /batch (aliases /run, /scenario): runs command lines one after another through the normal
/// pipeline (each step gets its own after-hooks). Scenario files: UserData\CreatorToolkitAgent\scenarios\*.txt.
/// </summary>
internal static class Scenario
{
    internal static readonly HashSet<string> Paths = new(StringComparer.Ordinal) { "/batch", "/run", "/scenario" };
    internal static string Dir;
    private const int MaxSteps = 300;

    internal static void Run(AgentRequest r, Action<AgentRequest> execute)
    {
        try { RunCore(r, execute); }
        catch (Exception e) { Commands.Fail(r, e); }
    }

    private static void RunCore(AgentRequest r, Action<AgentRequest> execute)
    {
        var a = r.Args;
        string source = "request", text;
        string file = a.Str("file");
        if (!string.IsNullOrWhiteSpace(file))
        {
            source = Resolve(file);
            text = File.ReadAllText(source, Encoding.UTF8);
        }
        else text = a.First("cmds", "lines") ?? r.RawBody ?? a.Str("body") ?? "";
        var lines = Split(text);
        if (lines.Count == 0)
            throw new ArgumentException("Пустой сценарий: передайте строки команд в теле (по одной на строку), cmds=a;;b или file=имя (папка " + Dir + ").");
        if (lines.Count > MaxSteps) throw new ArgumentException("Не больше " + MaxSteps + " шагов в одном сценарии.");
        bool stop = a.Bool("stop", true), brief = a.Bool("brief", false);
        int delay = Math.Clamp(a.Int("delay", 0), 0, 10000);
        double started = Clock.Now;
        var steps = new List<string>(lines.Count);
        int passed = 0, failedAt = -1, ran = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (AgentDirectorMod.I == null || AgentDirectorMod.I.ShuttingDown) { if (failedAt < 0) failedAt = i; break; }
            var sub = RequestFactory.FromLine(lines[i], "batch");
            double t0 = Clock.Now;
            bool ok;
            try
            {
                if (Paths.Contains(sub.Path)) sub.Json(400, "{\"ok\":false,\"status\":400,\"error\":\"вложенный /batch запрещён\"}");
                else if (!sub.IsCompleted) execute(sub);
                ran++;
                ok = sub.Status >= 200 && sub.Status < 300;
                var w = new JsonWriter(256);
                w.Obj().P("i", i).P("line", lines[i]).P("status", sub.Status).P("ms", (Clock.Now - t0) * 1000, 0);
                if (!brief || !ok) w.Name("body").Raw(BodyJson(sub));
                w.EndObj();
                steps.Add(w.ToString());
            }
            finally { sub.Release(); }
            if (ok) passed++;
            else
            {
                if (failedAt < 0) failedAt = i;
                if (stop) break;
            }
            if (delay > 0 && i + 1 < lines.Count) Thread.Sleep(delay);
        }
        bool allOk = failedAt < 0;
        var reply = Commands.Begin(r, allOk);
        reply.P("source", source).P("steps", lines.Count).P("ran", ran).P("passed", passed).P("failedAt", failedAt)
             .P("ms", (Clock.Now - started) * 1000, 0);
        if (!allOk) reply.P("failedLine", lines[failedAt]);
        reply.Name("results").Raw("[" + string.Join(",", steps) + "]");
        Commands.Done(r, reply, allOk ? 200 : 417);
    }

    internal static string BodyJson(AgentRequest r)
    {
        if (r.Body == null) return "null";
        string type = r.ContentType ?? "";
        if (type.StartsWith("application/json", StringComparison.Ordinal)) return Encoding.UTF8.GetString(r.Body);
        if (type.StartsWith("text/", StringComparison.Ordinal))
        {
            string t = Encoding.UTF8.GetString(r.Body);
            return JsonWriter.Quoted(t.Length > 4000 ? t.Substring(0, 4000) + "…" : t);
        }
        return "{\"binary\":true,\"bytes\":" + r.Body.Length + ",\"contentType\":" + JsonWriter.Quoted(type) + "}";
    }

    internal static List<string> Split(string text)
    {
        var list = new List<string>();
        foreach (var raw in (text ?? "").Replace("\r", "\n").Split(new[] { "\n", ";;" }, StringSplitOptions.None))
        {
            string line = raw.Trim().TrimStart((char)0xFEFF);
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
            list.Add(line);
        }
        return list;
    }

    private static string Resolve(string file)
    {
        string name = Shots.Sanitize(Path.GetFileNameWithoutExtension(file.Trim()));
        string path = Path.Combine(Dir, name + ".txt");
        if (File.Exists(path)) return path;
        throw new GateException(404, "Нет сценария «" + name + "» в " + Dir + ". Есть: " + string.Join(", ", Names()));
    }

    internal static List<string> Names()
    {
        var names = new List<string>();
        try { foreach (var f in Directory.GetFiles(Dir, "*.txt")) names.Add(Path.GetFileNameWithoutExtension(f)); }
        catch (Exception) { }
        return names;
    }

    internal static void WriteDefaults()
    {
        Directory.CreateDirectory(Dir);
        string smoke = Path.Combine(Dir, "smoke.txt");
        if (!File.Exists(smoke)) File.WriteAllText(smoke, Smoke, new UTF8Encoding(false));
    }

    private const string Smoke = """
# Смоук-тест Creator Toolkit: меню, все вкладки со снимками и метками, без новых ошибок.
# Запуск: POST /batch?file=smoke&brief=1   (или: ct smoke)
# Снимки: UserData\CreatorToolkitAgent\screenshots\smoke-tab*.view.png (+ .view.json — легенда меток)
# Формат строки: [GET|POST] /путь?параметры [тело]; пробелы в значениях — через %20 или +.
GET  /wait?until=ready&timeout=180
POST /errors/reset
POST /menu?action=open
POST /tab?index=0&shot=smoke-tab0&marks=imgui&scale=0.5
POST /tab?index=1&shot=smoke-tab1&marks=imgui&scale=0.5
POST /tab?index=2&shot=smoke-tab2&marks=imgui&scale=0.5
POST /tab?index=3&shot=smoke-tab3&marks=imgui&scale=0.5
POST /tab?index=4&shot=smoke-tab4&marks=imgui&scale=0.5
POST /tab?index=5&shot=smoke-tab5&marks=imgui&scale=0.5
POST /tab?index=6&shot=smoke-tab6&marks=imgui&scale=0.5
POST /tab?index=7&shot=smoke-tab7&marks=imgui&scale=0.5
GET  /assert?noerrors=1&menu=open
POST /menu?action=close
GET  /assert?noerrors=1&menu=closed
""";
}

/// <summary>/assert and extra /wait conditions.</summary>
internal static partial class Commands
{
    private static readonly HashSet<string> ClosedWords = new(StringComparer.OrdinalIgnoreCase) { "0", "false", "no", "off", "closed", "close", "нет" };

    /// <summary>Main thread: a visible Creator Toolkit element (and optionally a native uGUI one) matches.</summary>
    internal static bool UiPresent(string label, string kind, bool canvas)
    {
        string n = string.IsNullOrEmpty(label) ? null : Norm(label);
        if (UiRecorder.PublishedFrame >= 0 && FrameStats.Frame - UiRecorder.PublishedFrame <= 30)
            foreach (var v in UiRecorder.Snapshot())
                if (v.Visible && ItemMatches(v, kind, n, false)) return true;
        if (canvas && n != null)
            foreach (var h in CanvasTree.Selectables(false))
                if (Norm(h.Text).Contains(n, StringComparison.Ordinal) || Norm(h.Name).Contains(n, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>True once the IMGUI layout (labels, rects, toggles, tab, menu) is unchanged for quiet frames.</summary>
    private static Func<bool> StableCondition(int quiet)
    {
        long last = 0;
        int same = -1;
        return () =>
        {
            long signature = UiSignature();
            if (same >= 0 && signature == last) same++;
            else { same = 0; last = signature; }
            return same >= quiet;
        };
    }

    private static long UiSignature()
    {
        long h = 1469598103;
        unchecked
        {
            foreach (var v in UiRecorder.Snapshot())
            {
                h = h * 31 + (v.Label ?? "").GetHashCode();
                h = h * 31 + (long)Math.Round(v.X) * 7919 + (long)Math.Round(v.Y);
                h = h * 31 + (v.On ? 1 : 0) + (v.Visible ? 2 : 0) + (v.Selected ? 4 : 0);
            }
            h = h * 31 + (Toolkit.MenuOpen ? 1 : 0);
            h = h * 31 + Toolkit.Tab;
        }
        return h;
    }

    private static Func<bool> NearCondition(Args a)
    {
        string name = a.First("point", "near") ?? "test";
        var p = TestPoints.Get(name) ?? throw new GateException(404, "Нет тестовой точки «" + name + "» (см. /testpoints).");
        float radius = Math.Max(0.1f, a.Float("radius", 2f));
        return () =>
        {
            var player = PlayerObject();
            return player != null && Vector3.Distance(PlayerPosition(player), p.Position) <= radius;
        };
    }

    private sealed class AssertResult
    {
        internal readonly List<string> Passed = new();
        internal readonly List<string> Failures = new();
        internal void Check(string name, bool ok, string expected, string actual)
        {
            if (ok) { Passed.Add(name); return; }
            Failures.Add(new JsonWriter(160).Obj().P("check", name).P("expected", expected).P("actual", actual).EndObj().ToString());
        }
    }

    private static IEnumerable<string> Many(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;
        foreach (var part in value.Split("|"))
        {
            string p = part.Trim();
            if (p.Length > 0) yield return p;
        }
    }

    private static int TabIndex(string raw, string[] tabs)
    {
        raw = (raw ?? "").Trim();
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)) return n;
        string norm = Norm(raw);
        for (int i = 0; i < tabs.Length; i++) if (Norm(tabs[i]).StartsWith(norm, StringComparison.Ordinal)) return i;
        return -1;
    }

    private static string DescribeTab(int tab, string[] tabs) =>
        tab >= 0 && tab < tabs.Length ? tab.ToString(CultureInfo.InvariantCulture) + " " + tabs[tab] : tab.ToString(CultureInfo.InvariantCulture);

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>GET /assert: 200 when every requested check passes, otherwise 417 with the failures.</summary>
    private static void Assert(AgentRequest r)
    {
        var a = r.Args;
        var res = new AssertResult();
        if (a.Has("noerrors") && a.Bool("noerrors", true))
        {
            long n = ErrorWatch.ErrorsSinceReset;
            res.Check("noerrors", n == 0, "0", Num(n));
        }
        if (a.Has("maxerrors"))
        {
            long n = ErrorWatch.ErrorsSinceReset;
            int max = a.Int("maxerrors", 0);
            res.Check("maxerrors", n <= max, "<= " + max, Num(n));
        }
        if (a.Has("ready"))
        {
            bool want = a.Bool("ready", true), have = Toolkit.Bound ? Toolkit.Ready : Gameplay();
            res.Check("ready", have == want, want ? "true" : "false", have ? "true" : "false");
        }
        if (a.Has("menu"))
        {
            bool want = !ClosedWords.Contains((a.Str("menu") ?? "").Trim()), have = Toolkit.MenuOpen;
            res.Check("menu", have == want, want ? "open" : "closed", have ? "open" : "closed");
        }
        if (a.Has("tab"))
        {
            var tabs = Toolkit.Tabs;
            int want = TabIndex(a.Str("tab"), tabs), have = Toolkit.MenuOpen ? Toolkit.Tab : -1;
            res.Check("tab", want >= 0 && have == want, DescribeTab(want, tabs), DescribeTab(have, tabs));
        }
        string kind = a.Str("kind");
        bool canvas = a.Bool("canvas", false);
        foreach (string label in Many(a.Str("ui"))) res.Check("ui:" + label, UiPresent(label, kind, canvas), "visible", "absent");
        foreach (string label in Many(a.Str("noui"))) res.Check("noui:" + label, !UiPresent(label, kind, canvas), "absent", "visible");
        if (a.Has("scene"))
        {
            string want = Norm(a.Str("scene")), have = SceneManager.GetActiveScene().name + " / " + (SceneGroupName() ?? "");
            res.Check("scene", Norm(have).Contains(want, StringComparison.Ordinal), want, have);
        }
        if (a.Has("status"))
        {
            string want = Norm(a.Str("status")), have = Toolkit.Status ?? "";
            res.Check("status", Norm(have).Contains(want, StringComparison.Ordinal), want, have);
        }
        if (a.Has("near"))
        {
            string name = string.IsNullOrWhiteSpace(a.Str("near")) ? "test" : a.Str("near").Trim();
            var p = TestPoints.Get(name) ?? throw new GateException(404, "Нет тестовой точки «" + name + "» (см. /testpoints).");
            float radius = Math.Max(0.1f, a.Float("radius", 2f));
            var player = PlayerObject();
            double d = player == null ? double.PositiveInfinity : Vector3.Distance(PlayerPosition(player), p.Position);
            res.Check("near:" + name, d <= radius, "<= " + Num(radius) + " m", player == null ? "no player" : Num(d) + " m");
        }
        if (a.Has("fps"))
        {
            float min = a.Float("fps", 30f);
            double have = FrameStats.FpsAvg;
            res.Check("fps", have >= min, ">= " + Num(min), Num(have));
        }
        if (res.Passed.Count + res.Failures.Count == 0)
            throw new ArgumentException("Укажите проверки: noerrors=1, maxerrors=N, ready=1, menu=open|closed, tab=N|имя, ui=метка[|метка2], noui=метка, kind=, canvas=1, scene=, status=, near=точка&radius=2, fps=30");
        bool allOk = res.Failures.Count == 0;
        var w = Begin(r, allOk);
        w.P("passed", res.Passed.Count).P("failed", res.Failures.Count).Strings("pass", res.Passed);
        w.Name("failures").Raw("[" + string.Join(",", res.Failures) + "]");
        if (!allOk && ErrorWatch.ErrorsSinceReset > 0)
        {
            w.Name("recentErrors");
            ErrorWatch.WriteEntries(w, ErrorWatch.Since(ErrorWatch.ResetSerial, "error", null, 5));
        }
        Section(w, "toolkit", WriteToolkitBrief);
        Done(r, w, allOk ? 200 : 417);
    }
}
