'use strict';
/**
 * 打包绿色便携包：layerpeel.exe + 便携 7-Zip + 启动器 + 配置。
 * 目标机器不需要装 .NET SDK、不需要装 7-Zip。
 *
 *   node 打包发布.js
 */
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { spawnSync } = require('child_process');

const ROOT = __dirname;
const BUILD_EXE = path.join(ROOT, '发布', 'layerpeel.exe');
const OUT = path.join(ROOT, '绿色版', 'layerpeel');
const SEVENZIP_DIR = 'C:\\Program Files\\7-Zip';

// GPL 第 6 条要求：分发二进制时给出"对应源码"的获取方式。发布前把这里改成真实仓库地址。
const REPO_URL = 'https://github.com/orangtan/layerpeel';
const ORIGIN_URL = 'https://github.com/ssh-buanshishi/f-keydle______Multilayer-Compression--and--single-chained---Folder-Nesting--handler';

function sha(p) { return crypto.createHash('sha256').update(fs.readFileSync(p)).digest('hex'); }
function kb(n) { return (n / 1024).toFixed(1) + ' KB'; }

function dirSize(p) {
  let t = 0;
  const stack = [p];
  while (stack.length) {
    const cur = stack.pop();
    for (const e of fs.readdirSync(cur, { withFileTypes: true })) {
      const f = path.join(cur, e.name);
      if (e.isDirectory()) stack.push(f); else t += fs.statSync(f).size;
    }
  }
  return t;
}

