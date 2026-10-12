// layerpeel 3.0 —— 解压 · 去套娃助手
// Copyright (C) 2026 layerpeel contributors
//
// 本程序是 ssh-buanshishi / f-keydle（GPL-3.0）的 C# 重写版，自 2026-10-12 起：
// 用 C# 重新实现全部功能，解压后端换成 7-Zip，去掉了 Bandizip / AHK 依赖。
// 按 GNU General Public License v3.0（或更新版本）发布，全文见 LICENSE。
// 本程序不提供任何担保。
// layerpeel 3.0 (C# 版) — 核心引擎
// 扫描 → 试密码 → 解压到临时目录 → 对账 → 并入目标 → 原包进回收站 → 循环下钻
// 判成败只看 7-Zip 退出码 + `7z t` 全量校验；解压一律先落同盘临时目录，对账通过才并入。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace LayerPeel
{
    public class Stats
    {
        public int Archives, Extracted, Skipped, Failed, NoPassword, Deleted, Recycled, Staged, Kept;
        public int Matched, Flattened, Moved, Leftovers;
        public int Protected, Fixed, Restored;
        public long BytesOut, BytesIn;
        public long T0 = DateTime.UtcNow.Ticks;
        public long ElapsedMs;

        public Dictionary<string, object> ToMap()
        {
            var d = new Dictionary<string, object>();
            d["archives"] = Archives; d["extracted"] = Extracted; d["skipped"] = Skipped;
            d["failed"] = Failed; d["noPassword"] = NoPassword; d["deleted"] = Deleted;
            d["recycled"] = Recycled; d["staged"] = Staged; d["kept"] = Kept;
            d["matched"] = Matched; d["flattened"] = Flattened; d["moved"] = Moved;
            d["leftovers"] = Leftovers; d["protected"] = Protected;
            d["fixed"] = Fixed; d["restored"] = Restored;
            d["bytesOut"] = BytesOut; d["bytesIn"] = BytesIn;
            d["elapsedMs"] = ElapsedMs;
            return d;
        }
    }

    public class Engine
    {
        private readonly Config _cfg;
        private readonly Audit _audit;
        private readonly SevenZip _sz;
        private readonly SafeDelete _del;
        private readonly HashSet<string> _protectedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly string _tempPrefix;
        private readonly int _maxDepth;
        private readonly string _stagingName;
        private List<string> _passwordOrder = new List<string>();
        private int _tmpSeq;
        public Stats Stats = new Stats();
        public Action<Dictionary<string, object>> OnEvent;

        private static readonly string[] InternalPrefixes = new string[] { "_layerpeel_", "_fkeydle_" };

        public Engine(Config cfg, Audit audit)
        {
            _cfg = cfg; _audit = audit;
            _sz = new SevenZip(Config.ResolveSevenZip(cfg),
                               120,
                               7200);
            _stagingName = Json.PathStr(cfg.Data, "delete.stagingDirName", "_layerpeel_trash_");
            _del = new SafeDelete(Json.PathStr(cfg.Data, "delete.mode", "recycle"), _stagingName);
            foreach (string e in Json.StrList(Json.Obj(cfg.Data, "safety"), "protectedExtensions"))
                _protectedExt.Add(e.ToLowerInvariant());
            _tempPrefix = "_layerpeel_tmp_";
            _maxDepth = Json.PathInt(cfg.Data, "behavior.maxDepth", 64);
            _passwordOrder.AddRange(Json.StrList(cfg.Data, "passwords"));
        }

        private void Emit(string type, Dictionary<string, object> payload)
        {
            var ev = new Dictionary<string, object>();
            ev["time"] = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            if (payload != null) foreach (var kv in payload) ev[kv.Key] = kv.Value;
            ev["type"] = type;   // 事件类型放最后，payload 不允许覆盖
            if (OnEvent != null) { try { OnEvent(ev); } catch { } }
        }

        private static Dictionary<string, object> M(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[Convert.ToString(kv[i])] = kv[i + 1];
            return d;
        }

        private bool IsInternal(string name)
        {
            foreach (string p in InternalPrefixes) if (name.StartsWith(p, StringComparison.Ordinal)) return true;
            return name == _stagingName;
        }

        // ───────────── 扫描 ─────────────
        public void ListDir(string dir, out List<string> files, out List<string> dirs)
        {
            files = new List<string>(); dirs = new List<string>();
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(dir); }
            catch (Exception e) { _audit.Warn("list-failed", M("dir", dir, "detail", e.Message)); return; }
            foreach (string full in entries)
            {
                string name = System.IO.Path.GetFileName(full);
                if (IsInternal(name)) continue;
                try
                {
                    if (Directory.Exists(full)) dirs.Add(full);
                    else files.Add(full);
                }
                catch { }
            }
        }

        public SevenZip.Probe Probe(string file, bool ignoreProtectedExt)
        {
            string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
            if (!ignoreProtectedExt && _protectedExt.Contains(ext))
            {
                var p0 = new SevenZip.Probe();
                p0.Reason = "protected-ext";
                return p0;
            }
            return _sz.ProbeFile(file, _passwordOrder);
        }

        // ───────────── 分卷组 ─────────────
        public class VolumeGroup
        {
            public List<string> Members = new List<string>();
            public string Entry;
            public List<int> Missing = new List<int>();
            public bool IsGroup;
        }

        private class VRule
        {
            public System.Text.RegularExpressions.Regex Re;
            public Func<System.Text.RegularExpressions.Match, string> Key;
            public Func<System.Text.RegularExpressions.Match, int> Idx;
        }

        public VolumeGroup FindVolumeGroup(string file)
        {
            var g = new VolumeGroup();
            g.Members.Add(file); g.Entry = file;
            string dir = System.IO.Path.GetDirectoryName(file);
            string baseName = System.IO.Path.GetFileName(file);
            string[] siblings;
            try { siblings = Directory.GetFiles(dir); } catch { return g; }

            var rules = new List<VRule>();
            rules.Add(new VRule { Re = new System.Text.RegularExpressions.Regex(@"^(.*\.(zip|7z|rar|zipx))\.(\d{3})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                                  Key = delegate (System.Text.RegularExpressions.Match m) { return m.Groups[1].Value; },
                                  Idx = delegate (System.Text.RegularExpressions.Match m) { return int.Parse(m.Groups[3].Value); } });
            rules.Add(new VRule { Re = new System.Text.RegularExpressions.Regex(@"^(.*)\.part(\d+)\.rar$", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                                  Key = delegate (System.Text.RegularExpressions.Match m) { return m.Groups[1].Value + ".partN.rar"; },
                                  Idx = delegate (System.Text.RegularExpressions.Match m) { return int.Parse(m.Groups[2].Value); } });
            rules.Add(new VRule { Re = new System.Text.RegularExpressions.Regex(@"^(.*)\.r(\d{2})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                                  Key = delegate (System.Text.RegularExpressions.Match m) { return m.Groups[1].Value + ".rNN"; },
                                  Idx = delegate (System.Text.RegularExpressions.Match m) { return int.Parse(m.Groups[2].Value) + 1; } });
            rules.Add(new VRule { Re = new System.Text.RegularExpressions.Regex(@"^(.*)\.z(\d{2})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                                  Key = delegate (System.Text.RegularExpressions.Match m) { return m.Groups[1].Value + ".zNN"; },
                                  Idx = delegate (System.Text.RegularExpressions.Match m) { return int.Parse(m.Groups[2].Value); } });
            rules.Add(new VRule { Re = new System.Text.RegularExpressions.Regex(@"^(.*)\.zx(\d{2})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                                  Key = delegate (System.Text.RegularExpressions.Match m) { return m.Groups[1].Value + ".zxNN"; },
                                  Idx = delegate (System.Text.RegularExpressions.Match m) { return int.Parse(m.Groups[2].Value); } });
            rules.Add(new VRule { Re = new System.Text.RegularExpressions.Regex(@"^(.*)\.haozip(\d+)\.(zip|7z)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase),
                                  Key = delegate (System.Text.RegularExpressions.Match m) { return m.Groups[1].Value + ".haozipN"; },
                                  Idx = delegate (System.Text.RegularExpressions.Match m) { return int.Parse(m.Groups[2].Value); } });

            foreach (VRule rule in rules)
            {
                var m = rule.Re.Match(baseName);
                if (!m.Success) continue;
                string key = rule.Key(m);
                var members = new List<KeyValuePair<int, string>>();
                foreach (string s in siblings)
                {
                    string sn = System.IO.Path.GetFileName(s);
                    var sm = rule.Re.Match(sn);
                    if (!sm.Success) continue;
                    if (rule.Key(sm) != key) continue;
                    members.Add(new KeyValuePair<int, string>(rule.Idx(sm), s));
                }
                if (members.Count < 2) break;
                members.Sort(delegate (KeyValuePair<int, string> a, KeyValuePair<int, string> b) { return a.Key.CompareTo(b.Key); });
                for (int i = 0; i < members.Count - 1; i++)
                    if (members[i + 1].Key != members[i].Key + 1) g.Missing.Add(members[i].Key + 1);
                g.Members.Clear();
                foreach (var kv in members) g.Members.Add(kv.Value);
                g.Entry = g.Members[0];
                g.IsGroup = true;
                return g;
            }
            return g;
        }

        // ───────────── 对账 ─────────────
        public static Dictionary<string, long> WalkFiles(string root)
        {
            var map = new Dictionary<string, long>();
            var stack = new Stack<string>();
            stack.Push("");
            while (stack.Count > 0)
            {
                string rel = stack.Pop();
                string abs = rel == "" ? root : System.IO.Path.Combine(root, rel);
                string[] entries;
                try { entries = Directory.GetFileSystemEntries(abs); } catch { continue; }
                foreach (string full in entries)
                {
                    string name = System.IO.Path.GetFileName(full);
                    string childRel = rel == "" ? name : System.IO.Path.Combine(rel, name);
                    try
                    {
                        if (Directory.Exists(full)) stack.Push(childRel);
                        else map[childRel.Replace('\\', '/').ToLowerInvariant()] = new FileInfo(full).Length;
                    }
                    catch { }
                }
            }
            return map;
        }

        public class VerifyResult
        {
            public bool Ok = true;
            public int ExpectedCount, ActualCount;
            public List<string> Missing = new List<string>();
            public List<string> SizeMismatch = new List<string>();
        }

        public static VerifyResult CompareEntries(List<SevenZip.Entry> entries, string tempDir)
        {
            var expected = new Dictionary<string, long>();
            foreach (SevenZip.Entry e in entries)
            {
                if (e.IsFolder) continue;
                string rel = (e.Path ?? "").Replace('\\', '/').ToLowerInvariant();
                if (rel == "") continue;
                expected[rel] = e.Size;
            }
            var actual = WalkFiles(tempDir);
            var v = new VerifyResult();
            v.ExpectedCount = expected.Count;
            v.ActualCount = actual.Count;

            foreach (var kv in expected)
            {
                long got;
                if (!actual.TryGetValue(kv.Key, out got))
                {
                    bool alt = false;
                    foreach (string k in actual.Keys)
                        if (k.EndsWith("/" + kv.Key, StringComparison.Ordinal) || kv.Key.EndsWith("/" + k, StringComparison.Ordinal)) { alt = true; break; }
                    if (!alt && v.Missing.Count < 20) v.Missing.Add(kv.Key);
                    continue;
                }
                if (kv.Value > 0 && got != kv.Value && v.SizeMismatch.Count < 20)
                    v.SizeMismatch.Add(kv.Key + "（期望 " + kv.Value + "，实际 " + got + "）");
            }
            v.Ok = v.Missing.Count == 0 && v.SizeMismatch.Count == 0;
            return v;
        }

        // ───────────── 目录操作 ─────────────
        private string NewTempDir(string parent)
        {
            string name = _tempPrefix + System.Diagnostics.Process.GetCurrentProcess().Id + "_" + (++_tmpSeq);
            string dir = System.IO.Path.Combine(parent, name);
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static long FreeBytes(string dir)
        {
            try
            {
                string root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(dir));
                return new DriveInfo(root).AvailableFreeSpace;
            }
            catch { return long.MaxValue; }
        }

        /// <summary>把 src 的内容并入 targetDir；flattenSingleRoot=null 时取配置值</summary>
        private int MergeInto(string src, string targetDir, bool? flattenSingleRoot)
        {
            bool doFlatten = flattenSingleRoot.HasValue ? flattenSingleRoot.Value
                                                       : true;
            if (doFlatten)
            {
                string[] top;
                try { top = Directory.GetFileSystemEntries(src); } catch { top = new string[0]; }
                if (top.Length == 1 && Directory.Exists(top[0])) src = top[0];
            }
            int moved = 0;
            string[] names;
            try { names = Directory.GetFileSystemEntries(src); } catch { return 0; }
            foreach (string from in names)
            {
                string name = System.IO.Path.GetFileName(from);
                string to = System.IO.Path.Combine(targetDir, name);
                if (File.Exists(to) || Directory.Exists(to))
                {
                    string ext = System.IO.Path.GetExtension(name);
                    string stem = System.IO.Path.GetFileNameWithoutExtension(name);
                    int n = 1;
                    do { to = System.IO.Path.Combine(targetDir, stem + " (" + (++n) + ")" + ext); }
                    while (File.Exists(to) || Directory.Exists(to));
                }
                try { Directory.Move(from, to); }
                catch
                {
                    try { File.Move(from, to); }
                    catch
                    {
                        if (Directory.Exists(from)) CopyDir(from, to); else File.Copy(from, to, true);
                        try { if (Directory.Exists(from)) Directory.Delete(from, true); else File.Delete(from); } catch { }
                    }
                }
                moved++;
            }
            return moved;
        }

        private static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from)) File.Copy(f, System.IO.Path.Combine(to, System.IO.Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(from)) CopyDir(d, System.IO.Path.Combine(to, System.IO.Path.GetFileName(d)));
        }

        // ───────────── 单包处理 ─────────────
        public class ArchiveResult { public string Status; public string Detail; public string Password; }

        public ArchiveResult ProcessArchive(string file)
        {
            var res = new ArchiveResult();
            VolumeGroup g = FindVolumeGroup(file);
            bool isGroup = g.IsGroup && g.Entry == file;
            List<string> cleanup = isGroup ? g.Members : new List<string>(new string[] { file });

            if (isGroup && g.Missing.Count > 0)
            {
                Stats.Failed++;
                Emit("archive-failed", M("file", file, "reason", "missing-volume"));
                _audit.Warn("missing-volume", M("file", file, "detail", "缺少分卷 " + string.Join(",", g.Missing)));
                res.Status = "missing-volume"; return res;
            }
            if (g.IsGroup && g.Entry != file) { res.Status = "volume-skipped"; return res; }

            SevenZip.Probe probe = _sz.ProbeFile(file, _passwordOrder);
            if (!probe.IsArchive)
            {
                _audit.Debug("not-archive", M("file", file, "detail", probe.Reason));
                res.Status = "not-archive"; return res;
            }
            Stats.Archives++;

            string targetDir = System.IO.Path.GetDirectoryName(file);
            double sizeMB = probe.TotalSize / 1048576.0;
            double packedMB = probe.PackedSize / 1048576.0;

            int maxRatio = 2000;
            if (packedMB > 0.5 && probe.PackedSize > 0 && probe.TotalSize / Math.Max(probe.PackedSize, 1) > maxRatio)
            {
                Stats.Skipped++;
                Emit("archive-skipped", M("file", file, "reason", "bomb-ratio"));
                _audit.Warn("skip-bomb-ratio", M("file", file, "detail", "膨胀比 " + (probe.TotalSize / Math.Max(probe.PackedSize, 1))));
                res.Status = "bomb-ratio"; return res;
            }

            // 1) 找密码（t 通过 = 密码正确且 CRC 全对）
            string password = null;
            SevenZip.TestResult test = null;
            if (probe.Encrypted)
            {
                if (probe.Password != null)
                {
                    password = probe.Password;
                    test = _sz.Test(file, password, null);
                }
                else
                {
                    Emit("password-trying", M("file", file, "count", _passwordOrder.Count));
                    string mailFile = file;
                    SevenZip.FindPwResult found = _sz.FindPassword(file, _passwordOrder,
                        delegate (string pw) { Emit("password-try", M("file", mailFile, "password", pw)); });
                    password = found.Password;
                    test = found.Result;
                    if (password == null)
                    {
                        Stats.NoPassword++;
                        string why = (test != null && test.PasswordWrong) ? "密码不在密码表中"
                                   : (test != null ? "校验未通过(code=" + test.Code + ")" : "未测试");
                        Emit("archive-failed", M("file", file, "reason", "no-password", "detail", why));
                        _audit.Warn("no-password", M("file", file, "detail", why));
                        res.Status = "no-password"; res.Detail = why; return res;
                    }
                    _passwordOrder.Remove(password);
                    _passwordOrder.Insert(0, password);   // 命中密码提到最前
                }
            }

            // 2) 非加密包也做一次 t 校验
            if (test == null)
                test = _sz.Test(file, password, null);

            if (test != null && !test.Ok)
            {
                Stats.Failed++;
                string why = test.PasswordWrong ? "密码错误" : (test.MissingVolume ? "分卷缺失" : "完整性校验失败(code=" + test.Code + ")");
                Emit("archive-failed", M("file", file, "reason", "verify-failed", "detail", why));
                _audit.Warn("test-failed", M("file", file, "detail", why));
                res.Status = "verify-failed"; res.Detail = why; return res;
            }

            // 3) 空间检查
            long need = probe.TotalSize + 64L * 1024 * 1024;
            if (FreeBytes(targetDir) < need)
            {
                Stats.Failed++;
                Emit("archive-failed", M("file", file, "reason", "no-space"));
                _audit.Error("no-space", M("file", file));
                res.Status = "no-space"; return res;
            }

            // 4) 解压到临时目录
            string tempDir = NewTempDir(targetDir);
            Emit("archive-extracting", M("file", file, "entries", probe.EntryCount, "sizeMB", Math.Round(sizeMB, 2)));
            string evFile = file;
            SevenZip.RunResult ex;
            try
            {
                ex = _sz.Extract(file, tempDir, password, delegate (string line)
                {
                    int i = line.IndexOf('%');
                    if (i > 0)
                    {
                        int s = i - 1; while (s >= 0 && line[s] >= '0' && line[s] <= '9') s--;
                        int pct;
                        if (int.TryParse(line.Substring(s + 1, i - s - 1), out pct))
                            Emit("archive-progress", M("file", evFile, "percent", pct));
                    }
                });
            }
            catch (Exception e) { ex = new SevenZip.RunResult(); ex.Code = -1; ex.Err = e.Message; }

            if (ex.Code != 0)
            {
                Stats.Failed++;
                try { Directory.Delete(tempDir, true); } catch { }
                string detail = (ex.Err + ex.Out).Trim();
                Emit("archive-failed", M("file", file, "reason", "extract-error", "detail", detail));
                _audit.Error("extract-failed", M("file", file, "detail", detail, "exitCode", ex.Code));
                res.Status = "extract-error"; res.Detail = detail; return res;
            }

            // 5) 对账
            bool verified = false;
            if (true)
            {
                SevenZip.Listing fresh = _sz.List(file, password, 0);
                VerifyResult v = CompareEntries(fresh.Entries, tempDir);
                verified = v.Ok;
                if (!v.Ok)
                {
                    Stats.Failed++;
                    try { Directory.Delete(tempDir, true); } catch { }
                    string detail = v.Missing.Count > 0
                        ? "缺少 " + v.Missing.Count + " 个文件（如 " + v.Missing[0] + "）"
                        : v.SizeMismatch.Count + " 个文件大小不符";
                    Emit("archive-failed", M("file", file, "reason", "verify-mismatch", "detail", detail));
                    _audit.Error("verify-mismatch", M("file", file, "detail", detail));
                    res.Status = "verify-mismatch"; res.Detail = detail; return res;
                }
            }

            // 6) 并入目标目录
            int moved;
            try { moved = MergeInto(tempDir, targetDir, null); }
            catch (Exception e)
            {
                try { Directory.Delete(tempDir, true); } catch { }
                Stats.Failed++;
                Emit("archive-failed", M("file", file, "reason", "merge-error", "detail", e.Message));
                _audit.Error("merge-failed", M("file", file, "detail", e.Message));
                res.Status = "merge-error"; return res;
            }
            finally { try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { } }

            Stats.Extracted++;
            Stats.BytesOut += probe.TotalSize;

            // 7) 原包（含分卷整组）进回收站
            var deletions = new List<object>();
            {
                long inBytes = 0;
                foreach (string f in cleanup)
                {
                    try { inBytes += new FileInfo(f).Length; } catch { }
                    SafeDelete.Result dr = _del.Remove(f, targetDir);
                    var rec = new Dictionary<string, object>();
                    rec["file"] = f; rec["action"] = dr.Action; rec["ok"] = dr.Ok;
                    rec["recycled"] = dr.Recycled; rec["stagedTo"] = dr.StagedTo;
                    deletions.Add(rec);
                    if (!dr.Ok) Stats.Failed++;
                    else if (dr.Action == "keep") Stats.Kept++;
                    else { Stats.Deleted++; if (dr.Recycled) Stats.Recycled++; else if (dr.Staged) Stats.Staged++; }
                }
                Stats.BytesIn += inBytes;
            }

            Emit("archive-done", M("file", file, "archiveType", probe.Type, "entries", probe.EntryCount,
                                   "moved", moved, "volumes", cleanup.Count, "sizeMB", Math.Round(sizeMB, 2),
                                   "password", password, "verified", verified, "deletions", deletions.ToArray()));
            _audit.Info("archive-extracted", M("file", file, "type", probe.Type, "entries", probe.EntryCount,
                                               "movedFiles", moved, "sizeBytes", probe.TotalSize,
                                               "passwordUsed", password != null ? "(命中)" : "(无)",
                                               "verified", verified));
            res.Status = "extracted"; res.Password = password;
            return res;
        }

        // ───────────── 目录循环 ─────────────
        public bool ProcessDir(string dir, int depth, Func<bool> aborted)
        {
            Emit("dir-start", M("dir", dir, "depth", depth));
            _audit.Info("dir-start", M("dir", dir, "depth", depth));

            int maxIter = 100000;
            for (int iter = 0; iter < maxIter; iter++)
            {
                if (aborted != null && aborted()) return true;
                List<string> files, dirs;
                ListDir(dir, out files, out dirs);
                bool progressed = false;
                foreach (string f in files)
                {
                    if (aborted != null && aborted()) return true;
                    string ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                    if (_protectedExt.Contains(ext)) continue;
                    if (!SevenZip.SniffFile(f, 1024 * 1024).Hit) continue;
                    ArchiveResult r = ProcessArchive(f);
                    if (r.Status == "extracted") progressed = true;
                }
                if (!progressed) break;
            }

            if (_maxDepth > 0 && depth + 1 >= _maxDepth)
            {
                _audit.Warn("max-depth-reached", M("dir", dir, "detail", "已达深度上限 " + _maxDepth));
                return false;
            }
            List<string> f2, dirs2;
            ListDir(dir, out f2, out dirs2);
            foreach (string d in dirs2)
            {
                if (aborted != null && aborted()) return true;
                ProcessDir(d, depth + 1, aborted);
            }
            Emit("dir-done", M("dir", dir, "depth", depth));
            return false;
        }

        // ───────────── 仅去套娃 ─────────────
        public void FlattenOnly(List<string> roots, Func<bool> aborted)
        {
            foreach (string root in roots)
            {
                if (aborted != null && aborted()) return;
                string abs = System.IO.Path.GetFullPath(root);
                if (!Directory.Exists(abs)) { _audit.Warn("flatten-target-invalid", M("dir", abs)); continue; }
                Emit("dir-start", M("dir", abs, "depth", 0));
                FlattenWalk(abs, 0, aborted);
                Emit("dir-done", M("dir", abs, "depth", 0));
            }
        }

        private void FlattenWalk(string dir, int depth, Func<bool> aborted)
        {
            if (aborted != null && aborted()) return;
            if (_maxDepth > 0 && depth >= _maxDepth) { _audit.Warn("max-depth-reached", M("dir", dir)); return; }
            List<string> files, dirs;
            ListDir(dir, out files, out dirs);
            foreach (string d in dirs) FlattenWalk(d, depth + 1, aborted);

            for (int guard = 0; guard < 512; guard++)
            {
                if (aborted != null && aborted()) return;
                var names = new List<string>();
                try
                {
                    foreach (string full in Directory.GetFileSystemEntries(dir))
                    {
                        string n = System.IO.Path.GetFileName(full);
                        if (!IsInternal(n)) names.Add(full);
                    }
                }
                catch { return; }
                if (names.Count != 1) break;
                string only = names[0];
                if (!Directory.Exists(only)) break;

                Stats.Matched++;
                int moved = MergeInto(only, dir, false);
                try { Directory.Delete(only, false); } catch { }
                Stats.Flattened++; Stats.Moved += moved;
                Emit("flatten-step", M("dir", dir, "inner", only, "moved", moved));
                _audit.Info("flatten-step", M("dir", dir, "detail", "上提 " + moved + " 项，移除空壳 " + System.IO.Path.GetFileName(only)));
            }
        }

        // ───────────── 清理残留 ─────────────
        public void CleanupLeftovers(List<string> roots, Func<bool> aborted)
        {
            var found = new List<string>();
            Action<string, int> scan = null;
            scan = delegate (string dir, int depth)
            {
                if (depth > 200) return;
                string[] entries;
                try { entries = Directory.GetDirectories(dir); } catch { return; }
                foreach (string full in entries)
                {
                    if (aborted != null && aborted()) return;
                    string name = System.IO.Path.GetFileName(full);
                    if (name.StartsWith(_tempPrefix, StringComparison.Ordinal)) { found.Add(full); continue; }
                    scan(full, depth + 1);
                }
            };
            foreach (string root in roots)
            {
                if (aborted != null && aborted()) return;
                string abs = System.IO.Path.GetFullPath(root);
                if (!Directory.Exists(abs)) continue;
                Emit("dir-start", M("dir", abs, "depth", 0));
                scan(abs, 0);
                Emit("dir-done", M("dir", abs, "depth", 0));
            }
            foreach (string dir in found)
            {
                if (aborted != null && aborted()) return;
                long size = DirSize(dir);
                SafeDelete.Result r = _del.Remove(dir, System.IO.Path.GetDirectoryName(dir));
                if (r.Ok)
                {
                    Stats.Leftovers++;
                    Emit("leftover-removed", M("dir", dir, "size", size, "action", r.Action));
                    _audit.Info("leftover-removed", M("dir", dir, "detail", (size / 1048576.0).ToString("0.0") + "MB → " + r.Action));
                }
                else { Stats.Failed++; Emit("leftover-failed", M("dir", dir, "detail", r.Error)); }
            }
        }

        private static long DirSize(string dir)
        {
            long total = 0;
            var stack = new Stack<string>();
            stack.Push(dir);
            while (stack.Count > 0)
            {
                string cur = stack.Pop();
                try
                {
                    foreach (string f in Directory.GetFiles(cur)) { try { total += new FileInfo(f).Length; } catch { } }
                    foreach (string d in Directory.GetDirectories(cur)) stack.Push(d);
                }
                catch { }
            }
            return total;
        }

        // ───────────── 入口 ─────────────
        public void Run(List<string> roots, Func<bool> aborted)
        {
            Stats = new Stats();
            foreach (string root in roots)
            {
                if (aborted != null && aborted()) break;
                string abs = System.IO.Path.GetFullPath(root);
                if (!File.Exists(abs) && !Directory.Exists(abs))
                {
                    _audit.Error("target-missing", M("dir", abs));
                    continue;
                }
                if (File.Exists(abs)) ProcessArchive(abs);
                else ProcessDir(abs, 0, aborted);
            }
            RunFinish();
        }

        /// <summary>收尾：算耗时 + 发 run-done（flatten / cleanup 这类单独入口也要调）</summary>
        public void RunFinish()
        {
            Stats.ElapsedMs = (DateTime.UtcNow.Ticks - Stats.T0) / TimeSpan.TicksPerMillisecond;
            Emit("run-done", M("stats", Stats.ToMap()));
            _audit.Info("run-done", M());
        }
    }
}
