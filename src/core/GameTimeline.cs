// 游戏自带时刻表的对接层（引擎无关，可离线单测）
//
// 背景：
//   游戏自己算好了每一块砖的进入时刻和需要按几下，就在 scrLevelMaker.listFloors 的每个 scrFloor 上：
//     entryTime   Double   这块砖在第几秒
//     tapsNeeded  Int32    这块砖要按几下
//     midSpin     Boolean  是不是中旋（中旋也要按一下）
//     isLandable  Boolean  能不能落
//     seqID       Int32    第几块
//     speed       Single   速度倍率
//   （这些名字是从 Assembly-CSharp.dll 的元数据里读出来的真名，不是猜的。）
//
// 为什么不再自己算：
//   离线按公式算时间轴，任何一处（SetSpeed 语义、Twirl、offset、中旋）理解偏一点就整体错位，
//   而且要「猜」。直接用游戏自己的时刻表，再用它自己的时钟驱动冰音，
//   **对齐就是构造上精确的**，不依赖我的算法有多准。
//
// 这一层只做「把反射读出来的数组转成音符序列」，反射本身在 mod 层（Unity 里）。
using System;
using System.Collections.Generic;

namespace BingChart
{
    /// <summary>从 scrFloor 读出来的一串数组（反射层填好，核心层消费）。</summary>
    public class GameFloorData
    {
        public double[] Time;   // entryTime 秒
        public int[] Taps;      // tapsNeeded
        public bool[] Mid;      // midSpin
        public bool[] Land;     // isLandable
        public int[] Seq;       // seqID
        public float[] Speed;   // speed 倍率
        public string Note = "";

        public int Count { get { return Time == null ? 0 : Time.Length; } }
    }

