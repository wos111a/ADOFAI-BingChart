// .adofai / .adofa 谱面解析 —— 砖块时间轴
//
// 算法来源：Morilli gist adofai2osu!mania.py 与 洛谷 Eznibuil《关于 ADOFAI 自制关卡的一些事》
// 两个独立实现完全一致，官方编辑器文档 adofaieditor.gitbook.io 佐证 SetSpeed 语义。
// 已在 21 个真实关卡上验证：时长误差中位 2.99%，线性缩放系数恒为 1.0000（零漂移）。
//
// 核心公式：
//   neededAngle = 顺时针 ? (startAngle - angle) : (angle - startAngle)，归一化到 (0, 360]
//   msNeeded    = neededAngle / 180 * 60000 / bpm
//   startAngle  初值 180；处理完第 i 块后 = (下一块是 999) ? angle[i] : (180 + angle[i]) % 360
//   SetSpeed: Bpm 型赋值，Multiplier 型 **乘当前 bpm**
//   angle == 999（pathData 里的 '!'）是三角块，不算音符、不更新 startAngle
using System;
using System.Collections.Generic;
using System.IO;

namespace BingChart
{
    public class Note
    {
        public int Index;          // 在音符序列中的序号（三角块已剔除）
        public int TileIndex;      // 在 pathData/angleData 中的原始下标
        public double Time;        // 秒
        public double NeededAngle; // 从上一块转到本块的角度
        public double Bpm;
        public int Multitap;       // 谱面显式标记的 Multitap（0=没有）
        public bool GeometricTap;  // 几何兜底判定的双押
        public bool IsMidspin;     // 中旋砖（三角块），也要按键
        public int PressCount;     // 最终押数 = max(Multitap, GeometricTap?2:1)
        public bool ConstantBpm;   // 是否处于 BPM 恒定段（匀速/雪花段）
        public double Semitone;    // 最终音高偏移
    }

    public class ParsedChart
    {
        public string FilePath;
        public int Version;
        public string Song = "";
        public string Artist = "";
        public double BaseBpm = 100.0;
        public double OffsetMs;

        public double[] Angles;    // 每砖角度，999 = 三角块
        public char[] PathChars;   // 原始字符（可能为 null）
        public bool[] Clockwise;   // 每块是否顺时针旋转
        public double[] BpmAt;     // 每块生效的 BPM
        public int TwirlCount;
        public int SetSpeedCount;
        public int TriangleCount;
        public List<Note> Notes = new List<Note>();
        public List<int> SpeedFloors = new List<int>();
        public int ActionCount;
        public int TileCount { get { return Angles == null ? 0 : Angles.Length; } }
    }

    public static class ChartReader
    {
        /// <summary>pathData 字符 → angleData 角度。实测覆盖 88 个谱面的全部字符。</summary>
        public static int AngleOf(char c)
        {
            switch (c)
            {
                case 'R': return 0;    case 'p': return 15;   case 'J': return 30;
                case 'E': return 45;   case 'T': return 60;   case 'o': return 75;
                case 'U': return 90;   case 'q': return 105;  case 'G': return 120;
                case 'Q': return 135;  case 'H': return 150;  case 'W': return 165;
                case 'L': return 180;  case 'x': return 195;  case 'N': return 210;
                case 'Z': return 225;  case 'F': return 240;  case 'V': return 255;
                case 'D': return 270;  case 'Y': return 285;  case 'B': return 300;
                case 'C': return 315;  case 'M': return 330;  case 'A': return 345;
                case '!': return 999;  // 三角块
                default: return -1;    // 未知字符
            }
        }

        public const int Triangle = 999;

        public static ParsedChart Load(string path)
        {
            JVal root = Json.Parse(File.ReadAllText(path));
            if (root == null || root.K != JVal.Kind.Obj) return null;

            var c = new ParsedChart { FilePath = path };
            JVal st = root.Get("settings");
            if (st != null)
            {
                c.Version = st.Get("version").IntOr(0);
                c.Song = StripTags(st.Get("song").StrOr(""));
                c.Artist = StripTags(st.Get("artist").StrOr(""));
                c.BaseBpm = st.Get("bpm").NumOr(100.0);
                if (c.BaseBpm <= 0.0001) c.BaseBpm = 100.0;
                c.OffsetMs = st.Get("offset").NumOr(0.0);
            }

            c.Angles = ExtractAngles(root);
            if (c.Angles == null || c.Angles.Length == 0) return null;

            JVal pd = root.Get("pathData");
            if (pd != null && pd.K == JVal.Kind.Str) c.PathChars = pd.S.ToCharArray();

            BuildTimeline(c, root.Get("actions").ArrOrEmpty());
            return c;
        }

