'use strict';
/**
 * layerpeel 3.0 自测：拿同一套测试样本依次跑五个功能，断言文件系统的真实结果。
 *
 *   node 自测.js
 *
 * 退出码 0 = 全过。任何一条断言失败会打印出来并返回 1。
 */
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const ROOT = __dirname;
const EXE = path.join(ROOT, '发布', 'layerpeel.exe');
const WORK = path.join(ROOT, '_自测');

let pass = 0, fail = 0;
function check(name, cond, extra) {
  if (cond) { pass++; console.log('  ✔ ' + name); }
  else { fail++; console.log('  ✘ ' + name + (extra ? '  —— ' + extra : '')); }
}

/**
 * 运行 exe 并拿回输出。
 * 注意：控制台程序在中文 Windows 上把 stdout 按 OEM 代码页（936/GBK）写出，
 * 直接按 UTF-8 解会得到乱码、正则全都匹配不上。所以这里按字节收，再逐个编码试。
 */
function run(args) {
  const r = spawnSync(EXE, args, { windowsHide: true });
  const buf = Buffer.concat([r.stdout || Buffer.alloc(0), r.stderr || Buffer.alloc(0)]);
  let text = '';
  for (const enc of ['gbk', 'utf-8']) {
    try {
      const t = new TextDecoder(enc).decode(buf);
      if (t.indexOf('识别到压缩包') >= 0 || t.indexOf('汇总') >= 0 || enc === 'gbk') { text = t; if (t.indexOf('识别到压缩包') >= 0 || t.indexOf('汇总') >= 0) break; }
    } catch (_) { /* 该编码不可用时跳过 */ }
  }
  return { code: r.status, out: text };
}

function copyDir(from, to) {
  fs.rmSync(to, { recursive: true, force: true });
  fs.cpSync(from, to, { recursive: true });
}

function names(dir) {
  return fs.readdirSync(dir).sort();
}

function sddl(p) {
  const r = spawnSync('powershell.exe', ['-NoProfile', '-Command', '(Get-Acl -LiteralPath "' + p.replace(/"/g, '""') + '").Sddl'], { encoding: 'utf8', windowsHide: true });
  return (r.stdout || '').trim();
}

function main() {
  if (!fs.existsSync(EXE)) { console.error('先构建：node 构建.js'); process.exit(1); }

  fs.rmSync(WORK, { recursive: true, force: true });
  fs.mkdirSync(WORK, { recursive: true });

  console.log('=== 造样本 ===');
  const built = spawnSync(process.execPath, [path.join(ROOT, '_测试样本', '建样本.js')], { encoding: 'utf8', windowsHide: true });
  if (built.status !== 0) { console.error(built.stdout, built.stderr); process.exit(1); }

  const dir = path.join(WORK, '场景1');
  copyDir(path.join(ROOT, '_测试样本', '场景1'), dir);
  fs.writeFileSync(path.join(dir, '推广链接.url'), 'x');
  fs.mkdirSync(path.join(dir, '_layerpeel_tmp_9999_9'), { recursive: true });
  fs.writeFileSync(path.join(dir, '_layerpeel_tmp_9999_9', 'j.bin'), 'z'.repeat(500));
  fs.mkdirSync(path.join(dir, '套娃', 'A', 'B', 'C'), { recursive: true });
  fs.writeFileSync(path.join(dir, '套娃', 'A', 'B', 'C', '深.txt'), 'deep');

  // ── 1. 解压去套娃 ──
  console.log('\n=== 1. 解压去套娃 ===');
  let r = run([dir]);
  check('返回码 0', r.code === 0, 'exit=' + r.code);
  check('识别到 7 个压缩包', /识别到压缩包\s+7/.test(r.out), r.out.match(/识别到压缩包.*/));
  check('成功解压 7 个', /成功解压\s+7/.test(r.out), r.out.match(/成功解压.*/));
  check('原包全部进回收站', /回收站 7/.test(r.out), r.out.match(/原包处理.*/));
  const afterExtract = names(dir);
  check('最内层内容已解出（深文本.txt）', afterExtract.includes('深文本.txt'));
  check('干扰项 报告.docx 未被动', afterExtract.includes('报告.docx'));
  check('干扰项 表格.xlsx 未被动', afterExtract.includes('表格.xlsx'));
  check('干扰项 假程序.exe 未被动', afterExtract.includes('假程序.exe'));
  check('干扰项 普通文本.txt 未被动', afterExtract.includes('普通文本.txt'));
  check('压缩包已清空（无 .zip/.7z/.mp4 残留）',
    !afterExtract.some((n) => /\.(zip|7z|rar|mp4)$/i.test(n)), afterExtract.join(','));

  // ── 2. 仅去套娃 ──
  console.log('\n=== 2. 仅去套娃 ===');
  r = run(['--flatten', path.join(dir, '套娃')]);
  check('返回码 0', r.code === 0, 'exit=' + r.code);
  check('套娃层级被压平（深.txt 上提到 套娃\\）', fs.existsSync(path.join(dir, '套娃', '深.txt')));
  check('空壳目录已移除', !fs.existsSync(path.join(dir, '套娃', 'A')));

  // ── 3. 删除指定文件 ──
  console.log('\n=== 3. 删除指定文件 ===');
  r = run(['--clean', dir]);
  check('返回码 0', r.code === 0, 'exit=' + r.code);
  check('推广链接.url 已删', !fs.existsSync(path.join(dir, '推广链接.url')));
  check('正常文件未被误删（普通文本.txt 还在）', fs.existsSync(path.join(dir, '普通文本.txt')));

  // ── 4. 清理残留 ──
  console.log('\n=== 4. 清理残留 ===');
  r = run(['--cleanup', dir]);
  check('返回码 0', r.code === 0, 'exit=' + r.code);
  check('临时残留目录已清掉', !fs.existsSync(path.join(dir, '_layerpeel_tmp_9999_9')));

  // ── 5. 权限修复 + 回滚 ──
  console.log('\n=== 5. 权限修复与回滚 ===');
  const aclDir = path.join(dir, '套娃');
  const before = sddl(aclDir);
  r = run(['--fix-acl', aclDir]);
  check('返回码 0', r.code === 0, 'exit=' + r.code);
  const after = sddl(aclDir);
  check('ACL 确实被改动', before !== '' && before !== after);
  check('授权用的是 SID（S-1-1-0）', after.indexOf('WD') >= 0 || after.indexOf('S-1-1-0') >= 0, after);

  const backups = fs.readdirSync(path.join(WORK, '..', '发布', '运行数据', '权限备份')).filter((f) => f.endsWith('.json') && !f.endsWith('.meta.json'));
  check('生成了权限备份', backups.length > 0);
  if (backups.length) {
    const bf = path.join(WORK, '..', '发布', '运行数据', '权限备份', backups.sort().reverse()[0]);
    r = run(['--restore-acl', bf]);
    check('还原返回 0', r.code === 0, 'exit=' + r.code);
    check('还原后 ACL 与修复前逐字节相同', sddl(aclDir) === before, 'before=' + before + '\n    after=' + sddl(aclDir));
  }

  console.log('\n════════════════════════════');
  console.log('通过 ' + pass + ' 项，失败 ' + fail + ' 项');
  console.log('════════════════════════════');
  return fail === 0 ? 0 : 1;
}

if (require.main === module) process.exit(main());
