# -*- coding: utf-8 -*-
"""冰谱 Mod 静态自检：每个区域、每个文件逐项核对。
和 tests/ 的运行时测试互补 —— 这里查文件/清单/DLL/文档/部署一致性。"""
import hashlib, json, os, re, sys, zipfile

MOD = r"D:\Program Files\work\ADOFAI冰\冰谱Mod"
GAME = r"D:\SteamLibrary\steamapps\common\A Dance of Fire and Ice\Mods\冰谱Mod"
ok = fail = 0
bad = []

def chk(cond, label, extra=""):
    global ok, fail
    if cond:
        ok += 1
        print(f"    [OK] {label} {extra}")
    else:
        fail += 1
        bad.append(label + " " + extra)
        print(f"    [NG] {label} {extra}")

def md5(p):
    return hashlib.md5(open(p, "rb").read()).hexdigest()

print("=== 静态自检 ===")

# ---- 区域 A：文件齐备 ----
print("【A】文件齐备")
must = ["Info.json", "BingChart.dll", "README.md", "安装说明.md", "CHANGELOG.md",
        "audio/冰.wav", "src/BingChart.csproj", "src/BingChartMod.cs", "src/Overlay.cs",
        "src/core/Json.cs", "src/core/Wav.cs", "src/core/Chart.cs", "src/core/Dsp.cs",
        "src/core/IceChart.cs", "tests/BingChart.Tests.csproj", "tests/Program.cs", ".gitignore"]
for f in must:
    p = os.path.join(MOD, f.replace("/", os.sep))
    chk(os.path.exists(p) and os.path.getsize(p) > 0, f, f"{os.path.getsize(p) if os.path.exists(p) else 0}B")
chk(os.path.exists(os.path.join(MOD, "audio", "鸡.wav")), "audio/鸡.wav 存在（备选音色，不默认不发布）")

# ---- 区域 B：Info.json ----
print("【B】Info.json 清单")
info = json.load(open(os.path.join(MOD, "Info.json"), encoding="utf-8-sig"))
chk(info.get("Id") == "BingChart", "Id = BingChart", info.get("Id"))
chk(info.get("Author") == "By wos111(机人)", "Author = By wos111(机人)", info.get("Author"))
chk(info.get("Version") == "1.0.0", "Version = 1.0.0", info.get("Version"))
chk(info.get("AssemblyName") == "BingChart.dll", "AssemblyName 与 DLL 名一致", info.get("AssemblyName"))
chk(info.get("EntryMethod") == "BingChart.ModMain.Load", "EntryMethod 正确", info.get("EntryMethod"))
chk("ADOFAI-BingSfx" in (info.get("Repository") or ""), "Repository 已填", info.get("Repository"))

# ---- 区域 C：DLL 内容 ----
print("【C】BingChart.dll")
raw = open(os.path.join(MOD, "BingChart.dll"), "rb").read()
def has(s): return s.encode("utf-16-le") in raw or s.encode("utf-8") in raw
for s in ["BingChart", "ModMain", "Overlay", "ChartReader", "IceChartBuilder", "IceSynth",
          "Dsp", "Fft", "Wav", "Json", "GetOpenFileNameW"]:
    if s == "GetOpenFileNameW":
        continue
    chk(has(s), "DLL 含类型/符号 " + s)
for s in ["按 ", "开始转换", "冰谱 Mod 怎么用", "知道了", "冰谱播放中",
          "levelPath", "levelData", "scrConductor", "ice.wav", "BingChart_Hook",
          "ComboDown", "IsModifier", "songposition_minusi", "ice_only.wav"]:
    chk(has(s), "DLL 含字符串 " + s)
chk(b"0Harmony" not in raw, "不依赖 Harmony")
chk(b"comdlg32" not in raw, "不含旧的文件选择框 P/Invoke（新设计已去掉）")

# ---- 区域 D：文档 ----
print("【D】文档")
rd = open(os.path.join(MOD, "README.md"), encoding="utf-8").read()
ins = open(os.path.join(MOD, "安装说明.md"), encoding="utf-8").read()
cl = open(os.path.join(MOD, "CHANGELOG.md"), encoding="utf-8").read()
for name, doc in (("README", rd), ("安装说明", ins), ("CHANGELOG", cl)):
    chk("By wos111(机人)" in doc, f"{name} 标注作者")
    chk("1.0.0" in doc, f"{name} 标注版本 1.0.0")
    chk("🧊" in doc, f"{name} 含 🧊 emoji")
