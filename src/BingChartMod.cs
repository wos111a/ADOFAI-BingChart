// 冰谱 Mod —— UMM / Unity 集成层
//
// 职责：
//   1. 侦测当前关卡（scnGame.levelPath / levelData），关卡编辑器也支持
//   2. 快捷键（默认 左Ctrl+Tab）= 启动冰谱转换。不按就完全不干预，原曲照旧
//   3. 转换在后台线程跑，产物写 %temp%\BingChart\<关卡名>\（ice.wav / ice.ogg / report.txt）
//   4. 转换完成后静音原曲、播放冰谱，跟随游戏播放头同步
//   5. 退出关卡 -> 还原原曲 + 删除该关卡的临时产物
//   6. UMM 面板只留「设置」图形按钮 + 语言；点开是游戏内全屏设置页
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityModManagerNet;

namespace BingChart
{
    public class Config
    {
        public bool Enabled = true;
        public float Volume = 0.85f;
        // ★ 混合方式：true=叠加（原曲保留+冰音），false=替换（只有冰音）。默认叠加
        public bool MixOverlay = true;
        public float OriginalVolume = 1.00f;
        public float IceVolume = 0.60f;
        // 专业设置
        public float IceLengthMs = 120f;          // 冰音裁到多长（0=不裁）
        public float DensityCompensation = 0.5f;  // 密集段落降音量强度
        public float ExtraOffsetMs = 0f;          // 对齐微调（转换时用，改了要重新转换）
        public float PlaybackOffsetMs = 0f;       // 播放微调（边听边调，不用重新转换）
        // 音高（相对开头基准音）
        public bool PitchRelative = true;
        public float PitchCompress = 3.0f;
        public float PitchMaxSemitone = 7.0f;
        public float PitchLowHz = 70f;
        public float PitchHighHz = 1200f;
        public float OpeningPercent = 8f;         // 用开头百分之几当基准
        public string IceFile = "冰.wav";
        public string HotkeyMain = "LeftControl";
        public string HotkeySub = "Tab";
        public int LangIndex = 0;                 // 0中文 1English 2한국어

        public bool AutoSwitch = true;           // 转完自动切冰谱
        public bool ShowHintOnFail = true;       // 失败/重进时弹操作说明

        public float PressSemitone2 = 2f;
        public float PressSemitone3 = 3f;
        public float PressSemitone4 = 4f;
        public bool UseConstantSegmentPitch = true;
        public float ConstantAudioWeight = 0.90f;
        public float ConstantMaxShift = 2f;

        public bool MultiTapUseAudio = true;
        /// <summary>★多押主判据：相邻两砖间隔小于这个毫秒数就算"几乎同时按两下"。默认 40ms。</summary>
        public float MultiTapGapMs = 40f;
        public int MultiTapMaxCluster = 4;      // 最多算到几押

        public bool ChartOnlyFallback = true;    // 无音频时只按轨道转
        public float TailSeconds = 2f;
        public float OnsetSensitivity = 1f;
    }

    public static class ModMain
    {
        public const string Version = "1.0.0";
        /// <summary>反馈 / 交流 QQ 群（设置页有按钮可以直接打开）。</summary>
        public const string QqGroupUrl =
            "https://qm.qq.com/cgi-bin/qm/qr?k=XGwqmZVTbRTdm5SrmZA_JSl1e6OrBii-&group_code=807651876";
        public static Config cfg = new Config();
        public static UnityModManager.ModEntry Entry;
        public static string ModPath;

        public static int FirstRun = 0;             // 0 -> 1，之后永远是 1
        public static bool HintNeverAgain = false;  // 勾了「不再提醒」
        public static bool hintAcknowledged = false;

        public static string CurrentSongKey = "";
        public static string CurrentLevelDir = "";
        public static string CurrentChartPath = "";
        public static string CurrentAudioPath = "";
        public static string CurrentSongTitle = "";
        public static string ConvertDir = "";

        public static bool SongConverted = false;
        public static volatile bool Converting = false;
        public static string ConvertProgress = "";
        public static volatile float ConvertProgress01 = 0f;
        public static string LastReport = "";
        static string _lastError = "";
        public static string LastError
        {
            get { return _lastError; }
            set { _lastError = value ?? ""; if (_lastError.Length > 0) errorDismissed = false; }
        }
        static bool errorDismissed;
        public static int CurrentNoteCount;
        public static string ConvertStep = "";
        static float cardUntil;

        /// <summary>0=不显示 1=识别到谱面 2=转换中 3=出错。出错优先，其次是转换中。</summary>
        public static int CardMode
        {
            get
            {
                if (LastError.Length > 0 && !errorDismissed) return 3;
                if (Converting) return 2;
                if (Time.unscaledTime < cardUntil && CurrentSongKey.Length > 0) return 1;
                return 0;
            }
        }

        public static void DismissError() { errorDismissed = true; }
        static volatile bool pendingAttach = false;

        public static bool UiSettingsOpen = false;
        public static bool UiHintOpen = false;
        public static int CaptureMode = 0;
        public static string UiAction = "";
        public static int LangIndex { get { return cfg.LangIndex; } set { cfg.LangIndex = value; SaveConfig(); } }

        public static AudioSource PlaySource;
        public static AudioSource GameSong;
        public static AudioSource PreviewSource;
        public static AudioSource SoloPreviewSource;   // 试播「纯冰音层」用（不接游戏时钟）
        public static AudioClip SoloPreviewClip;
        public static AudioClip IceClip;
        public static AudioClip PreviewClip;
        static float lastPos = -1f;
        public static int restarts;
        public static int syncJumps;
        public static long Heartbeat;

        // ================= 入口 =================
        public static bool Load(UnityModManager.ModEntry mod)
        {
            Entry = mod;
            ModPath = mod.Path;
            LoadConfig();

            var go = new GameObject("BingChart_Hook");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Hook>();

            mod.OnGUI = OnGUI;
            mod.OnSaveGUI = OnSaveGUI;

            if (FirstRun == 0)
            {
                FirstRun = 1;
                hintAcknowledged = false;
                SaveConfig();
                mod.Logger.Log("[BingChart] 首次运行，将显示一次操作说明");
            }

            mod.Logger.Log("[BingChart] v" + Version + " loaded. ice=" + cfg.IceFile
                + " hotkey=" + HotkeyText() + " firstRun=" + FirstRun);
            return true;
        }

        class Hook : MonoBehaviour
        {
            private void Update()
            {
                Heartbeat++;
                FlushLogs();
                if (pendingAttach) { pendingAttach = false; RunPendingAttach(); }
                BlockEditorKeys(UiHintOpen || UiSettingsOpen);
                DetectLevel();
                HandleActions();
                HandleHotkey();
                HandleCapture();
                SyncPlayback();
            }
            private void OnGUI() { Overlay.Draw(); }
        }

        // ================= 关卡侦测 =================
        static string lastScene = "";

        static void DetectLevel()
        {
            string scene = "";
            try { scene = SceneManager.GetActiveScene().name; } catch { return; }

            if (scene != lastScene)
            {
                lastScene = scene;
                LeaveLevel();
                if (Entry != null)
                    Entry.Logger.Log("[BingChart] 场景切换 -> " + scene
                        + (InLevelScene() ? "（可转换场景）" : "（非游玩场景，暂不绑定）"));
            }

            // 不只在场景变化时试：编辑器里谱面可能是后来才打开的，所以要持续重试
            if (CurrentSongKey.Length == 0 && InLevelScene()) BindLevel();
        }

        public static bool InLevelScene()
        {
            // 游戏实际场景清单（从 globalgamemanagers 读出来的）：
            //   scnSplash / scnLevelSelect / scnCLS / scnCalibration / scnGame / scnEditor / scnMinesweeper / scnLoading
            // 注意：**没有 scnLevel**，真正的游玩场景是 scnGame。
            return lastScene == "scnGame" || lastScene == "scnEditor";
        }

        // ================= 定位关卡 =================
        static float nextBindTry;

        static void BindLevel()
        {
            if (Time.unscaledTime < nextBindTry) return;   // 最多每 0.5 秒试一次
            nextBindTry = Time.unscaledTime + 0.5f;

            var info = LocateLevel();
            if (info == null) return;

            CurrentLevelDir = info.dir;
            CurrentChartPath = info.chart;
            CurrentAudioPath = info.audio;
            CurrentSongTitle = info.title;
            CurrentSongKey = MakeKey(info.chart, info.title);
            ConvertDir = Path.Combine(Path.GetTempPath(), "BingChart", SafeName(CurrentSongKey));

            CurrentNoteCount = 0;
            try
            {
                var pc = ChartReader.Load(info.chart);
                if (pc == null) LastError = "谱面解析失败（格式不支持）：" + Path.GetFileName(info.chart);
                else CurrentNoteCount = pc.Notes.Count;
            }
            catch (Exception e) { LastError = "谱面读取出错: " + e.Message; }

            cardUntil = Time.unscaledTime + 3.5f;

            Entry.Logger.Log("[BingChart] 绑定关卡成功: " + CurrentSongTitle
                + " | 谱面=" + Path.GetFileName(info.chart)
                + " | 音频=" + (string.IsNullOrEmpty(info.audio) ? "无" : Path.GetFileName(info.audio))
                + " | 音符=" + CurrentNoteCount
                + " | 目录=" + info.dir + " | 方式=" + info.how);

            if (!hintAcknowledged && !HintNeverAgain && cfg.ShowHintOnFail)
                UiHintOpen = true;
        }

        class LevelInfo
        {
            public string dir, chart, audio, title, how;
        }

        static bool loggedLocateFail;

