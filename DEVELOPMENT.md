# layerpeel 3.0 — C# 版

Node.js 版的重写版。功能完全等价，但**体积从 88.7MB 降到 2.5MB**。

## 为什么能小这么多

Node 版 88.7MB 里有 88.5MB 是 `node.exe`，程序自己只有 166KB。
C# 版改用 **Windows 自带的 .NET Framework 4.8** 当运行时，不需要随包带运行时。

| | Node 版 | C# 版 |
|---|---|---|
| 主程序 | node.exe 88.5MB + 脚本 166KB | **layerpeel.exe 121.5 KB** |
| 解压后端 | 用系统已装的 7-Zip | **随包便携 7-Zip 2.4MB** |
| 合计 | 88.7 MB | **2.49 MB** |

## 怎么构建

```bat
node 构建.js        :: 编译（用系统自带的 csc.exe，不需要装 .NET SDK）
node 打包发布.js    :: 打绿色便携包 → 绿色版\layerpeel 3.0\
node 自测.js        :: 跑同一套样本，用断言核对五个功能的真实结果
```

编译器是 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`（.NET Framework 自带）。
它**只支持 C# 5**：不能写字符串插值 `$""`、`?.`、表达式体成员、自动属性初始化器。

界面是 **WPF 原生窗口**（纯 C# 写，不用 XAML 编译 / MSBuild / SDK）。WPF 属于 .NET Framework 自带组件，
**不随包分发**，所以换成它之后绿色包只大了几十 KB。

离线验收界面（不需要人盯着屏幕）：

```bat
发布\layerpeel.exe --ui-selftest _ui-selftest.png   :: 把窗口渲染成 PNG 后退出
```

## 目录

```
layerpeel 3.0\
├─ 构建.js              编译脚本（产出 发布\layerpeel.exe）
├─ 打包发布.js          打包绿色版（自带便携 7-Zip + 校验启动器 + 自动算 VERIFY.md 哈希）
├─ 自测.js              功能自测（24 条断言）
├─ config.json          配置（打包时复制进绿色版，7-Zip 路径会清空）
├─ Core.cs              JSON / 配置 / 审计日志 / 安全删除（回收站 + 暂存目录）
├─ SevenZip.cs          7-Zip 封装：魔数粗筛 / 探测 / 试密码 / t 校验 / 解压
├─ Engine.cs            核心引擎：解压去套娃 / 仅去套娃 / 清理残留 + 对账
├─ Junk.cs              删除指定文件（glob 规则 / 保护压缩包 / 拒绝通配一切）
├─ Acl.cs               权限备份 / 修复 / 还原（原生 .NET 存 SDDL）
├─ Jobs.cs              任务执行层（界面与命令行共用）
├─ Theme.cs             WPF 深色主题（运行时 XamlReader 解析）
├─ WpfUi.cs             WPF 原生窗口界面（默认）
├─ Menu.cs              资源管理器右键菜单（HKCU）
├─ Program.cs           入口：命令行 + 界面模式 + 启动器协议
├─ _测试样本\           测试固件（建样本.js 生成三层套娃等）
├─ 发布\                构建产物（开发版，可直接跑）
└─ 绿色版\layerpeel 3.0\  最终交付物
```



## 几个关键设计（都是这台机器上踩出来的）

- **界面是 WPF 原生窗口**：不监听端口、不依赖浏览器、拖放能拿到真实路径。视觉参考工作区的 `WPF界面模板`（无边框标题栏 + 左侧导航 + 卡片化 + 统一调色板，见 `Theme.cs`）。
  那套浏览器界面已经在精简里整个删掉，别再往回调。

- **权限备份不用 `icacls /save|/restore`**：后者遇到调用者解析不了的 SID 会整批失败（exit 1300）。改成原生 .NET 逐项存 SDDL + `SetAccessControl`。
- **回收站用 `SHFileOperation` + `FOF_ALLOWUNDO`**：并保留"超过回收站配额就改走暂存目录"的保护（Windows 在这种情况下会静默永久删除）。
- **编译成 `winexe`**：双击不弹控制台；命令行模式下用 `AttachConsole(ATTACH_PARENT_PROCESS)` 接回父控制台，输出照常可见。
- **WPF 里一个控件只能有一个父节点**：「深度」这类选项在**每个任务页各建一套**（`Page.Options`），共用同一个控件会抛 `InvalidOperationException`。
- **WPF 默认模板不吃 `ComboBox.Background`**：深色外观必须换模板。主题用 `XamlReader.Parse` 在运行时解析一段 XAML，不需要 MSBuild。
- **`.bat` 必须 CRLF + 纯 ASCII**（由脚本生成，打包时校验）。
- **控制台输出走系统 OEM 代码页（936/GBK）**。用别的程序抓 exe 的输出时要按 GBK 解，按 UTF-8 解会全是乱码（`自测.js` 里就是这么处理的）。

## 7-Zip 路径解析顺序

1. `config.json` 里 `backend.sevenZipPath` 指向的位置
2. **程序目录下的 `7z.exe`（绿色包自带这一份）**
3. `C:\Program Files\7-Zip\7z.exe` / `Program Files (x86)`

所以绿色包不依赖目标机器装过 7-Zip，用户装了你也能复用。

## 许可

**GPL-3.0（或更新版本）**，全文见 `LICENSE`。

本项目是 **ssh-buanshishi / f-keydle**（GPL-3.0）的 C# 重写版，**自 2026-10-12 起**。
之所以是 GPL 而不是 MIT：我们读过原项目源码，并沿用了它配置格式的标记行字符串与受保护扩展名清单，
主张"完全独立实现"不成立，因此按 GPL 第 5 条以同协议发布（`Core.cs` 的 `MigrateLegacy` 是唯一
逐字保留原版字符串的地方，为的是兼容 2.x 的配置文件）。

## 第三方组件

随包分发 **7-Zip** 的官方二进制 `7z.exe` / `7z.dll`（未做任何修改）。
版权归 Igor Pavlov，按 GNU LGPL 发布（unRAR 部分另有额外限制）。
来源 <https://www.7-zip.org/>，许可 <https://www.7-zip.org/license.txt>。
