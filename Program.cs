// layerpeel 3.0 —— 解压 · 去套娃助手
// Copyright (C) 2026 layerpeel contributors
//
// 本程序是 ssh-buanshishi / f-keydle（GPL-3.0）的 C# 重写版，自 2026-10-12 起：
// 用 C# 重新实现全部功能，解压后端换成 7-Zip，去掉了 Bandizip / AHK 依赖。
// 按 GNU General Public License v3.0（或更新版本）发布，全文见 LICENSE。
// 本程序不提供任何担保。
// layerpeel 3.0 (C# 版) — 入口：命令行 + 界面启动
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace LayerPeel
{
    class Program
    {
        public static string AppDir;
        public static string ConfigPath;
        public static string RunDir;
        public static string LogDir;
        public static string AclBackupDir;

        static void SetupPaths()
        {
            AppDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            ConfigPath = Path.Combine(AppDir, "config.json");
            RunDir = Path.Combine(AppDir, "运行数据");
            LogDir = Path.Combine(RunDir, "日志");
            AclBackupDir = Path.Combine(RunDir, "权限备份");
            AppLog.Init(RunDir);
        }

        // ───────────── 参数解析 ─────────────
        class Args
        {
            public List<string> Targets = new List<string>();
            public Dictionary<string, string> Flags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            public bool Has(string k) { return Flags.ContainsKey(k); }
            public string Get(string k) { string v; return Flags.TryGetValue(k, out v) ? v : null; }
            public int GetInt(string k, int def)
            {
                string v = Get(k); int n;
                if (v != null && int.TryParse(v, out n)) return n;
                return def;
            }
        }

        /// <summary>所有认得的开关（拼错时给个提醒，避免静默失效）</summary>
        static readonly string[] KnownFlags = new string[]
        { "clean", "cleanup", "config", "depth", "fix-acl", "flatten", "gui", "help",
          "launcher-opens", "list-backups", "migrate", "mode", "reset-config",
          "restore-acl", "acl-level", "ui-selftest", "undo" };

        static readonly string[] ValueFlags = new string[]
        { "mode", "depth", "config", "migrate", "undo", "acl-level", "restore-acl", "ui-selftest" };

        static Args ParseArgs(string[] argv)
        {
            var a = new Args();
            for (int i = 0; i < argv.Length; i++)
            {
                string s = argv[i];
                if (s.StartsWith("--"))
                {
                    string body = s.Substring(2);
                    int eq = body.IndexOf('=');          // 支持 --key=value 这种写法
                    if (eq > 0)
                    {
                        a.Flags[body.Substring(0, eq)] = body.Substring(eq + 1);
                        continue;
                    }
                    string key = body;
                    if (Array.IndexOf(ValueFlags, key) >= 0 && i + 1 < argv.Length) a.Flags[key] = argv[++i];
                    else a.Flags[key] = "1";
                }
                else if (s.Length > 1 && (s[0] == '-' || s[0] == '/') && !File.Exists(s) && !Directory.Exists(s))
                {
                    // 支持 -h / /? / /help 这类写法。
                    // 先排除真实存在的路径：相对路径可能以 / 开头，别把路径当开关。
                    string k = s.Substring(1).TrimEnd('?');
                    // -h / /? / /help 统一收敛到 help，别让 -h 落到“没带参数就去开窗口”那条路
                    if (k.Length == 0 || k == "h" || k == "?") k = "help";
                    a.Flags[k] = "1";
                }
                else a.Targets.Add(s);
            }
            return a;
        }

        static string UsageText()
        {
            return string.Join("\r\n", new string[]
            {
                "layerpeel 3.0 —— 解压 · 去套娃助手",
                "",
                "用法：",
                "  layerpeel.exe [选项] <目标...>",
                "",
                "不带任何参数（或带 --gui）会直接打开图形界面。",
                "",
                "任务（默认是「解压 · 去套娃」）：",
                "  --clean              删除指定文件（按 config.json 里的规则）",
                "  --flatten            仅去套娃（压平目录层级，不碰压缩包）",
                "  --fix-acl            修复目录权限（动手前自动备份 ACL）",
                "  --cleanup            清理残留（_layerpeel_tmp_* 临时目录）",
                "",
                "常用选项：",
                "  --mode <值>          覆盖删除策略：recycle | staging | keep",
                "  --depth <n>          套娃深度上限（默认 64）",
                "  --acl-level <n>      修复力度：1 只授权 | 2 重置+授权 | 3 接管属主+重置+授权",
                "  --config <路径>      用指定的配置文件（默认程序目录下的 config.json）",
                "",
                "其它：",
                "  --list-backups       列出权限备份",
                "  --restore-acl <文件> 从备份还原权限",
                "  --undo <目录>        从暂存目录还原",
                "  --migrate <txt>      从 f-keydle 2.x 的 txt 配置迁移密码与规则",
                "  --reset-config       配置恢复默认值（会先备份）",
                "  --gui                强制打开界面",
                "  --ui-selftest <png>  把界面渲染成 PNG 后退出（验收界面用）",
                "  --launcher-opens     启动器协议（右键菜单/启动器传入，无需手敲）",
                "  --help               显示这段帮助",
                "",
                "示例：",
                "  layerpeel.exe D:\\下载\\某某合集",
                "  layerpeel.exe --clean --mode keep D:\\下载\\某某合集",
                "  layerpeel.exe --flatten --depth 8 D:\\下载",
            });
        }

        /// <summary>显示用法。有父控制台就打控制台；没有（双击运行）就用消息框，别让用户以为程序没反应。</summary>
        static void ShowUsage()
        {
            string usage = UsageText();
            // 输出被重定向（管道 / 文件）时直接用 stdout —— 否则管道里什么都收不到
            if (Console.IsOutputRedirected)
            {
                Console.Out.WriteLine(usage);
                Console.Out.Flush();
                return;
            }
            if (AttachConsole(ATTACH_PARENT_PROCESS))
            {
                Console.Out.WriteLine(usage);
                Console.Out.Flush();
                return;
            }
            // 双击运行：没有父控制台也没有重定向，用消息框，别让用户以为程序没反应
            System.Windows.Forms.MessageBox.Show(usage, "layerpeel 3.0 — 命令行用法",
                System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
        }

        static string FmtBytes(long n)
        {
            string[] u = new string[] { "B", "KB", "MB", "GB", "TB" };
            int i = 0; double v = n;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(i == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + u[i];
        }

        static string FmtDuration(long ms)
        {
            long s = ms / 1000, m = s / 60, h = m / 60;
            if (h > 0) return h + "小时" + (m % 60) + "分" + (s % 60) + "秒";
            if (m > 0) return m + "分" + (s % 60) + "秒";
            return s + "秒";
        }

        static void EventPrinter(Dictionary<string, object> ev)
        {
            string t = Convert.ToString(ev["type"]);
            string f = ev.ContainsKey("file") ? Path.GetFileName(Convert.ToString(ev["file"])) : null;
            if (t == "archive-extracting") Console.WriteLine("→ 解压 " + f);
            else if (t == "flatten-step") Console.WriteLine("  · 压平 " + ev["dir"]);
            else if (t == "clean-protected") Console.WriteLine("  · 跳过（是压缩包）" + Path.GetFileName(Convert.ToString(ev["file"])));
            else if (t == "leftover-removed") Console.WriteLine("  · 已清掉残留 " + ev["dir"]);
            else if (t == "acl-backup") Console.WriteLine("  · 已备份 ACL → " + ev["backupFile"]);
            else if (t == "acl-step") Console.WriteLine("  · " + ev["step"]);
            else if (t == "acl-failed") Console.WriteLine("  × " + ev["dir"] + "：" + ev["detail"]);
        }

        // 程序编译成 winexe（双击不弹控制台）。命令行模式下要接回父控制台，输出才看得见。
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool AttachConsole(int dwProcessId);
        const int ATTACH_PARENT_PROCESS = -1;

        [STAThread]
        static int Main(string[] argv)
        {
            SetupPaths();
            Args args = ParseArgs(argv);

            // --help：打印用法后退出。
            // 必须放在下面 gui 判断之前，否则没带参数会被当成要开界面，敲 --help 反而弹个窗口。
            if (args.Has("help")) { ShowUsage(); return 0; }

            // 离线自检：把界面渲染成 PNG 后退出（用来验收界面长什么样）
            string st = args.Get("ui-selftest");
            if (st != null) return WpfUi.SelfTest(st);

            bool gui = args.Targets.Count == 0 || args.Has("gui") || args.Has("launcher-opens");
            if (!gui) AttachConsole(ATTACH_PARENT_PROCESS);

            // 拼错的开关给个提示，别让它静默失效
            foreach (string k in args.Flags.Keys)
                if (k != "help" && Array.IndexOf(KnownFlags, k) < 0)
                    Console.Error.WriteLine("未知参数：--" + k + "（用 --help 看用法）");

            // 迁移 2.1 配置
            string migrate = args.Get("migrate");
            if (migrate != null)
            {
                int pw, rm;
                Config.MigrateLegacy(migrate, out pw, out rm);
                Console.WriteLine("已从 " + migrate + " 迁移配置 → " + ConfigPath);
                Console.WriteLine("密码 " + pw + " 条，删除项 " + rm + " 条");
                return 0;
            }

            // 从暂存目录还原
            string undo = args.Get("undo");
            if (undo != null)
            {
                Config cfg = Config.Load(ConfigPath);
                int[] r = SafeDelete.Undo(undo, Json.PathStr(cfg.Data, "delete.stagingDirName", "_layerpeel_trash_"));
                Console.WriteLine("还原 " + r[0] + " 项，失败 " + r[1] + " 项");
                return r[1] > 0 ? 1 : 0;
            }

            // 权限备份列表
            if (args.Has("list-backups"))
            {
                var fixer = new AclFixer(AclBackupDir, new Audit());
                var list = fixer.ListBackups();
                if (list.Count == 0) { Console.WriteLine("还没有权限备份。"); return 0; }
                Console.WriteLine("备份目录：" + AclBackupDir);
                foreach (AclFixer.BackupInfo b in list)
                    Console.WriteLine("  " + (b.Time ?? "").Substring(0, Math.Min(19, (b.Time ?? "").Length)) + "  " + b.Target + "  (属主 " + (b.Owner ?? "-") + ")  ← " + b.Name);
                return 0;
            }

            // 从备份还原权限
            string restoreAcl = args.Get("restore-acl");
            if (restoreAcl != null)
            {
                var fixer = new AclFixer(AclBackupDir, new Audit());
                AclFixer.RestoreResult r = fixer.Restore(restoreAcl);
                Console.WriteLine(r.Ok ? ("已还原 " + r.Target + "（属主" + (r.OwnerRestored ? "已回写" : "未回写") + "）") : ("还原失败：" + r.Detail));
                return r.Ok ? 0 : 1;
            }

            // 重置配置
            if (args.Has("reset-config"))
            {
                string backup = Config.Reset(ConfigPath);
                Console.WriteLine("配置已重置：" + ConfigPath);
                if (backup != null) Console.WriteLine("旧配置已备份：" + backup);
                return 0;
            }

            if (gui)
                return RunUi(args);

            Config config = Config.Load(args.Get("config") ?? ConfigPath);
            string mode = args.Get("mode");
            if (mode != null) config.Data["delete"] = MergeMode(Json.Obj(config.Data, "delete"), mode);
            int depth = args.GetInt("depth", -1);
            if (depth > 0)
            {
                var beh = Json.Obj(config.Data, "behavior");
                beh["maxDepth"] = depth;
            }
            if (config.Errors.Count > 0)
            {
                Console.Error.WriteLine("配置有错，先修配置：");
                foreach (string e in config.Errors) Console.Error.WriteLine("  × " + e);
                return 2;
            }
            foreach (string w in config.Warnings) Console.Error.WriteLine("! " + w);

            string task = args.Has("fix-acl") ? "acl"
                        : args.Has("clean") ? "clean"
                        : args.Has("flatten") ? "flatten"
                        : args.Has("cleanup") ? "cleanup"
                        : "extract";

            var audit = new Audit();
            string tag = task != "extract" ? task : "";
            string logFile = audit.Open(LogDir, tag);
            Console.WriteLine("日志：" + logFile);
            Console.WriteLine();

            string[] taskNames = new string[] { "extract", "clean", "acl", "flatten", "cleanup" };
            var names = new Dictionary<string, string>();
            names["extract"] = "解压 · 去套娃"; names["clean"] = "删除指定文件";
            names["acl"] = "修复权限"; names["flatten"] = "仅去套娃"; names["cleanup"] = "清理残留";
            Console.WriteLine("任务：" + names[task]);
            Console.WriteLine("目标：" + string.Join(" , ", args.Targets.ToArray()));
            Console.WriteLine("模式：" + "执行"
                + "｜删除策略：" + Json.PathStr(config.Data, "delete.mode", "recycle")
                + "｜深度上限：" + Json.PathInt(config.Data, "behavior.maxDepth", 64));
            Console.WriteLine();

            Func<bool> aborted = delegate () { return false; };
            Stats stats;
            var engine = new Engine(config, audit);
            engine.OnEvent = EventPrinter;

            if (task == "extract")
            {
                engine.Run(args.Targets, aborted);
                stats = engine.Stats;
            }
            else if (task == "flatten")
            {
                engine.FlattenOnly(args.Targets, aborted);
                engine.RunFinish();
                stats = engine.Stats;
            }
            else if (task == "cleanup")
            {
                engine.CleanupLeftovers(args.Targets, aborted);
                engine.RunFinish();
                stats = engine.Stats;
            }
            else if (task == "clean")
            {
                var cleaner = new JunkCleaner(config, audit, engine);
                cleaner.OnEvent = EventPrinter;
                JunkCleaner.RunOutcome o = cleaner.Run(args.Targets, aborted);
                if (o.Refused)
                {
                    foreach (string e in o.Errors) Console.Error.WriteLine("× " + e);
                    audit.Close();
                    return 2;
                }
                stats = o.Stats;
            }
            else
            {
                var fixer = new AclFixer(AclBackupDir, audit);
                fixer.OnEvent = EventPrinter;
                int ok = 0, bad = 0;
                foreach (string t in args.Targets)
                {
                    AclFixer.FixResult r = fixer.Fix(t, args.GetInt("acl-level", 2));
                    if (r.Ok) ok++; else bad++;
                }
                stats = new Stats();
                stats.Fixed = ok; stats.Failed = bad;
                stats.ElapsedMs = (DateTime.UtcNow.Ticks - stats.T0) / TimeSpan.TicksPerMillisecond;
            }

            audit.Close();

            Console.WriteLine();
            Console.WriteLine("──────────── 汇总 ────────────");
            if (task == "clean")
            {
                Console.WriteLine("命中规则       " + stats.Matched);
                Console.WriteLine("受保护跳过     " + stats.Protected + "（是压缩包）");
                Console.WriteLine("已删除         " + stats.Deleted + " 个（回收站 " + stats.Recycled + "｜暂存 " + stats.Staged + "）");
                if (stats.Failed > 0) Console.WriteLine("失败           " + stats.Failed);
            }
            else if (task == "flatten")
            {
                Console.WriteLine("可压平层级     " + stats.Matched);
                Console.WriteLine("已压平         " + stats.Flattened + " 层（上提 " + stats.Moved + " 项）");
            }
            else if (task == "cleanup")
            {
                Console.WriteLine("发现的残留目录 " + stats.Leftovers);
            }
            else if (task == "acl")
            {
                Console.WriteLine("修复成功       " + stats.Fixed + " 个目录");
                if (stats.Failed > 0) Console.WriteLine("失败           " + stats.Failed);
                var fixer = new AclFixer(AclBackupDir, new Audit());
                var list = fixer.ListBackups();
                if (list.Count > 0) Console.WriteLine("最近备份       " + list[0].File);
            }
            else
            {
                Console.WriteLine("识别到压缩包   " + stats.Archives);
                Console.WriteLine("成功解压       " + stats.Extracted + "（解出 " + FmtBytes(stats.BytesOut) + "）");
                Console.WriteLine("原包处理       " + stats.Deleted + " 个（回收站 " + stats.Recycled + "｜暂存 " + stats.Staged + "）");
                if (stats.NoPassword > 0) Console.WriteLine("缺密码         " + stats.NoPassword + "（已保留原包）");
                if (stats.Skipped > 0) Console.WriteLine("安全跳过       " + stats.Skipped);
                if (stats.Failed > 0) Console.WriteLine("失败           " + stats.Failed + "（已保留原包）");
            }
            Console.WriteLine("耗时           " + FmtDuration(stats.ElapsedMs));
            Console.WriteLine("日志           " + logFile);
            return stats.Failed > 0 ? 3 : 0;
        }

        private static Dictionary<string, object> MergeMode(Dictionary<string, object> del, string mode)
        {
            var copy = new Dictionary<string, object>();
            foreach (var kv in del) copy[kv.Key] = kv.Value;
            copy["mode"] = mode;
            return copy;
        }

        // ───────────── 界面模式 ─────────────
        /// <summary>界面入口：WPF 原生窗口。不起 HTTP 服务、不用浏览器、不监听端口。</summary>
        static int RunUi(Args args)
        {
            var ctx = new JobContext();
            ctx.ConfigPath = ConfigPath;
            ctx.LogDir = LogDir;
            ctx.AclBackupDir = AclBackupDir;
            ctx.SilentConsole = true;
            // 单实例：WPF 模式允许多开（桌面应用的常规行为）
            return WpfUi.Run(ctx, args.Targets);
        }
    }
}