        /// <summary>
        /// 找当前关卡。按可靠性从高到低试：
        ///   1) scnGame / scnEditor 组件上的 levelPath（游玩时和编辑器打开谱面后都有值）
        ///   2) scnEditor.levelToOpenOnLoad（编辑器里刚打开还没进时）
        ///   3) get_levelData().get_songFilename() -> 拿音频名去工坊目录里搜是哪张谱
        ///   4) scrConductor.song.clip.name -> 同上（只有 scnGame 有 scrConductor）
        /// 编辑器场景里没有 scrConductor，所以第 3 条是编辑器的主要通路。
        /// </summary>
        static LevelInfo LocateLevel()
        {
            string how = "", dir = null, title = "", songFile = null;

            try
            {
                Component c = FindAnyOf("scnGame", "scnEditor");
                if (c != null)
                {
                    dir = GetFieldString(c, "levelPath");
                    if (string.IsNullOrEmpty(dir)) dir = GetFieldString(c, "levelToOpenOnLoad");

                    // 谱面内部信息：歌名 + 音频文件名
                    object data = GetFieldObject(c, "levelData");
                    if (data == null) data = CallObject(c, "get_levelData");
                    if (data == null) data = GetFieldObject(c, "customLevel");
                    if (data != null)
                    {
                        title = ChartReader.StripTags(CallString(data, "get_song") ?? "");
                        songFile = CallString(data, "get_songFilename");
                    }
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) how = "组件 levelPath";
                    else dir = null;
                }
            }
            catch (Exception e) { Entry.Logger.Error("反射关卡出错: " + e.Message); }

            // 音频名兜底：优先用谱面自带的 songFilename，其次用正在播的 clip 名
            if (dir == null)
            {
                string key = !string.IsNullOrEmpty(songFile) ? Path.GetFileName(songFile) : PlayingClipName();
                if (!string.IsNullOrEmpty(key))
                {
                    string found = SearchLevelFolderByAudio(key);
                    if (found != null) { dir = found; how = "按音频名搜索(" + key + ")"; }
                }
            }

            if (dir == null)
            {
                if (!loggedLocateFail)
                {
                    loggedLocateFail = true;
                    Entry.Logger.Log("[BingChart] 暂未定位到关卡（场景=" + lastScene
                        + "）。如果你已经打开了谱面，请把这行之后的场景结构发给开发者：\n" + DumpScene());
                }
                return null;
            }
            loggedLocateFail = false;

            string chart = FindChart(dir);
            if (chart == null) return null;
            string audio = FindAudio(dir) ?? FindAudio2(dir);
            if (string.IsNullOrEmpty(title)) title = Path.GetFileName(dir);
            return new LevelInfo { dir = dir, chart = chart, audio = audio, title = title, how = how };
        }

        /// <summary>调实例方法拿返回值（用于 get_levelData()）。</summary>
        static object CallObject(object target, string method)
        {
            if (target == null) return null;
            try
            {
                var mi = target.GetType().GetMethod(method,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return mi == null ? null : mi.Invoke(target, null);
            }
            catch { return null; }
        }

        // ---- 音频名兜底搜索 ----
        static string PlayingClipName()
        {
            try
            {
                var src = FindGameSong();
                if (src == null || src.clip == null) return null;
                string n = src.clip.name;
                return string.IsNullOrEmpty(n) ? null : n;
            }
            catch { return null; }
        }

        static string[] LevelRoots()
        {
            var list = new List<string>();
            try
            {
                foreach (string drive in new[] { "C", "D", "E", "F", "G" })
                {
                    for (int lib = 0; lib < 4; lib++)
                    {
                        string p = lib == 0
                            ? drive + ":\\SteamLibrary\\steamapps\\workshop\\content\\977950"
                            : drive + ":\\SteamLibrary" + lib + "\\steamapps\\workshop\\content\\977950";
                        if (Directory.Exists(p)) list.Add(p);
                    }
                    string pf = drive + ":\\Program Files (x86)\\Steam\\steamapps\\workshop\\content\\977950";
                    if (Directory.Exists(pf)) list.Add(pf);
                }
                string gd = GuessGameDir();
                if (gd != null)
                {
                    list.Add(Path.Combine(gd, "CustomLevels"));
                    list.Add(Path.Combine(gd, "Mods", "LevelLibrary", "backup"));
                }
            }
            catch { }
            return list.ToArray();
        }

