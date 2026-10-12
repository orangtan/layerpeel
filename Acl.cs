// layerpeel 3.0 —— 解压 · 去套娃助手
// Copyright (C) 2026 layerpeel contributors
//
// 本程序是 ssh-buanshishi / f-keydle（GPL-3.0）的 C# 重写版，自 2026-10-12 起：
// 用 C# 重新实现全部功能，解压后端换成 7-Zip，去掉了 Bandizip / AHK 依赖。
// 按 GNU General Public License v3.0（或更新版本）发布，全文见 LICENSE。
// 本程序不提供任何担保。
// layerpeel 3.0 (C# 版) — 权限备份 / 修复 / 还原
// 备份走原生 .NET（逐项存 SDDL + 属主），不依赖 icacls /save /restore ——
// 后者遇到调用者解析不了的 SID 会整批失败（实测 exit 1300）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace LayerPeel
{
    public class AclFixer
    {
        private readonly string _backupRoot;
        private readonly Audit _audit;
        public Action<Dictionary<string, object>> OnEvent;

        public AclFixer(string backupRoot, Audit audit)
        {
            _backupRoot = backupRoot; _audit = audit;
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

        /// <summary>危险目标：盘根、系统目录、整个用户目录 —— 一律拒绝</summary>
        public static string RefuseReason(string target)
        {
            string abs = Path.GetFullPath(target).TrimEnd('\\');
            string root = Path.GetPathRoot(abs).TrimEnd('\\');
            if (abs.Equals(root, StringComparison.OrdinalIgnoreCase)) return "不能对整个盘（" + root + "）操作";
            string win = (Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows").TrimEnd('\\');
            string[] guards = new string[]
            {
                win, Path.Combine(win, "System32"), "C:\\Program Files", "C:\\Program Files (x86)", "C:\\ProgramData"
            };
            foreach (string g in guards)
            {
                if (abs.Equals(g, StringComparison.OrdinalIgnoreCase)) return "不能对系统目录操作：" + abs;
                if (abs.StartsWith(g + "\\", StringComparison.OrdinalIgnoreCase)) return "不能对系统目录操作：" + abs;
            }
            string home = (Environment.GetEnvironmentVariable("USERPROFILE") ?? "").TrimEnd('\\');
            if (home != "" && abs.Equals(home, StringComparison.OrdinalIgnoreCase)) return "不能对整个用户目录操作";
            return null;
        }

        public class BackupResult
        {
            public bool Ok;
            public string File;
            public string Owner;
            public int Count;
            public string Raw;
        }

        public BackupResult Backup(string target)
        {
            Directory.CreateDirectory(_backupRoot);
            string stamp = DateTime.Now.ToString("yyyy-MM-ddTHH-mm-ss-fff", System.Globalization.CultureInfo.InvariantCulture);
            string file = Path.Combine(_backupRoot, "acl-" + stamp + ".json");
            var r = new BackupResult();

            var items = new List<object>();
            string rootOwner = "";
            try
            {
                var rootInfo = new DirectoryInfo(target);
                rootOwner = SafeOwner(rootInfo);
                var all = new List<FileSystemInfo>();
                all.Add(rootInfo);
                all.AddRange(DeepList(target));
                foreach (FileSystemInfo fi in all)
                {
                    try
                    {
                        var rec = new Dictionary<string, object>();
                        string full = fi.FullName;
                        rec["rel"] = full.Length > target.Length ? full.Substring(target.Length).TrimStart('\\') : "";
                        rec["type"] = (fi is DirectoryInfo) ? "D" : "F";
                        rec["sddl"] = SafeSddl(fi);
                        rec["owner"] = SafeOwner(fi);
                        items.Add(rec);
                    }
                    catch { }
                }
            }
            catch (Exception e) { r.Raw = e.Message; r.Ok = false; return r; }

            var payload = new Dictionary<string, object>();
            payload["version"] = 1;
            payload["time"] = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            payload["target"] = target;
            payload["rootOwner"] = rootOwner;
            payload["count"] = items.Count;
            payload["items"] = items.ToArray();
            File.WriteAllText(file, Json.Stringify(payload), new UTF8Encoding(false));

            r.Count = items.Count;
            r.Owner = rootOwner;
            r.Ok = items.Count > 0;
            r.File = r.Ok ? file : null;
            if (r.Ok)
            {
                var meta = new Dictionary<string, object>();
                meta["target"] = target; meta["backupFile"] = file; meta["owner"] = rootOwner;
                meta["count"] = items.Count; meta["time"] = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
                File.WriteAllText(file + ".meta.json", Json.StringifyPretty(meta), new UTF8Encoding(false));
                _audit.Info("acl-backup-ok", M("dir", target, "detail", file + "（" + items.Count + " 项）"));
            }
            else _audit.Error("acl-backup-failed", M("dir", target, "detail", r.Raw ?? ""));
            return r;
        }

        private static IEnumerable<FileSystemInfo> DeepList(string root)
        {
            var result = new List<FileSystemInfo>();
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string cur = stack.Pop();
                string[] dirs;
                string[] files;
                try { dirs = Directory.GetDirectories(cur); files = Directory.GetFiles(cur); }
                catch { continue; }
                foreach (string f in files) { try { result.Add(new FileInfo(f)); } catch { } }
                foreach (string d in dirs) { try { result.Add(new DirectoryInfo(d)); } catch { } stack.Push(d); }
            }
            return result;
        }

        private static string SafeSddl(FileSystemInfo fi)
        {
            try
            {
                if (fi is DirectoryInfo)
                    return ((DirectoryInfo)fi).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
                return ((FileInfo)fi).GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            }
            catch { return null; }
        }

        private static string SafeOwner(FileSystemInfo fi)
        {
            try
            {
                FileSystemSecurity sec = (fi is DirectoryInfo)
                    ? (FileSystemSecurity)((DirectoryInfo)fi).GetAccessControl(AccessControlSections.Owner)
                    : (FileSystemSecurity)((FileInfo)fi).GetAccessControl(AccessControlSections.Owner);
                IdentityReference id = sec.GetOwner(typeof(NTAccount));
                return id == null ? null : id.Value;
            }
            catch { return null; }
        }

        public class FixResult
        {
            public bool Ok;
            public string Detail;
            public string BackupFile;
            public string Owner;
            public List<string> Steps = new List<string>();
        }

        /// <summary>level: 1=只授权 2=重置继承+授权 3=接管属主+重置+授权（等价 2.1 的破坏性做法）</summary>
        public FixResult Fix(string target, int level)
        {
            var r = new FixResult();
            string bad = RefuseReason(target);
            if (bad != null)
            {
                r.Detail = bad;
                Emit("acl-failed", M("dir", target, "detail", bad));
                _audit.Error("acl-refused", M("dir", target, "detail", bad));
                return r;
            }
            Emit("acl-start", M("dir", target, "level", level));

            BackupResult b = Backup(target);
            if (!b.Ok)
            {
                r.Detail = "备份 ACL 失败，已中止（不做不可回滚的改动）";
                Emit("acl-failed", M("dir", target, "detail", r.Detail));
                return r;
            }
            r.BackupFile = b.File; r.Owner = b.Owner;
            Emit("acl-backup", M("dir", target, "backupFile", b.File, "owner", b.Owner));

            bool allOk = true;
            if (level >= 3)
            {
                Emit("acl-step", M("dir", target, "step", "接管属主（takeown /R）"));
                var rr = SevenZip.Run("takeown.exe", new List<string>(new string[] { "/F", target, "/R", "/D", "Y" }), 1800000, null);
                if (rr.Code != 0) allOk = false;
                r.Steps.Add("takeown:" + rr.Code);
            }
            if (level >= 2)
            {
                Emit("acl-step", M("dir", target, "step", "重置为继承的 ACL（icacls /reset /T）"));
                var rr = SevenZip.Run("icacls.exe", new List<string>(new string[] { target, "/reset", "/T", "/C", "/Q" }), 1800000, null);
                if (rr.Code != 0) allOk = false;
                r.Steps.Add("reset:" + rr.Code);
            }
            Emit("acl-step", M("dir", target, "step", "授予所有人完全控制（用 SID，避免语言差异）"));
            var g = SevenZip.Run("icacls.exe", new List<string>(new string[] { target, "/grant", "*S-1-1-0:(OI)(CI)F", "/T", "/C", "/Q" }), 1800000, null);
            if (g.Code != 0) allOk = false;
            r.Steps.Add("grant:" + g.Code);

            r.Ok = allOk;
            Emit(r.Ok ? "acl-done" : "acl-failed", M("dir", target, "level", level, "backupFile", b.File));
            _audit.Write(r.Ok ? "info" : "warn", r.Ok ? "acl-fixed" : "acl-fix-partial",
                M("dir", target, "detail", "level=" + level, "backupFile", b.File));
            return r;
        }

        public class RestoreResult
        {
            public bool Ok;
            public string Detail;
            public string Target;
            public string Owner;
            public bool OwnerRestored;
            public int Ok_Count, Failed;
        }

        public RestoreResult Restore(string backupFile)
        {
            var r = new RestoreResult();
            string metaFile = backupFile + ".meta.json";
            string target = null;
            if (File.Exists(metaFile))
            {
                try { target = Json.PathStr(Json.ParseObject(File.ReadAllText(metaFile, Encoding.UTF8)), "target", null); } catch { }
            }
            if (target == null)
            {
                r.Detail = "找不到备份对应的元数据（" + metaFile + "）";
                return r;
            }
            if (!File.Exists(backupFile)) { r.Detail = "备份文件不存在：" + backupFile; return r; }

            Emit("acl-restore-start", M("dir", target, "backupFile", backupFile));
            Dictionary<string, object> data;
            try { data = Json.ParseObject(File.ReadAllText(backupFile, Encoding.UTF8)); }
            catch (Exception e) { r.Detail = "备份文件读不了：" + e.Message; return r; }

            foreach (object o in Json.Arr(data, "items"))
            {
                var it = o as Dictionary<string, object>;
                if (it == null) continue;
                string rel = Json.Str(it, "rel", "");
                string type = Json.Str(it, "type", "F");
                string sddl = Json.Str(it, "sddl", null);
                string full = rel == "" ? target : Path.Combine(target, rel);
                if (!File.Exists(full) && !Directory.Exists(full)) continue;
                if (sddl == null) { r.Failed++; continue; }
                try
                {
                    if (type == "D")
                    {
                        var sec = new DirectorySecurity();
                        sec.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Access);
                        Directory.SetAccessControl(full, sec);
                    }
                    else
                    {
                        var sec = new FileSecurity();
                        sec.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Access);
                        File.SetAccessControl(full, sec);
                    }
                    r.Ok_Count++;
                }
                catch { r.Failed++; }
            }

            // 属主单独回写（可能需要 SeRestorePrivilege，失败容忍）
            string owner = Json.Str(data, "rootOwner", null);
            r.OwnerRestored = false;
            if (!string.IsNullOrEmpty(owner))
            {
                try
                {
                    var di = new DirectoryInfo(target);
                    DirectorySecurity sec = di.GetAccessControl(AccessControlSections.Access);
                    sec.SetOwner(new NTAccount(owner));
                    di.SetAccessControl(sec);
                    r.OwnerRestored = true;
                }
                catch { }
            }

            r.Ok = r.Failed == 0;
            r.Target = target; r.Owner = owner;
            Emit(r.Ok ? "acl-restore-done" : "acl-restore-failed", M("dir", target, "ownerOk", r.OwnerRestored));
            _audit.Write(r.Ok ? "info" : "error", r.Ok ? "acl-restored" : "acl-restore-failed",
                M("dir", target, "detail", "还原 " + r.Ok_Count + " 项，失败 " + r.Failed));
            return r;
        }

        public class BackupInfo
        {
            public string File, Name, Target, Owner, Time;
            public int Count;
        }

        public List<BackupInfo> ListBackups()
        {
            var list = new List<BackupInfo>();
            if (!Directory.Exists(_backupRoot)) return list;
            foreach (string f in Directory.GetFiles(_backupRoot, "*.json"))
            {
                if (f.EndsWith(".meta.json", StringComparison.OrdinalIgnoreCase)) continue;
                string meta = f + ".meta.json";
                if (!File.Exists(meta)) continue;
                try
                {
                    Dictionary<string, object> m = Json.ParseObject(File.ReadAllText(meta, Encoding.UTF8));
                    var bi = new BackupInfo();
                    bi.File = f; bi.Name = Path.GetFileName(f);
                    bi.Target = Json.Str(m, "target", null);
                    bi.Owner = Json.Str(m, "owner", null);
                    bi.Time = Json.Str(m, "time", null);
                    bi.Count = Json.Int(m, "count", 0);
                    list.Add(bi);
                }
                catch { }
            }
            list.Sort(delegate (BackupInfo a, BackupInfo b) { return string.CompareOrdinal(b.Time, a.Time); });
            return list;
        }
    }
}
