# layerpeel 3.0（本目录项目约定）

## 这是什么

从零重写的「解压 · 去套娃助手」。**不是 f-keydle 2.x 的补丁**，两者代码无任何继承关系。
参考成品在 `..\f-keydle_v2.1\`（2.1 的完整发布包，含 Bandizip 便携版与 4 个编译好的 exe），
GitHub 上那份只有带 `.txt` 后缀的源码、不含编译产物，别当基线。

## 已定的技术选择（用户 2026-10-12 明确选定，不要擅自更改）

- **后端**：7-Zip（`C:\Program Files\7-Zip\7z.exe`），不再用 Bandizip + AHK 抓窗口
- **删除策略**：默认**回收站**，超配额自动退到暂存目录 `_layerpeel_trash_`（可一键还原）
- **实现**：C# + .NET Framework 4.8（系统自带，用 `csc.exe` 编译），单文件 exe ~142KB；
  界面是 **WPF 原生窗口**（纯 C# 写，不用 XAML 编译 / MSBuild / SDK）；WPF 属 .NET Framework 自带组件，
  **不随包分发**。
- **随包只带便携 7-Zip**（2.4MB）；不需要装 Node、不需要 .NET SDK、不需要装 7-Zip。
- **功能不缺失**：以 `..\f-keydle_v2.1\` 的能力为基线
- ✅ **许可：已定，走 A —— 本仓库按 GPL-3.0 发布**（用户 2026-10-12 决定）。
  原因：原仓库 `ssh-buanshishi/f-keydle` 是 GPL-3.0，而我们读过它的源码、逐字保留了 4 个配置标记线
  字符串（`Core.cs` 的 `MigrateLegacy`）、受保护扩展名清单也基本照搬了它的 C++ 识别器 ——
  "接触过源码 + 存在逐字相同片段"两条同时在，主张独立实现不成立，按 GPL 第 5 条必须以同协议发布。
  已做：`LICENSE` 换成 GPL-3.0 全文、7 个 `.cs` 加 GPL 文件头、两份 README 写明"重写自哪个项目 + 日期"、
  随包分发 `LICENSE`、`说明.txt` 加许可与来源声明。
- ⚠️ **发布前必做**：把 `打包发布.js` 顶部的 `REPO_URL` 改成真实仓库地址再重新打包 ——
  GPL 第 6 条要求分发二进制时给出"对应源码"的获取方式，留占位符等于不合规（脚本会打印警告）。
- 另外 `..\f-keydle_v2.1\` 那份本地成品**绝对不要分发**（内含商业软件 Bandizip 及其编译产物）

## 改名：f-keydle 3.0 → layerpeel（2026-10-12，用户决定）

**为什么改**：GPL 那条已经用"按 GPL-3.0 发布衍生作品"解决了，**改名不是法律要求**。
改的是混淆风险 —— 原项目叫 f-keydle，同名发布容易被当成官方续作；而且这个重写版
后端/界面/构建链全换了，本来就是另一个东西了。名称 `layerpeel` 由用户选定。

**改了什么**（约 180 处、18 个文件）：产品名 / exe 名 / 启动器名 / 窗口标题 / 标题栏 / 状态栏 /
右键菜单文案 / 说明.txt / 两份 README / VERIFY.md / zip 名 / `namespace FKeydle` → `LayerPeel` /
`_fkeydle_tmp_` → `_layerpeel_tmp_` / `_fkeydle_trash_` → `_layerpeel_trash_`。

**绝对不能跟着改的（指的是原项目，GPL 要求保留）**：
- `ssh-buanshishi/f-keydle`（原仓库名）与它的完整 URL
- `..\f-keydle_v2.1\`（本地参考成品的目录名）
- `f-keydle 2.x` / `原项目 f-keydle` 这类表述
- `Core.cs` 里那 4 条配置标记线字符串（原作者的配置格式，改了就失去 2.1 迁移能力）
批量替换时必须先把这些串用哨兵保护起来，替换完再还原 —— 直接全局替换会毁掉来源声明。

**改名留下的兼容处理（别删）**：
- `MenuHelper.LegacyMenuKey = "fkeydle3"`：安装/卸载时一并清理旧注册表键，
  否则用户机器上残留一条点了没反应的旧右键菜单项。
- `SafeDelete.LegacyStagingName = "_fkeydle_trash_"`：`Undo` 会同时找新旧两个暂存目录名，
  否则改名后老暂存文件撤不回来。
- `Engine.InternalPrefixes = { "_layerpeel_", "_fkeydle_" }`：「清理残留」同时认新旧临时目录前缀。

**随包配置只发干净默认值**：`打包发布.js` 写进包里的 `config.json` 会把密码表强制成
`["123456"]`、`sevenZipPath` 留空 —— 不要把开发机上那套密码表随包发出去。
## 精简（2026-10-12，用户要求"去掉多余功能"）

- **删掉整套浏览器模式**（`Http.cs` / `ui\index.html` / `测界面.js` / `--ui` 开关 / `ui-url.txt`）：
  WPF 换上去之后它只是没人会用的备用界面，却占着 1300 行、一个监听端口和 token。
  右键菜单那段（`MenuHelper`）搬到独立的 `Menu.cs` 保留。exe 因此 187KB → 142KB。
- **配置从 20+ 个键砍到 6 个**（`config.json` 86 行 → 34 行）：maxIterations / 两个超时 /
  extractTempPrefix / flattenSingleRoot / deleteSourceAfterExtract / maxRatioBomb /
  `ui.*` 全部写死到代码里；`delete.mode` 去掉 `permanent`（危险且与回收站/暂存重复）；
  界面上「对账校验」开关也去掉了（恒定开启，没人该关）。
- **预览（dry-run）功能已按用户要求移除**（2026-10-12）：`Job.DryRun` / `--dry-run` / 界面上的「运行方式」下拉
  / `Stats.Planned` 全部删掉，`ProcessArchive`/`ProcessDir`/`FlattenOnly`/`Run`/`JunkCleaner.Run`
  /`Handle` 的 `dryRun` 参数一并去掉。**连带删掉 4 个只服务预览的事件**（`archive-plan`/`flatten-plan`/
  `clean-plan`/`leftover-plan`）及其处理器 —— 删 `if (dryRun)` 分支时要注意它们只在该分支里 Emit。
  现在点「开始」就是直接执行；想后悔靠**回收站 / 暂存目录**（未变）。
- ⚠️ **删 DEFAULTS 里的成员时小心尾逗号**：JSON 不允许尾逗号，删掉 `ui` 段之后 `safety` 后面
  多了一个 `,`，直接让 `Config.Load` 抛 `ArgumentException: 无效的 JSON 基元`，
  表现为**程序退出码 0xE0434352 且所有功能全崩**，很容易误判成引擎坏了。改完必须跑 `node 自测.js`。

## 界面视觉（2026-10-12 参考 ..\WPF界面模板\ 重做）

- **设计系统在 Theme.cs**：调色板（键名 B.*）+ 控件样式库，全部是一段运行时 XAML，
  用 XamlReader.Parse 加载。**改配色只改 Theme.cs 的 PALETTE 段**；WpfUi.cs 只通过
  Theme.B("B.Accent") / Theme.S("BtnPrimary") 取，不写死颜色。
  图标统一用 Segoe MDL2 Assets（Theme.Ico* 常量已列好码位）。
- **窗口结构**：无边框（WindowStyle=None + WindowChrome，CaptionHeight=44）自绘标题栏
  / 左侧 226px 导航 / 卡片化内容区（左 396px 控件列 + 右侧日志）/ 底部 28px 状态栏。
  最大化时要给根 Border 留 7px 边距，否则无边框窗口会顶出屏幕。
- **标题栏里的可交互控件必须 WindowChrome.IsHitTestVisibleInChrome = true**，否则点不到（被当拖动区）。
- ⚠️ **自检截图必须切软件渲染**：RenderOptions.ProcessRenderMode = SoftwareOnly，
  否则硬件加速窗口用 RenderTargetBitmap 抓会出残影/黑块。
- ⚠️ **根 Border 必须给不透明背景**（_chromeRoot.Background）：Window.Background 不在这个可视子树的
  绘制范围内，漏了的话抓出来是一片 **alpha=0 的透明区**，图里显示成纯白竖带，看起来像"渲染坏了"。
  排查手段：Bitmap.GetPixel(...).A 看 alpha，别看颜色猜。
- ⚠️ **验收截图时别信预览缩略图**：曾把"看着像重影"当成 bug 追了一轮，实际是预览图的合成假象；
  逐行扫亮色区段 + 采样像素才是判据。

## 铁律（2.1 踩过的坑，不许回退）

1. **判成败只认 7-Zip 退出码 + `7z t` 校验**。绝不允许再出现"靠界面文字/窗口焦点判断成功"的实现。
2. **解压必须先落同盘临时目录 `_layerpeel_tmp_*`，对账通过才并入目标目录**。
   实测 7z 用错密码也返回 2 并且已经把半成品写到磁盘上，直接解到目标目录会污染用户目录。
3. **删原包前必须确认包是好的**；校验不过只提示，不动原包。
4. **所有外部进程用参数数组启动**，绝不把配置内容拼进命令行（2.1 的 `del /f /s /q "用户输入"` 是提权命令注入）。
5. **默认不提权**（2.1 用 mshta/PowerShell `RunAs` 每次都要管理员）。
6. 每个包处理完必须写审计日志（`运行数据\日志\audit-*.jsonl`）。

## 本机环境坑（写代码时别踩）

- **没有 Python、没有 .NET SDK**。可用工具链只有 Node 24 / Edge / PowerShell 5.1 / 7-Zip。
- PowerShell 是 **5.1**（Desktop），不是 pwsh：没有 `??`、三元 `? :`、`&&`。
- `.ps1` 辅助脚本**正文只用 ASCII**（PS 5.1 按 ANSI 解析无 BOM 的 UTF-8 会乱码）；
  `.bat` 正文同理用 ASCII，中文都放到 GUI 里。**已踩过：acl-helper.ps1 的中文注释直接让脚本语法崩掉。**
- 用 `spawn` 起子进程时，若调用方是 DSH 的工具通道，**子进程会继承 stdout 句柄导致工具调用不返回** ——
  测试启动 GUI 时要 `-RedirectStandardOutput` 到文件。
- PS 5.1 的 `[int]` 是银行家舍入，算时间/索引用 `[math]::Floor`。

### PS 5.1 特有的两个坑（都实际踩过）

- **`Split-Path -LiteralPath X -Parent` 报 `Parameter set cannot be resolved using the specified named parameters`**
  —— PS 5.1 里 `-LiteralPath` 只属于 `-Resolve` 参数集。用 `[System.IO.Path]::GetDirectoryName()` 代替。
- **脚本参数名不要写成 `-B64` 和 `-B64Arg` 这种前缀关系**（PowerShell 前缀匹配会歧义）。用 `-Path64` / `-Extra64`。

### 权限操作（`Acl.cs`）

- **不要用 `icacls /save` + `icacls /restore` 做备份/还原**：实测 `/restore` 遇到调用者解析不了的 SID
  或需要 `SeRestorePrivilege` 的条目就整批失败，报 **exit 1300 / `Not all privileges or groups referenced are assigned to the caller`**。
  当前实现是逐项存 SDDL（`GetAccessControl().GetSecurityDescriptorSddlForm()`）+ `SetAccessControl` 写回，
  SID 不需要解析成名字。
- 授权用 **Everyone 的 SID `*S-1-1-0`**，不要用字面量 `Everyone`（非英文系统上名字不同）。
- 验证回滚正确性的方法：记录修复前的 `(Get-Acl $p).Sddl`，修复 → 还原 → 断言字符串完全相等
  （`自测.js` 第 5 组就是这么断言）。

### 打包与界面（都实际踩过）

- ⚠️ **`.bat` 必须 CRLF 换行 + 纯 ASCII 正文**。代码编辑工具的默认是 LF，而 **LF 的 .bat 会被 cmd
  逐行当命令执行**（连 `rem` 注释都报 `'xxx' is not recognized`）；中文正文在非 936 代码页下
  也会破坏解析。**别直接改那个 .bat，它由 `打包发布.js` 现生成**；脚本会校验格式并在不合格时中止。
- **打包前要确认发布目录里的实例已停**：否则进程占着文件，删除/覆盖会失败。
- 🔴 **界面是 WPF 原生窗口**。历史上曾用过浏览器界面，且 Edge 的 `--app` 独立窗口在本机会白屏
  （窗口起得来、服务端只收到一个 `GET /`、页面脚本一次都不执行、窗口标题显示成网址），
  普通标签页则正常 —— 那套浏览器界面已在 2026-10-12 的精简里整个删掉，别再往回调。
- **WPF 的三个坑（都实际踩过）**：
  1. **一个控件只能有一个父节点** —— 把「运行方式」「深度」这类控件共用给多个标签页会抛
     `InvalidOperationException：元素已经是另一个元素的子元素`。每个标签页各建一套（`TabCtl`）。
  2. **默认模板不吃 `ComboBox.Background`** —— 深色外观必须换 ControlTemplate。主题用
     `XamlReader.Parse` 在运行时解析 XAML（`Theme.cs`），**不需要 MSBuild / XAML 编译**。
  3. **验收界面别靠肉眼看截图猜** —— 用 `layerpeel.exe --ui-selftest _ui-selftest.png` 渲染成 PNG；
     要确认颜色就**采样像素**（`Bitmap.GetPixel`），别凭缩略图观感下结论
     （踩过：把 `#1F232D` 的深色输入框看成了白色，白折腾一轮）。
