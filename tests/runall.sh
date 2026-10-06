#!/bin/bash
DOT="/c/Users/qiqi/.workbuddy/binaries/dotnet/dotnet.exe"
PY="C:/Users/qiqi/.workbuddy/binaries/python/envs/default/Scripts/python.exe"
MOD="D://Program Files//work//ADOFAI冰//冰谱Mod"
echo "################ 3 遍详细测试 ################"
for i in 1 2 3; do
  rm -rf "$TEMP/BingChartTest" 2>/dev/null
  echo "---- 运行时 第 $i 遍 ----"
  "$DOT" run -c Release --no-build -- "$MOD" 2>&1 | grep -E "✗|通过 |失败:"
  echo "---- 静态 第 $i 遍 ----"
  "$PY" -u selfcheck.py 2>&1 | grep -E "\[NG\]|==="
done
echo "################ 5 遍连续自检 ################"
streak=0
for i in 1 2 3 4 5 6 7; do
  rm -rf "$TEMP/BingChartTest" 2>/dev/null
  R=$("$DOT" run -c Release --no-build -- "$MOD" 2>&1 | grep -cE "✗")
  S=$("$PY" -u selfcheck.py 2>&1 | grep -cE "\[NG\]")
  echo "自检 $i: 运行时失败 $R 项, 静态失败 $S 项"
  if [ "$R" = "0" ] && [ "$S" = "0" ]; then streak=$((streak+1)); else streak=0; fi
  if [ "$streak" -ge 5 ]; then echo ">>> 连续 $streak 遍全绿，达标"; break; fi
done
echo "最终连续全绿遍数: $streak"
