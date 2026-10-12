'use strict';
/**
 * 构建 C# 版：用系统自带的 .NET Framework 编译器（csc.exe）编译成单个小 exe。
 * 不需要装 .NET SDK、不需要 NuGet。
 *
 *   node 构建.js
 */
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const ROOT = __dirname;
const OUT_DIR = path.join(ROOT, '发布');
const OUT_EXE = path.join(OUT_DIR, 'layerpeel.exe');

const CSC_CANDIDATES = [
  'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe',
  'C:\\Windows\\Microsoft.NET\\Framework\\v4.0.30319\\csc.exe',
];

/** 编进去的引用（都是 .NET Framework 自带的） */
const FW_DIR = 'C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319';
const REFS = [
  'System.dll',
  'System.Core.dll',
  'System.Web.Extensions.dll',   // JavaScriptSerializer（JSON）
  'System.Drawing.dll',          // 文件夹选择框用到
  'System.Windows.Forms.dll',    // 文件夹选择框
  'System.Xaml.dll',             // WPF
  // WPF 的几个程序集在 Framework 目录的 WPF 子目录里，要用全路径
  FW_DIR + '\\WPF\\PresentationFramework.dll',
  FW_DIR + '\\WPF\\PresentationCore.dll',
  FW_DIR + '\\WPF\\WindowsBase.dll',
];

const SOURCES = [
  'Core.cs',
  'SevenZip.cs',
  'Engine.cs',
  'Junk.cs',
  'Acl.cs',
  'Menu.cs',
  'Jobs.cs',
  'Theme.cs',
  'WpfUi.cs',
  'Program.cs',
];

function findCsc() {
  for (const p of CSC_CANDIDATES) if (fs.existsSync(p)) return p;
  throw new Error('找不到 csc.exe（.NET Framework 4.x 应该自带）');
}

function main() {
  const csc = findCsc();
  const missing = SOURCES.filter((f) => !fs.existsSync(path.join(ROOT, f)));
  if (missing.length) throw new Error('缺少源文件：' + missing.join(', '));

  fs.mkdirSync(OUT_DIR, { recursive: true });

  const args = [
    '/nologo',
    '/target:winexe',        // GUI 程序：双击不弹控制台；命令行模式靠 AttachConsole 接回父控制台
    '/platform:anycpu',
    '/optimize+',
    '/warn:4',
    '/out:' + OUT_EXE,
  ];
  for (const r of REFS) args.push('/r:' + r);
  for (const s of SOURCES) args.push(path.join(ROOT, s));

  console.log('编译器：' + csc);
  const r = spawnSync(csc, args, { encoding: 'utf8', windowsHide: true });
  if (r.stdout) process.stdout.write(r.stdout);
  if (r.stderr) process.stderr.write(r.stderr);
  if (r.status !== 0) {
    console.error('编译失败，退出码 ' + r.status);
    process.exit(1);
  }

  const size = fs.statSync(OUT_EXE).size;
  console.log('编译成功：' + OUT_EXE);
  console.log('exe 大小：' + size.toLocaleString() + ' 字节（' + (size / 1024).toFixed(1) + ' KB）');

  // 把配置放到构建目录旁边，这样 发布\ 本身就是一个能直接跑的开发版。
  // 漏掉这一步 exe 会退回默认配置（密码表是空的），表现为"加密包全都没被解开"—— 踩过。
  const cfgSrc = path.join(ROOT, 'config.json');
  if (fs.existsSync(cfgSrc)) {
    fs.copyFileSync(cfgSrc, path.join(OUT_DIR, 'config.json'));
    console.log('已复制 config.json 到构建目录');
  }
  return 0;
}

if (require.main === module) {
  try { process.exit(main()); } catch (e) { console.error('构建失败：' + e.message); process.exit(1); }
}

module.exports = { main, OUT_EXE, OUT_DIR };