- **编译成 `winexe`**：双击不弹控制台；命令行模式靠 `AttachConsole(ATTACH_PARENT_PROCESS)` 接回父控制台，
  输出照常能被 `自测.js` 捕获。
- **参数解析要同时支持 `--key value` 和 `--key=value`**（踩过：只支持前者，导致 `--ui=browser` 静默失效、
  落到 WPF 分支去开窗口了）。
- **通用教训（浏览器时代留下的，仍然适用）**：判断 GUI"到底打开没有"不要看窗口是否存在、
  也不要用 `PrintWindow` 截图（硬件加速窗口截出来是空白，必误判）—— 要靠程序自己可验证的自检输出。

## 只有 C# 版（Node 版已按用户要求删除）

早期有一版 Node.js 实现，已删除（用户 2026-10-12 要求"只留 C# 版"）。
现在顶层就是 C# 工程，**不要再引入 Node 运行时** —— 用 Node 只是为了跑构建/测试脚本（本机有 Node 24）。

```
layerpeel 3.0\
├─ *.cs                 源码（10 个：Core / SevenZip / Engine / Junk / Acl / Menu / Jobs / Theme / WpfUi / Program）
├─ 构建.js / 打包发布.js / 自测.js
├─ config.json          配置源（打包时复制进绿色版并清空 7-Zip 路径）
├─ _测试样本\           测试固件
├─ 发布\                构建产物（exe + config.json，可直接跑的开发版）
└─ 绿色版\layerpeel\  最终交付物（约 2.6MB）
```