for k in ["Ctrl", "Tab", "angleData", "pathData", "ice_only.wav", "Player.log",
          "多押间隔阈值", "试播冰音层", "试播冰音层", "播放微调", "不再提醒", "807651876"]:
    chk(k in rd, f"README 提到 {k}")
chk("替换播放的 AudioClip" not in rd and "直接替换" not in rd,
    "README 不再宣传「替换游戏 AudioClip」这种已经废掉的方案")
chk("69" in cl and "7.3ms" in cl, "CHANGELOG 记录了对齐验证数据")

# ---- 区域 E：核心算法关键行还在 ----
print("【E】核心算法关键实现")
ch = open(os.path.join(MOD, "src", "core", "Chart.cs"), encoding="utf-8").read()
chk("case '!': return 999" in ch, "pathData 三角块 ! = 999")
chk("startAngle" in ch and "180.0 + ang" in ch, "startAngle 初值 180 且按 180+angle 更新")
chk("need / 180.0 * 60000.0 / bpm[i]" in ch, "ms = neededAngle/180*60000/bpm")
chk("if (setBpm[i] < 0) cur = cur * (-setBpm[i]);" in ch, "Multiplier 乘当前 bpm")
chk('a.Get("floor").IntOr(-1)' in ch, "floor 用 Get().IntOr() 取值（不是对象本身）")
ds = open(os.path.join(MOD, "src", "core", "Dsp.cs"), encoding="utf-8").read()
chk("CalibrateOffset" in ds and "Tol = 0.025" in ds, "偏移标定最大化 25ms 命中率")
chk("DetectAttack" in ds, "冰音 attack 自动检测")
ic = open(os.path.join(MOD, "src", "core", "IceChart.cs"), encoding="utf-8").read()
chk("同时用到" in ic or "必须同时" in ic, "强制谱面+音频双来源")
chk("ice.ogg" in ic, "导出 ice.ogg")
chk("reject" in ic.lower() or "拒绝" in ic or "Error" in ic, "不合格时拒绝产出")
md = open(os.path.join(MOD, "src", "BingChartMod.cs"), encoding="utf-8").read()
chk("IsModifier" in md and "Input.GetKey(main)" in md, "组合键用修饰键按住语义")
chk("PreviewSource" in md, "试听用独立 AudioSource（不打断冰谱）")
chk("gameSongVolume" in md, "退出时恢复原曲原音量")
ov = open(os.path.join(MOD, "src", "Overlay.cs"), encoding="utf-8").read()
# 🧊 这个 emoji 在游戏字体里没有字形，会显示成方框；所以改用预渲染的 PNG 冰块图（Icons.Cube / CubeBig）
chk("Icons.Cube" in ov, "覆盖层用图形冰块图标（不是会显示成方框的 emoji）")
ic2 = open(os.path.join(MOD, "src", "Icons.cs"), encoding="utf-8").read()
chk("ice.png" in ic2 and "LoadPng" in ic2, "Icons 从 ui/ice.png 读预渲染冰块图")
chk("lang_ko" in ic2, "韩文「한」也用预渲染图（Malgun Gothic 字体游戏里没有）")
chk("不再提醒" in ov, "首次提示可永久关闭")
chk("开始转换" in ov, "有关键操作提示（按 Ctrl+Tab 开始转换）")
chk("放弃保存" in ov, "红色提醒退出编辑器要选「放弃保存」")

# ---- 区域 G：播放层（绝不替换游戏 AudioClip + 锁游戏时钟） ----
# 字段名全部来自游戏 Assembly-CSharp.dll 的元数据实测，不是猜的：
#   scrConductor(实例属性) songposition_minusi / (实例字段) _songposition_minusi
#   scrConductor(实例字段) song / song2 / song3
#   scrConductor(静态字段) _instance / (静态属性) instance / (实例字段) hasSongStarted
#   注意：scrConductor 上**没有** songposition 这个成员 —— 写它就是错的。
print("【G】播放层（锁游戏时钟）")
chk('"songposition_minusi"' in md, "读 songposition_minusi（元数据实测存在）")
chk('"_songposition_minusi"' in md, "含 _songposition_minusi 兜底")
chk(not re.search(r'HasSongStarted\(\)', md),
    "没有拿 hasSongStarted 当播放门槛（编辑器里它不一定是真 → 会全程不出声）")
