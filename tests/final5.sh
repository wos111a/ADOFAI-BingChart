#!/bin/bash
DOT="/c/Users/qiqi/.workbuddy/binaries/dotnet/dotnet.exe"
PY="C:/Users/qiqi/.workbuddy/binaries/python/envs/default/Scripts/python.exe"
MOD="D://Program Files//work//ADOFAI冰//冰谱Mod"
streak=0
for i in 1 2 3 4 5 6 7; do
  rm -rf "$TEMP/BingChartTest" 2>/dev/null
  RO=$("$DOT" run -c Release --no-build -- "$MOD" 2>&1)
  R=$(echo "$RO" | grep -cE "✗")
  RP=$(echo "$RO" | grep -oE "通过 [0-9]+ 项，失败 [0-9]+ 项" | tail -1)
  SO=$("$PY" -u selfcheck.py 2>&1)
  S=$(echo "$SO" | grep -cE "\[NG\]")
  SP=$(echo "$SO" | grep -oE "通过 [0-9]+ 项，失败 [0-9]+ 项" | tail -1)
  HIT=$(echo "$RO" | grep -oE "平均对齐命中率 ≥30%（音符有音频起始点支撑） 平均 [0-9.]+%" | tail -1)
  E2E=$(echo "$RO" | grep -oE "端到端跑了 [0-9]+ 个关卡，成功 [0-9]+ 个" | tail -1)
  echo "自检 $i | 运行时[$RP] 静态[$SP] | $E2E | $HIT"
  if [ "$R" = "0" ] && [ "$S" = "0" ]; then streak=$((streak+1)); else streak=0; fi
  if [ "$streak" -ge 5 ]; then echo ">>> 连续 $streak 遍全绿，达标"; break; fi
done
echo "最终连续全绿遍数: $streak"
