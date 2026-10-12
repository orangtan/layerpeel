// layerpeel 3.0 —— 解压 · 去套娃助手
// Copyright (C) 2026 layerpeel contributors
//
// 本程序是 ssh-buanshishi / f-keydle（GPL-3.0）的 C# 重写版，自 2026-10-12 起：
// 用 C# 重新实现全部功能，解压后端换成 7-Zip，去掉了 Bandizip / AHK 依赖。
// 按 GNU General Public License v3.0（或更新版本）发布，全文见 LICENSE。
// 本程序不提供任何担保。
//
// 资源管理器右键菜单（注册到 HKCU，只影响当前用户，不需要管理员）。
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Win32;

namespace LayerPeel
{
    public static class MenuHelper
    {
        public const string Label = "用 layerpeel 打开";

        /// <summary>当前版本的注册表键后缀</summary>
        private const string MenuKey = "layerpeel";
        /// <summary>改名前的旧键（f-keydle 时代）。安装/卸载时要一并清掉，
        /// 否则用户机器上会残留一条点了没反应的旧菜单项。</summary>
        private const string LegacyMenuKey = "fkeydle3";

        private static readonly string[] ArchiveExts = new string[]
        { ".zip", ".zipx", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".iso", ".wim", ".cab", ".zst" };

        private class Target
        {
            public string Key;
            public string Arg;
            public bool Legacy;
        }

        private static void AddBoth(List<Target> list, string prefix, string arg)
        {
            list.Add(new Target { Key = prefix + MenuKey, Arg = arg, Legacy = false });
            list.Add(new Target { Key = prefix + LegacyMenuKey, Arg = arg, Legacy = true });
        }

        private static List<Target> Targets()
        {
            var list = new List<Target>();
            AddBoth(list, @"Software\Classes\Directory\shell\", "%1");
            AddBoth(list, @"Software\Classes\Directory\Background\shell\", "%V");
            foreach (string ext in ArchiveExts)
                AddBoth(list, @"Software\Classes\SystemFileAssociations\" + ext + @"\shell\", "%1");
            return list;
        }

        private static string ExePath()
        {
            return Assembly.GetExecutingAssembly().Location;
        }

        /// <summary>install / uninstall / status，返回每处菜单项的结果</summary>
        public static List<object> Run(string action)
        {
            var outList = new List<object>();
            foreach (Target t in Targets())
            {
                // 状态查询只关心当前版本的键
                if (action == "status" && t.Legacy) continue;

                var d = new Dictionary<string, object>();
                d["key"] = t.Key;
                try
                {
                    if (action == "install")
                    {
                        if (t.Legacy)
                        {
                            // 旧键先清掉，避免菜单里同时出现两条
                            Registry.CurrentUser.DeleteSubKeyTree(t.Key, false);
                            d["ok"] = true;
                            d["note"] = "清理旧版菜单项";
                            outList.Add(d);
                            continue;
                        }
                        using (RegistryKey k = Registry.CurrentUser.CreateSubKey(t.Key))
                        {
                            k.SetValue(null, Label);
                            k.SetValue("Icon", ExePath() + ",0");
                            k.SetValue("MultiSelectModel", "Player");
                            using (RegistryKey c = k.CreateSubKey("command"))
                                c.SetValue(null, "\"" + ExePath() + "\" --launcher-opens \"" + t.Arg + "\"");
                        }
                        d["ok"] = true;
                    }
                    else if (action == "uninstall")
                    {
                        // 新旧键都清，保证卸干净
                        Registry.CurrentUser.DeleteSubKeyTree(t.Key, false);
                        d["ok"] = true;
                    }
                    else
                    {
                        using (RegistryKey k = Registry.CurrentUser.OpenSubKey(t.Key))
                            d["installed"] = k != null;
                    }
                }
                catch (Exception e) { d["ok"] = false; d["err"] = e.Message; }
                outList.Add(d);
            }
            return outList;
        }
    }
}
