'use strict';
// 造测试样本：真实的多层压缩 + 文件夹套娃 + 加密包 + 伪装包 + 应当跳过的文件
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const SEVENZ = 'C:\\Program Files\\7-Zip\\7z.exe';
const ROOT = __dirname;
const STAGE = path.join(ROOT, '_stage');
const CASE = path.join(ROOT, '场景1');

function sh(...args) {
  const r = spawnSync(SEVENZ, args, { encoding: 'utf8', windowsHide: true });
  if (r.status !== 0) {
    console.error('7z 失败：', args.join(' '), r.stdout, r.stderr);
    process.exit(1);
  }
}
function mk(p, content) {
  fs.mkdirSync(path.dirname(p), { recursive: true });
  fs.writeFileSync(p, content, 'utf8');
}

fs.rmSync(STAGE, { recursive: true, force: true });
fs.rmSync(CASE, { recursive: true, force: true });
fs.mkdirSync(STAGE, { recursive: true });
fs.mkdirSync(CASE, { recursive: true });

// ── 第一层：最里面的内容 ──
mk(path.join(STAGE, 'L3', '深文本.txt'), '第三层的内容 ' + 'A'.repeat(3000) + '\n');
mk(path.join(STAGE, 'L3', '再来一个.txt'), 'hello ' + 'B'.repeat(500) + '\n');
// 最里面还塞一个压缩包 → 测"多层压缩"
mk(path.join(STAGE, 'L3', '最内层.zip.src'), 'x');
sh('a', '-tzip', '-p999888', path.join(STAGE, 'L3', '最内层.zip'), path.join(STAGE, 'L3', '深文本.txt'), '-bso0', '-bsp0');

// ── 第二层：加密 7z（头部加密） ──
mk(path.join(STAGE, 'L2', '说明.txt'), '第二层说明\n');
fs.copyFileSync(path.join(STAGE, 'L3', '最内层.zip'), path.join(STAGE, 'L2', '最内层.zip'));
fs.copyFileSync(path.join(STAGE, 'L3', '再来一个.txt'), path.join(STAGE, 'L2', '再来一个.txt'));
sh('a', '-t7z', '-pabc', '-mhe=on', path.join(STAGE, 'L2.7z'), path.join(STAGE, 'L2', '*'), '-bso0', '-bsp0');

// ── 最外层：加密 zip，里面同时有压缩包和垃圾文件 → 套娃 + 多层 ──
mk(path.join(STAGE, 'L1', '已阅.txt'), '外层说明\n');
mk(path.join(STAGE, 'L1', '推广链接.url'), '[InternetShortcut]\nURL=http://example.com\n');
mk(path.join(STAGE, 'L1', '福利推广.txt'), '扫码关注\n');
fs.copyFileSync(path.join(STAGE, 'L2.7z'), path.join(STAGE, 'L1', '内层.7z'));
sh('a', '-tzip', '-p123456', '-mem=AES256', path.join(CASE, '外层.zip'), path.join(STAGE, 'L1', '*'), '-bso0', '-bsp0');

// ── 独立无密码包（内含一个文件夹套娃） ──
mk(path.join(STAGE, 'P1', '唯一内容.txt'), '无密码包的内容\n');
sh('a', '-tzip', path.join(CASE, '独立包.zip'), path.join(STAGE, 'P1', '*'), '-bso0', '-bsp0');

// ── 伪装扩展名的包（内容其实是 zip） ──
fs.copyFileSync(path.join(CASE, '独立包.zip'), path.join(CASE, '伪装成视频.mp4'));

// ── 应当被跳过的：office 文档 / 假 exe / 普通文本 ──
const docxStage = path.join(STAGE, 'docx');
mkdirDocx(docxStage);
sh('a', '-tzip', path.join(CASE, '报告.docx'), path.join(docxStage, '*'), '-bso0', '-bsp0');
function mkdirDocx(d) {
  mk(path.join(d, '[Content_Types].xml'), '<?xml version="1.0"?><Types/>');
  mk(path.join(d, 'word', 'document.xml'), '<w/>');
}
sh('a', '-tzip', path.join(CASE, '表格.xlsx'), path.join(docxStage, '*'), '-bso0', '-bsp0');
fs.writeFileSync(path.join(CASE, '假程序.exe'), Buffer.concat([Buffer.from([0x4d, 0x5a]), Buffer.alloc(2048, 0)]));
mk(path.join(CASE, '普通文本.txt'), '这不是压缩包\n');

// ── 分卷包 ──
sh('a', '-tzip', '-v20k', path.join(STAGE, '分卷.zip'), path.join(STAGE, 'L3', '*'), '-bso0', '-bsp0');
for (const f of fs.readdirSync(STAGE)) {
  if (/^分卷\.zip\.\d{3}$/.test(f)) fs.copyFileSync(path.join(STAGE, f), path.join(CASE, f));
}

console.log('样本已生成 →', CASE);
console.log(fs.readdirSync(CASE).join('\n'));
