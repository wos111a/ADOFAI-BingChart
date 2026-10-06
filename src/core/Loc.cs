// 界面多语言（中 / EN / 한국어）
//
// 做法：**在布局助手函数里统一翻译**，而不是在几百个调用点挨个包一层。
//   Head / Note / TextRow / Toggle / Slide / BtnRow / CheckRow / Button / Toast 都过一遍 Loc.T()。
// 按键动作（ModMain.UiAction）用的仍然是未翻译的中文原串，所以切语言不会把按钮功能弄坏。
//
// 表里没有的串原样返回（优雅降级成中文），不会显示成 key。
using System;
using System.Collections.Generic;
using System.Text;

namespace BingChart
{
    public static class Loc
    {
        /// <summary>0=简体中文 1=English 2=한국어</summary>
        public static int Lang = 0;

        public static string L(string zh) { return T(zh); }

        public static string T(string zh)
        {
            if (zh == null) return "";
            if (Lang == 0 || zh.Length == 0) return zh;
            string[] v;
            if (Map.TryGetValue(zh, out v))
            {
                string s = (Lang == 1) ? v[0] : v[1];
                if (!string.IsNullOrEmpty(s)) return s;
            }
            return Unitize(zh);
        }

        /// <summary>纯单位/后缀的替换（滑条读数里那些数字后面的词）。</summary>
        static readonly Dictionary<string, string[]> Units = new Dictionary<string, string[]>
        {
            { " 半音", new[] { " semitones", " 반음" } },
            { " 秒",   new[] { " s", " 초" } },
            { " 押",   new[] { " taps", " 탭" } },
            { "毫秒",  new[] { "ms", "ms" } },
            { "不裁",  new[] { "no trim", "자르지 않음" } },
            { "（1 个八度差多少半音）", new[] { " (semitones per octave)", " (옥타브당 반음)" } },
            { "（一秒内音符越多，单个冰音越轻）", new[] { " (denser = quieter)", " (촘촘할수록 작게)" } },
            { "（下次不弹这个）", new[] { " (don't show again)", " (다시 보지 않기)" } },
            { "开头 ", new[] { "first ", "앞 " } },
            { "（关掉后完全不干预）", new[] { " (off = no interference)", " (끄면 개입 안 함)" } },
        };

        static string Unitize(string zh)
        {
            string s = zh;
            foreach (var kv in Units)
            {
                string rep = (Lang == 1) ? kv.Value[0] : kv.Value[1];
                if (s.Contains(kv.Key)) s = s.Replace(kv.Key, rep);
            }
            return s;
        }

        public static readonly string[] LangNames = { "简体中文", "English", "한국어" };