    public static class GameTimelineKit
    {
        /// <summary>
        /// 用游戏时刻表替换离线音符序列。
        /// 返回 null = 成功；返回字符串 = 失败原因（调用方退回离线时间轴）。
        /// pitchShift 出参：游戏时刻 → 原曲 ogg 播放头 的常数偏移（测音高要用）。
        /// </summary>
        public static string Apply(ParsedChart pc, GameFloorData gt,
                                   out double pitchShift, out string detail)
        {
            pitchShift = 0; detail = "";
            if (pc == null || gt == null) return "没有游戏时刻表";
            if (pc.Notes == null || pc.Notes.Count < 4) return "离线音符太少";
            if (gt.Time == null || gt.Time.Length < 8) return "游戏时刻表为空";
            if (gt.Taps == null || gt.Mid == null || gt.Land == null || gt.Seq == null || gt.Speed == null
                || gt.Taps.Length != gt.Time.Length || gt.Mid.Length != gt.Time.Length
                || gt.Land.Length != gt.Time.Length || gt.Seq.Length != gt.Time.Length
                || gt.Speed.Length != gt.Time.Length)
                return "游戏时刻表数组长度不一致";

            var oa = new double[pc.Notes.Count];
            for (int i = 0; i < oa.Length; i++) oa[i] = pc.Notes[i].Time;

            // ---- 1) 挑出「玩家要按的砖」：跳过起始砖，保留能落的砖和中旋砖 ----
            var keep = new List<int>();
            for (int i = 0; i < gt.Time.Length; i++)
            {
                if (gt.Seq[i] <= 0) continue;                       // 起始砖不用按
                if (!gt.Land[i] && !gt.Mid[i]) continue;            // 既不能落也不是中旋 → 装饰/容器
                keep.Add(i);
            }
            if (keep.Count < 8) return "游戏时刻表里可用砖太少（" + keep.Count + "）";

            // ---- 2) 单位：只看「跨度比」----
            //   两套时刻覆盖同一张谱，跨度必然相等；游戏那边跨了 1000 倍 → 毫秒。
            //   ★刻意不用「和离线的吻合率」当门槛：万一我离线算错了，就会把正确的游戏时刻表否掉，
            //     又退回错的那条路。吻合率只写进日志当诊断。
            var ga = new double[keep.Count];
            for (int k = 0; k < keep.Count; k++) ga[k] = gt.Time[keep[k]];
            double spanGame = ga[ga.Length - 1] - ga[0];
            double spanOff = oa[oa.Length - 1] - oa[0];
            double scale = 1.0;
            if (spanOff > 1.0 && spanGame > 1.0)
            {
                double r = spanGame / spanOff;
                if (r > 100.0) scale = 0.001;
                else if (r < 0.01) return "游戏时刻跨度不合理（比例 " + r.ToString("0.###") + "）";
            }
            for (int k = 0; k < ga.Length; k++) ga[k] *= scale;
            for (int k = 1; k < ga.Length; k++)
                if (ga[k] < ga[k - 1] - 0.002) return "游戏时刻不是递增的（第 " + k + " 块）";

            double lastOff = oa[oa.Length - 1], lastGame = ga[ga.Length - 1];
            if (lastOff > 1.0)
            {
                double rr = lastGame / lastOff;
                if (rr < 0.3 || rr > 3.0)
                    return "游戏时刻量级不合理（末块 " + lastGame.ToString("0.0")
                           + "s vs 离线 " + lastOff.ToString("0.0") + "s）";
            }

            // ---- 3) 偏移与吻合率（诊断用）----
            double shift = Dsp.EstimateSetShift(oa, ga, 1.5);
            double rate = Dsp.SetShiftMatchRate(oa, ga, shift, 0.030);

            // 剔掉整体偏移后的残差 —— 这就是「我离线那条时间轴」的真实精度
            var resid = new List<double>();
            int stride = Math.Max(1, oa.Length / 400);
            for (int i = 0; i < oa.Length; i += stride)
            {
                double t = oa[i] - shift;
                int lo = 0, hi = ga.Length - 1;
                while (hi - lo > 1) { int m = (lo + hi) / 2; if (ga[m] < t) lo = m; else hi = m; }
                resid.Add(Math.Min(Math.Abs(ga[lo] - t), Math.Abs(ga[hi] - t)));
            }
            resid.Sort();
            double medResid = resid.Count > 0 ? resid[resid.Count / 2] : 0;
            double p90 = resid.Count > 0 ? resid[(int)(resid.Count * 0.90)] : 0;

            detail = "游戏 " + keep.Count + " 块 / 离线 " + pc.Notes.Count + " 个；单位="
                   + (scale == 1.0 ? "秒" : "毫秒")
                   + "；游戏→原曲偏移=" + (shift * 1000).ToString("+0;-0;0") + "ms"
                   + "；吻合率=" + (rate * 100).ToString("0.0") + "%"
                   + "；离线时间轴剔偏移后残差 中位=" + (medResid * 1000).ToString("0.0")
                   + "ms P90=" + (p90 * 1000).ToString("0.0") + "ms";
            pitchShift = shift;

            // ---- 4) 用游戏时刻重建音符序列 ----
            int n = keep.Count;
            var nl = new List<Note>(n);
            for (int k = 0; k < n; k++)
            {
                int gi = keep[k];
                int taps = gt.Taps[gi];
                if (taps <= 0) taps = 1;

                // 匀速段（雪花）：前后连续 >=8 块速度倍率相同
                bool constBpm = false;
                if (n >= 16)
                {
                    int lo = Math.Max(0, k - 4), hi = Math.Min(n - 1, k + 4);
                    float sp = gt.Speed[keep[k]];
                    bool same = true;
                    for (int q = lo; q <= hi; q++)
                        if (Math.Abs(gt.Speed[keep[q]] - sp) > 1e-4f) { same = false; break; }
                    constBpm = same && (hi - lo + 1) >= 8;
                }

                nl.Add(new Note
                {
                    Index = k,
                    TileIndex = gt.Seq[gi],
                    Time = gt.Time[gi] * scale,
                    NeededAngle = 0,
                    Bpm = pc.BaseBpm * (gt.Speed[gi] > 0 ? gt.Speed[gi] : 1f),
                    Multitap = taps >= 2 ? Math.Min(4, taps) : 0,
                    IsMidspin = gt.Mid[gi],
                    ConstantBpm = constBpm
                });
            }
            for (int k = 1; k < nl.Count; k++)
                if (nl[k].Time < nl[k - 1].Time - 0.002) return "游戏时刻不是递增的（第 " + k + " 块）";

            pc.Notes = nl;
            return null;
        }
    }
}
