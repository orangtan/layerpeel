'use strict';
/**
 * 发一个新版本：改版本号 → 构建 → 自测 → 打包 → 生成 Release 文案。
 *
 *   node 发布新版本.js 3.0.1              :: 只做到准备好（不改 git）
 *   node 发布新版本.js 3.0.1 --push       :: 再 git commit + push + 打 tag
 *
 * 之所以要有这个脚本：Release 文案里要贴 zip 的 SHA-256，而 zip 每次打包都不一样，
 * 手抄必然出错（抄早了、抄错了、或者打包后又改了代码）。这里统一在一处生成。
 *
 * 版本号的唯一来源是 config.json 的 version 字段：
 *   - zip 名、VERIFY.md、Release 文案 都从它派生
 *   - 界面上的版本号（状态栏）也是运行时读 config.json
 * 所以改版本只需要改这一个地方。
 */
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { spawnSync } = require('child_process');

const ROOT = __dirname;
const CONFIG = path.join(ROOT, 'config.json');
const TEMPLATE = path.join(ROOT, '发布说明模板.md');
const PKG_DIR = path.join(ROOT, '发布包');
const VERIFY = path.join(PKG_DIR, 'VERIFY.md');
const OUT_TEXT = path.join(PKG_DIR, 'Release-说明.md');

function die(msg) { console.error('× ' + msg); process.exit(1); }

function step(title, script) {
  console.log('');
  console.log('══ ' + title + ' ══');
  // stdio: inherit —— 子进程直接写终端，输出实时可见
  const r = spawnSync(process.execPath, [path.join(ROOT, script)], { cwd: ROOT, stdio: 'inherit' });
  if (r.status !== 0) die(title + ' 失败（退出码 ' + r.status + '）—— 已中止，版本号保持原样');
}

function main() {
  const ver = (process.argv[2] || '').trim().replace(/^v/i, '');
  if (!/^\d+\.\d+\.\d+$/.test(ver)) die('用法：node 发布新版本.js <版本号>  例如 node 发布新版本.js 3.0.1');
  const doPush = process.argv.indexOf('--push') >= 0;

  // 1) 改版本号：只替换 config.json 里的 version，保留其余格式，diff 最小
  const raw = fs.readFileSync(CONFIG, 'utf8');
  const oldVer = (raw.match(/"version"\s*:\s*"([^"]+)"/) || [])[1];
  if (oldVer === ver) {
    console.log('版本号已经是 ' + ver + '，跳过改写');
  } else {
    fs.writeFileSync(CONFIG, raw.replace(/("version"\s*:\s*")([^"]+)(")/, '$1' + ver + '$3'), 'utf8');
    console.log('版本号：' + (oldVer || '(无)') + ' → ' + ver);
  }

  // 2) 构建 → 自测 → 打包（任何一步失败都中止）
  step('构建', '构建.js');
  step('自测', '自测.js');
  step('打包', '打包发布.js');

  // 3) 从刚生成的 VERIFY.md 取真实数字，填进 Release 文案
  if (!fs.existsSync(VERIFY)) die('找不到 ' + VERIFY + '（打包脚本应该会生成它）');
  const v = fs.readFileSync(VERIFY, 'utf8');
  const pick = (re, name) => {
    const m = v.match(re);
    if (!m) die('从 VERIFY.md 里读不到「' + name + '」，格式可能变了');
    return m[1];
  };
  const sha = pick(/SHA-256.*?\|\s*`([0-9a-f]{64})`/, 'SHA-256');
  const zipBytes = pick(/压缩包大小 \| ([\d,]+) 字节/, '压缩包大小');
  const outBytes = pick(/解压后总大小 \| ([\d,]+) 字节/, '解压后总大小');
  const zipName = 'layerpeel-' + ver + '-portable.zip';
  const zipPath = path.join(PKG_DIR, zipName);
  if (!fs.existsSync(zipPath)) die('找不到打包出来的 ' + zipPath);

  // 4) 自己算一遍哈希，和 VERIFY.md 对一下 —— 不一致就别发
  const realSha = crypto.createHash('sha256').update(fs.readFileSync(zipPath)).digest('hex');
  if (realSha !== sha) die('VERIFY.md 的哈希与 zip 实际不一致！\n  VERIFY: ' + sha + '\n  实际  : ' + realSha);

  if (!fs.existsSync(TEMPLATE)) die('找不到发布说明模板 ' + TEMPLATE);
  const text = fs.readFileSync(TEMPLATE, 'utf8')
    .replace(/\{\{VERSION\}\}/g, ver)
    .replace(/\{\{ZIP\}\}/g, zipName)
    .replace(/\{\{SHA\}\}/g, sha)
    .replace(/\{\{ZIP_BYTES\}\}/g, zipBytes)
    .replace(/\{\{OUT_BYTES\}\}/g, outBytes);
  fs.writeFileSync(OUT_TEXT, text, 'utf8');

  console.log('');
  console.log('══════════════════════════════════════');
  console.log('  版本号      ' + ver);
  console.log('  附件        ' + zipName + '（' + (fs.statSync(zipPath).size / 1024).toFixed(1) + ' KB）');
  console.log('  SHA-256     ' + sha);
  console.log('  Release 文案 ' + OUT_TEXT);
  console.log('══════════════════════════════════════');

  // 5) 可选：提交并推送 + 打 tag
  if (doPush) {
    const git = (args, ignoreFail) => {
      const r = spawnSync('git', args, { cwd: ROOT, stdio: 'inherit' });
      if (r.status !== 0 && !ignoreFail) die('git ' + args.join(' ') + ' 失败');
      return r.status;
    };
    console.log('');
    console.log('══ 提交并推送 ══');
    git(['add', '-A']);
    git(['commit', '-m', 'release: v' + ver]);
    git(['push']);
    git(['tag', '-a', 'v' + ver, '-m', 'layerpeel ' + ver], true);   // 已存在就忽略
    git(['push', 'origin', 'v' + ver], true);
    console.log('');
    console.log('已推送。接下来去 GitHub 建 Release：');
    console.log('  tag 选 v' + ver + '，正文粘 ' + path.join('发布包', 'Release-说明.md') + ' 的内容，');
    console.log('  附加上传 ' + zipName);
  } else {
    console.log('');
    console.log('下一步：');
    console.log('  1. 翻一下 发布包\\Release-说明.md 有没有要改的');
    console.log('  2. node 发布新版本.js ' + ver + ' --push     （或自己 git add/commit/push）');
    console.log('  3. 去 GitHub 建 Release：tag v' + ver + '，正文粘 Release-说明.md，附加上传 ' + zipName);
  }
  return 0;
}

process.exit(main());