        static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>
        {
            // ---------- 顶部 / 标签页 ----------
            { "冰谱 设置", new[] { "Bing Chart · Settings", "빙차트 · 설정" } },
            { "A Dance of Fire and Ice · 把每一颗砖换成冰音", new[] { "A Dance of Fire and Ice · every tile becomes an ice hit", "A Dance of Fire and Ice · 모든 타일을 얼음 소리로" } },
            { "基础设置", new[] { "Basic", "기본" } },
            { "专业设置", new[] { "Advanced", "고급" } },
            { "其他设置", new[] { "Other", "기타" } },
            { "重置", new[] { "Reset", "초기화" } },
            { "设置", new[] { "Settings", "설정" } },
            { "语言", new[] { "Language", "언어" } },

            // ---------- 基础设置 ----------
            { "开关与音量", new[] { "Power & volume", "전원 및 볼륨" } },
            { "重置基础", new[] { "重置基础", "重置基础" } },
            { "启用冰谱（关掉后完全不干预）", new[] { "Enable Bing Chart (off = do nothing)", "빙차트 사용 (끄면 아무것도 안 함)" } },
            { "冰谱音量", new[] { "Ice volume", "얼음 볼륨" } },
            { "叠加：原曲保留 + 叠冰音（关掉 = 只放冰音）", new[] { "Overlay: keep song + add ice (off = ice only)", "겹치기: 원곡 유지 + 얼음 추가 (끄면 얼음만)" } },
            { "原曲音量", new[] { "Song volume", "원곡 볼륨" } },
            { "★ 判断「冰有没有踩准」时，建议先把叠加关掉、只听冰音 —— 没被原曲盖住才能一个点一个点数。对准了再打开。", new[] { "★ To check whether the ice lines up, turn overlay OFF and listen to the ice alone — you can only count point by point when the song is not covering it. Turn it back on once it lines up.", "★ 얼음이 제대로 맞는지 확인할 때는 겹치기를 끄고 얼음만 들으세요. 원곡에 가려지면 하나씩 셀 수 없습니다." } },

            { "音色", new[] { "Sound sample", "음색" } },
            { "当前音色", new[] { "Current sample", "현재 음색" } },
            { "切换音色", new[] { "Next sample", "음색 변경" } },
            { "试听", new[] { "Preview", "미리듣기" } },
            { "试播冰音层", new[] { "Preview ice layer", "얼음 레이어 재생" } },
            { "「试听」放的是冰音样本本身（一小段）；「试播冰音层」放的是这个谱子转出来的纯冰音（不接游戏时钟、不管原曲）。如果试播有声音、进关卡却没声音，问题就在同步那一段，不在音频。", new[] { "\"Preview\" plays the raw ice sample. \"Preview ice layer\" plays the ice-only track rendered for this chart — it does NOT use the game clock or the song. If the second one has sound but the level does not, the problem is in the sync step, not in the audio.", "\"미리듣기\"는 얼음 샘플 자체를 재생합니다. \"얼음 레이어 재생\"은 이 채보용으로 만든 얼음 전용 트랙을 재생합니다(게임 시계·원곡과 무관)." } },

            { "转换快捷键", new[] { "Convert hotkey", "변환 단축키" } },
            { "主键 / 副键", new[] { "Main / modifier", "주 키 / 보조 키" } },
            { "改主键", new[] { "Set main", "주 키 변경" } },
            { "改副键", new[] { "Set modifier", "보조 키 변경" } },
            { "在游戏画面上直接按一个键即可改绑", new[] { "Just press a key on the game screen to rebind", "게임 화면에서 키를 누르면 다시 지정됩니다" } },
            { "现在按一个键…（Esc 取消）", new[] { "Press a key now… (Esc to cancel)", "지금 키를 누르세요… (Esc 취소)" } },

            { "行为", new[] { "Behaviour", "동작" } },
            { "转换完成后自动切到冰谱", new[] { "Switch to ice automatically when done", "변환 완료 시 자동 전환" } },
            { "球失败时显示操作说明", new[] { "Show the how-to sheet when you fail", "실패 시 사용 안내 표시" } },
            { "首次提示状态", new[] { "First-run hint", "첫 안내 상태" } },
            { "重新打开首次提示", new[] { "Re-open the first-run hint", "첫 안내 다시 열기" } },
            { "「知道了」只是关掉本次启动的提示，下次开游戏还会弹；想永久不再出现，要在提示里勾「不再提醒」。", new[] { "\"Got it\" only hides the hint for this session — it comes back next launch. To stop it for good, tick \"don't remind me again\" inside the hint.", "\"확인\"은 이번 실행에서만 숨깁니다. 영구적으로 끄려면 안내에서 \"다시 보지 않기\"를 체크하세요." } },
            { "知道了", new[] { "Got it", "확인" } },
            { "我已知晓", new[] { "Got it", "확인했습니다" } },
            { "不再提醒（下次不弹这个）", new[] { "Don't remind me again", "다시 보지 않기" } },

            { "反馈 / 交流", new[] { "Feedback", "피드백" } },
            { "加 QQ 群", new[] { "Join QQ group", "QQ 그룹 가입" } },
            { "遇到问题、想要新功能、想反馈冰音好不好听，都可以进群说。群号 807651876。", new[] { "Bugs, feature requests, or feedback on how the ice sounds — the group is the place. Group no. 807651876.", "버그·요청·피드백은 그룹으로. 그룹 번호 807651876." } },
            { "作者 / 版本", new[] { "Author / version", "제작자 / 버전" } },

            // ---------- 专业设置 ----------
            { "音高：怎么定冰音的高低", new[] { "Pitch: how each ice hit is tuned", "음높이: 얼음 소리의 높낮이" } },
            { "重置音高", new[] { "重置音高", "重置音高" } },
            { "规则：在小球到砖块的那一刻，测原曲那一小段的音高，拿「开头部分」当基准音。比基准高就升调、低就降调 —— 这样冰音跟着旋律起伏走。", new[] { "At the instant the planet lands on a tile we measure the song's pitch there, and use the opening of the song as the reference. Higher than the reference goes up, lower goes down — so the ice follows the melody.", "행성이 타일에 닿는 순간 원곡의 음높이를 재고, 곡 도입부를 기준 음으로 씁니다." } },
            { "按原曲音高定调（关掉 = 所有冰音同调）", new[] { "Tune by the song's pitch (off = all ice same pitch)", "원곡 음높이로 조율 (끄면 모두 같은 음)" } },
            { "升降幅度", new[] { "Pitch range", "음정 범위" } },
            { "12 = 精确音程，实测太跳（约 30% 的音符顶到上限）；3 只保留方向、去掉幅度，约 77% 的相邻音符音高相同，成段保持更像音乐。", new[] { "12 = exact interval, but measured too jumpy (about 30% of notes hit the cap). 3 keeps only the direction and drops the magnitude; about 77% of neighbouring notes end up the same pitch, which sounds much more musical.", "12 = 정확한 음정이지만 실제로는 너무 튑니다. 3은 방향만 남기고 폭을 줄여 이웃 음이 같은 경우가 약 77%가 됩니다." } },
            { "最多升降", new[] { "Max shift", "최대 음정" } },
            { "基准音取样位置", new[] { "Reference sample point", "기준 음 샘플 위치" } },
            { "基频下限", new[] { "Pitch floor", "기본 주파수 하한" } },
            { "基频上限", new[] { "Pitch ceiling", "기본 주파수 상한" } },
            { "基频范围太低会混进贝斯、太高会混进泛音，都不稳。", new[] { "Too low pulls in the bass, too high pulls in harmonics. Both are unstable.", "너무 낮으면 베이스가, 너무 높으면 배음이 섞여 불안정합니다." } },

            { "多押：一块砖要按几下", new[] { "Multi-tap: how many presses a tile needs", "다중 탭: 타일당 입력 수" } },
            { "重置多押", new[] { "重置多押", "重置多押" } },
            { "官方定义：「同时按两个键 / 同时击打两格相邻轨道」。而「双押砖块」是官方 RJ-X 关卡独有的砖，**自制谱根本造不出来**。所以自制谱里的多押只有一种来源：相邻两块砖挨得极近，近到必须几乎同时点两下。", new[] { "Official definition: \"two keys at the same time\" / \"hitting two adjacent tiles at once\". The \"double tile\" is exclusive to the official RJ-X level and cannot be made in the editor, so in custom charts the only source of a multi-tap is two tiles so close together that you must press almost simultaneously.", "공식 정의: \"동시에 두 키\" / \"인접한 두 타일을 동시에\". 더블 타일은 공식 RJ-X 전용이라 커스텀 채보에서는 만들 수 없습니다." } },
            { "★ 这里直接用「时间间隔」判，比用角度准：角度还得乘上 BPM 才是时间。实测 186 个真实自制谱、35050 个砖间隔：<20ms 占 0.73%，<30ms 占 1.76%，<40ms 占 2.78%，<60ms 占 5.25%。", new[] { "★ This uses a time gap rather than an angle, which is more accurate: an angle only becomes a time after multiplying by BPM. Measured over 186 real custom charts / 35,050 tile gaps: <20ms 0.73%, <30ms 1.76%, <40ms 2.78%, <60ms 5.25%.", "★ 각도 대신 시간 간격으로 판정합니다. 186개 채보·35,050개 간격 실측: <20ms 0.73%, <30ms 1.76%, <40ms 2.78%, <60ms 5.25%." } },
            { "多押间隔阈值", new[] { "Multi-tap gap", "다중 탭 간격" } },
            { "两块砖隔得比这个值还近，就算「几乎同时按两下」。BPM 中途变化会自动跟着走，不用分档。想要最接近原声的重音就调到 20~25ms；想多抓一些就 40~60ms。", new[] { "Two tiles closer than this count as \"almost simultaneous\". BPM changes are handled automatically. For the most faithful accents use 20–25ms; to catch more, use 40–60ms.", "이 값보다 가까운 두 타일을 \"거의 동시\"로 봅니다. 원음에 가깝게 하려면 20~25ms, 더 많이 잡으려면 40~60ms." } },
            { "最多算到几押", new[] { "Max taps", "최대 탭 수" } },
            { "结合音频验证（这一簇附近得有原曲重音才算）", new[] { "Verify with audio (needs an accent nearby)", "오디오로 검증 (근처에 강세 필요)" } },

            { "多押加成（押得越多音越高）", new[] { "Multi-tap bonus (more taps = higher)", "다중 탭 보너스 (많을수록 높게)" } },
            { "双押", new[] { "Double tap", "두 번" } },
            { "三押", new[] { "Triple tap", "세 번" } },
            { "四押及以上", new[] { "4 taps and up", "네 번 이상" } },

            { "冰音听感", new[] { "Ice feel", "얼음 느낌" } },
            { "重置听感", new[] { "重置听感", "重置听感" } },
            { "冰音样本本身 0.28 秒长，但大部分是拖尾。音符密集时前后会重叠，听起来就是一片连续的啪啦声、数不出点。裁短后上一个响完下一个才来，就能听出一个个点。设成 0 = 不裁。", new[] { "The sample is 0.28s long but mostly tail. When notes are dense they overlap into one continuous clatter and you cannot count the points. Trimming means each hit finishes before the next starts. 0 = no trim.", "샘플은 0.28초지만 대부분이 여음입니다. 촘촘하면 겹쳐서 하나의 소리로 들립니다. 0 = 자르지 않음." } },
            { "冰音长度", new[] { "Ice length", "얼음 길이" } },
            { "密集段落压制强度", new[] { "Density damping", "밀도 감쇠" } },

            { "时间轴与播放微调", new[] { "Timeline & playback nudge", "타임라인 및 재생 미세조정" } },
            { "重置同步", new[] { "重置同步", "重置同步" } },
            { "对齐微调（转换时用）", new[] { "Timeline nudge (needs re-convert)", "타임라인 미세조정 (재변환 필요)" } },
            { "范围 ±3 秒。听感偏早就往右调（冰音更晚），偏晚就往左调。**改完要重新转换**。", new[] { "Range ±3s. If the ice feels early, move right (later); if late, move left. Changing this requires re-converting.", "범위 ±3초. 빠르면 오른쪽(더 늦게), 느리면 왼쪽으로. 변경 후 재변환이 필요합니다." } },
            { "播放微调（立刻生效）", new[] { "Playback nudge (instant)", "재생 미세조정 (즉시)" } },
            { "冰音和游戏音乐是两份独立播放的，播放那一层可能整体差一点点。这里挪的是播放位置，**改完立刻生效、不用重转**。正数 = 冰音更晚出来。左上角卡片会实时显示「偏差 XXXms」，照着那个数往反方向调就行。", new[] { "The ice and the song are two independent players, so the ice layer may be slightly off as a whole. This nudges the playback position — it takes effect immediately, no re-convert needed. Positive = ice comes later. The top-left card shows the live offset in ms; adjust in the opposite direction of that number.", "얼음과 원곡은 별도로 재생되므로 전체가 조금 어긋날 수 있습니다. 이 값은 재생 위치를 옮기며 즉시 적용됩니다. 왼쪽 위 카드에 실시간 오차(ms)가 표시됩니다." } },

            { "转换", new[] { "Convert", "변환" } },
            { "重置转换", new[] { "重置转换", "重置转换" } },
            { "没有音频时只按轨道转（备用方案）", new[] { "Chart-only fallback when audio is missing", "오디오 없으면 채보만으로 변환" } },
            { "结尾额外保留", new[] { "Extra tail", "끝 여유" } },
            { "起始点检测灵敏度", new[] { "Onset sensitivity", "온셋 감도" } },

            // ---------- 其他设置 ----------
            { "界面", new[] { "Interface", "인터페이스" } },
            { "重置界面", new[] { "重置界面", "重置界面" } },
            { "设置页背景飘雪", new[] { "Snow in the settings background", "설정 배경 눈" } },
            { "飘雪只是装饰，卡的话关掉能省一点性能。", new[] { "The snow is decoration only — turn it off if you need the performance.", "눈은 장식입니다. 버벅이면 꺼두세요." } },
            { "维护", new[] { "Maintenance", "유지보수" } },
            { "清除 %temp% 缓存", new[] { "Clear %temp% cache", "%temp% 캐시 삭제" } },
            { "打开产物目录", new[] { "Open output folder", "출력 폴더 열기" } },
            { "转换产物放在 %temp%\\BingChart\\<关卡名>\\ 里，离开关卡时会自动删掉，按一次「清除」可以连根目录一起清。", new[] { "Output lives in %temp%\\BingChart\\<level>\\ and is deleted automatically when you leave the level. \"Clear\" wipes the whole root folder.", "출력은 %temp%\\BingChart\\<레벨>\\ 에 저장되며 레벨을 나가면 자동 삭제됩니다." } },
            { "诊断", new[] { "Diagnostics", "진단" } },
            { "进关卡后 mod 的日志写在游戏目录上一级的 Player.log 里，路径：%USERPROFILE%\\AppData\\LocalLow\\7th Beat Games\\A Dance of Fire and Ice\\Player.log", new[] { "In-game logs go to Player.log, searched with [BingChart]: %USERPROFILE%\\AppData\\LocalLow\\7th Beat Games\\A Dance of Fire and Ice\\Player.log", "게임 내 로그: %USERPROFILE%\\AppData\\LocalLow\\7th Beat Games\\A Dance of Fire and Ice\\Player.log" } },
            { "点下面这个按钮，**所有设置**都会回到出厂默认（语言和「不再提醒」会保留）。", new[] { "The button below resets **all** settings to factory defaults (language and \"don't remind me\" are kept).", "아래 버튼은 모든 설정을 기본값으로 되돌립니다(언어와 다시 보지 않기는 유지)." } },
            { "全部恢复默认", new[] { "Reset everything", "전체 초기화" } },

            // ---------- 状态卡片 ----------
            { "已识别谱面", new[] { "Chart detected", "채보 인식됨" } },
            { "正在转换冰谱…", new[] { "Converting…", "변환 중…" } },
            { "冰谱 出错了", new[] { "Bing Chart error", "빙차트 오류" } },
            { "谱面 已找到", new[] { "chart OK", "채보 OK" } },
            { "谱面 缺失", new[] { "chart missing", "채보 없음" } },
            { "音频 已找到", new[] { "audio OK", "오디오 OK" } },
            { "音频 缺失（只按轨道转）", new[] { "audio missing (chart only)", "오디오 없음(채보만)" } },
            { " 个音符", new[] { " notes", " 음" } },
            { "按 ", new[] { "Press ", "누르세요 " } },
            { " 开始转换", new[] { " to convert", " 로 변환" } },
            { "已有的可以先玩原曲，转完自动接上", new[] { "You can keep playing the song; ice starts when it's ready", "원곡을 계속 플레이하세요. 완료되면 자동으로 이어집니다" } },
            { "原曲不受影响，可以继续玩", new[] { "The song is untouched — keep playing", "원곡은 그대로입니다" } },

            // ---------- 首次说明 ----------
            { "冰谱 Mod 怎么用", new[] { "How to use Bing Chart", "빙차트 사용법" } },
            { "第一次使用，说明一下就这几条", new[] { "First time — just these few things", "처음이시죠 — 몇 가지만 알려드립니다" } },
            { "进关卡后按 <color=#7FD4F5><b>", new[] { "In a level, press <color=#7FD4F5><b>", "레벨에서 <color=#7FD4F5><b>" } },
            { "</b></color> 开始把原曲转成冰谱", new[] { "</b></color> to turn the song into ice", "</b></color> 를 눌러 변환" } },
            { "转换在后台跑（长曲子几十秒），期间照常玩原曲", new[] { "It converts in the background (tens of seconds for long songs) — keep playing", "백그라운드에서 변환됩니다(긴 곡은 수십 초). 원곡을 계속 플레이하세요" } },
            { "转完后原曲自动静音、改放冰谱 —— 砖块怎么落，冰就怎么响", new[] { "When done the song is muted and the ice takes over — every tile landing becomes an ice hit", "완료되면 원곡이 음소거되고 얼음이 대신 재생됩니다" } },
            { "不想转就什么都不按，原曲一点不变", new[] { "Don't want it? Press nothing — the song stays exactly as it is", "원하지 않으면 아무것도 누르지 마세요. 원곡은 그대로입니다" } },
            { "退出关卡自动还原，%temp% 里的产物一并清掉", new[] { "Leaving the level restores everything and clears the %temp% output", "레벨을 나가면 원래대로 돌아가고 %temp% 출력도 삭제됩니다" } },
            { "快捷键、音量、音色都在 UMM 面板的「设置」里。", new[] { "Hotkey, volume and sample live in the UMM panel under \"Settings\".", "단축키·볼륨·음색은 UMM 패널의 \"설정\"에 있습니다." } },
            { "★ 在关卡编辑器里测试的话，退出谱子时记得选「放弃保存」", new[] { "★ Testing in the level editor? Choose \"Discard changes\" when you exit, or your chart may be overwritten", "★ 레벨 에디터에서 테스트했다면 나갈 때 \"저장 안 함\"을 선택하세요" } },

            // ---------- 提示条 / 错误 ----------
            { "冰音已就绪（叠加在原曲上）", new[] { "Ice ready (overlaid on the song)", "얼음 준비됨 (원곡 위에 겹침)" } },
            { "冰音已就绪（原曲已静音）", new[] { "Ice ready (song muted)", "얼음 준비됨 (원곡 음소거)" } },
            { "冰谱已就绪（未自动切换）", new[] { "Ice ready (auto-switch off)", "얼음 준비됨 (자동 전환 꺼짐)" } },
            { "开始转换冰谱…", new[] { "Converting…", "변환 시작…" } },
            { "已清除转换缓存", new[] { "Cache cleared", "캐시 삭제됨" } },
            { "首次提示已重新打开", new[] { "First-run hint re-enabled", "첫 안내 다시 켜짐" } },
            { "已全部恢复默认设置", new[] { "All settings reset to default", "모든 설정 초기화됨" } },
            { "这一组已恢复默认", new[] { "This group reset to default", "이 그룹 초기화됨" } },
            { "已在浏览器打开", new[] { "Opened in your browser", "브라우저에서 열었습니다" } },
            { "试播冰音层（原曲不管，只放冰音）", new[] { "Playing the ice layer (song ignored)", "얼음 레이어 재생 중" } },
            { "音色 → ", new[] { "Sample → ", "음색 → " } },

            // ---------- 面板 / 诊断 ----------
            { "冰谱", new[] { "Bing Chart", "빙차트" } },
            { "设置页已在游戏画面打开", new[] { "Settings page is open on screen", "설정 화면이 열려 있습니다" } },
            { "进关卡后按 ", new[] { "In a level press ", "레벨에서 " } },
            { " 转冰谱", new[] { " to convert", " 를 눌러 변환" } },
            { "已勾选「不再提醒」，不会再出现", new[] { "Ticked \"don't remind me\" — it will not show again", "\"다시 보지 않기\" 체크됨 — 다시 나오지 않습니다" } },
            { "本次已看过（下次启动游戏还会提示）", new[] { "Seen this session (it comes back next launch)", "이번 실행에서 확인함 (다음 실행에 다시 나옵니다)" } },
            { "还没看过（下次进关卡就会提示）", new[] { "Not seen yet (it will show on your next level)", "아직 안 봄 (다음 레벨에서 표시됩니다)" } },
            { "版本 ", new[] { "Version ", "버전 " } },
            { "　心跳 ", new[] { "  ·  frames ", "  ·  프레임 " } },
            { "场景 ", new[] { "Scene ", "씬 " } },
            { "　关卡 ", new[] { "  ·  Level ", "  ·  레벨 " } },
            { "未绑定", new[] { "not bound", "미연결" } },
            { "已绑定", new[] { "bound", "연결됨" } },
            { "曲名 ", new[] { "Song ", "곡 " } },
            { "谱面 ", new[] { "Chart ", "채보 " } },
            { "　音频 ", new[] { "  ·  Audio ", "  ·  오디오 " } },
            { "无", new[] { "none", "없음" } },
            { "产物 ", new[] { "Output ", "출력 " } },
            { "状态 ", new[] { "State ", "상태 " } },
            { "转换中", new[] { "converting", "변환 중" } },
            { "冰谱播放中", new[] { "ice playing", "얼음 재생 중" } },
            { "未转换", new[] { "not converted", "미변환" } },
            { "失败重进 ", new[] { "restarts ", "재시작 " } },
            { "　重定位 ", new[] { "  ·  resyncs ", "  ·  재동기 " } },
            { "播放头 ", new[] { "Playhead ", "재생 헤드 " } },
            { "（读不到 ", new[] { " (missed ", " (실패 " } },
            { " 次）", new[] { ")", ")" } },
            { "（正常）", new[] { " (ok)", " (정상)" } },
            { "　偏差 ", new[] { "  ·  offset ", "  ·  오차 " } },
            { "错误: ", new[] { "Error: ", "오류: " } },
            { "未尝试", new[] { "not tried", "미시도" } },

            // ---------- 错误信息 ----------
            { "冰谱功能已关闭（基础设置里可打开）", new[] { "Bing Chart is disabled (turn it on in Basic)", "빙차트가 꺼져 있습니다(기본에서 켜세요)" } },
            { "当前关卡没有可用谱面", new[] { "No usable chart in this level", "이 레벨에 사용할 채보가 없습니다" } },
            { "找不到游戏目录下的 ffmpeg.exe", new[] { "ffmpeg.exe not found in the game folder", "게임 폴더에 ffmpeg.exe가 없습니다" } },
            { "找不到冰音: ", new[] { "Ice sample not found: ", "얼음 샘플 없음: " } },
            { "找不到转换产物", new[] { "Converted output not found", "변환 결과물 없음" } },
            { "冰音文件读不出来", new[] { "Could not read the ice audio file", "얼음 오디오 파일을 읽을 수 없습니다" } },
            { "挂载冰音失败: ", new[] { "Failed to attach the ice layer: ", "얼음 레이어 연결 실패: " } },
            { "试播失败: ", new[] { "Preview failed: ", "미리듣기 실패: " } },
            { "还没有转换产物，先在关卡里按一次转换快捷键", new[] { "No output yet — press the convert hotkey in a level first", "아직 결과물이 없습니다. 레벨에서 변환 단축키를 먼저 누르세요" } },
            { "冰音层读不出来", new[] { "Could not read the ice layer", "얼음 레이어를 읽을 수 없습니다" } },
            { "冰音层几乎是静音（峰值 ", new[] { "The ice layer is almost silent (peak ", "얼음 레이어가 거의 무음입니다 (피크 " } },
            { "试听失败: ", new[] { "Preview failed: ", "미리듣기 실패: " } },
            { "冰音读不出来", new[] { "Could not read the ice sample", "얼음 샘플을 읽을 수 없습니다" } },
            { "清除失败: ", new[] { "Clear failed: ", "삭제 실패: " } },
            { "打不开目录: ", new[] { "Could not open the folder: ", "폴더를 열 수 없습니다: " } },
            { "打不开链接: ", new[] { "Could not open the link: ", "링크를 열 수 없습니다: " } },
            { "谱面解析失败（格式不支持）", new[] { "Chart parse failed (unsupported format)", "채보 파싱 실패(지원하지 않는 형식)" } },
            { "谱面读取出错: ", new[] { "Chart read error: ", "채보 읽기 오류: " } },
            { "谱面解析失败（格式不支持）：", new[] { "Chart parse failed (unsupported format): ", "채보 파싱 실패(지원하지 않는 형식): " } },
            { "语言 → ", new[] { "Language → ", "언어 → " } },
        };
    }
}