        static string SearchLevelFolderByAudio(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            string want = Path.GetFileNameWithoutExtension(fileName);
            foreach (string root in LevelRoots())
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    foreach (string d in Directory.GetDirectories(root))
                        if (ContainsAudio(d, want)) return d;
                }
                catch { }
            }
            return null;
        }

        static bool ContainsAudio(string dir, string wantNoExt)
        {
            try
            {
                foreach (string pat in new[] { "*.ogg", "*.wav", "*.mp3" })
                    foreach (string f in Directory.GetFiles(dir, pat))
                        if (string.Equals(Path.GetFileNameWithoutExtension(f), wantNoExt,
                                          StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        // ---- 场景结构转储（定位不了关卡时写进日志，方便排查）----
        static string DumpScene()
        {
            var sb = new StringBuilder();
            try
            {
                Component c = FindAnyOf("scnGame", "scnEditor");
                sb.Append("  scnGame/scnEditor: ").Append(c == null ? "未找到" : "找到 (" + c.gameObject.name + ")").Append('\n');
                Component cd = FindAnyOf("scrConductor");
                sb.Append("  scrConductor: ").Append(cd == null ? "未找到" : "找到").Append('\n');
                var roots = SceneManager.GetActiveScene().GetRootGameObjects();
                sb.Append("  根对象 ").Append(roots.Length).Append(" 个：\n");
                for (int i = 0; i < roots.Length && i < 40; i++)
                {
                    sb.Append("   - ").Append(roots[i].name).Append("  [");
                    var comps = roots[i].GetComponents<Component>();
                    for (int k = 0; k < comps.Length && k < 14; k++)
                    {
                        if (comps[k] == null) continue;
                        sb.Append(comps[k].GetType().Name);
                        if (k < comps.Length - 1) sb.Append(", ");
                    }
                    sb.Append("]\n");
                }
            }
            catch (Exception e) { sb.Append("  转储失败: ").Append(e.Message); }
            return sb.ToString();
        }

        // ---- 反射小工具 ----
        static Component FindAnyOf(params string[] typeNames)
        {
            try
            {
                var roots = SceneManager.GetActiveScene().GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    var f = ScanGo(roots[i], typeNames, 0);
                    if (f != null) return f;
                }
            }
            catch { }
            return null;
        }

        static Component ScanGo(GameObject go, string[] names, int depth)
        {
            var comps = go.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) continue;
                string tn = comps[i].GetType().Name;
                for (int k = 0; k < names.Length; k++)
                    if (tn == names[k]) return comps[i];
            }
            if (depth >= 4) return null;
            var tr = go.transform;
            for (int i = 0; i < tr.childCount; i++)
            {
                var f = ScanGo(tr.GetChild(i).gameObject, names, depth + 1);
                if (f != null) return f;
            }
            return null;
        }

        static string GetFieldString(object o, string field)
        {
            try
            {
                var fi = o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return fi == null ? null : fi.GetValue(o) as string;
            }
            catch { return null; }
        }

        static object GetFieldObject(object o, string field)
        {
            try
            {
                var fi = o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return fi == null ? null : fi.GetValue(o);
            }
            catch { return null; }
        }

        static void LeaveLevel()
        {
            StopPlayback();
            if (!string.IsNullOrEmpty(ConvertDir))
            {
                try { if (Directory.Exists(ConvertDir)) Directory.Delete(ConvertDir, true); }
                catch { }
            }
            CurrentSongKey = ""; CurrentLevelDir = ""; CurrentChartPath = "";
            CurrentAudioPath = ""; CurrentSongTitle = ""; ConvertDir = "";
            SongConverted = false;
            nextBindTry = 0f;
            loggedLocateFail = false;
            LastReport = "";
        }

        // ================= 快捷键 =================
        static bool IsModifier(KeyCode k)
        {
            return k == KeyCode.LeftControl || k == KeyCode.RightControl
                || k == KeyCode.LeftShift || k == KeyCode.RightShift
                || k == KeyCode.LeftAlt || k == KeyCode.RightAlt
                || k == KeyCode.LeftCommand || k == KeyCode.RightCommand;
        }

        static bool ComboDown(KeyCode main, KeyCode sub)
        {
            if (main == KeyCode.None || sub == KeyCode.None) return false;
            if (Input.GetKeyDown(main) && Input.GetKeyDown(sub)) return true;
            if (IsModifier(main) && Input.GetKey(main) && Input.GetKeyDown(sub)) return true;
            if (IsModifier(sub) && Input.GetKey(sub) && Input.GetKeyDown(main)) return true;
            return false;
        }

        /// <summary>
        /// 弹窗/说明显示时，屏蔽编辑器按键 —— 否则在关卡编辑器里按 adws 会改轨道。
        /// 只处理 scnEditor：正式游玩（scnGame）不受影响。做法是让编辑器进入"正在弹窗"状态，
        /// 它自己的输入逻辑就会跳过。
        /// </summary>
        static bool editorBlocked;
        static void BlockEditorKeys(bool block)
        {
            if (nextBlockCheck > Time.unscaledTime) return;
            nextBlockCheck = Time.unscaledTime + 0.15f;
            try
            {
                if (lastScene != "scnEditor") return;
                Component ed = FindAnyOf("scnEditor");
                if (ed == null) return;
                if (block == editorBlocked) return;
                var fi = ed.GetType().GetField("showingPopup",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null && fi.FieldType == typeof(bool))
                {
                    fi.SetValue(ed, block);
                    editorBlocked = block;
                    Entry.Logger.Log("[BingChart] 编辑器弹窗状态 -> " + block);
                    return;
                }
                // 退一步：用弹窗遮罩物体
                var fi2 = ed.GetType().GetField("popupBlocker",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi2 != null)
                {
                    var go = fi2.GetValue(ed) as GameObject;
                    if (go != null) { go.SetActive(block); editorBlocked = block; }
                }
            }
            catch { }
        }

        static float nextBlockCheck;

        static void HandleHotkey()
        {
            if (UiSettingsOpen || UiHintOpen) return;
            if (CaptureMode != 0) return;
            if (CurrentSongKey.Length == 0) return;
            if (Converting || SongConverted) return;
            if (ComboDown(ParseKey(cfg.HotkeyMain), ParseKey(cfg.HotkeySub)))
            {
                StartConvert();
                Overlay.Toast("开始转换冰谱…");
            }
        }

        static void HandleCapture()
        {
            if (CaptureMode == 0) return;
            if (Event.current == null || Event.current.type != EventType.KeyDown) return;
            KeyCode kc = Event.current.keyCode;
            if (kc == KeyCode.Escape) { CaptureMode = 0; Event.current.Use(); return; }
            if (kc == KeyCode.None) return;
            if (CaptureMode == 1) cfg.HotkeyMain = kc.ToString(); else cfg.HotkeySub = kc.ToString();
            CaptureMode = 0;
            Event.current.Use();
            SaveConfig();
            Overlay.Toast("快捷键 → " + HotkeyText());
        }

        static void HandleActions()
        {
            if (string.IsNullOrEmpty(UiAction)) return;
            string a = UiAction; UiAction = "";
            switch (a)
            {
                case "切换音色": CycleIce(); break;
                case "试听冰音": case "试听": PreviewIce(); break;
                case "试播冰音层": PreviewSolo(); break;
                case "重新打开首次提示": ResetHint(); break;
                case "加 QQ 群": OpenUrl(QqGroupUrl); break;
                case "全部恢复默认": ResetAll(); break;
                case "重置基础": ResetSection("basic"); break;
                case "重置音高": ResetSection("pitch"); break;
                case "重置多押": ResetSection("multi"); break;
                case "重置听感": ResetSection("sound"); break;
                case "重置同步": ResetSection("sync"); break;
                case "重置转换": ResetSection("convert"); break;
                case "重置界面": ResetSection("ui"); break;
                case "改主键": CaptureMode = 1; break;
                case "改副键": CaptureMode = 2; break;
                case "清除 %temp% 缓存": ClearTemp(); break;
                case "打开产物目录": OpenTempDir(); break;
            }
            SaveConfig();
        }

        // ================= 转换 =================
        //
        // ★★ 时间轴的来源在 v1.0.0 这一版彻底换了：
        //   以前是我自己离线解析 .adofai 再按公式算每块砖的时刻；
        //   现在**直接读游戏自己算好的时刻表**（scrLevelMaker.listFloors 里每个 scrFloor 的 entryTime），
        //   并且用游戏自己的时钟（scrConductor.songposition_minusi）驱动播放。
        //   两者配对使用 → 冰音落点在**构造上**就和砖块严丝合缝，不再依赖我的算法有多准。

        /// <summary>从游戏里读出来的逐砖时刻表。字段名全部来自 Assembly-CSharp.dll 元数据实测。</summary>
        // 数据结构与「转换逻辑」都在 core/GameTimeline.cs 里（引擎无关，可离线单测），
        // 这里只负责用反射把游戏里的数读出来。

        static double AsDouble(object v)
        {
            if (v == null) return 0;
            try { return Convert.ToDouble(v); } catch { return 0; }
        }

        static bool AsBool(object v) { return v is bool && (bool)v; }

        /// <summary>
        /// 读游戏的逐砖时刻表：scrLevelMaker(listFloors) → 每块 scrFloor 的 entryTime / tapsNeeded / midSpin …
        /// 必须**在主线程**调用（Unity API）。
        /// </summary>
        static GameFloorData ReadGameTimeline()
        {
            var gt = new GameFloorData();
            try
            {
                Type t = FindType("scrLevelMaker");
                if (t == null) { gt.Note = "找不到 scrLevelMaker"; QueueLog("[BingChart] 读时刻表失败: " + gt.Note); return null; }
                object lm = GetStaticMember(t, "_instance");
                if (lm == null) lm = GetStaticMember(t, "instance");
                if (lm == null) { gt.Note = "scrLevelMaker 实例为空"; QueueLog("[BingChart] 读时刻表失败: " + gt.Note); return null; }

                var lf = t.GetField("listFloors", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (lf == null) { gt.Note = "找不到 listFloors"; QueueLog("[BingChart] 读时刻表失败: " + gt.Note); return null; }
                var list = lf.GetValue(lm) as System.Collections.IList;
                if (list == null || list.Count == 0) { gt.Note = "listFloors 为空"; QueueLog("[BingChart] 读时刻表失败: " + gt.Note); return null; }

                int n = list.Count;
                gt.Time = new double[n]; gt.Taps = new int[n]; gt.Mid = new bool[n];
                gt.Land = new bool[n]; gt.Seq = new int[n]; gt.Speed = new float[n];
                int got = 0;
                for (int i = 0; i < n; i++)
                {
                    object f = list[i];
                    if (f == null) continue;
                    gt.Time[i] = AsDouble(GetInstanceMember(f, "entryTime"));
                    gt.Taps[i] = (int)Math.Round(AsDouble(GetInstanceMember(f, "tapsNeeded")));
                    gt.Mid[i] = AsBool(GetInstanceMember(f, "midSpin"));
                    gt.Land[i] = AsBool(GetInstanceMember(f, "isLandable"));
                    gt.Seq[i] = (int)Math.Round(AsDouble(GetInstanceMember(f, "seqID")));
                    gt.Speed[i] = (float)AsDouble(GetInstanceMember(f, "speed"));
                    got++;
                }
                if (got == 0) { gt.Note = "listFloors 里读不出字段"; QueueLog("[BingChart] 读时刻表失败: " + gt.Note); return null; }
                int taps2 = 0;
                for (int i = 0; i < n; i++) if (gt.Taps[i] >= 2) taps2++;
                gt.Note = "游戏 scrFloor 时刻表(" + n + " 块，其中 tapsNeeded>=2 的有 " + taps2 + " 块)";
                return gt;
            }
            catch (Exception e)
            {
                gt.Note = "读时刻表出错: " + e.GetType().Name + " " + e.Message;
                QueueLog("[BingChart] " + gt.Note);
                return null;
            }
        }


        public static void StartConvert()
        {
            if (Converting || SongConverted) return;
            if (string.IsNullOrEmpty(CurrentChartPath)) { LastError = "当前关卡没有可用谱面"; return; }
            if (!cfg.Enabled) { LastError = "冰谱功能已关闭（基础设置里可打开）"; return; }

            string chart = CurrentChartPath, audio = CurrentAudioPath, outDir = ConvertDir;
            string ice = Path.Combine(ModPath, "audio", cfg.IceFile);
            string ffmpeg = Ffmpeg.FindExe(GuessGameDir());
            if (!string.IsNullOrEmpty(audio) && ffmpeg == null)
            { LastError = "找不到游戏目录下的 ffmpeg.exe"; return; }
            if (!File.Exists(ice)) { LastError = "找不到冰音: " + ice; return; }

            var opt = new IceOptions
            {
                Volume = cfg.Volume,
                PressSemitone2 = cfg.PressSemitone2,
                PressSemitone3 = cfg.PressSemitone3,
                PressSemitone4 = cfg.PressSemitone4,
                UseAudioPitchInConstant = cfg.UseConstantSegmentPitch,
                ConstantAudioWeight = cfg.ConstantAudioWeight,
                ConstantMaxShift = cfg.ConstantMaxShift,
                DurationTailSec = cfg.TailSeconds,
                ChartOnlyFallback = cfg.ChartOnlyFallback,
                MultiTapUseAudio = cfg.MultiTapUseAudio,
                MultiTapGapMs = cfg.MultiTapGapMs,
                MultiTapMaxCluster = cfg.MultiTapMaxCluster,
                OnsetSensitivity = cfg.OnsetSensitivity,
                KeepOriginal = cfg.MixOverlay,
                OriginalVolume = cfg.OriginalVolume,
                IceVolume = cfg.IceVolume,
                IceLengthMs = cfg.IceLengthMs,
                DensityCompensation = cfg.DensityCompensation,
                ExtraOffsetSec = cfg.ExtraOffsetMs / 1000.0,
                PitchRelativeToOpening = cfg.PitchRelative,
                PitchCompress = cfg.PitchCompress,
                PitchMaxSemitone = cfg.PitchMaxSemitone,
                PitchLowHz = cfg.PitchLowHz,
                PitchHighHz = cfg.PitchHighHz,
                OpeningSeconds = cfg.OpeningPercent / 100.0,
                Progress = (step, p) => { ConvertStep = step; ConvertProgress01 = p; }
            };

            Converting = true;
            ConvertStep = "1/4 解析谱面…";
            ConvertProgress = "解析谱面…";
            ConvertProgress01 = 0.05f;
            LastError = ""; LastReport = "";
            errorDismissed = false;
            var sw = Stopwatch.StartNew();
            QueueLog("[BingChart] 开始转换 谱面=" + chart + " 音频=" + (string.IsNullOrEmpty(audio) ? "无" : audio)
                + " 冰音=" + ice + " ffmpeg=" + (ffmpeg ?? "无") + " → " + outDir);

            // ★★ 先在**主线程**把游戏自己的逐砖时刻表读出来（Unity API 不能在后台线程调）
            var gameTimeline = ReadGameTimeline();
            QueueLog("[BingChart] 游戏时刻表: " + (gameTimeline == null ? "读取失败" : gameTimeline.Note)
                + (gameTimeline != null && gameTimeline.Time != null
                   ? ("  首块 seq=" + gameTimeline.Seq[0] + " t=" + gameTimeline.Time[0].ToString("0.000")
                      + "  末块 t=" + gameTimeline.Time[gameTimeline.Time.Length - 1].ToString("0.000"))
                   : ""));

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var pc = ChartReader.Load(chart);
                    if (pc == null) { LastError = "谱面解析失败（格式不支持）"; return; }

                    // ★ 时间轴：优先用游戏自己算好的逐砖时刻（entryTime）
                    double shift = 0; string tlDetail = "";
                    string tlFail = GameTimelineKit.Apply(pc, gameTimeline, out shift, out tlDetail);
                    if (tlFail == null)
                    {
                        opt.TimelineLabel = "★游戏自带 scrFloor.entryTime";
                        opt.PitchTimeShiftSec = shift;
                        opt.SkipAudioCalibration = true;   // 时刻已经是绝对的，别再按音频重标定
                        opt.MultitapFromGame = true;       // 押数也用游戏的 tapsNeeded，别再跑毫秒启发式
                        QueueLog("[BingChart] 时间轴=游戏自带 entryTime ✓  音高取样偏移="
                            + (shift * 1000).ToString("+0;-0;0") + "ms  " + tlDetail);
                        ConvertStep = "2/4 已取到游戏自带时刻表…";
                    }
                    else
                    {
                        opt.TimelineLabel = "离线解析（游戏时刻表不可用：" + tlFail + "）";
                        opt.PitchTimeShiftSec = 0;
                        opt.SkipAudioCalibration = false;
                        opt.MultitapFromGame = false;
                        QueueLog("[BingChart] ⚠ 时间轴退回离线解析。原因：" + tlFail);
                    }

                    var r = IceChartBuilder.Build(pc, audio, ice, ffmpeg, outDir, opt);
                    if (!r.Ok) { LastError = r.Error; return; }
                    LastReport = r.Chart.Report;
                    ConvertProgress01 = 1f;
                    SongConverted = true;
                }
                catch (Exception e) { LastError = e.GetType().Name + ": " + e.Message; }
                finally
                {
                    Converting = false;
                    ConvertProgress = "完成，用时 " + sw.Elapsed.TotalSeconds.ToString("0.0") + "s";
                    if (LastError.Length == 0)
                    {
                        ConvertStep = "转换完成";
                        QueueLog("[BingChart] 转换完成 用时 " + sw.Elapsed.TotalSeconds.ToString("0.0") + "s："
                            + ConvertProgress + " ｜" + LastReport);
                    }
                    else QueueLog("[BingChart] 转换失败：" + LastError);
                    pendingAttach = true;
                }
            });
        }

        // 后台线程不直接调 UMM 的 Logger（怕不是线程安全的），先存下来由主线程发出去
        static readonly object logLock = new object();
        static readonly List<string> pendingLogs = new List<string>();

        static void QueueLog(string s)
        {
            lock (logLock) { if (pendingLogs.Count < 64) pendingLogs.Add(s); }
        }

        static void FlushLogs()
        {
            string[] arr = null;
            lock (logLock)
            {
                if (pendingLogs.Count == 0) return;
                arr = pendingLogs.ToArray();
                pendingLogs.Clear();
            }
            if (Entry == null) return;
            foreach (string s in arr) Entry.Logger.Log(s);
        }

        internal static void RunPendingAttach()
        {
            if (!SongConverted) return;
            if (!cfg.AutoSwitch)
            {
                Overlay.Toast("冰谱已就绪（未自动切换）");
                return;
            }
            AttachIce();
        }

        // ================= 播放 =================
        //
        // ★ 关键设计：**绝不替换游戏自己的 AudioClip**。
        //   之前用 AudioClip.Create(stream:false) + SetData 造了一个 150 秒 ≈26MB 的内存音频去替换，
        //   游戏用 PlayScheduled(dspTime) 采样级同步，那一刻数据还没上传到音频线程 →
        //   实际起播晚一个不固定的量 → 听感就是"忽前忽后、±3 秒都有"。
        //
        //   现在改成：游戏音乐完全不动；我们另开一路音源播「纯冰音层」，
        //   并且**每帧把我们的播放位置同步到游戏自己的播放进度**（scrConductor 的播放头）。
        //   游戏判定砖块用的就是这个进度，所以冰音天然和砖块同一个时间基准。
        static AudioSource IceSource;
        static AudioClip IceSoloClip;
        public static int songPosMiss;          // 读不到播放进度的次数（诊断用）
        public static string songPosHow = "未尝试";
        public static float lastDriftMs;        // 最近一次测到的偏差（毫秒）
        static int diagFrames;                  // 进关卡后打了多少帧诊断日志
        static double lastGamePos = -1;         // 上一帧的游戏播放进度
        static float lastGameAdvanceAt;         // 游戏播放进度最后一次变化的时刻
        static float lastDriftLog;              // 上次写同步日志的时刻
        static float lastClockDiag;             // 上次写「三个时钟对照」的时刻
        static bool wasMuted;                   // 上一帧是否把游戏音乐静音了
        static float gameSongVolume = 1f;       // 挂载时记住的游戏音乐音量
        static int posUnit;                     // 播放头单位：0=未知 1=秒 2=毫秒

        static void AttachIce()
        {
            StopPlayback();
            string solo = Path.Combine(ConvertDir, "ice_only.wav");
            string mixed = Path.Combine(ConvertDir, "ice.wav");
            string use = File.Exists(solo) ? solo : mixed;
            if (!File.Exists(use)) { LastError = "找不到转换产物"; return; }
            try
            {
                float[] data; int rate, ch, frames;
                rate = Wav.ReadPcm(File.ReadAllBytes(use), out data, out ch, out frames);
                if (rate <= 0 || frames <= 0) { LastError = "冰音文件读不出来"; return; }
                // ★ lengthSamples 是「每声道」的采样数。Wav.ReadPcm 返回的是交错数组（frames*ch），
                //   直接拿 data.Length 当长度会按声道数放大（立体声就长一倍，后半段变静音）。
                IceSoloClip = AudioClip.Create("bingchart_ice", frames, ch, rate, false);
                IceSoloClip.SetData(data, 0);

                var go = new GameObject("BingChart_Ice");
                UnityEngine.Object.DontDestroyOnLoad(go);
                IceSource = go.AddComponent<AudioSource>();
                IceSource.playOnAwake = false;
                IceSource.loop = false;
                IceSource.spatialBlend = 0f;
                IceSource.volume = cfg.IceVolume;
                IceSource.ignoreListenerPause = true;
                IceSource.clip = IceSoloClip;

                GameSong = FindGameSong();
                if (GameSong != null)
                {
                    gameSongVolume = GameSong.volume;
                    if (!cfg.MixOverlay)
                    {
                        // 替换模式：把游戏音乐静音（但仍然在播，用来提供播放头）
                        GameSong.volume = 0f;
                        wasMuted = true;
                    }
                    else wasMuted = false;
                }
                lastGamePos = -1;
                lastGameAdvanceAt = Time.unscaledTime;
                Overlay.Toast(cfg.MixOverlay ? "冰音已就绪（叠加在原曲上）" : "冰音已就绪（原曲已静音）");
                Entry.Logger.Log("[BingChart] 冰音层已挂载: " + Path.GetFileName(use)
                    + " 时长 " + IceSoloClip.length.ToString("0.0") + "s  叠加=" + cfg.MixOverlay
                    + "  播放头来源=" + songPosHow);
            }
            catch (Exception e) { LastError = "挂载冰音失败: " + e.Message; }
        }

        static void StopPlayback()
        {
            if (IceSource != null)
            {
                try { IceSource.Stop(); UnityEngine.Object.Destroy(IceSource.gameObject); } catch { }
                IceSource = null;
            }
            if (GameSong != null)
            {
                if (wasMuted) { try { GameSong.volume = gameSongVolume; } catch { } }  // 恢复游戏音量
                GameSong = null;
            }
            IceSoloClip = null;
            lastPos = -1f;
            diagFrames = 0;
            wasMuted = false;
            lastGamePos = -1;
            posUnit = 0;
        }

        /// <summary>每帧：把我们的播放位置掰到游戏自己的播放进度上。</summary>
        static void SyncPlayback()
        {
            if (IceSource == null || IceSoloClip == null) return;
            if (!cfg.Enabled) { if (IceSource.isPlaying) IceSource.Pause(); return; }
            if (GameSong == null) GameSong = FindGameSong();

            // 叠加模式：游戏音乐原样播；替换模式：把游戏音乐静音（但让它继续播，用它的播放头当基准）
            if (GameSong != null && !cfg.MixOverlay)
            {
                if (GameSong.volume > 0.001f) GameSong.volume = 0f;
                wasMuted = true;
            }
            else if (GameSong != null && cfg.MixOverlay && wasMuted)
            {
                GameSong.volume = gameSongVolume;      // 从替换切回叠加，恢复音量
                wasMuted = false;
            }

            double raw;
            double pos = GameSongPos(out raw);
            float now = Time.unscaledTime;
            double songTime = (GameSong != null) ? GameSong.time : -1.0;

            // ---- 诊断：进关卡后头 300 帧每帧打一条，之后每秒一条 ----
            //   ★ 之前就是「默默什么都不做」，外面完全看不见哪里断了。现在每一帧都有数可查。
            diagFrames++;
            if (diagFrames < 300 || now - lastDriftLog > 1f)
            {
                var sb = new StringBuilder();
                sb.Append("[BingChart] 播放头 ").Append(songPosHow)
                  .Append(" 原始=").Append(raw.ToString("0.###"))
                  .Append(" 选用=").Append(pos.ToString("0.###")).Append("s")
                  .Append(" song.time=").Append(songTime.ToString("0.###"))
                  .Append(" 冰音=").Append(IceSource.time.ToString("0.###"))
                  .Append(" 在播=").Append(IceSource.isPlaying)
                  .Append(" 重定位=").Append(syncJumps);
                if (now - lastClockDiag > 1f)
                {
                    lastClockDiag = now;
                    sb.Append(" ｜").Append(ClockDiag());
                }
                lastDriftLog = now;
                if (Entry != null) Entry.Logger.Log(sb.ToString());
            }

            if (pos < 0) { if (IceSource.isPlaying) IceSource.Pause(); return; }

            pos -= cfg.PlaybackOffsetMs / 1000.0;      // 播放微调
            IceSource.volume = cfg.IceVolume;

            // ---- 游戏音乐还没真正开始播 → 冰音也别出声 ----
            //   用户反馈：「转换完就自己开始播了」。那是因为编辑器静止时那个时钟也在慢慢走
            //   （它很可能是拿全局 dspTime 算出来的），所以必须等音乐真的动了才放。
            //   判据用 song.time（音频源自己的播放头），没找到音频源就放行（fail-open，宁可出声也别哑）。
            bool songStarted = (GameSong == null) || (GameSong.clip == null) || GameSong.time > 0.001f;
            if (!songStarted)
            {
                if (IceSource.isPlaying) IceSource.Pause();
                return;
            }

            // ---- 判「游戏在不在走」----
            //   ★ 不再用 hasSongStarted 硬拦 —— 编辑器里那个字段不一定为真，
            //     上一版就是这样把冰音全程按住了（用户看到"转换成功但一点声都没有"）。
            //
            //   ★★ 关键修复：播放头回退时必须**重置基准**。
            //     用户反馈：转换完冰音开始播了，一点「开始游戏」就再也不出声。
            //     原因是编辑器静止时游戏时钟在慢慢走，lastGamePos 已经涨到很大；
            //     真正开局时时钟归零，`pos > lastGamePos` 就**永远**不成立 →
            //     advancing 永远 false → 冰音被永久按住。
            //     现在只要发现回退（>=0.25s）就把基准重置回去，并当作"重开一局"。
            if (pos < lastGamePos - 0.25)
            {
                restarts++;
                lastGamePos = pos;
                lastGameAdvanceAt = now;
                if (IceSource.isPlaying) { IceSource.Stop(); }   // 回退时分位全错，直接停掉等重新对表
                lastPos = (float)pos;
                if (!hintAcknowledged && !HintNeverAgain && cfg.ShowHintOnFail) UiHintOpen = true;
                return;
            }
            if (pos > lastGamePos + 1e-6) { lastGamePos = pos; lastGameAdvanceAt = now; }
            bool advancing = (now - lastGameAdvanceAt) < 0.5f;

            if (!advancing)
            {
                if (IceSource.isPlaying) IceSource.Pause();
                return;
            }
            if (pos >= IceSoloClip.length - 0.03)
            {
                if (IceSource.isPlaying) IceSource.Stop();
                return;
            }

            if (!IceSource.isPlaying)
            {
                IceSource.time = (float)Mathf.Clamp((float)pos, 0f, IceSoloClip.length - 0.01f);
                IceSource.Play();
                lastPos = (float)pos;
                return;
            }

            // 偏差超过 25ms 就掰回去（游戏判定砖块的精度就是这个量级）
            float drift = (float)pos - IceSource.time;
            lastDriftMs = drift * 1000f;
            if (Mathf.Abs(drift) > 0.025f)
            {
                IceSource.time = (float)Mathf.Clamp((float)pos, 0f, IceSoloClip.length - 0.01f);
                syncJumps++;
            }
            lastPos = (float)pos;
        }

        /// <summary>
        /// 读游戏自己的播放进度（秒）—— 这就是游戏判定砖块用的那个时钟，
        /// 也是 `scrFloor.entryTime` 所在的那个时间基准，所以冰音的落点必须用它。
        ///
        /// 成员名是从游戏 Assembly-CSharp.dll 元数据实测出来的真名：
        ///   scrConductor（实例属性）songposition_minusi   ← 首选
        ///   scrConductor（实例字段）_songposition_minusi   ← 备选（属性背后的字段）
        ///   scrConductor（实例属性）songposition_minusv   ← 再备选
        /// 注意：scrConductor 上**没有** songposition 这个成员，别再退回它。
        ///
        /// ★单位判断只用「范围」，不跟 song.time 比大小。
        ///   因为实测两者差 ~70ms（关卡 offset + 校准），拿它们互比会被这个偏移带偏，
        ///   在刚开播、两个候选都没拉开距离时判错单位 → 整个位置全错。
        ///   范围判定：真实的秒值一定落在曲子长度附近；真实毫秒值只有 ÷1000 才落进去。
        /// </summary>
        static double GameSongPos(out double raw)
        {
            raw = 0;
            try
            {
                object inst = FindConductor();
                if (inst == null) { songPosHow = "无 scrConductor 实例"; songPosMiss++; return -1; }

                string how = "songposition_minusi";
                object v = GetInstanceMember(inst, "songposition_minusi");
                if (v == null)
                {
                    how = "_songposition_minusi";
                    v = GetInstanceMember(inst, "_songposition_minusi");
                }
                if (v == null)
                {
                    how = "songposition_minusv";
                    v = GetInstanceMember(inst, "songposition_minusv");
                }
                if (v == null)
                {
                    if (GameSong != null) { songPosHow = "兜底 song.time"; raw = GameSong.time; return GameSong.time; }
                    songPosHow = "读不到播放头"; songPosMiss++;
                    return -1;
                }

                raw = Convert.ToDouble(v);

                // 单位：只用范围判（IceSoloClip 时长 + 60s 容差）
                double lim = (IceSoloClip != null ? IceSoloClip.length : 600.0) + 60.0;
                double a = Math.Abs(raw);
                if (a <= lim) posUnit = 1;                    // 直接落在曲长范围内 → 秒
                else if (a / 1000.0 <= lim) posUnit = 2;       // 除以 1000 才落进来 → 毫秒
                else if (posUnit == 0) posUnit = 1;            // 两个都不像 → 先按秒，别乱除

                songPosHow = how + (posUnit == 2 ? "(ms)" : "(s)");
                return posUnit == 2 ? raw / 1000.0 : raw;
            }
            catch (Exception e) { songPosHow = "读取出错 " + e.GetType().Name; songPosMiss++; return -1; }
        }

        /// <summary>拿 scrConductor 的单例（静态字段 _instance → 静态属性 instance）。</summary>
        static object FindConductor()
        {
            Type t = FindType("scrConductor");
            if (t == null) return null;
            object inst = GetStaticMember(t, "_instance");
            if (inst == null) inst = GetStaticMember(t, "instance");
            return inst;
        }

        /// <summary>
        /// 把游戏这一帧的三个时钟一起打出来，用来量「固定差多少」：
        ///   minusi = 判定用的时钟（冰音跟的就是它）
        ///   minusv = 视觉用的时钟（画面上砖块落点的时钟）
        ///   song.time = 音频源自己的播放头
        /// 三者之间的差就是「校准偏移」，通常几十毫秒。
        /// </summary>
        static string ClockDiag()
        {
            try
            {
                object inst = FindConductor();
                if (inst == null) return "时钟: 无实例";
                string mi = Num(GetInstanceMember(inst, "songposition_minusi"));
                string mv = Num(GetInstanceMember(inst, "songposition_minusv"));
                string st = (GameSong != null) ? GameSong.time.ToString("0.000") : "无";
                return "minusi=" + mi + " minusv=" + mv + " song.time=" + st;
            }
            catch { return "时钟: 读取异常"; }
        }

        static string Num(object v)
        {
            if (v == null) return "无";
            try { return Convert.ToDouble(v).ToString("0.000"); }
            catch { return "非数字"; }
        }

        // 注：scrConductor 上确实有 hasSongStarted 字段（元数据实测），但**不能拿它当播放门槛** ——
        // 在关卡编辑器里这个字段不一定为真，用它拦会导致冰音全程不出声。
        // 现在只按「播放头有没有在推进」判断游戏在不在走。

        static object GetStaticMember(Type t, string name)
        {
            try
            {
                var fi = t.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null) return fi.GetValue(null);
                var pi = t.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null && pi.CanRead) return pi.GetValue(null, null);
            }
            catch { }
            return null;
        }

        static object GetInstanceMember(object inst, string name)
        {
            try
            {
                var ty = inst.GetType();
                var pi = ty.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null && pi.CanRead && pi.GetIndexParameters().Length == 0)
                    return pi.GetValue(inst, null);
                var mi = ty.GetMethod("get_" + name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi != null && mi.GetParameters().Length == 0)
                    return mi.Invoke(inst, null);
                var fi = ty.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null) return fi.GetValue(inst);
            }
            catch { }
            return null;
        }

        static AudioSource FindGameSong()
        {
            try
            {
                object inst = FindConductor();
                if (inst == null) return null;
                var ty = inst.GetType();
                // 元数据实测：scrConductor 上有 song / song2 / song3 三个 AudioSource 字段，
                // 常规游玩用的是 song；歌曲开始前 song.clip 可能还没设，所以按顺序取第一个带 clip 的。
                AudioSource first = null;
                foreach (string name in new[] { "song", "song2", "song3" })
                {
                    var fi = ty.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (fi == null) continue;
                    var src = fi.GetValue(inst) as AudioSource;
                    if (src == null) continue;
                    if (first == null) first = src;
                    if (src.clip != null) return src;
                }
                return first;
            }
            catch { return null; }
        }

        // ================= 试听 / 音色 =================
        public static void PreviewIce()
        {
            try
            {
                if (PreviewSource == null)
                {
                    var go = new GameObject("BingChart_Preview");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    PreviewSource = go.AddComponent<AudioSource>();
                    PreviewSource.playOnAwake = false;
                    PreviewSource.spatialBlend = 0f;
                    PreviewSource.ignoreListenerPause = true;
                }
                if (PreviewClip == null)
                {
                    float[] data; int rate, ch, frames;
                    rate = Wav.ReadPcm(File.ReadAllBytes(Path.Combine(ModPath, "audio", cfg.IceFile)),
                                      out data, out ch, out frames);
                    if (rate <= 0 || frames <= 0) { LastError = "冰音读不出来"; return; }
                    PreviewClip = AudioClip.Create("bingpreview", frames, ch, rate, false);
                    PreviewClip.SetData(data, 0);
                }
                PreviewSource.clip = PreviewClip;
                PreviewSource.volume = cfg.Volume;
                PreviewSource.time = 0;
                PreviewSource.Play();
            }
            catch (Exception e) { Entry.Logger.Error("试听失败: " + e.Message); }
        }

        /// <summary>
        /// 试播「纯冰音层」（转换产物 ice_only.wav），**完全不接游戏时钟、不用游戏音乐**。
        /// 用途：把「音频这一层本身有没有声音」和「跟游戏时钟同步对不对」分开验证。
        /// 如果这个按钮点了有声音、而进关卡没声音，那问题一定在同步那一段，不在音频。
        /// </summary>
        public static void PreviewSolo()
        {
            try
            {
                string solo = Path.Combine(ConvertDir, "ice_only.wav");
                string mixed = Path.Combine(ConvertDir, "ice.wav");
                string use = File.Exists(solo) ? solo : mixed;
                if (!File.Exists(use)) { LastError = "还没有转换产物，先在关卡里按一次转换快捷键"; return; }
                float[] data; int rate, ch, frames;
                rate = Wav.ReadPcm(File.ReadAllBytes(use), out data, out ch, out frames);
                if (rate <= 0 || frames <= 0) { LastError = "冰音层读不出来"; return; }

                if (SoloPreviewSource == null)
                {
                    var go = new GameObject("BingChart_SoloPreview");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    SoloPreviewSource = go.AddComponent<AudioSource>();
                    SoloPreviewSource.playOnAwake = false;
                    SoloPreviewSource.spatialBlend = 0f;
                    SoloPreviewSource.ignoreListenerPause = true;
                }
                SoloPreviewClip = AudioClip.Create("bingchart_solo", frames, ch, rate, false);
                SoloPreviewClip.SetData(data, 0);

                // 顺便算一下这层到底有没有信号（没信号就是转换那一端的问题）
                float peak = 0f; double sq = 0;
                for (int i = 0; i < data.Length; i++) { float a = Math.Abs(data[i]); if (a > peak) peak = a; sq += (double)data[i] * data[i]; }
                double rms = Math.Sqrt(sq / Math.Max(1, data.Length));

                SoloPreviewSource.Stop();
                SoloPreviewSource.clip = SoloPreviewClip;
                SoloPreviewSource.volume = cfg.IceVolume;
                SoloPreviewSource.time = 0f;
                SoloPreviewSource.Play();

                Overlay.Toast("试播冰音层（不接游戏时钟）：峰值 " + peak.ToString("0.000"));
                Entry.Logger.Log("[BingChart] 试播 ice_only.wav  帧=" + frames + " 声道=" + ch + " 采样率=" + rate
                    + " 峰值=" + peak.ToString("0.0000") + " RMS=" + rms.ToString("0.0000")
                    + " 音量=" + cfg.IceVolume.ToString("0.00"));
                if (peak < 0.0005) LastError = "冰音层几乎是静音（峰值 " + peak.ToString("0.0000") + "）";
            }
            catch (Exception e) { LastError = "试播失败: " + e.Message; }
        }

        public static void CycleIce()
        {
            try
            {
                string dir = Path.Combine(ModPath, "audio");
                var list = new List<string>();
                foreach (string f in Directory.GetFiles(dir, "*.wav")) list.Add(Path.GetFileName(f));
                list.Sort(StringComparer.OrdinalIgnoreCase);
                if (list.Count == 0) return;
                int i = list.IndexOf(cfg.IceFile);
                cfg.IceFile = list[(i + 1) % list.Count];
                PreviewClip = null;
                Overlay.Toast("音色 → " + cfg.IceFile);
            }
            catch { }
        }

        // ================= UI 辅助 =================
        /// <summary>切换界面语言（以前这里只存了个配置，什么都没变 —— 用户反馈"改语言没反应"）。</summary>
        public static void ApplyLanguage()
        {
            Loc.Lang = cfg.LangIndex;
            SaveConfig();
            Overlay.Toast("语言 → " + LangName());
        }

        static string LangName()
        {
            switch (cfg.LangIndex)
            {
                case 1: return "English";
                case 2: return "한국어";
                default: return "简体中文";
            }
        }

        public static string HotkeyText() { return KeyName(cfg.HotkeyMain) + " + " + KeyName(cfg.HotkeySub); }

        static string KeyName(string k)
        {
            switch (k)
            {
                case "LeftControl": return "左Ctrl";
                case "RightControl": return "右Ctrl";
                case "LeftShift": return "左Shift";
                case "RightShift": return "右Shift";
                case "LeftAlt": return "左Alt";
                case "RightAlt": return "右Alt";
                case "Tab": return "Tab";
                case "Space": return "空格";
                default: return k;
            }
        }

        public static void AcknowledgeHint()
        {
            // 只关掉「本次启动」的提示，不写盘。想永久关闭要勾「不再提醒」。
            hintAcknowledged = true;
        }

        /// <summary>把首次提示恢复成「还没看过」的状态（设置页的按钮用）。</summary>
        public static void ResetHint()
        {
            hintAcknowledged = false;
            HintNeverAgain = false;
            SaveConfig();
            UiHintOpen = true;
            Overlay.Toast("首次提示已重新打开");
        }

        /// <summary>全部重置成出厂默认（保留语言和「不再提醒」这种界面偏好）。</summary>
        public static void ResetAll()
        {
            int lang = cfg.LangIndex;
            bool never = HintNeverAgain;
            cfg = new Config();
            cfg.LangIndex = lang;
            HintNeverAgain = never;
            hintAcknowledged = false;
            SaveConfig();
            Overlay.Toast("已全部恢复默认设置");
            if (Entry != null) Entry.Logger.Log("[BingChart] 全部设置已重置为默认");
        }

        /// <summary>只重置某一组设置成默认值。section 见 Overlay 里各分区的 id。</summary>
        public static void ResetSection(string section)
        {
            var d = new Config();
            switch (section)
            {
                case "basic":
                    cfg.Enabled = d.Enabled; cfg.Volume = d.Volume;
                    cfg.MixOverlay = d.MixOverlay; cfg.OriginalVolume = d.OriginalVolume;
                    cfg.IceVolume = d.IceVolume; cfg.IceFile = d.IceFile;
                    cfg.AutoSwitch = d.AutoSwitch; cfg.ShowHintOnFail = d.ShowHintOnFail;
                    cfg.HotkeyMain = d.HotkeyMain; cfg.HotkeySub = d.HotkeySub;
                    break;
                case "pitch":
                    cfg.PitchRelative = d.PitchRelative; cfg.PitchCompress = d.PitchCompress;
                    cfg.PitchMaxSemitone = d.PitchMaxSemitone; cfg.PitchLowHz = d.PitchLowHz;
                    cfg.PitchHighHz = d.PitchHighHz; cfg.OpeningPercent = d.OpeningPercent;
                    break;
                case "multi":
                    cfg.PressSemitone2 = d.PressSemitone2; cfg.PressSemitone3 = d.PressSemitone3;
                    cfg.PressSemitone4 = d.PressSemitone4; cfg.MultiTapUseAudio = d.MultiTapUseAudio;
                    cfg.MultiTapGapMs = d.MultiTapGapMs; cfg.MultiTapMaxCluster = d.MultiTapMaxCluster;
                    break;
                case "sound":
                    cfg.IceLengthMs = d.IceLengthMs; cfg.DensityCompensation = d.DensityCompensation;
                    break;
                case "sync":
                    cfg.ExtraOffsetMs = d.ExtraOffsetMs; cfg.PlaybackOffsetMs = d.PlaybackOffsetMs;
                    break;
                case "convert":
                    cfg.ChartOnlyFallback = d.ChartOnlyFallback; cfg.TailSeconds = d.TailSeconds;
                    cfg.OnsetSensitivity = d.OnsetSensitivity;
                    break;
                case "ui":
                    cfg.LangIndex = d.LangIndex;
                    break;
            }
            SaveConfig();
            Overlay.Toast("这一组已恢复默认");
        }

        public static string HintStateText()
        {
            if (HintNeverAgain) return "已勾选「不再提醒」，不会再出现";
            if (hintAcknowledged) return "本次已看过（下次启动游戏还会提示）";
            return "还没看过（下次进关卡就会提示）";
        }

        public static string DiagText()
        {
            // 每一小段都单独过 Loc.T：整串是拼起来的，只有逐段翻译才认得出来
            var sb = new StringBuilder();
            sb.Append(Loc.T("版本 ")).Append(Version).Append(Loc.T("　心跳 ")).Append(Heartbeat).Append('\n');
            sb.Append(Loc.T("场景 ")).Append(lastScene).Append(Loc.T("　关卡 "))
              .Append(Loc.T(string.IsNullOrEmpty(CurrentSongKey) ? "未绑定" : "已绑定")).Append('\n');
            if (!string.IsNullOrEmpty(CurrentSongKey))
            {
                sb.Append(Loc.T("曲名 ")).Append(CurrentSongTitle).Append('\n');
                sb.Append(Loc.T("谱面 ")).Append(Path.GetFileName(CurrentChartPath))
                  .Append(Loc.T("　音频 "))
                  .Append(string.IsNullOrEmpty(CurrentAudioPath) ? Loc.T("无") : Path.GetFileName(CurrentAudioPath)).Append('\n');
                sb.Append(Loc.T("产物 ")).Append(ConvertDir).Append('\n');
            }
            sb.Append(Loc.T("状态 "))
              .Append(Loc.T(Converting ? "转换中" : (SongConverted ? "冰谱播放中" : "未转换")))
              .Append(Loc.T("失败重进 ")).Append(restarts).Append(Loc.T("　重定位 "))
              .Append(syncJumps).Append('\n');
            sb.Append(Loc.T("播放头 ")).Append(Loc.T(songPosHow))
              .Append(songPosMiss > 0 ? (Loc.T("（读不到 ") + songPosMiss + Loc.T(" 次）")) : Loc.T("（正常）"))
              .Append(Loc.T("　偏差 ")).Append(lastDriftMs.ToString("+0;-0;0")).Append("ms");
            if (!string.IsNullOrEmpty(LastReport)) sb.Append('\n').Append(LastReport);
            if (!string.IsNullOrEmpty(LastError)) sb.Append('\n').Append(Loc.T("错误: ")).Append(Loc.T(LastError));
            return sb.ToString();
        }

        public static void ClearTemp()
        {
            try
            {
                string root = Path.Combine(Path.GetTempPath(), "BingChart");
                if (Directory.Exists(root)) Directory.Delete(root, true);
                StopPlayback();
                SongConverted = false;
                Overlay.Toast("已清除转换缓存");
                Entry.Logger.Log("[BingChart] 清除 %temp%\\BingChart");
            }
            catch (Exception e) { LastError = "清除失败: " + e.Message; }
        }

        public static void OpenTempDir()
        {
            try
            {
                string root = Path.Combine(Path.GetTempPath(), "BingChart");
                Directory.CreateDirectory(root);
                Process.Start("explorer.exe", root);
            }
            catch (Exception e) { LastError = "打不开目录: " + e.Message; }
        }

        /// <summary>用系统默认浏览器打开一个链接（失败也不抛，只写日志）。</summary>
        public static void OpenUrl(string url)
        {
            try
            {
                Application.OpenURL(url);
                Overlay.Toast("已在浏览器打开");
                Entry.Logger.Log("[BingChart] 打开链接 " + url);
            }
            catch (Exception e)
            {
                LastError = "打不开链接: " + e.Message;
                try { Process.Start(url); } catch { }
            }
        }

        // ================= UMM 面板：全部自己绘制 =================
        // UMM 皮肤按钮内边距大 + 面板实际宽度不是固定的（实测能到 1000px 以上），
        // 所以不能按固定宽度排版，也不能用 GUILayout 的按钮：文字会被切、会被拉得很散。
        // 做法：先拿到"这一块能用多大"，再在一个限宽的左边栏里逐像素画。
        const float ContentMaxW = 620f;   // 内容最宽这么多，再宽也不拉伸
        const float PanelH = 190f;

        static readonly Color CTxt = new Color(0.91f, 0.95f, 0.99f, 1f);
        static readonly Color CDim = new Color(0.60f, 0.68f, 0.78f, 1f);
        static readonly Color CAcc = new Color(0.50f, 0.86f, 1.00f, 1f);
        static readonly Color CBtn = new Color(0.19f, 0.37f, 0.53f, 1f);
        static readonly Color CBtnHover = new Color(0.26f, 0.50f, 0.69f, 1f);
        static readonly Color CSegOn = new Color(0.22f, 0.47f, 0.66f, 1f);
        static readonly Color CSegOff = new Color(0.14f, 0.17f, 0.22f, 1f);
        static readonly Color CSegHover = new Color(0.20f, 0.26f, 0.34f, 1f);

        public static void OnGUI(UnityModManager.ModEntry mod)
        {
            var area = GUILayoutUtility.GetRect(1f, PanelH, GUILayout.ExpandWidth(true));
            var r = new Rect(area.x, area.y, Mathf.Min(area.width, ContentMaxW), PanelH);
            DrawPanel(r);
            SaveConfig();
        }

        static void DrawPanel(Rect r)
        {
            Icons.RoundPanel(r, 10f, new Color(0.105f, 0.135f, 0.185f, 1f), 2f,
                             new Color(0.40f, 0.72f, 0.92f, 0.35f));
            float w = r.width, pad = 12f;

            // ---- 第一行：冰方块(52) + 名字(26px) + 版本 ----
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(r.x + pad + 2, r.y + 12, 52, 52), Icons.CubeBig);
            var stName = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            stName.normal.textColor = CTxt;
            GUI.Label(new Rect(r.x + pad + 64, r.y + 14, w - pad * 2 - 68, 34), Loc.T("冰谱"), stName);
            var stVer = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            stVer.normal.textColor = CDim;
            GUI.Label(new Rect(r.x + pad + 66, r.y + 48, w - pad * 2 - 70, 20),
                      "Bing Chart · v" + Version + "    A Dance of Fire and Ice", stVer);

            // ---- 第二行：语言（文A 图标 + 三个大一点的方块，限宽不拉伸）----
            float ly = r.y + 78;
            GUI.DrawTexture(new Rect(r.x + pad + 4, ly + 6, 26, 26), Icons.LangA);
            var stLbl = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
            stLbl.normal.textColor = CDim;
            GUI.Label(new Rect(r.x + pad + 38, ly + 8, 62, 22), Loc.T("语言"), stLbl);

            float segX = r.x + pad + 92;
            float segW = 108f, segH = 36f, segGap = 8f;
            // 用 KR 而不是 한：中文字体（雅黑/宋体/黑体/等线）全都没有韩文字形，
            // 韩文只在 malgun.ttf 里。Unity 的系统字体回退能否同时找到中文+韩文字体无法保证，
            // 一旦找不到「한」就是一个方框。纯字母的 KR 在任何字体里都有。
            string[] labels = { "中", "EN", "KR" };
            string[] tips = { "简体中文", "English", "한국어" };
            for (int i = 0; i < 3; i++)
            {
                var b = new Rect(segX + (segW + segGap) * i, ly + 2, segW, segH);
                bool sel = cfg.LangIndex == i;
                bool hov = b.Contains(Event.current.mousePosition);
                Icons.RoundPanel(b, 7f, sel ? CSegOn : (hov ? CSegHover : CSegOff),
                                 sel ? 2f : 0f, CAcc);
                bool korean = i == 2;
                if (korean && Icons.LangKo != null)
                {
                    // 韩文用预渲染的透明图（白色），这里只染色
                    float g = 24f;
                    GUI.color = sel ? Color.white : CDim;
                    GUI.DrawTexture(new Rect(b.x + (b.width - g) * 0.5f, b.y + (b.height - g) * 0.5f, g, g),
                                    Icons.LangKo);
                    GUI.color = Color.white;
                }
                else
                {
                    var st = new GUIStyle(GUI.skin.label) { fontSize = 19, alignment = TextAnchor.MiddleCenter };
                    st.normal.textColor = sel ? Color.white : CDim;
                    if (sel) st.fontStyle = FontStyle.Bold;
                    GUI.Label(b, labels[i], st);
                }
                if (hov && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    cfg.LangIndex = i; Event.current.Use(); SaveConfig();
                    Overlay.Toast("语言 → " + tips[i]);
                }
            }

            // ---- 第三行：设置按钮（限宽，不铺满整行）----
            var br = new Rect(r.x + pad + 2, r.y + 126, 260f, 46f);
            bool bh = br.Contains(Event.current.mousePosition);
            Icons.RoundPanel(br, 10f, bh ? CBtnHover : CBtn, 2f,
                             bh ? CAcc : new Color(0.45f, 0.75f, 0.95f, 0.45f));
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(br.x + 22, br.y + 11, 24, 24), Icons.Gear);
            var stBtn = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            stBtn.normal.textColor = Color.white;
            GUI.Label(new Rect(br.x + 56, br.y, 180, br.height), Loc.T("设置"), stBtn);
            if (bh && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            { UiSettingsOpen = true; Event.current.Use(); }

            // ---- 状态行 ----
            var stSt = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.MiddleLeft };
            stSt.normal.textColor = CDim;
            string line = UiSettingsOpen ? Loc.T("设置页已在游戏画面打开")
                : (string.IsNullOrEmpty(CurrentSongKey) ? Loc.T("进关卡后按 ") + HotkeyText() + Loc.T(" 转冰谱")
                   : (CurrentSongTitle.Length > 0 ? CurrentSongTitle : CurrentSongKey));
            GUI.Label(new Rect(r.x + pad + 4, r.y + 178, w - pad * 2 - 8, 22), line, stSt);
        }

        public static void OnSaveGUI(UnityModManager.ModEntry mod) { SaveConfig(); }

        // ================= 反射 / 文件 =================
        static Type FindType(string name)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(name);
                if (t != null) return t;
            }
            return null;
        }

        static string CallString(object target, string method)
        {
            if (target == null) return null;
            try
            {
                var mi = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public);
                return mi == null ? null : mi.Invoke(target, null) as string;
            }
            catch { return null; }
        }

        public static string FindChart(string dir)
        {
            try
            {
                string[] f = Directory.GetFiles(dir, "*.adofai");
                if (f.Length > 0) return f[0];
                f = Directory.GetFiles(dir, "*.adofa");
                if (f.Length > 0) return f[0];
            }
            catch { }
            return null;
        }

        public static string FindAudio2(string dir)
        {
            try
            {
                string[] f = Directory.GetFiles(dir, "*.mp3");
                if (f.Length > 0) return f[0];
            }
            catch { }
            return null;
        }

        public static string FindAudio(string dir)
        {
            try
            {
                string[] f = Directory.GetFiles(dir, "*.ogg");
                if (f.Length > 0) return f[0];
            }
            catch { }
            return null;
        }

        static string MakeKey(string chartPath, string title)
        {
            string s = !string.IsNullOrEmpty(title) ? title : Path.GetFileNameWithoutExtension(chartPath);
            return SafeName(s);
        }

        public static string SafeName(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s) if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c > 127) sb.Append(c);
            string k = sb.ToString().Trim();
            return k.Length > 60 ? k.Substring(0, 60) : k;
        }

        public static string GuessGameDir()
        {
            try
            {
                string p = ModPath;
                for (int i = 0; i < 8 && !string.IsNullOrEmpty(p); i++)
                {
                    p = Path.GetDirectoryName(p);
                    if (p == null) break;
                    if (Directory.Exists(Path.Combine(p, "ffmpeg"))) return p;
                }
            }
            catch { }
            return null;
        }

        public static KeyCode ParseKey(string name)
        {
            if (string.IsNullOrEmpty(name)) return KeyCode.None;
            try { return (KeyCode)Enum.Parse(typeof(KeyCode), name); }
            catch { return KeyCode.None; }
        }

        // ================= 配置 =================
        static string ConfigPath { get { return Path.Combine(ModPath, "config.json"); } }
        static string lastJson = null;

        static void LoadConfig()
        {
            try
            {
                if (!File.Exists(ConfigPath)) { SaveConfig(); return; }
                string j = File.ReadAllText(ConfigPath, Encoding.UTF8);
                cfg.Enabled = GetBool(j, "enabled", cfg.Enabled);
                cfg.Volume = (float)GetNum(j, "volume", cfg.Volume);
                cfg.IceFile = GetStr(j, "iceFile", cfg.IceFile);
                cfg.IceLengthMs = (float)GetNum(j, "iceLenMs", cfg.IceLengthMs);
                cfg.DensityCompensation = (float)GetNum(j, "densityComp", cfg.DensityCompensation);
                cfg.ExtraOffsetMs = (float)GetNum(j, "offsetMs", cfg.ExtraOffsetMs);
                cfg.PlaybackOffsetMs = (float)GetNum(j, "playOffsetMs", cfg.PlaybackOffsetMs);
                cfg.PitchRelative = GetBool(j, "pitchRel", cfg.PitchRelative);
                cfg.PitchCompress = (float)GetNum(j, "pitchComp", cfg.PitchCompress);
                cfg.PitchMaxSemitone = (float)GetNum(j, "pitchMax", cfg.PitchMaxSemitone);
                cfg.PitchLowHz = (float)GetNum(j, "pitchLo", cfg.PitchLowHz);
                cfg.PitchHighHz = (float)GetNum(j, "pitchHi", cfg.PitchHighHz);
                cfg.OpeningPercent = (float)GetNum(j, "pitchOpenPct", cfg.OpeningPercent);
                cfg.HotkeyMain = GetStr(j, "hotkeyMain", cfg.HotkeyMain);
                cfg.HotkeySub = GetStr(j, "hotkeySub", cfg.HotkeySub);
                cfg.LangIndex = (int)GetNum(j, "lang", cfg.LangIndex);
                cfg.AutoSwitch = GetBool(j, "autoSwitch", cfg.AutoSwitch);
                cfg.ShowHintOnFail = GetBool(j, "showHint", cfg.ShowHintOnFail);
                cfg.PressSemitone2 = (float)GetNum(j, "press2", cfg.PressSemitone2);
                cfg.PressSemitone3 = (float)GetNum(j, "press3", cfg.PressSemitone3);
                cfg.PressSemitone4 = (float)GetNum(j, "press4", cfg.PressSemitone4);
                cfg.UseConstantSegmentPitch = GetBool(j, "constantPitch", cfg.UseConstantSegmentPitch);
                cfg.ConstantAudioWeight = (float)GetNum(j, "constantAudioW", cfg.ConstantAudioWeight);
                cfg.ConstantMaxShift = (float)GetNum(j, "constantMaxShift", cfg.ConstantMaxShift);
                cfg.MultiTapUseAudio = GetBool(j, "mtAudio", cfg.MultiTapUseAudio);
                cfg.MultiTapGapMs = (float)GetNum(j, "mtGapMs", cfg.MultiTapGapMs);
                cfg.MultiTapMaxCluster = (int)GetNum(j, "mtMaxCluster", cfg.MultiTapMaxCluster);
                cfg.ChartOnlyFallback = GetBool(j, "chartOnly", cfg.ChartOnlyFallback);
                cfg.TailSeconds = (float)GetNum(j, "tail", cfg.TailSeconds);
                cfg.OnsetSensitivity = (float)GetNum(j, "onsetSens", cfg.OnsetSensitivity);
                FirstRun = (int)GetNum(j, "firstRun", 0);
                Loc.Lang = cfg.LangIndex;
                HintNeverAgain = GetBool(j, "hintNever", false);
                // ★「知道了」只关掉**本次启动**的提示，不写进配置。
                //   只有勾了「不再提醒」才永久关闭。
                //   （旧版把 hintAck 存了盘，用户点过一次「知道了」以后就再也看不到提示了 —— 用户反馈的正是这个。）
                hintAcknowledged = false;
                if (cfg.Volume < 0) cfg.Volume = 0;
                if (cfg.Volume > 1) cfg.Volume = 1;
                if (cfg.LangIndex < 0 || cfg.LangIndex > 2) cfg.LangIndex = 0;
            }
            catch { }
        }

        public static void SaveConfig()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("{\n");
                B(sb, "enabled", cfg.Enabled);
                N(sb, "volume", cfg.Volume);
                S(sb, "iceFile", cfg.IceFile);
                N(sb, "iceLenMs", cfg.IceLengthMs);
                N(sb, "densityComp", cfg.DensityCompensation);
                N(sb, "offsetMs", cfg.ExtraOffsetMs);
                N(sb, "playOffsetMs", cfg.PlaybackOffsetMs);
                B(sb, "pitchRel", cfg.PitchRelative);
                N(sb, "pitchComp", cfg.PitchCompress);
                N(sb, "pitchMax", cfg.PitchMaxSemitone);
                N(sb, "pitchLo", cfg.PitchLowHz);
                N(sb, "pitchHi", cfg.PitchHighHz);
                N(sb, "pitchOpenPct", cfg.OpeningPercent);
                S(sb, "hotkeyMain", cfg.HotkeyMain);
                S(sb, "hotkeySub", cfg.HotkeySub);
                N(sb, "lang", cfg.LangIndex);
                B(sb, "autoSwitch", cfg.AutoSwitch);
                B(sb, "showHint", cfg.ShowHintOnFail);
                N(sb, "press2", cfg.PressSemitone2);
                N(sb, "press3", cfg.PressSemitone3);
                N(sb, "press4", cfg.PressSemitone4);
                B(sb, "constantPitch", cfg.UseConstantSegmentPitch);
                N(sb, "constantAudioW", cfg.ConstantAudioWeight);
                N(sb, "constantMaxShift", cfg.ConstantMaxShift);
                B(sb, "mtAudio", cfg.MultiTapUseAudio);
                N(sb, "mtGapMs", cfg.MultiTapGapMs);
                N(sb, "mtMaxCluster", cfg.MultiTapMaxCluster);
                B(sb, "chartOnly", cfg.ChartOnlyFallback);
                N(sb, "tail", cfg.TailSeconds);
                N(sb, "onsetSens", cfg.OnsetSensitivity);
                N(sb, "firstRun", FirstRun);
                B(sb, "hintNever", HintNeverAgain);
                sb.Append("\n}");
                string json = sb.ToString();
                if (json == lastJson) return;
                lastJson = json;
                File.WriteAllText(ConfigPath, json, Encoding.UTF8);
            }
            catch { }
        }

        static void B(StringBuilder sb, string k, bool v) { sb.Append("  \"").Append(k).Append("\": ").Append(v ? "true" : "false").Append(",\n"); }
        static void N(StringBuilder sb, string k, float v) { sb.Append("  \"").Append(k).Append("\": ").Append(v.ToString("0.###", CultureInfo.InvariantCulture)).Append(",\n"); }
        static void N(StringBuilder sb, string k, int v) { sb.Append("  \"").Append(k).Append("\": ").Append(v).Append(",\n"); }
        static void S(StringBuilder sb, string k, string v)
        { sb.Append("  \"").Append(k).Append("\": \"").Append((v ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\",\n"); }

        static bool GetBool(string j, string k, bool def)
        {
            var m = System.Text.RegularExpressions.Regex.Match(j, "\"" + k + "\"\\s*:\\s*(true|false)");
            return m.Success ? m.Groups[1].Value == "true" : def;
        }
        static double GetNum(string j, string k, double def)
        {
            var m = System.Text.RegularExpressions.Regex.Match(j, "\"" + k + "\"\\s*:\\s*(-?[0-9]*\\.?[0-9]+)");
            if (!m.Success) return def;
            double d;
            return double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : def;
        }
        static string GetStr(string j, string k, string def)
        {
            var m = System.Text.RegularExpressions.Regex.Match(j, "\"" + k + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : def;
        }
    }
}