chk('"_instance"' in md, "读静态字段 _instance")
chk('"instance"' in md, "含静态属性 instance 兜底")
chk(not re.search(r'["\']songposition["\']', md),
    "没有误用不存在的成员 songposition（scrConductor 上确实没有它）")
chk('"song"' in md and '"song2"' in md and '"song3"' in md, "song/song2/song3 三路兜底取游戏音频源")
chk("SyncPlayback" in md, "每帧同步函数存在")
chk("GameSongPos" in md, "播放头读取函数存在")
chk("PlaybackOffsetMs" in md, "播放微调项存在（不用重新转换）")
chk("PlaybackOffsetMs" in ov, "播放微调项在设置界面可见")
chk("lastDriftMs" in md and "偏差" in ov, "偏差实时显示到卡片")
chk("ice_only.wav" in md, "播放用的是「纯冰音层」ice_only.wav")
chk("ice_only.wav" in ic, "IceChart 会导出 ice_only.wav")
chk('IceSource.Stop(); UnityEngine.Object.Destroy' in md or "StopPlayback" in md, "退出时销毁自己的音源")
chk("Time.unscaledTime" in md, "用 unscaledTime 判游戏是否在走（暂停时停冰音）")
chk("Mathf.Clamp" in md, "塞播放位置前先夹到合法范围")

# ---- 区域 G2：这一轮修的 4 个具体问题（防回归） ----
print("【G2】本轮修的问题")
# 1) 单位不能在第一帧锁死（上一版就是这样导致全程没声）
chk("songPos <= 0.3" not in md and "refSec" not in md,
    "单位判断不再用「第一帧锁死」的老写法")
chk("每帧重新定单位" in md or "每帧重新定" in md, "单位是每帧现算的")
chk("posUnit = 2" in md and "posUnit = 1" in md, "单位两条独立判据都还在")
# 2) 音频层可单独试播（把「音频没声」和「同步没声」分开）
chk("PreviewSolo" in md, "有「试播冰音层」按钮（不接游戏时钟）")
chk("试播冰音层" in ov, "「试播冰音层」在设置界面可见")
# 3) AudioClip.Create 的长度必须是「每声道采样数」
chk(re.search(r'AudioClip\.Create\([^)]*,\s*frames\s*,', md) is not None,
    "AudioClip.Create 用 frames（不是 data.Length）")
chk("AudioClip.Create(\"bingchart_ice\", frames" in md
    or re.search(r'bingchart_ice",\s*frames', md) is not None, "冰音层用 frames 建 clip")
# 4) 首次提示「知道了」不再写盘（只关本次启动）
chk("hintAcknowledged = false;" in md
    and 'B(sb, "hintAck"' not in md and 'GetBool(j, "hintAck"' not in md,
    "「知道了」不再读写配置（旧版存了盘 → 用户再也看不到提示）")
chk("ResetHint" in md and "重新打开首次提示" in ov, "有「重新打开首次提示」按钮")
# 5) 转换要有日志（上一版整个转换流程一条日志都没有，没法诊断）
chk("QueueLog" in md and "开始转换" in md and "转换完成" in md, "转换开始/完成都会写日志")
chk("FlushLogs" in md, "后台线程的日志由主线程转发")
# 6) QQ 群链接
chk("QqGroupUrl" in md and "807651876" in md, "QQ 群链接已内置")
chk("加 QQ 群" in ov, "设置界面有「加 QQ 群」按钮")

# ---- 区域 G3：设置页重做（三页 / 重置 / 红 X / 滚动 / 按钮风格） ----
print("【G3】设置页重做")
chk('"基础设置", "专业设置", "其他设置"' in ov or ('基础设置' in ov and '专业设置' in ov and '其他设置' in ov),
    "三个标签 = 基础设置 / 专业设置 / 其他设置")
