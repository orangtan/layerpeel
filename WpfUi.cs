// layerpeel 3.0 —— 解压 · 去套娃助手
// Copyright (C) 2026 layerpeel contributors
//
// 本程序是 ssh-buanshishi / f-keydle（GPL-3.0）的 C# 重写版，自 2026-10-12 起：
// 用 C# 重新实现全部功能，解压后端换成 7-Zip，去掉了 Bandizip / AHK 依赖。
// 按 GNU General Public License v3.0（或更新版本）发布，全文见 LICENSE。
// 本程序不提供任何担保。
//
// WPF 界面：无边框自绘标题栏 + 左侧导航 + 卡片化内容 + 底部状态栏。
// 纯 C# 写，不用 XAML 编译 / MSBuild / SDK —— 用系统自带 csc.exe 直接编。
// 配色与控件样式见 Theme.cs（参考工作区里的 WPF界面模板）。
//
// 两个必须记住的 WPF 约束：
//   1. 一个控件只能有一个父节点 —— 「选项」卡里每个任务页各建一套分组，用 Visibility 切换，绝不共用控件。
//   2. 隐式 TextBlock 样式会盖掉字体设置 —— 图标按钮的内容要写成显式 TextBlock 并显式指定 FontFamily。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LayerPeel
{
    public class WpfUi
    {
        static Brush Bg(string k) { return Theme.B(k); }
        static Style S(string k) { return Theme.S(k); }

        static readonly string[] Modes = new string[] { "recycle", "staging", "keep" };

        /// <summary>导航项：一个任务页 + 它专属的选项分组</summary>
        class Page
        {
            public string Title, Sub, Glyph;
            public StackPanel Options;
            public bool NeedsPassword;
        }

        // ── 状态 ──
        readonly List<string> _targets = new List<string>();
        readonly List<Page> _pages = new List<Page>();
        JobContext _ctx;
        Job _job;
        int _page = 0;

        Window _win;
        Border _chromeRoot, _passwordCardBorder;
        ListBox _nav, _targetList;
        TextBox _log, _password, _pathInput, _filePatterns, _dirPatterns, _depth, _depth2;
        ComboBox _delMode, _aclLevel;
        CheckBox _protect;
        StackPanel _statsHost, _backupPanel, _optionHost, _passwordCardInner;
        TextBlock _statusText, _titleTask, _pageTitle, _pageSub, _statusBarText, _targetCountText, _hint, _maxGlyph, _versionText;
        System.Windows.Shapes.Ellipse _statusDot;
        Button _btnStart, _btnStop;

        // ═══════════════════════════ 入口 ═══════════════════════════
        public static int Run(JobContext ctx, List<string> prefill)
        {
            var ui = new WpfUi();
            ui._ctx = ctx;
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.Run(ui.BuildWindow(prefill));
            return 0;
        }

        /// <summary>离线自检：把界面渲染成 PNG 后退出（用来在没有可见桌面时验收界面）</summary>
        public static int SelfTest(string pngPath)
        {
            // 关键：硬件加速窗口用 RenderTargetBitmap 抓会出残影/黑块，自检必须切软件渲染
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            var ui = new WpfUi();
            ui._ctx = new JobContext();
            ui._ctx.ConfigPath = Program.ConfigPath;
            ui._ctx.LogDir = Program.LogDir;
            ui._ctx.AclBackupDir = Program.AclBackupDir;
            ui._ctx.SilentConsole = true;
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Window w = null;
            try
            {
                w = ui.BuildWindow(new List<string>());
                w.Left = -4000;
                w.Top = 0;
                w.Show();
                w.UpdateLayout();
            }
            catch (Exception ex)
            {
                Console.WriteLine("ui-selftest FAILED(构建): " + ex);
                return 2;
            }

            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(1400);
            timer.Tick += delegate (object s, EventArgs e)
            {
                timer.Stop();
                try
                {
                    w.UpdateLayout();
                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        (int)Math.Ceiling(w.ActualWidth), (int)Math.Ceiling(w.ActualHeight), 96, 96,
                        PixelFormats.Pbgra32);
                    rtb.Render(w);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    using (var fs = new FileStream(pngPath, FileMode.Create)) enc.Save(fs);
                    Console.WriteLine("ui-selftest ok  " + (int)w.ActualWidth + "x" + (int)w.ActualHeight);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("ui-selftest FAILED(渲染): " + ex);
                }
                try { w.Close(); } catch { }
                app.Shutdown();
            };
            timer.Start();
            app.Run();
            return 0;
        }

        // ═══════════════════════════ 构造小工具 ═══════════════════════════
        static TextBlock Txt(string text, string styleKey, double size)
        {
            var t = new TextBlock();
            t.Text = text;
            if (styleKey != null) t.Style = S(styleKey);
            if (size > 0) t.FontSize = size;
            return t;
        }

        /// <summary>图标：必须显式指定 FontFamily（隐式 TextBlock 样式会盖掉字体）</summary>
        static TextBlock Icon(string glyph, double size, Brush fg)
        {
            var t = new TextBlock();
            t.Text = glyph;
            t.FontFamily = new FontFamily(Theme.IconFont);
            t.FontSize = size;
            if (fg != null) t.Foreground = fg;
            t.VerticalAlignment = VerticalAlignment.Center;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            return t;
        }

        Button Btn(string text, string glyph, string styleKey, RoutedEventHandler onClick)
        {
            var b = new Button();
            b.Style = S(styleKey);
            var sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            if (glyph != null)
            {
                TextBlock ic = Icon(glyph, 12, null);
                ic.Margin = new Thickness(0, 0, 7, 0);
                sp.Children.Add(ic);
            }
            var t = new TextBlock();
            t.Text = text;
            t.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(t);
            b.Content = sp;
            if (onClick != null) b.Click += onClick;
            return b;
        }

        /// <summary>改按钮上的文字（跳过图标那个 TextBlock）</summary>
        static void SetBtnText(Button b, string text)
        {
            var sp = b.Content as StackPanel;
            if (sp == null) return;
            foreach (UIElement e in sp.Children)
            {
                var t = e as TextBlock;
                if (t == null) continue;
                if (t.FontFamily != null && t.FontFamily.Source.IndexOf("MDL2") >= 0) continue;
                t.Text = text;
                return;
            }
        }

        static Border Card(UIElement child)
        {
            var b = new Border();
            b.Style = S("Card");
            b.Child = child;
            return b;
        }

        StackPanel HB()
        {
            var sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            return sp;
        }

        StackPanel Field(string label, UIElement control, double right)
        {
            var sp = new StackPanel();
            sp.Margin = new Thickness(0, 0, right, 12);
            sp.Children.Add(Txt(label, "FieldLabel", 0));
            sp.Children.Add(control);
            return sp;
        }

        TextBox Input(string text, double width, double height)
        {
            var t = new TextBox();
            if (text != null) t.Text = text;
            if (width > 0) t.Width = width;
            if (height > 0) t.Height = height;
            return t;
        }

        TextBox SmallInput(string text)
        {
            TextBox t = Input(text, 110, 0);
            t.HorizontalAlignment = HorizontalAlignment.Left;
            return t;
        }

        ComboBox Combo(string[] items, double width)
        {
            var c = new ComboBox();
            foreach (string s in items) c.Items.Add(s);
            c.SelectedIndex = 0;
            if (width > 0) c.Width = width;
            return c;
        }

        // ═══════════════════════════ 窗口 ═══════════════════════════
        Window BuildWindow(List<string> prefill)
        {
            _win = new Window();
            _win.Title = "layerpeel 3.0";
            _win.Width = 1220;
            _win.Height = 780;
            _win.MinWidth = 980;
            _win.MinHeight = 620;
            _win.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _win.WindowStyle = WindowStyle.None;
            _win.ResizeMode = ResizeMode.CanResize;
            _win.Background = Bg("B.Bg.Window");
            _win.Foreground = Bg("B.Text.Primary");
            _win.FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
            _win.FontSize = 12.5;
            _win.AllowDrop = true;
            _win.DragOver += OnDragOver;
            _win.Drop += OnDrop;
            _win.UseLayoutRounding = true;
            _win.SnapsToDevicePixels = true;
            TextOptions.SetTextFormattingMode(_win, TextFormattingMode.Display);
            Theme.Apply(_win);

            // 无边框：44px 标题栏区域原生可拖动 / 双击最大化
            var chrome = new System.Windows.Shell.WindowChrome();
            chrome.CaptionHeight = 44;
            chrome.ResizeBorderThickness = new Thickness(6);
            chrome.GlassFrameThickness = new Thickness(0);
            chrome.CornerRadius = new CornerRadius(0);
            chrome.UseAeroCaptionButtons = false;
            System.Windows.Shell.WindowChrome.SetWindowChrome(_win, chrome);
            _win.StateChanged += delegate { OnStateChanged(); };

            // 必须给根 Border 一个不透明背景：Window 自己的 Background 不在这个可视子树里，
            // 否则 RenderTargetBitmap 抓出来是透明（PNG 里显示成纯白），截图/自检都会看到白带。
            _chromeRoot = new Border();
            _chromeRoot.Background = Bg("B.Bg.Window");
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

            UIElement titleBar = BuildTitleBar();
            Grid.SetRow(titleBar, 0);
            root.Children.Add(titleBar);
            UIElement body = BuildBody();
            Grid.SetRow(body, 1);
            root.Children.Add(body);
            UIElement statusBar = BuildStatusBar();
            Grid.SetRow(statusBar, 2);
            root.Children.Add(statusBar);

            _chromeRoot.Child = root;
            _win.Content = _chromeRoot;

            LoadConfigToUi();
            if (prefill != null) foreach (string p in prefill) AddTarget(p);
            Log("就绪。选好目标文件夹后点右上角「开始」。", Bg("B.Text.Muted"));
            SelectPage(0);
            return _win;
        }

        void OnStateChanged()
        {
            bool max = _win.WindowState == WindowState.Maximized;
            // 最大化时留 7px，否则无边框窗口会顶出屏幕
            _chromeRoot.Margin = max ? new Thickness(7) : new Thickness(0);
            if (_maxGlyph != null) _maxGlyph.Text = max ? Theme.IcoRestore : Theme.IcoMax;
        }

        // ── 标题栏 ──
        UIElement BuildTitleBar()
        {
            var g = new Grid();
            g.Background = Bg("B.Bg.Panel");
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = HB();
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Margin = new Thickness(14, 0, 0, 0);
            var logo = new Border();
            logo.Width = 24; logo.Height = 24;
            logo.CornerRadius = new CornerRadius(7);
            logo.Background = Bg("B.Accent");
            var lt = new TextBlock();
            lt.Text = "L"; lt.FontSize = 13; lt.FontWeight = FontWeights.Bold;
            lt.Foreground = Brushes.White;
            lt.HorizontalAlignment = HorizontalAlignment.Center;
            lt.VerticalAlignment = VerticalAlignment.Center;
            logo.Child = lt;
            left.Children.Add(logo);
            var name = Txt("layerpeel", null, 13.5);
            name.FontWeight = FontWeights.SemiBold;
            name.Foreground = Bg("B.Text.Primary");
            name.Margin = new Thickness(10, 0, 0, 0);
            name.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(name);
            _titleTask = Txt("/ 解压 · 去套娃", null, 12);
            _titleTask.Foreground = Bg("B.Text.Muted");
            _titleTask.Margin = new Thickness(7, 0, 0, 0);
            _titleTask.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(_titleTask);
            Grid.SetColumn(left, 0);
            g.Children.Add(left);

            var right = HB();
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(0, 0, 8, 0);

            var pill = new Border();
            pill.CornerRadius = new CornerRadius(10);
            pill.Padding = new Thickness(10, 3, 10, 3);
            pill.Background = Bg("B.Bg.PanelAlt");
            pill.BorderBrush = Bg("B.BorderSoft");
            pill.BorderThickness = new Thickness(1);
            pill.Margin = new Thickness(0, 0, 10, 0);
            var pillSp = HB();
            _statusDot = new System.Windows.Shapes.Ellipse();
            _statusDot.Width = 6; _statusDot.Height = 6;
            _statusDot.Fill = Bg("B.Text.Muted");
            _statusDot.VerticalAlignment = VerticalAlignment.Center;
            pillSp.Children.Add(_statusDot);
            _statusText = Txt("空闲", null, 11.5);
            _statusText.Foreground = Bg("B.Text.Secondary");
            _statusText.Margin = new Thickness(7, 0, 0, 0);
            _statusText.VerticalAlignment = VerticalAlignment.Center;
            pillSp.Children.Add(_statusText);
            pill.Child = pillSp;
            right.Children.Add(pill);

            var sep = new Border();
            sep.Width = 1; sep.Height = 18;
            sep.Background = Bg("B.Border");
            sep.Margin = new Thickness(0, 0, 6, 0);
            right.Children.Add(sep);

            right.Children.Add(WinBtn(Theme.IcoMin, "WindowButton", delegate { _win.WindowState = WindowState.Minimized; }));
            Button mx = WinBtn(Theme.IcoMax, "WindowButton", delegate
            {
                _win.WindowState = _win.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            });
            _maxGlyph = (TextBlock)mx.Content;
            right.Children.Add(mx);
            right.Children.Add(WinBtn(Theme.IcoClose, "WindowCloseButton", delegate { _win.Close(); }));

            Grid.SetColumn(right, 2);
            g.Children.Add(right);
            return g;
        }

        Button WinBtn(string glyph, string styleKey, RoutedEventHandler onClick)
        {
            var b = new Button();
            b.Style = S(styleKey);
            b.Content = Icon(glyph, 10, null);
            b.Click += onClick;
            // 标题栏里的可交互元素必须显式声明，否则会被当成拖动区域点不到
            System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(b, true);
            return b;
        }

        // ── 主体 ──
        UIElement BuildBody()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(226) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            UIElement nav = BuildNav();
            Grid.SetColumn(nav, 0); g.Children.Add(nav);
            UIElement body = BuildContent();
            Grid.SetColumn(body, 1); g.Children.Add(body);
            return g;
        }

        UIElement BuildNav()
        {
            var outer = new Border();
            outer.Background = Bg("B.Bg.Panel");
            outer.BorderBrush = Bg("B.Border");
            outer.BorderThickness = new Thickness(0, 1, 1, 0);

            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var cap = Txt("任务", "Muted", 0);
            cap.Margin = new Thickness(20, 14, 0, 6);
            Grid.SetRow(cap, 0);
            g.Children.Add(cap);

            _nav = new ListBox();
            _nav.Style = S("NavList");
            AddPage("解压 · 去套娃", "多层压缩逐层解开，再按策略处理原包", Theme.IcoFolder, true);
            AddPage("仅去套娃", "只压平目录层级，不碰压缩包", Theme.IcoFlatten, false);
            AddPage("删除指定文件", "按 glob 规则清理推广链接等垃圾文件", Theme.IcoDelete, false);
            AddPage("修复权限", "修复目录读写权限，动手前先备份 ACL", Theme.IcoLock, false);
            AddPage("还原权限", "从备份还原权限与属主", Theme.IcoUnlock, false);
            _nav.SelectionChanged += delegate { if (_nav.SelectedIndex >= 0) SelectPage(_nav.SelectedIndex); };
            Grid.SetRow(_nav, 1);
            g.Children.Add(_nav);

            var foot = new Border();
            foot.Background = Bg("B.Bg.PanelAlt");
            foot.BorderBrush = Bg("B.BorderSoft");
            foot.BorderThickness = new Thickness(1);
            foot.CornerRadius = new CornerRadius(8);
            foot.Margin = new Thickness(12, 0, 12, 12);
            foot.Padding = new Thickness(12, 10, 12, 10);
            var fs = new StackPanel();
            _targetCountText = Txt("目标 0 个", null, 12);
            _targetCountText.Foreground = Bg("B.Text.Primary");
            fs.Children.Add(_targetCountText);
            var line2 = Txt("把文件夹拖进窗口即可添加", "Muted", 0);
            line2.Margin = new Thickness(0, 3, 0, 9);
            line2.TextWrapping = TextWrapping.Wrap;
            fs.Children.Add(line2);
            var row = HB();
            row.Children.Add(IconBtn(Theme.IcoDelete, "打开回收站", delegate { OpenPath("shell:RecycleBinFolder"); }));
            row.Children.Add(IconBtn(Theme.IcoDoc, "打开日志目录", delegate { OpenPath(_ctx.LogDir); }));
            row.Children.Add(IconBtn(Theme.IcoRefresh, "重置配置", delegate { ResetConfig(); }));
            fs.Children.Add(row);
            foot.Child = fs;
            Grid.SetRow(foot, 2);
            g.Children.Add(foot);

            outer.Child = g;
            return outer;
        }

        Button IconBtn(string glyph, string tip, RoutedEventHandler onClick)
        {
            var b = new Button();
            b.Style = S("IconButton");
            b.ToolTip = tip;
            b.Content = Icon(glyph, 13, null);
            b.Click += onClick;
            return b;
        }

        void AddPage(string title, string sub, string glyph, bool needsPassword)
        {
            var p = new Page();
            p.Title = title; p.Sub = sub; p.Glyph = glyph; p.NeedsPassword = needsPassword;
            _pages.Add(p);

            var content = HB();
            var ic = Icon(glyph, 14, null);
            ic.Margin = new Thickness(0, 0, 11, 0);
            ic.SetBinding(TextBlock.ForegroundProperty, AncestorBinding());
            content.Children.Add(ic);
            var tx = Txt(title, null, 12.5);
            tx.VerticalAlignment = VerticalAlignment.Center;
            tx.SetBinding(TextBlock.ForegroundProperty, AncestorBinding());
            content.Children.Add(tx);

            var item = new ListBoxItem();
            item.Content = content;
            _nav.Items.Add(item);
        }

        static Binding AncestorBinding()
        {
            var b = new Binding("Foreground");
            b.RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1);
            return b;
        }

        // ── 内容区 ──
        UIElement BuildContent()
        {
            var g = new Grid();
            g.Margin = new Thickness(18, 16, 18, 14);
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hs = new StackPanel();
            _pageTitle = Txt("解压 · 去套娃", "PageTitle", 0);
            hs.Children.Add(_pageTitle);
            _pageSub = Txt("", "PageSub", 0);
            hs.Children.Add(_pageSub);
            head.Children.Add(hs);

            var acts = HB();
            acts.VerticalAlignment = VerticalAlignment.Center;
            _hint = Txt("", "Muted", 0);
            _hint.Margin = new Thickness(0, 0, 12, 0);
            _hint.VerticalAlignment = VerticalAlignment.Center;
            acts.Children.Add(_hint);
            _btnStop = Btn("停止", Theme.IcoStop, "BtnGhost", delegate { StopJob(); });
            _btnStop.IsEnabled = false;
            _btnStop.Margin = new Thickness(0, 0, 8, 0);
            acts.Children.Add(_btnStop);
            _btnStart = Btn("开始", Theme.IcoPlay, "BtnPrimary", delegate { StartJob(); });
            acts.Children.Add(_btnStart);
            Grid.SetColumn(acts, 1);
            head.Children.Add(acts);
            Grid.SetRow(head, 0);
            g.Children.Add(head);

            var body = new Grid();
            body.Margin = new Thickness(0, 16, 0, 0);
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(396) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var leftCol = new StackPanel();
            leftCol.Children.Add(Card(BuildTargetCard()));

            var oc = new StackPanel();
            oc.Children.Add(SectionTitle("选项"));
            _optionHost = new StackPanel();
            _optionHost.Margin = new Thickness(0, 9, 0, 0);
            oc.Children.Add(_optionHost);
            var optCard = Card(oc);
            optCard.Margin = new Thickness(0, 12, 0, 0);
            leftCol.Children.Add(optCard);

            _passwordCardInner = new StackPanel();
            _passwordCardInner.Children.Add(SectionTitle("密码表"));
            var pl = Txt("解压用；一行一个，按顺序尝试，命中的会提到最前", "Muted", 0);
            pl.Margin = new Thickness(0, 3, 0, 8);
            pl.TextWrapping = TextWrapping.Wrap;
            _passwordCardInner.Children.Add(pl);
            _password = Input("", 0, 76);
            _password.AcceptsReturn = true;
            _password.FontFamily = new FontFamily("Consolas, Cascadia Mono");
            _password.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _passwordCardInner.Children.Add(_password);
            var pwBtn = Btn("保存到配置", null, "BtnGhost", delegate { SaveConfig(); });
            pwBtn.Margin = new Thickness(0, 10, 0, 0);
            pwBtn.HorizontalAlignment = HorizontalAlignment.Left;
            _passwordCardInner.Children.Add(pwBtn);
            _passwordCardBorder = Card(_passwordCardInner);
            _passwordCardBorder.Margin = new Thickness(0, 12, 0, 0);
            leftCol.Children.Add(_passwordCardBorder);

            var sc = new StackPanel();
            sc.Children.Add(SectionTitle("结果"));
            _statsHost = new StackPanel();
            _statsHost.Orientation = Orientation.Horizontal;
            _statsHost.Margin = new Thickness(0, 9, 0, 0);
            sc.Children.Add(_statsHost);
            var statCard = Card(sc);
            statCard.Margin = new Thickness(0, 12, 0, 0);
            leftCol.Children.Add(statCard);

            var leftScroll = new ScrollViewer();
            leftScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            leftScroll.Padding = new Thickness(0, 0, 6, 0);
            leftScroll.Content = leftCol;
            Grid.SetColumn(leftScroll, 0);
            body.Children.Add(leftScroll);

            var logCol = new StackPanel();
            logCol.Children.Add(SectionTitle("实时日志"));
            _log = new TextBox();
            _log.IsReadOnly = true;
            _log.AcceptsReturn = true;
            _log.TextWrapping = TextWrapping.NoWrap;
            _log.Background = Bg("B.Bg.Input");
            _log.Foreground = Bg("B.Text.Primary");
            _log.BorderThickness = new Thickness(0);
            _log.FontFamily = new FontFamily("Consolas, Cascadia Mono");
            _log.FontSize = 12;
            _log.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _log.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _log.Margin = new Thickness(0, 9, 0, 0);
            logCol.Children.Add(_log);
            var logCard = Card(logCol);
            logCard.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(logCard, 1);
            body.Children.Add(logCard);

            Grid.SetRow(body, 1);
            g.Children.Add(body);

            BuildOptions();
            RenderStats(null);
            return g;
        }

        TextBlock SectionTitle(string text)
        {
            return Txt(text, "CardTitle", 0);
        }

        UIElement BuildTargetCard()
        {
            var sp = new StackPanel();
            sp.Children.Add(SectionTitle("目标"));
            var hint = Txt("文件夹或压缩包都可以，可加多个", "Muted", 0);
            hint.Margin = new Thickness(0, 3, 0, 9);
            sp.Children.Add(hint);

            var row = HB();
            _pathInput = Input("", 0, 0);
            _pathInput.MinWidth = 140;
            _pathInput.Margin = new Thickness(0, 0, 8, 0);
            _pathInput.KeyDown += delegate (object s, KeyEventArgs e)
            { if (e.Key == Key.Enter) AddTarget(_pathInput.Text); };
            row.Children.Add(_pathInput);
            var pick = Btn("选择…", null, "BtnGhost", delegate { PickFolder(); });
            pick.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(pick);
            row.Children.Add(Btn("添加", Theme.IcoAdd, "BtnGhost", delegate { AddTarget(_pathInput.Text); }));
            sp.Children.Add(row);

            _targetList = new ListBox();
            _targetList.Height = 96;
            _targetList.Margin = new Thickness(0, 10, 0, 0);
            _targetList.FontFamily = new FontFamily("Consolas, Cascadia Mono");
            _targetList.FontSize = 11.5;
            sp.Children.Add(_targetList);

            var more = HB();
            more.Margin = new Thickness(0, 10, 0, 0);
            more.Children.Add(Btn("清理残留", null, "BtnGhost", delegate { RunSpecial("cleanup"); }));
            more.Children.Add(Btn("撤销暂存", Theme.IcoUndo, "BtnGhost", delegate { UndoStaged(); }));
            sp.Children.Add(more);
            var more2 = HB();
            more2.Margin = new Thickness(0, 8, 0, 0);
            more2.Children.Add(Btn("注册右键菜单", null, "BtnGhost", delegate { MenuAction("install"); }));
            more2.Children.Add(Btn("卸载右键菜单", null, "BtnGhost", delegate { MenuAction("uninstall"); }));
            sp.Children.Add(more2);
            return sp;
        }

        // ── 选项：每个任务页一套分组，Visibility 切换 ──
        void BuildOptions()
        {
            for (int i = 0; i < _pages.Count; i++)
            {
                var g = new StackPanel();
                g.Visibility = Visibility.Collapsed;
                _pages[i].Options = g;
                _optionHost.Children.Add(g);
            }

            // 0 解压
            var p0 = _pages[0].Options;
            _delMode = Combo(new string[] { "移入回收站（默认）", "移到暂存目录（可一键还原）", "保留，不删" }, 0);
            p0.Children.Add(Field("解压成功后的原包", _delMode, 0));
            _depth = SmallInput("64");
            p0.Children.Add(Field("套娃深度上限", _depth, 0));

            // 1 仅去套娃
            var p1 = _pages[1].Options;
            _depth2 = SmallInput("64");
            p1.Children.Add(Field("套娃深度上限", _depth2, 0));

            // 2 删除指定文件
            var p2 = _pages[2].Options;
            _filePatterns = Input("", 0, 74);
            _filePatterns.AcceptsReturn = true;
            _filePatterns.FontFamily = new FontFamily("Consolas, Cascadia Mono");
            _filePatterns.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            p2.Children.Add(Field("文件名规则", _filePatterns, 0));
            _dirPatterns = Input("", 0, 74);
            _dirPatterns.AcceptsReturn = true;
            _dirPatterns.FontFamily = new FontFamily("Consolas, Cascadia Mono");
            _dirPatterns.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            p2.Children.Add(Field("文件夹名规则（留空则不动目录）", _dirPatterns, 0));
            _protect = new CheckBox();
            _protect.Content = "保护压缩包（是压缩包就不删）";
            _protect.IsChecked = true;
            p2.Children.Add(_protect);
            var sb = Btn("保存规则", null, "BtnGhost", delegate { SaveConfig(); });
            sb.Margin = new Thickness(0, 12, 0, 0);
            sb.HorizontalAlignment = HorizontalAlignment.Left;
            p2.Children.Add(sb);

            // 3 修复权限
            var p3 = _pages[3].Options;
            _aclLevel = Combo(new string[] { "重置继承 + 授权（推荐）", "只授权（最轻）", "接管属主 + 重置 + 授权" }, 0);
            p3.Children.Add(Field("修复力度", _aclLevel, 0));

            // 4 还原权限
            var p4 = _pages[4].Options;
            var rh = Txt("从备份还原权限（含属主回写）", "Muted", 0);
            rh.Margin = new Thickness(0, 0, 0, 8);
            p4.Children.Add(rh);
            _backupPanel = new StackPanel();
            var sv = new ScrollViewer();
            sv.MaxHeight = 200;
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.Content = _backupPanel;
            p4.Children.Add(sv);
        }

        // ── 状态栏 ──
        UIElement BuildStatusBar()
        {
            var border = new Border();
            border.Background = Bg("B.Bg.Panel");
            border.BorderBrush = Bg("B.Border");
            border.BorderThickness = new Thickness(0, 1, 0, 0);
            var g = new Grid();
            g.Margin = new Thickness(16, 0, 16, 0);
            var l = HB();
            l.VerticalAlignment = VerticalAlignment.Center;
            var dot = new System.Windows.Shapes.Ellipse();
            dot.Width = 6; dot.Height = 6;
            dot.Fill = Bg("B.Success");
            dot.VerticalAlignment = VerticalAlignment.Center;
            l.Children.Add(dot);
            _statusBarText = Txt("就绪", "Muted", 0);
            _statusBarText.Margin = new Thickness(7, 0, 0, 0);
            l.Children.Add(_statusBarText);
            g.Children.Add(l);
            _versionText = Txt("layerpeel · 后端 7-Zip", "Muted", 0);
            _versionText.HorizontalAlignment = HorizontalAlignment.Right;
            _versionText.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(_versionText);
            border.Child = g;
            return border;
        }

        // ═══════════════════════════ 页面切换 ═══════════════════════════
        void SelectPage(int i)
        {
            if (_nav != null && _nav.SelectedIndex != i) { _nav.SelectedIndex = i; return; }
            _page = i;
            for (int k = 0; k < _pages.Count; k++)
                if (_pages[k].Options != null)
                    _pages[k].Options.Visibility = k == i ? Visibility.Visible : Visibility.Collapsed;

            Page p = _pages[i];
            _pageTitle.Text = p.Title;
            _pageSub.Text = p.Sub;
            _titleTask.Text = "/ " + p.Title;
            _passwordCardBorder.Visibility = p.NeedsPassword ? Visibility.Visible : Visibility.Collapsed;
            if (_btnStart != null)
            {
                _btnStart.IsEnabled = i != 4 && _job == null;
                SetBtnText(_btnStart, i == 3 ? "开始修复" : (i == 1 ? "开始压平" : "开始"));
            }
            if (i == 4) RefreshBackups();
            RenderStats(null);
        }

        // ═══════════════════════════ 交互 ═══════════════════════════
        void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] items = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (items == null) return;
            foreach (string p in items) AddTarget(p);
            Log("已拖入 " + items.Length + " 个目标", Bg("B.Accent"));
        }

        void AddTarget(string p)
        {
            if (p == null) return;
            p = p.Trim().Trim('"');
            if (p.Length == 0) return;
            if (!_targets.Contains(p))
            {
                _targets.Add(p);
                _targetList.Items.Add(p);
            }
            _pathInput.Clear();
            if (_targetCountText != null) _targetCountText.Text = "目标 " + _targets.Count + " 个";
        }

        void PickFolder()
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog();
            dlg.Description = "选择要处理的文件夹";
            dlg.ShowNewFolderButton = false;
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK) AddTarget(dlg.SelectedPath);
        }

        void StartJob()
        {
            if (_job != null) { Log("已有任务在运行", Bg("B.Warn")); return; }
            if (_targets.Count == 0) { Log("先添加一个目标文件夹", Bg("B.Warn")); return; }

            var job = new Job();
            job.Task = _page == 0 ? "extract" : _page == 1 ? "flatten" : _page == 2 ? "clean" : "acl";
            job.Targets.AddRange(_targets);
            int d;
            TextBox depthBox = _page == 1 ? _depth2 : _depth;
            job.Depth = (depthBox != null && int.TryParse(depthBox.Text.Trim(), out d)) ? d : 64;
            if (_page == 0 && _delMode != null) job.Mode = Modes[_delMode.SelectedIndex];
            if (_page == 3 && _aclLevel != null)
                job.AclLevel = _aclLevel.SelectedIndex == 0 ? 2 : (_aclLevel.SelectedIndex == 1 ? 1 : 3);
            if (job.Task == "clean")
            {
                job.FilePatterns = SplitLines(_filePatterns.Text);
                job.DirPatterns = SplitLines(_dirPatterns.Text);
                try
                {
                    Config cfg = Config.Load(_ctx.ConfigPath);
                    Json.Obj(cfg.Data, "delete")["protectArchives"] = _protect.IsChecked == true;
                    cfg.Save();
                }
                catch { }
            }
            RunJob(job);
        }

        void RunSpecial(string task)
        {
            if (_job != null) { Log("已有任务在运行", Bg("B.Warn")); return; }
            if (_targets.Count == 0) { Log("先添加一个目标文件夹", Bg("B.Warn")); return; }
            var job = new Job();
            job.Task = task;
            job.Targets.AddRange(_targets);
            RunJob(job);
        }

        void RunJob(Job job)
        {
            _job = job;
            _btnStart.IsEnabled = false;
            _btnStop.IsEnabled = true;
            SetStatus("运行中", Bg("B.Success"));
            _hint.Text = "任务进行中…";

            var ctx = new JobContext();
            ctx.ConfigPath = _ctx.ConfigPath;
            ctx.LogDir = _ctx.LogDir;
            ctx.AclBackupDir = _ctx.AclBackupDir;
            ctx.SilentConsole = true;
            ctx.OnEvent = delegate (Dictionary<string, object> ev) { Ui(delegate { OnEvent(ev); }); };

            job.Aborted = false;
            JobService.Start(ctx, job, delegate ()
            {
                Ui(delegate ()
                {
                    _job = null;
                    _btnStart.IsEnabled = _page != 4;
                    _btnStop.IsEnabled = false;
                    SetStatus("空闲", Bg("B.Text.Muted"));
                    _hint.Text = "";
                    RefreshBackups();
                });
            });
        }

        void StopJob()
        {
            if (_job == null) return;
            _job.Aborted = true;
            Log("正在停止…（当前这一步做完就停）", Bg("B.Warn"));
        }

        void Ui(Action a)
        {
            if (_win == null) return;
            try { _win.Dispatcher.BeginInvoke(DispatcherPriority.Background, a); } catch { }
        }

        void SetStatus(string text, Brush color)
        {
            if (_statusText != null) { _statusText.Text = text; _statusText.Foreground = color; }
            if (_statusDot != null) _statusDot.Fill = color;
            if (_statusBarText != null) _statusBarText.Text = text == "运行中" ? "正在处理…" : "就绪";
        }

        // ═══════════════════════════ 事件 → 界面 ═══════════════════════════
        void OnEvent(Dictionary<string, object> ev)
        {
            string t = Convert.ToString(ev["type"]);
            string file = ev.ContainsKey("file") ? BaseName(Convert.ToString(ev["file"])) : null;

            switch (t)
            {
                case "job-start":
                    _log.Clear();
                    Log("开始执行：" + Join(ev, "targets"), Bg("B.Accent"));
                    break;
                case "dir-start":
                    Log("▸ " + Convert.ToString(ev["dir"]), Bg("B.Text.Muted"));
                    break;
                case "archive-extracting":
                    Log("解压 " + file + " … " + Convert.ToString(ev["entries"]) + " 项 / " + Convert.ToString(ev["sizeMB"]) + "MB", Bg("B.Text.Primary"));
                    break;
                case "password-try":
                    Log("  试密码：" + Convert.ToString(ev["password"]), Bg("B.Text.Muted"));
                    break;
                case "archive-done":
                    Log("✔ " + file + " 解出 " + Convert.ToString(ev["moved"]) + " 项，校验"
                        + (Truthy(ev, "verified") ? "通过" : "未做"), Bg("B.Success"));
                    break;
                case "archive-failed":
                    Log("✘ " + file + " — " + Convert.ToString(ev["reason"]) + "：" + Convert.ToString(ev["detail"]), Bg("B.Danger"));
                    break;
                case "archive-skipped":
                    Log("跳过 " + file + " — " + Convert.ToString(ev["reason"]), Bg("B.Warn"));
                    break;
                case "clean-start":
                    Log("删除规则：" + Join(ev, "rules"), Bg("B.Accent"));
                    break;
                case "clean-protected":
                    Log("跳过（是压缩包）：" + BaseName(Convert.ToString(ev["file"])), Bg("B.Text.Muted"));
                    break;
                case "clean-done":
                    Log("✔ 已删除 " + BaseName(Convert.ToString(ev["target"])), Bg("B.Success"));
                    break;
                case "clean-failed":
                    Log("✘ " + Convert.ToString(ev["target"]) + " — " + Convert.ToString(ev["detail"]), Bg("B.Danger"));
                    break;
                case "clean-error":
                    Log("规则有问题：" + Join(ev, "errors"), Bg("B.Danger"));
                    break;
                case "flatten-step":
                    Log("✔ 压平 " + Convert.ToString(ev["dir"]) + "（上提 " + Convert.ToString(ev["moved"]) + " 项）", Bg("B.Success"));
                    break;
                case "leftover-removed":
                    Log("✔ 已清掉残留 " + Convert.ToString(ev["dir"]), Bg("B.Success"));
                    break;
                case "leftover-failed":
                    Log("✘ 清理残留失败 " + Convert.ToString(ev["dir"]), Bg("B.Danger"));
                    break;
                case "acl-start":
                    Log("修复权限：" + Convert.ToString(ev["dir"]), Bg("B.Accent"));
                    break;
                case "acl-backup":
                    Log("✔ 已备份 ACL → " + BaseName(Convert.ToString(ev["backupFile"])), Bg("B.Success"));
                    break;
                case "acl-step":
                    Log("  · " + Convert.ToString(ev["step"]), Bg("B.Text.Muted"));
                    break;
                case "acl-done":
                    Log("✔ 权限已修复：" + Convert.ToString(ev["dir"]), Bg("B.Success"));
                    break;
                case "acl-failed":
                    Log("✘ " + Convert.ToString(ev["dir"]) + " — " + Convert.ToString(ev["detail"]), Bg("B.Danger"));
                    break;
                case "acl-restore-start":
                    Log("还原权限：" + Convert.ToString(ev["dir"]), Bg("B.Accent"));
                    break;
                case "acl-restore-done":
                    Log("✔ 已还原：" + Convert.ToString(ev["dir"]), Bg("B.Success"));
                    break;
                case "acl-restore-failed":
                    Log("✘ 还原失败：" + Convert.ToString(ev["dir"]), Bg("B.Danger"));
                    break;
                case "job-done":
                    Log("完成。" + Summarize(ev), Bg("B.Accent"));
                    RenderStats(ev["stats"] as Dictionary<string, object>);
                    SetStatus("空闲", Bg("B.Text.Muted"));
                    break;
                case "job-error":
                    Log("出错：" + Convert.ToString(ev["detail"]), Bg("B.Danger"));
                    SetStatus("空闲", Bg("B.Text.Muted"));
                    break;
            }
        }

        string Summarize(Dictionary<string, object> ev)
        {
            Dictionary<string, object> s = ev["stats"] as Dictionary<string, object>;
            if (s == null) return "";
            string task = Convert.ToString(ev["task"]);
            if (task == "clean") return "命中 " + N(s, "matched") + "，删除 " + N(s, "deleted") + "，保护跳过 " + N(s, "protected") + "，失败 " + N(s, "failed");
            if (task == "flatten") return "压平 " + N(s, "flattened") + " 层，上提 " + N(s, "moved") + " 项";
            if (task == "cleanup") return "清理残留 " + N(s, "leftovers") + " 个目录";
            if (task == "acl") return "修复 " + N(s, "fixed") + " 个目录，失败 " + N(s, "failed");
            if (task == "acl-restore") return "还原 " + N(s, "restored") + "，失败 " + N(s, "failed");
            return "识别 " + N(s, "archives") + " 个包，解压 " + N(s, "extracted") + "，原包处理 " + N(s, "deleted") + "，失败 " + N(s, "failed");
        }

        static string N(Dictionary<string, object> d, string k)
        {
            object v;
            if (d != null && d.TryGetValue(k, out v) && v != null) return Convert.ToString(v);
            return "0";
        }

        void RenderStats(Dictionary<string, object> s)
        {
            if (_statsHost == null) return;
            _statsHost.Children.Clear();
            string[][] spec;
            if (_page == 1) spec = new string[][] { new[] { "可压平层级", "matched", "" }, new[] { "已压平", "flattened", "ok" }, new[] { "上提项目", "moved", "" } };
            else if (_page == 2) spec = new string[][] { new[] { "命中规则", "matched", "" }, new[] { "受保护跳过", "protected", "w" }, new[] { "已删除", "deleted", "ok" }, new[] { "失败", "failed", "e" } };
            else if (_page == 3) spec = new string[][] { new[] { "修复成功", "fixed", "ok" }, new[] { "失败", "failed", "e" } };
            else if (_page == 4) spec = new string[][] { new[] { "还原成功", "restored", "ok" }, new[] { "失败", "failed", "e" } };
            else spec = new string[][] { new[] { "识别到压缩包", "archives", "" }, new[] { "成功解压", "extracted", "ok" }, new[] { "原包处理", "deleted", "" }, new[] { "缺密码", "noPassword", "w" }, new[] { "失败", "failed", "e" } };

            foreach (string[] it in spec)
            {
                string raw = s == null ? "0" : N(s, it[1]);
                Brush fg = it[2] == "ok" ? Bg("B.Success") : it[2] == "w" ? Bg("B.Warn")
                         : it[2] == "e" ? Bg("B.Danger") : Bg("B.Text.Primary");

                var inner = new StackPanel();
                inner.Children.Add(Txt(it[0], "Muted", 10.5));
                var v = Txt(raw, null, 20);
                v.FontWeight = FontWeights.SemiBold;
                v.Foreground = fg;
                v.Margin = new Thickness(0, 2, 0, 0);
                inner.Children.Add(v);

                var box = new Border();
                box.Background = Bg("B.Bg.PanelAlt");
                box.BorderBrush = Bg("B.BorderSoft");
                box.BorderThickness = new Thickness(1);
                box.CornerRadius = new CornerRadius(8);
                box.Padding = new Thickness(11, 8, 11, 8);
                box.Margin = new Thickness(0, 0, 8, 8);
                box.MinWidth = 88;
                box.Child = inner;
                _statsHost.Children.Add(box);
            }
        }

        void Log(string text, Brush color)
        {
            if (_log == null) return;
            _log.AppendText(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + text + "\r\n");
            _log.ScrollToEnd();
        }

        // ═══════════════════════════ 配置 ═══════════════════════════
        void LoadConfigToUi()
        {
            try
            {
                Config cfg = Config.Load(_ctx.ConfigPath);
                _password.Text = string.Join("\r\n", Json.StrList(cfg.Data, "passwords").ToArray());
                Dictionary<string, object> del = Json.Obj(cfg.Data, "delete");
                _filePatterns.Text = string.Join("\r\n", Json.StrList(del, "filePatterns").ToArray());
                _dirPatterns.Text = string.Join("\r\n", Json.StrList(del, "dirPatterns").ToArray());
                _protect.IsChecked = Json.Bool(del, "protectArchives", true);
                string depth = Convert.ToString(Json.PathInt(cfg.Data, "behavior.maxDepth", 64));
                _versionText.Text = "layerpeel " + Json.Str(cfg.Data, "version", "3.0.0") + " · 后端 7-Zip";
                _depth.Text = depth;
                if (_depth2 != null) _depth2.Text = depth;
                string mode = Json.PathStr(cfg.Data, "delete.mode", "recycle");
                _delMode.SelectedIndex = mode == "staging" ? 1 : mode == "keep" ? 2 : 0;
                foreach (string w in cfg.Warnings) Log("! " + w, Bg("B.Warn"));
                foreach (string e in cfg.Errors) Log("× " + e, Bg("B.Danger"));
            }
            catch (Exception e) { Log("读配置失败：" + e.Message, Bg("B.Danger")); }
        }

        void SaveConfig()
        {
            try
            {
                Config cfg = Config.Load(_ctx.ConfigPath);
                cfg.Data["passwords"] = SplitLines(_password.Text).ToArray();
                Dictionary<string, object> del = Json.Obj(cfg.Data, "delete");
                del["filePatterns"] = SplitLines(_filePatterns.Text).ToArray();
                del["dirPatterns"] = SplitLines(_dirPatterns.Text).ToArray();
                del["protectArchives"] = _protect.IsChecked == true;
                del["mode"] = Modes[_delMode.SelectedIndex];
                int d;
                if (int.TryParse(_depth.Text.Trim(), out d)) Json.Obj(cfg.Data, "behavior")["maxDepth"] = d;
                cfg.Save();
                Log("✔ 配置已保存：" + _ctx.ConfigPath, Bg("B.Success"));
            }
            catch (Exception e) { Log("× 保存配置失败：" + e.Message, Bg("B.Danger")); }
        }

        void ResetConfig()
        {
            if (MessageBox.Show(_win, "把 config.json 恢复成默认值？现有配置会先备份。", "layerpeel",
                    MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            string backup = Config.Reset(_ctx.ConfigPath);
            Log("✔ 配置已重置" + (backup != null ? "，旧配置备份在 " + BaseName(backup) : ""), Bg("B.Success"));
            LoadConfigToUi();
        }

        void RefreshBackups()
        {
            if (_backupPanel == null) return;
            _backupPanel.Children.Clear();
            var fixer = new AclFixer(_ctx.AclBackupDir, new Audit());
            List<AclFixer.BackupInfo> list = fixer.ListBackups();
            if (list.Count == 0)
            {
                _backupPanel.Children.Add(Txt("还没有备份。做「修复权限」时会自动生成。", "Muted", 0));
                return;
            }
            foreach (AclFixer.BackupInfo b in list)
            {
                var row = HB();
                row.Margin = new Thickness(0, 3, 0, 3);
                var t = Txt("", null, 11.5);
                string tm = b.Time ?? "";
                t.Text = (tm.Length > 19 ? tm.Substring(0, 19) : tm).Replace("T", " ") + "   " + b.Target;
                t.Foreground = Bg("B.Text.Secondary");
                t.Width = 210;
                t.TextTrimming = TextTrimming.CharacterEllipsis;
                t.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(t);
                string file = b.File;
                row.Children.Add(Btn("还原", null, "BtnGhost", delegate
                {
                    var job = new Job();
                    job.Task = "acl-restore";
                    job.Targets.Add(file);
                    RunJob(job);
                }));
                _backupPanel.Children.Add(row);
            }
        }

        void UndoStaged()
        {
            if (_targets.Count == 0) { Log("先添加一个目标文件夹", Bg("B.Warn")); return; }
            Config cfg = Config.Load(_ctx.ConfigPath);
            string staging = Json.PathStr(cfg.Data, "delete.stagingDirName", "_layerpeel_trash_");
            foreach (string t in _targets)
            {
                int[] r = SafeDelete.Undo(t, staging);
                Log("还原 " + BaseName(t) + "：成功 " + r[0] + "，失败 " + r[1], Bg("B.Success"));
            }
        }

        void MenuAction(string action)
        {
            List<object> res = MenuHelper.Run(action);
            int ok = 0;
            foreach (object o in res)
            {
                var d = o as Dictionary<string, object>;
                if (d != null && d.ContainsKey("ok") && Convert.ToBoolean(d["ok"])) ok++;
            }
            Log((action == "install" ? "已注册" : "已卸载") + " " + ok + "/" + res.Count + " 处右键菜单项", Bg("B.Success"));
        }

        void OpenPath(string p)
        {
            try { Process.Start("explorer.exe", SevenZip.Quote(p)); }
            catch (Exception e) { Log("打不开：" + e.Message, Bg("B.Danger")); }
        }

        // ═══════════════════════════ 静态小工具 ═══════════════════════════
        static List<string> SplitLines(string text)
        {
            var list = new List<string>();
            if (text == null) return list;
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string s = raw.Trim();
                if (s.Length > 0) list.Add(s);
            }
            return list;
        }

        static bool Truthy(Dictionary<string, object> ev, string key)
        {
            object v;
            if (!ev.TryGetValue(key, out v) || v == null) return false;
            try { return Convert.ToBoolean(v); } catch { return false; }
        }

        static string Join(Dictionary<string, object> ev, string key)
        {
            object[] a = Json.Arr(ev, key);
            var parts = new List<string>();
            foreach (object o in a) parts.Add(Convert.ToString(o));
            return string.Join(" , ", parts.ToArray());
        }

        static string BaseName(string p)
        {
            if (string.IsNullOrEmpty(p)) return "";
            try { return Path.GetFileName(p.TrimEnd('\\')); } catch { return p; }
        }
    }
}
