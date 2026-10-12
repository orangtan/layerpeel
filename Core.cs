// layerpeel 3.0 —— 解压 · 去套娃助手
// Copyright (C) 2026 layerpeel contributors
//
// 本程序是 ssh-buanshishi / f-keydle（GPL-3.0）的 C# 重写版，自 2026-10-12 起：
// 用 C# 重新实现全部功能，解压后端换成 7-Zip，去掉了 Bandizip / AHK 依赖。
// 按 GNU General Public License v3.0（或更新版本）发布，全文见 LICENSE。
// 本程序不提供任何担保。
// layerpeel 3.0 (C# 版) — 核心：JSON / 配置 / 审计日志 / 安全删除
// 目标框架 .NET Framework 4.8（系统自带），编译器 csc.exe 只支持 C# 5：
// 不要用字符串插值 $""、?.、表达式体成员、自动属性初始化器。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace LayerPeel
{
    // ───────────────────────────── JSON 小工具（基于 System.Web.Extensions 的 JavaScriptSerializer）─────────────────────────────
    public static class Json
    {
        private static readonly JavaScriptSerializer Ser = new JavaScriptSerializer();

        public static Dictionary<string, object> ParseObject(string text)
        {
            if (string.IsNullOrEmpty(text)) return new Dictionary<string, object>();
            object o = Ser.DeserializeObject(text);
            Dictionary<string, object> d = o as Dictionary<string, object>;
            if (d == null) throw new Exception("配置不是合法的 JSON 对象");
            return d;
        }

        public static string Stringify(object o)
        {
            return Ser.Serialize(o);
        }

        public static string StringifyPretty(object o)
        {
            // JavaScriptSerializer 没有缩进输出，这里自己排一下，方便人工编辑配置
            string compact = Ser.Serialize(o);
            var sb = new StringBuilder();
            int indent = 0;
            bool inStr = false;
            for (int i = 0; i < compact.Length; i++)
            {
                char c = compact[i];
                if (inStr)
                {
                    sb.Append(c);
                    if (c == '\\') { if (i + 1 < compact.Length) { sb.Append(compact[i + 1]); i++; } }
                    else if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; sb.Append(c); continue; }
                if (c == '{' || c == '[') { sb.Append(c); indent++; sb.Append("\r\n"); sb.Append(new string(' ', indent * 2)); continue; }
                if (c == '}' || c == ']') { indent--; sb.Append("\r\n"); sb.Append(new string(' ', indent * 2)); sb.Append(c); continue; }
                if (c == ',') { sb.Append(c); sb.Append("\r\n"); sb.Append(new string(' ', indent * 2)); continue; }
                if (c == ':') { sb.Append(": "); continue; }
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ── 取值助手（配置结构是松散的，用字典比定义一堆类省事）──
        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
        {
            if (d == null) return new Dictionary<string, object>();
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return new Dictionary<string, object>();
            Dictionary<string, object> sub = v as Dictionary<string, object>;
            return sub != null ? sub : new Dictionary<string, object>();
        }

        public static object[] Arr(Dictionary<string, object> d, string key)
        {
            if (d == null) return new object[0];
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return new object[0];
            object[] a = v as object[];
            return a != null ? a : new object[0];
        }

        public static List<string> StrList(Dictionary<string, object> d, string key)
        {
            var list = new List<string>();
            foreach (object o in Arr(d, key)) if (o != null) list.Add(Convert.ToString(o));
            return list;
        }

        public static string Str(Dictionary<string, object> d, string key, string def)
        {
            if (d == null) return def;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return def;
            return Convert.ToString(v);
        }

        public static int Int(Dictionary<string, object> d, string key, int def)
        {
            if (d == null) return def;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return def;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return def; }
        }

        public static bool Bool(Dictionary<string, object> d, string key, bool def)
        {
            if (d == null) return def;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return def;
            try { return Convert.ToBoolean(v, CultureInfo.InvariantCulture); } catch { return def; }
        }

        /// <summary>按 "a.b.c" 路径取值，取不到返回默认值</summary>
        public static object Path(Dictionary<string, object> d, string path, object def)
        {
            string[] parts = path.Split('.');
            Dictionary<string, object> cur = d;
            for (int i = 0; i < parts.Length; i++)
            {
                if (cur == null) return def;
                object v;
                if (!cur.TryGetValue(parts[i], out v) || v == null) return def;
                if (i == parts.Length - 1) return v;
                cur = v as Dictionary<string, object>;
            }
            return def;
        }

        public static int PathInt(Dictionary<string, object> d, string path, int def)
        {
            object v = Path(d, path, null);
            if (v == null) return def;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); } catch { return def; }
        }

        public static bool PathBool(Dictionary<string, object> d, string path, bool def)
        {
            object v = Path(d, path, null);
            if (v == null) return def;
            try { return Convert.ToBoolean(v, CultureInfo.InvariantCulture); } catch { return def; }
        }

        public static string PathStr(Dictionary<string, object> d, string path, string def)
        {
            object v = Path(d, path, null);
            return v == null ? def : Convert.ToString(v);
        }
    }

    // ───────────────────────────── 应用日志（gui.log）─────────────────────────────
    public static class AppLog
    {
        private static readonly object Gate = new object();
        private static string _file;

        public static void Init(string runDir)
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(runDir, "日志"));
                _file = Path.Combine(runDir, "日志", "gui.log");
            }
            catch { }
        }

        public static void Write(string msg)
        {
            string line = "[" + DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture) + "] " + msg + Environment.NewLine;
            lock (Gate)
            {
                try { if (_file != null) File.AppendAllText(_file, line, new UTF8Encoding(false)); } catch { }
                try { Console.Out.Write(line); Console.Out.Flush(); } catch { }
            }
        }
    }

    // ───────────────────────────── 配置 ─────────────────────────────
    public class Config
    {
        public Dictionary<string, object> Data;
        public string Path;
        public List<string> Errors = new List<string>();
        public List<string> Warnings = new List<string>();

        private const string DefaultsJson = @"
{
  ""version"": ""3.0.0"",
  ""backend"": { ""sevenZipPath"": ""C:\\Program Files\\7-Zip\\7z.exe"" },
  ""passwords"": [ ""123456"" ],
  ""delete"": {
    ""mode"": ""recycle"",
    ""filePatterns"": [ ""*.url"", ""*推广*"" ],
    ""dirPatterns"": [],
    ""protectArchives"": true
  },
  ""behavior"": { ""maxDepth"": 64 },
  ""safety"": {
    ""protectedExtensions"": [ "".apk"", "".epub"", "".xps"", "".jar"", "".wmz"", "".kmz"", "".kwd"", "".odt"", "".odp"", "".ott"", "".oxps"", "".xpi"", "".xpt"", "".sxc"", "".sxd"", "".sxi"", "".sxw"", "".exe"", "".dll"", "".msi"", "".sys"", "".scr"", "".cpl"", "".ocx"", "".lnk"", "".url"", "".docx"", "".xlsx"", "".pptx"", "".docm"", "".xlsm"", "".pptm"" ]
  }
}";

        public static Dictionary<string, object> Defaults()
        {
            return Json.ParseObject(DefaultsJson);
        }

        public static Dictionary<string, object> DeepMerge(Dictionary<string, object> baseD, Dictionary<string, object> over)
        {
            var result = new Dictionary<string, object>();
            foreach (var kv in baseD) result[kv.Key] = kv.Value;
            if (over == null) return result;
            foreach (var kv in over)
            {
                Dictionary<string, object> b = null;
                if (result.ContainsKey(kv.Key)) b = result[kv.Key] as Dictionary<string, object>;
                Dictionary<string, object> o = kv.Value as Dictionary<string, object>;
                if (b != null && o != null) result[kv.Key] = DeepMerge(b, o);
                else result[kv.Key] = kv.Value;
            }
            return result;
        }

        public static Config Load(string path)
        {
            var c = new Config();
            c.Path = path;
            Dictionary<string, object> user = new Dictionary<string, object>();
            if (File.Exists(path))
            {
                string raw = File.ReadAllText(path, Encoding.UTF8).TrimStart('\uFEFF');
                try { user = Json.ParseObject(raw); }
                catch (Exception e) { throw new Exception("配置文件不是合法 JSON：" + path + "\r\n" + e.Message); }
            }
            c.Data = DeepMerge(Defaults(), user);
            c.Validate();
            return c;
        }

        private void Validate()
        {
            string sz = ResolveSevenZip(this);
            if (sz == "" || !File.Exists(sz)) Errors.Add("找不到 7-Zip：配置里是「" + Json.PathStr(Data, "backend.sevenZipPath", "") + "」，程序目录和常见安装位置也都没有 7z.exe");
            string mode = Json.PathStr(Data, "delete.mode", "recycle");
            if (mode != "recycle" && mode != "staging" && mode != "keep")
                Errors.Add("delete.mode 只能是 recycle / staging / keep，当前是 " + mode);
            int depth = Json.PathInt(Data, "behavior.maxDepth", 64);
            if (depth < 1) Errors.Add("behavior.maxDepth 必须是不小于 1 的整数");
            if (depth > 512) Warnings.Add("behavior.maxDepth 大于 512，套娃极深时可能耗尽句柄");
            if (Json.StrList(Data, "passwords").Count == 0) Warnings.Add("passwords 为空：加密包将无法处理（会保留原包并记失败）");

        }

        public void Save()
        {
            File.WriteAllText(Path, Json.StringifyPretty(Data), new UTF8Encoding(false));
        }

        /// <summary>
        /// 解析实际使用的 7-Zip：
        /// 1) 配置里写的位置  2) 程序目录下的随包 7z.exe  3) 常见安装位置
        /// 这样绿色包里自带一份就能独立跑，用户装了 7-Zip 也能复用。
        /// </summary>
        public static string ResolveSevenZip(Config cfg)
        {
            string p = Json.PathStr(cfg.Data, "backend.sevenZipPath", "");
            if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
            string local = System.IO.Path.Combine(Program.AppDir ?? "", "7z.exe");
            if (Program.AppDir != null && File.Exists(local)) return local;
            string[] std = new string[]
            {
                @"C:\Program Files\7-Zip\7z.exe",
                @"C:\Program Files (x86)\7-Zip\7z.exe",
            };
            foreach (string s in std) if (File.Exists(s)) return s;
            return p;
        }

        /// <summary>重置配置：先备份，再写默认值</summary>
        public static string Reset(string path)
        {
            string backup = null;
            if (File.Exists(path))
            {
                backup = path + ".备份-" + DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss", CultureInfo.InvariantCulture) + ".json";
                File.Copy(path, backup, true);
            }
            File.WriteAllText(path, Json.StringifyPretty(Defaults()), new UTF8Encoding(false));
            return backup;
        }

        /// <summary>从 2.1 的 txt 配置迁移密码与删除规则</summary>
        public static void MigrateLegacy(string txtPath, out int pwCount, out int rmCount)
        {
            const string PW_START = "------------------------ 密码起始线 ------------------------";
            const string PW_END = "------------------------ 密码终止线 ------------------------";
            const string RM_START = "-------------------- 删除指定文件起始线 --------------------";
            const string RM_END = "-------------------- 删除指定文件终止线 --------------------";

            var passwords = new List<string>();
            var rms = new List<string>();
            string mode = null;
            foreach (string line in File.ReadAllLines(txtPath, Encoding.UTF8))
            {
                string t = line.TrimEnd('\r', '\n');
                if (t == PW_START) { mode = "pw"; continue; }
                if (t == PW_END) { mode = null; continue; }
                if (t == RM_START) { mode = "rm"; continue; }
                if (t == RM_END) { mode = null; continue; }
                if (mode == "pw" && t != "") passwords.Add(t);
                if (mode == "rm" && t != "") rms.Add(t);
            }
            pwCount = passwords.Count;
            rmCount = rms.Count;

            var cfg = Load(Program.ConfigPath);
            var merged = new List<string>();
            foreach (string p in passwords) if (!merged.Contains(p)) merged.Add(p);
            foreach (string p in Json.StrList(cfg.Data, "passwords")) if (!merged.Contains(p)) merged.Add(p);
            cfg.Data["passwords"] = merged.ToArray();

            var files = new List<string>();
            foreach (string r in rms) if (r.IndexOf('*') >= 0 || r.IndexOf('?') >= 0 || System.IO.Path.GetExtension(r) != "") files.Add(r);
            if (files.Count > 0)
            {
                var del = Json.Obj(cfg.Data, "delete");
                del["filePatterns"] = files.ToArray();
            }
            cfg.Save();
        }
    }

    // ───────────────────────────── 审计日志 ─────────────────────────────
    public class Audit
    {
        private readonly object Gate = new object();
        private StreamWriter _w;
        public string File;
        public bool EchoConsole = true;
        public string Level = "debug";
        private static readonly string[] Levels = new string[] { "debug", "info", "warn", "error" };

        public string Open(string dir, string tag)
        {
            Directory.CreateDirectory(dir);
            string name = "audit-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            if (!string.IsNullOrEmpty(tag)) name += "-" + tag;
            File = System.IO.Path.Combine(dir, name + ".jsonl");
            _w = new StreamWriter(new FileStream(File, FileMode.Append, FileAccess.Write, FileShare.Read), new UTF8Encoding(false));
            _w.AutoFlush = true;
            return File;
        }

        public void Write(string level, string ev, Dictionary<string, object> data)
        {
            if (Array.IndexOf(Levels, level) < Array.IndexOf(Levels, Level)) return;
            var rec = new Dictionary<string, object>();
            rec["time"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            rec["level"] = level;
            rec["event"] = ev;
            if (data != null) foreach (var kv in data) rec[kv.Key] = kv.Value;
            lock (Gate)
            {
                try { if (_w != null) _w.WriteLine(Json.Stringify(rec)); } catch { }
            }
            if (EchoConsole)
            {
                string brief = ev;
                if (data != null && data.ContainsKey("file") && data["file"] != null)
                    brief += " " + System.IO.Path.GetFileName(Convert.ToString(data["file"]));
                if (data != null && data.ContainsKey("detail") && data["detail"] != null)
                {
                    string d = Convert.ToString(data["detail"]);
                    brief += " — " + (d.Length > 200 ? d.Substring(0, 200) : d);
                }
                Console.WriteLine("· " + brief);
            }
        }

        public void Debug(string e, Dictionary<string, object> d) { Write("debug", e, d); }
        public void Info(string e, Dictionary<string, object> d) { Write("info", e, d); }
        public void Warn(string e, Dictionary<string, object> d) { Write("warn", e, d); }
        public void Error(string e, Dictionary<string, object> d) { Write("error", e, d); }

        public void Close()
        {
            lock (Gate) { try { if (_w != null) { _w.Flush(); _w.Close(); _w = null; } } catch { } }
        }

        // 便捷重载
        public void Info(string e) { Info(e, null); }
        public void Warn(string e) { Warn(e, null); }
        public void Error(string e) { Error(e, null); }
        public void Debug(string e) { Debug(e, null); }
    }

    // ───────────────────────────── 安全删除：回收站 / 暂存目录 ─────────────────────────────
    public class SafeDelete
    {
        public string Mode;
        public string StagingDirName = "_layerpeel_trash_";

        // 回收站默认配额比例（保守取 5%）：超过配额时 Windows 会静默永久删除，必须自己拦住
        private const double QuotaRatio = 0.05;
        private const long MinQuotaBytes = 1024L * 1024 * 1024;

        public SafeDelete(string mode, string stagingName)
        {
            Mode = mode;
            if (!string.IsNullOrEmpty(stagingName)) StagingDirName = stagingName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOERRORUI = 0x0400;

        public static bool RecycleAvailable(string target)
        {
            try
            {
                string root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(target));
                return Directory.Exists(System.IO.Path.Combine(root, "$Recycle.Bin"));
            }
            catch { return false; }
        }

        public static long RecycleQuotaBytes(string target)
        {
            try
            {
                string root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(target));
                var di = new DriveInfo(root);
                long total = di.TotalSize;
                return Math.Max((long)(total * QuotaRatio), MinQuotaBytes);
            }
            catch { return MinQuotaBytes; }
        }

        public class Decision
        {
            public string Action;   // recycle | staging | keep
            public string Why;
            public long Size;
        }

        public Decision Decide(string target)
        {
            var d = new Decision();

            if (Mode == "keep") { d.Action = "keep"; d.Why = "配置为保留原包"; return d; }
            try { d.Size = new FileInfo(target).Length; } catch { d.Size = 0; }

            if (!RecycleAvailable(target)) { d.Action = "staging"; d.Why = "目标盘没有回收站（网络盘/可移动盘）"; return d; }
            long quota = RecycleQuotaBytes(target);
            if (d.Size > quota)
            {
                d.Action = "staging";
                d.Why = "文件 " + Mb(d.Size) + "MB 超过回收站配额 " + Mb(quota) + "MB（回收站会静默永久删除，已改用暂存目录）";
                return d;
            }
            d.Action = "recycle";
            d.Why = "进回收站，可手动还原";
            return d;
        }

        private static string Mb(long bytes) { return (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture); }

        public class Result
        {
            public string Action;
            public string Why;
            public bool Ok;
            public bool Recycled;
            public bool Staged;
            public string StagedTo;
            public string Error;
            public bool Kept;
        }

        public Result Remove(string target, string rootDir)
        {
            Decision d = Decide(target);
            var r = new Result();
            r.Action = d.Action; r.Why = d.Why;

            if (d.Action == "keep") { r.Ok = true; r.Kept = true; return r; }


            if (d.Action == "recycle")
            {
                string err = Recycle(target);
                if (err == null) { r.Ok = true; r.Recycled = true; return r; }
                AppLog.Write("回收站删除失败，改走暂存目录：" + err);
                Stage(target, rootDir, "回收站删除失败", r);
                return r;
            }

            Stage(target, rootDir, d.Why, r);
            return r;
        }

        /// <summary>SHFileOperation + FOF_ALLOWUNDO = 进回收站。返回 null 表示成功，否则是错误说明。</summary>
        private static string Recycle(string target)
        {
            if (!File.Exists(target) && !Directory.Exists(target)) return null; // 已经不在，视作成功（幂等）
            var op = new SHFILEOPSTRUCT();
            op.wFunc = FO_DELETE;
            op.pFrom = System.IO.Path.GetFullPath(target) + "\0\0";
            op.fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI);
            try
            {
                int rc = SHFileOperation(ref op);
                if (rc != 0) return "SHFileOperation 返回 " + rc;
                if (op.fAnyOperationsAborted) return "操作被中止";
                return null;
            }
            catch (Exception e) { return e.Message; }
        }

        private void Stage(string target, string rootDir, string reason, Result r)
        {
            try
            {
                string staging = System.IO.Path.Combine(rootDir, StagingDirName);
                Directory.CreateDirectory(staging);
                string baseName = System.IO.Path.GetFileName(target);
                string dest = System.IO.Path.Combine(staging, baseName);
                int n = 1;
                while (File.Exists(dest) || Directory.Exists(dest))
                {
                    string ext = System.IO.Path.GetExtension(baseName);
                    dest = System.IO.Path.Combine(staging, System.IO.Path.GetFileNameWithoutExtension(baseName) + " (" + (++n) + ")" + ext);
                }
                if (Directory.Exists(target)) Directory.Move(target, dest);
                else File.Move(target, dest);

                string manifest = System.IO.Path.Combine(staging, "manifest.jsonl");
                var rec = new Dictionary<string, object>();
                rec["time"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                rec["from"] = System.IO.Path.GetFullPath(target);
                rec["to"] = dest;
                rec["reason"] = reason;
                File.AppendAllText(manifest, Json.Stringify(rec) + Environment.NewLine, new UTF8Encoding(false));

                r.Ok = true; r.Staged = true; r.StagedTo = dest;
            }
            catch (Exception e) { r.Ok = false; r.Error = e.Message; }
        }

        /// <summary>改名前的旧暂存目录名（f-keydle 时代）。撤销时要一并找，否则老文件撤不回来。</summary>
        public const string LegacyStagingName = "_fkeydle_trash_";

        /// <summary>从暂存目录还原（新旧两个暂存目录名都找）</summary>
        public static int[] Undo(string rootDir, string stagingName)
        {
            int[] a = UndoOne(rootDir, stagingName);
            int[] b = string.Equals(stagingName, LegacyStagingName, StringComparison.OrdinalIgnoreCase)
                    ? new int[] { 0, 0 } : UndoOne(rootDir, LegacyStagingName);
            return new int[] { a[0] + b[0], a[1] + b[1] };
        }

        private static int[] UndoOne(string rootDir, string stagingName)
        {
            int restored = 0, failed = 0;
            string staging = System.IO.Path.Combine(rootDir, stagingName);
            string manifest = System.IO.Path.Combine(staging, "manifest.jsonl");
            if (!File.Exists(manifest)) return new int[] { 0, 0 };
            foreach (string line in File.ReadAllLines(manifest, Encoding.UTF8))
            {
                if (line.Trim() == "") continue;
                Dictionary<string, object> rec;
                try { rec = Json.ParseObject(line); } catch { continue; }
                string from = Json.Str(rec, "from", null);
                string to = Json.Str(rec, "to", null);
                if (from == null || to == null) continue;
                try
                {
                    if (!File.Exists(to) && !Directory.Exists(to)) continue;
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(from));
                    if (File.Exists(from) || Directory.Exists(from)) { failed++; continue; }
                    if (Directory.Exists(to)) Directory.Move(to, from);
                    else File.Move(to, from);
                    restored++;
                }
                catch { failed++; }
            }
            return new int[] { restored, failed };
        }
    }
}

