#!/bin/bash
DOT="/c/Users/qiqi/.workbuddy/binaries/dotnet/dotnet.exe"
MOD="D://Program Files//work//ADOFAI冰//冰谱Mod"
for i in 1 2 3; do
  rm -rf "$TEMP/BingChartTest" 2>/dev/null
  echo "########## 第 $i 遍 ##########"
  "$DOT" run -c Release --no-build -- "$MOD" 2>&1 | grep -E "✗|通过 |失败:|区域|【"
  echo
done
