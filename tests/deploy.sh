#!/bin/bash
# 冰谱 Mod 部署脚本 —— 把编译产物同步到游戏 Mods 目录
#
# ★ 铁律：鸡.wav 只准待在源码仓库里，**永远不许进游戏目录**
#   （用户要求：鸡那个音频放在新 mod 存着，但不写进 mod、不默认）
#
# 不会覆盖用户的 config.json（设置 + 首次运行状态）。
set -e

SRC="D:/Program Files/work/ADOFAI冰/冰谱Mod"
DST="D:/SteamLibrary/steamapps/common/A Dance of Fire and Ice/Mods/冰谱Mod"

if pgrep -f "A Dance of Fire and Ice" >/dev/null 2>&1; then
  echo "!! 游戏正在运行，DLL 会被占用。先关掉游戏再部署。"
  exit 1
fi

mkdir -p "$DST/audio" "$DST/ui"

# UMM 会缓存打过补丁的 DLL，不清掉可能加载到旧版本
rm -f "$DST/BingChart.dll".*.cache

cp "$SRC/BingChart.dll"  "$DST/BingChart.dll"
cp "$SRC/Info.json"      "$DST/Info.json"
cp "$SRC/README.md"      "$DST/README.md"
cp "$SRC/CHANGELOG.md"   "$DST/CHANGELOG.md"
cp "$SRC/安装说明.md"     "$DST/安装说明.md"

# ui 只拷图片
for f in "$SRC"/ui/*.png; do [ -e "$f" ] && cp "$f" "$DST/ui/"; done
# audio 只拷冰.wav（明确排除 鸡.wav 和任何其它备选音色）
cp "$SRC/audio/冰.wav" "$DST/audio/冰.wav"

# 兜底：万一之前被误拷进去，这里删掉
rm -f "$DST/audio/鸡.wav"

echo "=== 部署完成 ==="
ls -la "$DST/"
echo "--- audio ---"
ls -la "$DST/audio/"
echo "--- 校验 ---"
md5sum "$SRC/BingChart.dll" "$DST/BingChart.dll"
if [ -e "$DST/audio/鸡.wav" ]; then echo "!! 鸡.wav 竟然还在游戏目录，异常"; exit 1; fi
echo "OK：鸡.wav 未进入游戏目录"