chk("详细设置" not in ov, "旧的「详细设置」标签已去掉")
chk("CloseX" in ov and "DrawCross" in ov, "右上角是画出来的 ✕（不是「关闭」按钮）")
chk('"关闭"' not in ov, "设置页不再有「关闭」文字按钮")
chk("ResetAll" in md and "全部恢复默认" in ov, "有总的「全部恢复默认」按钮")
chk("ResetSection" in md and '"重置"' in ov, "每一组都有「重置」按钮")
for sec in ("basic", "pitch", "multi", "sound", "sync", "convert", "ui"):
    chk(('ResetSection("%s")' % sec) in md, "重置分区 " + sec + " 已接好")
chk("ScrollWheel" in ov, "设置页内容支持滚轮滚动（以前内容多了直接被切掉）")
# 按钮文字样式必须基于 GUI.skin.label —— 基于 GUI.skin.button 会把系统按钮背景也画出来
chk("new GUIStyle(GUI.skin.button)" not in ov,
    "没有任何样式基于 GUI.skin.button（那会叠出系统灰框，就是用户说的「按钮与 UI 不合」）")
chk("公共按钮样式" in ov or "CBtnFace" in ov, "按钮统一走自己的一套配色")
chk("Danger" in ov and "Btn.Primary" in ov, "按钮区分 普通/主按钮/危险 三档")

# ---- 区域 G4：多押改成按毫秒 ----
print("【G4】多押按毫秒")
chk("MultiTapGapMs" in ic, "IceChart 用毫秒间隔阈值")
chk("MultiTapGapMs" in md, "Config 有 MultiTapGapMs")
chk("MultiTapBpmLow" not in ic and "MultiTapAngleLow" not in ic,
    "旧的「BPM 分档 + 角度阈值」已从引擎里删掉")
chk("MultiTapBpmLow" not in md and "MultiTapBpmHigh" not in md,
    "旧的四个角度/BPM 旋钮已从配置里删掉")
chk('"mtGapMs"' in md, "配置读写用 mtGapMs")
chk("mtBpmLow" not in md and "mtAngleLow" not in md, "旧的配置键已不再读写")
chk("多押间隔阈值" in ov, "设置页有「多押间隔阈值」一个滑条")
chk("MultiTapMaxCluster" in ic and "MultiTapMaxCluster" in md, "簇上限可配（最多算到几押）")
chk("PressesConfirmByAudio" in ic, "多押仍可选「结合音频验证」")

# ---- 区域 G5：这一轮的两个★关键 bug 修复 ----
print("【G5】两个关键 bug 的修复不能回退")
chk("pos < lastGamePos - 0.25" in md,
    "播放头回退时重置基准（否则点开始游戏后 lastGamePos 卡在大值 → 永久静音）")
chk("lastGamePos = pos;" in md and "lastGameAdvanceAt = now;" in md, "回退分支里确实重置了这两个")
chk("GameSong.time > 0.001f" in md or "songStarted" in md,
    "游戏音乐没真正开始播之前不出冰音（否则编辑器里静止也会自己播）")
chk("AudioClip.Create(\"bingchart_ice\", frames" in md, "冰音 clip 用 frames 建（不是交错数组长度）")

# ---- 区域 F：部署一致性 ----
print("【F】游戏目录部署")
for f in ["Info.json", "BingChart.dll", "audio/冰.wav"]:
    a = os.path.join(MOD, f.replace("/", os.sep))
    b = os.path.join(GAME, f.replace("/", os.sep))
    chk(os.path.exists(b), "游戏目录有 " + f)
    if os.path.exists(b) and os.path.exists(a):
        chk(md5(a) == md5(b), "MD5 一致 " + f, md5(b)[:12])
chk(not os.path.exists(os.path.join(GAME, "audio", "鸡.wav")), "鸡.wav 未部署到游戏目录（不随包发布）")
chk(not os.path.exists(r"D:\Program Files\work\ADOFAI冰\冰音效Mod"), "旧 mod 文件夹已删除")
chk(not os.path.exists(os.path.join(GAME, "冰音效Mod.zip")), "旧 mod zip 已删除")
gi = json.load(open(os.path.join(GAME, "Info.json"), encoding="utf-8-sig"))
chk(gi.get("Id") == "BingChart" and gi.get("Author") == "By wos111(机人)", "游戏目录清单正确")

print(f"\n=== 静态自检：通过 {ok} 项，失败 {fail} 项 ===")
for b in bad: print("  失败:", b)
sys.exit(0 if fail == 0 else 1)