### C# 版专属坑（都实际踩过）

- **编译器是 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（.NET Framework 自带），只支持 C# 5**：
  不能写 `$""` 插值、`?.`、表达式体成员、自动属性初始化器。写错会直接编译失败。
- **csc 的输出目录必须先存在**，否则报 `CS1567 生成 Win32 资源时出错`（看着像资源问题，其实是目录没有）。
- **`/resource:` 的参数含空格时，在 PowerShell 里调用要整体加引号**，否则路径被空格切开。
- **`.NET Framework 没有 `ProcessStartInfo.ArgumentList`**，参数要自己按 Windows 规则加引号后再拼成 `Arguments`
  （`SevenZip.Quote()` 就是干这个的）。
- **权限备份不要用 `icacls /save` + `/restore`**：见上文 exit 1300。改成原生 .NET 存 SDDL。
- 🔴 **控制台输出是系统 OEM 代码页（936/GBK）**：别的程序（Node/Python）抓这个 exe 的 stdout 时，
  按 UTF-8 解会全是乱码、正则全部失配。要按 GBK 解（`自测.js` 里用 `new TextDecoder('gbk')`）。**踩过**。
- **构建脚本要把 `config.json` 复制进构建目录**：漏了的话 exe 退回默认配置（密码表为空），
  表现为"加密包一个都没解开"，很容易误判成引擎有 bug。**踩过**。
- 绿色包里 7-Zip 走"配置 → 程序目录 `7z.exe` → 常见安装位置"三级解析，所以随包自带就能独立跑。

## 发布链路（对外分发用）

- 面向陌生人的说明是 **`README.md`**（仓库首页与 Release 说明都用它）；开发笔记是 `DEVELOPMENT.md`。
- 分发 zip 放 `发布包\`，**不要放进 `绿色版\`**：`打包发布.js` 对整个 `绿色版\` 做 `rmSync`，
  zip 留在里面会以 `EPERM` 失败，报错看不出原因（2026-10-12 实测踩过）。
- 打包前确认 `绿色版\layerpeel\` 里的实例已停；跑过的实例会留下 `运行数据\`（日志、权限备份），
  这是运行时数据，不许进 zip、不许进仓库。
- 正确顺序：`node 构建.js` → `node 自测.js` → `node 打包发布.js`。`打包发布.js` 会**自动**重算
  `发布包\VERIFY.md` 的哈希、自动打 zip，所以别手动抄哈希（zip 本身没有确定性，跨次的哈希不能互相比）。
- 要核对界面：`发布\layerpeel.exe --ui-selftest _ui-selftest.png`（渲染成 PNG，无需人盯着屏幕）。

## 验证方式（改完必须跑）

```bat
node 构建.js     :: 编译
node 自测.js     :: 24 条断言：五功能 + 干扰项不被误删 + ACL 回滚的 SDDL 逐字节相同
node 打包发布.js :: 打包绿色版 + 自动重算 VERIFY.md + 打 zip
发布\layerpeel.exe --ui-selftest _ui-selftest.png   :: WPF 界面渲染成 PNG
```

`自测.js` 的判断标准（都是真实结果，不是"应该能跑"）：
三层套娃全部解出且压缩包清空、`报告.docx / 表格.xlsx / 假程序.exe / 普通文本.txt` 一个都不许动、
`推广链接.url` 被删而普通文件不误删、临时残留被清掉、权限修复后能还原成与修复前**逐字节相同**的 SDDL。
