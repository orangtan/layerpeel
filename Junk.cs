// layerpeel 3.0 —— 解压 · 去套娃助手
// Copyright (C) 2026 layerpeel contributors
//
// 本程序是 ssh-buanshishi / f-keydle（GPL-3.0）的 C# 重写版，自 2026-10-12 起：
// 用 C# 重新实现全部功能，解压后端换成 7-Zip，去掉了 Bandizip / AHK 依赖。
// 按 GNU General Public License v3.0（或更新版本）发布，全文见 LICENSE。
// 本程序不提供任何担保。
// layerpeel 3.0 (C# 版) — 删除指定文件（可保护压缩包）
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LayerPeel
{
    public class JunkCleaner
    {
        private readonly Config _cfg;
        private readonly Audit _audit;
        private readonly Engine _engine;
        private readonly SafeDelete _del;
        public Stats Stats = new Stats();
        public Action<Dictionary<string, object>> OnEvent;

        public JunkCleaner(Config cfg, Audit audit, Engine engine)
        {
            _cfg = cfg; _audit = audit; _engine = engine;
            _del = new SafeDelete(Json.PathStr(cfg.Data, "delete.mode", "recycle"),
                                  Json.PathStr(cfg.Data, "delete.stagingDirName", "_layerpeel_trash_"));
        }

        private void Emit(string type, Dictionary<string, object> payload)
        {
            var ev = new Dictionary<string, object>();
            ev["time"] = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            if (payload != null) foreach (var kv in payload) ev[kv.Key] = kv.Value;
            ev["type"] = type;
            if (OnEvent != null) { try { OnEvent(ev); } catch { } }
        }

        private static Dictionary<string, object> M(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[Convert.ToString(kv[i])] = kv[i + 1];
            return d;
        }

        public static Regex GlobToRegex(string pattern)
        {
            var sb = new StringBuilder("^");
            foreach (char c in pattern)
            {
                if (c == '*') sb.Append(".*");
                else if (c == '?') sb.Append('.');
                else sb.Append(Regex.Escape(c.ToString()));
            }
            sb.Append("$");
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase);
        }

        /// <summary>危险规则：会匹配到一切</summary>
        public static bool IsMassRule(string rule)
        {
            string r = (rule ?? "").Trim();
            if (r == "") return true;
            if (Regex.IsMatch(r, @"^\*+$")) return true;
            if (Regex.IsMatch(r, @"^\*\.\*+$")) return true;
            if (Regex.IsMatch(r, @"^\?+$")) return true;
            if (Regex.IsMatch(r, @"^\*\.\?+$")) return true;
            return false;
        }

        public class Compiled
        {
            public List<KeyValuePair<string, Func<string, bool>>> FileRules = new List<KeyValuePair<string, Func<string, bool>>>();
            public List<KeyValuePair<string, Func<string, bool>>> DirRules = new List<KeyValuePair<string, Func<string, bool>>>();
            public List<string> Errors = new List<string>();
            public bool ProtectArchives = true;
        }

        public static Compiled Plan(Config cfg)
        {
            var p = new Compiled();
            var fileRules = Json.StrList(Json.Obj(cfg.Data, "delete"), "filePatterns");
            var dirRules = Json.StrList(Json.Obj(cfg.Data, "delete"), "dirPatterns");
            foreach (string r in fileRules)
                if (IsMassRule(r)) p.Errors.Add("规则「" + r + "」会匹配到所有文件，已拒绝执行（要批量清空请手动做）");
            foreach (string r in dirRules)
                if (IsMassRule(r)) p.Errors.Add("规则「" + r + "」会匹配到所有目录，已拒绝执行");
            foreach (string r0 in fileRules)
            {
                if (r0.Trim() == "") continue;
                string r = r0; Regex re = GlobToRegex(r);
                p.FileRules.Add(new KeyValuePair<string, Func<string, bool>>(r, delegate (string name) { return re.IsMatch(name); }));
            }
            foreach (string r0 in dirRules)
            {
                if (r0.Trim() == "") continue;
                string r = r0; Regex re = GlobToRegex(r);
                p.DirRules.Add(new KeyValuePair<string, Func<string, bool>>(r, delegate (string name) { return re.IsMatch(name); }));
            }
            p.ProtectArchives = Json.PathBool(cfg.Data, "delete.protectArchives", true);
            return p;
        }

        private class Item { public string Abs; public string Name; public int Depth; }

        private static void Collect(string root, List<Item> files, List<Item> dirs)
        {
            var stack = new Stack<string>();
            stack.Push("");
            while (stack.Count > 0)
            {
                string rel = stack.Pop();
                string abs = rel == "" ? root : Path.Combine(root, rel);
                string[] entries;
                try { entries = Directory.GetFileSystemEntries(abs); } catch { continue; }
                foreach (string full in entries)
                {
                    string name = Path.GetFileName(full);
                    int depth = rel == "" ? 0 : rel.Split('\\').Length;
                    if (Directory.Exists(full)) { dirs.Add(new Item { Abs = full, Name = name, Depth = depth }); stack.Push(rel == "" ? name : Path.Combine(rel, name)); }
                    else files.Add(new Item { Abs = full, Name = name, Depth = depth });
                }
            }
            dirs.Sort(delegate (Item a, Item b) { return b.Depth.CompareTo(a.Depth); });   // 深的先删
        }

        public class RunOutcome { public Stats Stats; public bool Refused; public List<string> Errors = new List<string>(); }

        public RunOutcome Run(List<string> roots, Func<bool> aborted)        {
            Stats = new Stats();
            Compiled plan = Plan(_cfg);
            var outcome = new RunOutcome();
            if (plan.Errors.Count > 0)
            {
                foreach (string e in plan.Errors) _audit.Error("junk-rule-refused", M("detail", e));
                Emit("clean-error", M("errors", plan.Errors.ToArray()));
                outcome.Refused = true; outcome.Errors = plan.Errors; outcome.Stats = Stats;
                return outcome;
            }
            if (plan.FileRules.Count == 0 && plan.DirRules.Count == 0)
            {
                string msg = "删除规则是空的：请先在配置里填写要删的文件（如 *.url、*推广*）";
                _audit.Warn("junk-no-rules", M("detail", msg));
                Emit("clean-error", M("errors", new string[] { msg }));
                outcome.Refused = true; outcome.Errors.Add(msg); outcome.Stats = Stats;
                return outcome;
            }

            var ruleNames = new List<string>();
            foreach (var r in plan.FileRules) ruleNames.Add(r.Key);
            foreach (var r in plan.DirRules) ruleNames.Add(r.Key);
            Emit("clean-start", M("rules", ruleNames.ToArray(), "protectArchives", plan.ProtectArchives));
            foreach (string root in roots)
            {
                if (aborted != null && aborted()) break;
                string abs = Path.GetFullPath(root);
                if (!Directory.Exists(abs)) { _audit.Warn("clean-target-invalid", M("dir", abs)); continue; }
                Emit("dir-start", M("dir", abs, "depth", 0));
                var files = new List<Item>(); var dirs = new List<Item>();
                Collect(abs, files, dirs);

                foreach (Item f in files)
                {
                    if (aborted != null && aborted()) break;
                    string hit = null;
                    foreach (var r in plan.FileRules) if (r.Value(f.Name)) { hit = r.Key; break; }
                    if (hit == null) continue;
                    Stats.Matched++;
                    Handle(f.Abs, abs, hit, plan, false);                }
                foreach (Item d in dirs)
                {
                    if (aborted != null && aborted()) break;
                    string hit = null;
                    foreach (var r in plan.DirRules) if (r.Value(d.Name)) { hit = r.Key; break; }
                    if (hit == null) continue;
                    if (!Directory.Exists(d.Abs) && !File.Exists(d.Abs)) continue;
                    Stats.Matched++;
                    Handle(d.Abs, abs, hit, plan, true);                }
                Emit("dir-done", M("dir", abs, "depth", 0));
            }

            Stats.ElapsedMs = (DateTime.UtcNow.Ticks - Stats.T0) / TimeSpan.TicksPerMillisecond;
            Emit("run-done", M("stats", Stats.ToMap()));            _audit.Info("clean-done", M());            outcome.Stats = Stats;
            return outcome;
        }

        private void Handle(string target, string rootDir, string rule, Compiled plan, bool isDir)        {
            if (!isDir && plan.ProtectArchives && _engine != null)
            {
                SevenZip.Probe probe = _engine.Probe(target, true);   // 绕过扩展名白名单，伪装成 .url 的包也要保护
                if (probe != null && probe.IsArchive)
                {
                    Stats.Protected++;
                    Emit("clean-protected", M("file", target, "reason", "是压缩包，保护模式已跳过"));
                    _audit.Info("junk-protected", M("file", target, "detail", "命中规则 " + rule + "，但它是压缩包"));
                    return;
                }
            }
            SafeDelete.Decision d = _del.Decide(target);
            SafeDelete.Result r = _del.Remove(target, rootDir);
            if (r.Ok)
            {
                Stats.Deleted++;
                if (r.Recycled) Stats.Recycled++; else if (r.Staged) Stats.Staged++;
                Emit("clean-done", M("target", target, "rule", rule, "kind", isDir ? "dir" : "file",
                                     "action", r.Action, "recycled", r.Recycled, "stagedTo", r.StagedTo));
                _audit.Info("junk-deleted", M("file", target, "detail", "规则 " + rule + " → " + r.Action));
            }
            else
            {
                Stats.Failed++;
                Emit("clean-failed", M("target", target, "rule", rule, "detail", r.Error));
                _audit.Error("junk-delete-failed", M("file", target, "detail", r.Error));
            }
        }
    }
}