function main() {
  if (!fs.existsSync(BUILD_EXE)) throw new Error('先构建：node 构建.js（找不到 ' + BUILD_EXE + '）');

  // 启动器直接在这里生成：正文必须 CRLF + 纯 ASCII
  // （LF 的 .bat 会被 cmd 逐行当命令执行；中文正文在非 936 代码页下会破坏解析）
  const batLines = [
    '@echo off',
    'rem layerpeel 3.0 launcher. Body must stay ASCII-only and CRLF-terminated.',
    'rem The UI is a native WPF window; this launcher only forwards dropped folder paths.',
    'setlocal',
    'cd /d "%~dp0"',
    '',
    'if not exist "%~dp0layerpeel.exe" (',
    '  echo.',
    '  echo [layerpeel 3.0] layerpeel.exe not found next to this .bat.',
    '  echo.',
    '  pause',
    '  exit /b 1',
    ')',
    '',
    'if "%~1"=="" (',
    '  start "" "%~dp0layerpeel.exe"',
    ') else (',
    '  start "" "%~dp0layerpeel.exe" --launcher-opens %*',
    ')',
    'exit /b 0',
  ];
  const bat = path.join(ROOT, '绿色版-启动器.bat');
  fs.writeFileSync(bat, batLines.join('\r\n') + '\r\n', 'ascii');
  const batBuf = fs.readFileSync(bat);
  const cr = (batBuf.toString('latin1').match(/\r/g) || []).length;
  const lf = (batBuf.toString('latin1').match(/\n/g) || []).length;
  const nonAscii = [...batBuf].filter((b) => b > 127).length;
  if (cr !== lf || nonAscii > 0) throw new Error('启动器格式不合法（CR=' + cr + ' LF=' + lf + ' 非ASCII=' + nonAscii + '）');

  fs.rmSync(OUT, { recursive: true, force: true });
  fs.mkdirSync(OUT, { recursive: true });

  // 1) 应用本体
  fs.copyFileSync(BUILD_EXE, path.join(OUT, 'layerpeel.exe'));

  // 2) 便携 7-Zip（官方 7z.exe + 7z.dll 一对即可独立运行）
  const parts = [];
  for (const f of ['7z.exe', '7z.dll']) {
    const src = path.join(SEVENZIP_DIR, f);
    if (!fs.existsSync(src)) throw new Error('找不到 ' + src);
    fs.copyFileSync(src, path.join(OUT, f));
    parts.push({ file: f, size: fs.statSync(src).size, sha256: sha(src) });
  }

  // 3) 配置（7-Zip 路径留空：程序会先找程序目录下的 7z.exe）
  const cfg = JSON.parse(fs.readFileSync(path.join(ROOT, 'config.json'), 'utf8'));
  cfg.backend = cfg.backend || {};
  cfg.backend.sevenZipPath = '';
  // 只发一份干净的默认配置：不把开发机上那套密码表随包发出去
  cfg.passwords = ['123456'];
  fs.writeFileSync(path.join(OUT, 'config.json'), JSON.stringify(cfg, null, 2) + '\n', 'utf8');

  // 4) 启动器
  fs.copyFileSync(bat, path.join(OUT, '启动 layerpeel 3.0.bat'));

  // 5) 说明与许可
  fs.writeFileSync(path.join(OUT, '说明.txt'), [
    'layerpeel 3.0（绿色便携版）',
    '',
    '双击「启动 layerpeel 3.0.bat」即可，会直接打开程序窗口。',
    '也可以把文件夹拖到那个 .bat 上，或者直接拖进程序窗口里（拖进窗口会拿到真实路径）。',
    '界面是原生 Windows 窗口，不需要浏览器、不监听任何端口。',
    '',
    '本目录内容：',
    '  layerpeel.exe            主程序（单文件，界面已内嵌）',
    '  7z.exe / 7z.dll         便携版 7-Zip（解压后端；程序优先用本目录这一份，',
    '                          目标机器没装 7-Zip 也能直接跑）',
    '  config.json             配置（密码表、删除策略、匹配规则等）',
    '  启动 layerpeel 3.0.bat    启动器（正文纯 ASCII + CRLF，请勿用编辑器另存）',
    '  LICENSE                 本程序的许可全文（GNU GPL v3.0）',
    '  运行数据\\               运行时产物：日志、权限备份，可以随时删',
    '',
    '依赖：Windows + .NET Framework 4.x（Win10 1809 起与 Win11 系统自带，无需另装）。',
    '',
    '── 本程序的许可（GNU GPL v3.0）──',
    'layerpeel 3.0 是自由软件，按 GNU General Public License v3.0（或更新版本）发布，',
    '不提供任何担保。许可全文见同目录的 LICENSE。',
    '',
    '本程序是 ssh-buanshishi / f-keydle（同样按 GPL-3.0 发布）的 C# 重写版：',
    '  自 2026-10-12 起用 C# 重新实现全部功能，解压后端由 Bandizip + AutoHotkey 换成 7-Zip。',
    '  原始项目：' + ORIGIN_URL,
    '  原始项目版权归原作者所有。',
    '',
    '完整对应源码（GPL 第 6 条要求）：',
    '  ' + REPO_URL,
    '',
    '── 第三方组件许可 ──',
    '7-Zip 版权归 Igor Pavlov 所有，按 GNU LGPL 发布（其中 unRAR 代码受额外限制）。',
    '本包原样分发了其官方二进制 7z.exe / 7z.dll，未做任何修改。',
    '来源：https://www.7-zip.org/   许可：https://www.7-zip.org/license.txt',
    '',
  ].join('\r\n'), 'utf8');

  // 5b) GPL 要求：二进制分发必须随附许可全文
  const licSrc = path.join(ROOT, 'LICENSE');
  if (!fs.existsSync(licSrc)) throw new Error('找不到 LICENSE，GPL 要求分发二进制时必须随附许可全文');
  fs.copyFileSync(licSrc, path.join(OUT, 'LICENSE'));

  // 6) 清单
  const files = fs.readdirSync(OUT).map((n) => {
    const f = path.join(OUT, n);
    return { name: n, size: fs.statSync(f).size };
  });
  fs.writeFileSync(path.join(OUT, '构建信息.json'), JSON.stringify({
    name: 'layerpeel 3.0',
    builtAt: new Date().toISOString(),
    runtime: '.NET Framework 4.x（系统自带）',
    sevenZip: parts,
    files: files.filter((f) => f.name !== '构建信息.json'),
  }, null, 2), 'utf8');

  const total = dirSize(OUT);
  console.log('发布目录：' + OUT);
  for (const f of files.sort((a, b) => b.size - a.size)) console.log('  ' + kb(f.size).padStart(10) + '  ' + f.name);
  console.log('─────────────────────────');
  console.log('总体积：' + kb(total) + '（' + (total / 1048576).toFixed(2) + ' MB）');
  console.log('启动器校验：CRLF + 纯 ASCII 通过');

  // 7) 打成分发用 zip（README.md 里承诺的 layerpeel-<版本>-portable.zip）
  //    放在 发布包\ —— 与投放指南的目录约定一致（exe/zip 走 Release 附件，不进仓库）
  const version = (JSON.parse(fs.readFileSync(path.join(ROOT, 'config.json'), 'utf8')).version || '3.0.0');
  const pkgDir = path.join(ROOT, '发布包');
  fs.mkdirSync(pkgDir, { recursive: true });
  // Release 说明用公开版 README，一并放过去，免得两份走偏
  const pubReadme = path.join(ROOT, 'README.md');   // 仓库首页 = 公开版说明
  if (fs.existsSync(pubReadme)) fs.copyFileSync(pubReadme, path.join(pkgDir, 'README.md'));
  const zipPath = path.join(pkgDir, `layerpeel-${version}-portable.zip`);
  try {
    fs.rmSync(zipPath, { force: true });
    const sevenZipExe = path.join(SEVENZIP_DIR, '7z.exe');
    const r = spawnSync(sevenZipExe, ['a', '-tzip', '-mx=9', zipPath, path.basename(OUT)],
      { cwd: path.dirname(OUT), encoding: 'utf8', windowsHide: true });
    if (r.status !== 0) throw new Error((r.stdout || '') + (r.stderr || ''));
    console.log('分发 zip：' + zipPath + '（' + kb(fs.statSync(zipPath).size) + '）');
  } catch (e) {
    console.error('× 打 zip 失败（不影响绿色目录）：' + e.message);
  }

  // 8) 生成 VERIFY.md（哈希必须在最后一次打包之后生成，手动抄必然过期 —— 所以自动化）
  try {
    if (fs.existsSync(zipPath)) {
      const zipSize = fs.statSync(zipPath).size;
      const zipSha = crypto.createHash('sha256').update(fs.readFileSync(zipPath)).digest('hex');
      const exeSha = crypto.createHash('sha256').update(fs.readFileSync(path.join(OUT, 'layerpeel.exe'))).digest('hex');
      const rows = files.filter((f) => f.name !== '构建信息.json')
        .sort((a, b) => b.size - a.size)
        .map((f) => '| `' + f.name + '` | ' + f.size.toLocaleString('en-US') + ' |');
      const md = [
        '# 完整性校验 / Verify',
        '',
        '发布包：`' + path.basename(zipPath) + '`',
        '',
        '| 项目 | 值 |',
        '|---|---|',
        '| 压缩包大小 | ' + zipSize.toLocaleString('en-US') + ' 字节（' + (zipSize / 1048576).toFixed(2) + ' MB） |',
        '| 解压后总大小 | ' + total.toLocaleString('en-US') + ' 字节（' + (total / 1048576).toFixed(2) + ' MB） |',
        '| **SHA-256** | `' + zipSha + '` |',
        '',
        '校验方法（Windows 自带，不用装东西）：',
        '',
        '```bat',
        'certutil -hashfile ' + path.basename(zipPath) + ' SHA256',
        '```',
        '',
        '输出的哈希与上表一致，说明文件在下载过程中没被改动。',
        '',
        '包内 `layerpeel.exe` 的 SHA-256：`' + exeSha + '`',
        '（与从源码 `node 构建.js` 编出来的那份逐字节相同。）',
        '',
        '## 包内文件清单',
        '',
        '| 文件 | 字节 |',
        '|---|---|',
      ].concat(rows).concat([
        '',
        '`运行数据\\` 目录是程序首次运行时自己创建的，发布包里没有这一项。',
        '',
        '## 许可',
        '',
        '本程序按 **GNU GPL v3.0** 发布，包内附有 `LICENSE` 全文。',
        '它是 [ssh-buanshishi / f-keydle](' + ORIGIN_URL + ') 的 C# 重写版，自 2026-10-12 起。',
        '对应源码：' + REPO_URL,
        '',
        '## 更彻底的验证：自己编译',
        '',
        '不想信任预编译的 exe，可以从源码自己构建（结果与发布包功能一致）：',
        '',
        '```bat',
        'node 构建.js       :: 编译 → 发布\\layerpeel.exe',
        'node 自测.js       :: 24 条断言，全过才算正常',
        'node 打包发布.js   :: 打包 → 绿色版\\layerpeel 3.0\\',
        '```',
        '',
        '需要 Windows + Node.js + .NET Framework 4.8（系统自带）；编译测试样本还需要',
        '`C:\\Program Files\\7-Zip\\7z.exe`。不需要 .NET SDK，没有任何 NuGet 依赖。',
        '',
      ]).join('\r\n');
      fs.writeFileSync(path.join(pkgDir, 'VERIFY.md'), md, 'utf8');
      console.log('校验说明：' + path.join(pkgDir, 'VERIFY.md') + '（已按本次打包重算哈希）');
    }
  } catch (e) {
    console.error('× 生成 VERIFY.md 失败：' + e.message);
  }

  // GPL 合规自检：仓库地址还留着占位符就没法对外发（GPL 第 6 条要求给出对应源码位置）
  if (REPO_URL.indexOf('<') >= 0) {
    console.log('');
    console.log('⚠️  发布前必做：把 打包发布.js 顶部的 REPO_URL 改成你的真实仓库地址，然后重新打包。');
    console.log('   现在说明.txt 里写的是占位符：' + REPO_URL);
    console.log('   GPL 要求分发二进制时给出对应源码的获取方式，留占位符等于不合规。');
  }
  return 0;
}

if (require.main === module) {
  try { process.exit(main()); } catch (e) { console.error('打包失败：' + e.message); process.exit(1); }
}