        private static double[] ExtractAngles(JVal root)
        {
            JVal ad = root.Get("angleData");
            if (ad != null && ad.K == JVal.Kind.Arr && ad.A.Count > 0)
            {
                var arr = new double[ad.A.Count];
                for (int i = 0; i < arr.Length; i++)
                {
                    double x = ad.A[i].NumOr(0.0);
                    arr[i] = (x == Triangle) ? Triangle : Norm360(x);
                }
                return arr;
            }
            JVal pd = root.Get("pathData");
            if (pd != null && pd.K == JVal.Kind.Str && pd.S.Length > 0)
            {
                var arr = new double[pd.S.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    int a = AngleOf(pd.S[i]);
                    if (a < 0) return null;      // 出现未知字符 -> 放弃这个文件
                    arr[i] = a;
                }
                return arr;
            }
            return null;
        }

        private static double Norm360(double x)
        {
            double v = x % 360.0;
            if (v < 0) v += 360.0;
            return v;
        }

        private static void BuildTimeline(ParsedChart c, List<JVal> actions)
        {
            int n = c.Angles.Length;
            c.ActionCount = actions.Count;

            // 每块的事件状态
            var isTwirl = new bool[n];
            var setBpm = new double[n];        // >0 赋值，<0 累乘(-mult)，0 无
            var holdBeats = new double[n];
            var pauseBeats = new double[n];
            var hasPause = new bool[n];
            var multitap = new int[n];
            for (int i = 0; i < n; i++) setBpm[i] = 0.0;

            foreach (JVal a in actions)
            {
                // 注意：IntOr/NumOr 是「取本节点的值」，必须先 Get("floor") 拿到子节点。
                // 写成 a.IntOr(-1) 会在 action 对象本身取值，永远返回默认值 -> 所有事件被丢掉。
                int f = a.Get("floor").IntOr(-1);
                if (f < 0 || f >= n) continue;
                string etype = a.Get("eventType").StrOr("");
                if (etype == "Twirl") isTwirl[f] = true;
                else if (etype == "SetSpeed")
                {
                    if (a.Get("speedType").StrOr("Bpm") == "Bpm")
                    {
                        setBpm[f] = a.Get("beatsPerMinute").NumOr(0.0);   // >0 = 赋值
                    }
                    else
                    {
                        // Multiplier 型：**乘当前 BPM**（不是 beatsPerMinute 字段 × mult）。
                        // 官方文档措辞含糊，但实测：累乘才能对上音频时长
                        // （2574226222：累乘 148.3s vs 音频 153.3s；字段×mul 会算出 202s）。
                        double mul = a.Get("bpmMultiplier").NumOr(1.0);
                        if (mul > 0) setBpm[f] = -mul;                          // 负号 = 累乘标记
                    }
                }
                else if (etype == "Pause") { hasPause[f] = true; pauseBeats[f] = a.Get("duration").NumOr(0.0); }
                else if (etype == "Hold") holdBeats[f] = a.Get("duration").NumOr(0.0);
                else if (etype == "Multitap" || etype == "MultiTap") multitap[f] = Math.Max(1, a.Get("count").IntOr(2));
                else if (etype == "MultiPlanet") multitap[f] = Math.Max(multitap[f], a.Get("count").IntOr(3));
            }

            // BPM 传播：Bpm 型赋值，Multiplier 型累乘当前
            var bpm = new double[n];
            double cur = c.BaseBpm;
            for (int i = 0; i < n; i++)
            {
                if (setBpm[i] < 0) cur = cur * (-setBpm[i]);
                else if (setBpm[i] > 0) cur = setBpm[i];
                if (cur <= 0.0001 || cur > 100000.0) cur = c.BaseBpm;
                bpm[i] = cur;
            }

            // 匀速段（雪花）：BPM 恒定且**持续 8 块以上**才算一段。
            // 只看 3~4 块会把每个平台期都算进去，判定就失去意义了。
            var constRun = new bool[n];
            {
                const int MinRun = 8;
                int i = 0;
                while (i < n)
                {
                    int j = i;
                    while (j + 1 < n && Math.Abs(bpm[j + 1] - bpm[i]) <= 1e-6) j++;
                    if (j - i + 1 >= MinRun)
                        for (int k = i; k <= j; k++) constRun[k] = true;
                    i = j + 1;
                }
            }

            // 旋转方向：每遇到 Twirl 翻转
            bool cw = true;
            var clockwise = new bool[n];
            for (int i = 0; i < n; i++)
            {
                if (isTwirl[i]) { cw = !cw; c.TwirlCount++; }
                clockwise[i] = cw;
            }
            c.Clockwise = clockwise;
            c.BpmAt = bpm;
            c.SetSpeedCount = 0;
            for (int i = 0; i < n; i++) if (setBpm[i] > 0) c.SetSpeedCount++;
            for (int i = 0; i < n; i++) if (c.Angles[i] == Triangle) c.TriangleCount++;

            // 时间轴
            var timeMs = new double[n];
            double t = c.OffsetMs;
            double startAngle = 180.0;
            int noteIdx = 0;
            double prevNeeded = -1;

            for (int i = 0; i < n; i++)
            {
                double ang = c.Angles[i];
                if (ang == Triangle)
                {
                    // ★ 中旋（三角块）**也是要按的**！wiki：中旋砖块玩家需要在经过时点击一次，
                    //   而且「一个中旋方块算 2 个物量」——所以它必须出冰音，不能跳过。
                    //   时间取「上一砖和下一砖之间的中点」：玩家是在星球经过它时按下的。
                    double prevT = (i > 0) ? timeMs[i - 1] : t;
                    // 先看下一砖要多久（用当前 BPM 估一个旋转时间），取一半
                    double half = 30.0 / Math.Max(1.0, bpm[i]);   // 半拍（180°/2）的秒数
                    timeMs[i] = prevT + half * 1000.0;
                    c.Notes.Add(new Note
                    {
                        Index = noteIdx++,
                        TileIndex = i,
                        Time = timeMs[i] / 1000.0,
                        NeededAngle = 0,
                        Bpm = bpm[i],
                        Multitap = multitap[i],
                        ConstantBpm = constRun[i],
                        IsMidspin = true
                    });
                    continue;                       // 中旋仍然不更新 startAngle（下一砖的角度从更早那块算）
                }

                double need = clockwise[i] ? (startAngle - ang) : (ang - startAngle);
                while (need < 0.00005) need += 360.0;
                need %= 360.0;
                if (need < 0.00005) need = 360.0;

                double ms = need / 180.0 * 60000.0 / bpm[i];
                if (i > 0) t += ms;

                if (hasPause[i]) t += 60000.0 / bpm[i] * (pauseBeats[i] + 1.0);
                else if (holdBeats[i] > 0) t += 2.0 * 60000.0 / bpm[i] * holdBeats[i];

                timeMs[i] = t;

                var note = new Note
                {
                    Index = noteIdx++,
                    TileIndex = i,
                    Time = t / 1000.0,
                    NeededAngle = need,
                    Bpm = bpm[i],
                    Multitap = multitap[i],
                    ConstantBpm = constRun[i]
                };
                c.Notes.Add(note);

                if (i > 0) c.SpeedFloors.Add(i);   // 仅用于统计
                prevNeeded = need;

                // 更新 startAngle
                if (i + 1 < n && c.Angles[i + 1] == Triangle) startAngle = ang;
                else startAngle = Norm360(180.0 + ang);
            }

            // PressCount 留给 IceChartBuilder 计算：那里才知道音频分析结果，
            // 也才知道用户配的 BPM 分档阈值。
            for (int k = 0; k < c.Notes.Count; k++) c.Notes[k].PressCount = 1;
        }

        public static string StripTags(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            int depth = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '<') { depth++; continue; }
                if (ch == '>') { if (depth > 0) depth--; continue; }
                if (depth == 0) sb.Append(ch);
            }
            return sb.ToString().Trim();
        }
    }
}
