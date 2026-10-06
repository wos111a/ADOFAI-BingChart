// DSP：起始点检测、基频估计、时间轴偏移标定、冰音 attack 检测
// 纯 C#，零依赖，不依赖 Unity，可在游戏外单测。
using System;
using System.Collections.Generic;

namespace BingChart
{
    // ---------------- 迭代式基 2 FFT ----------------
    internal static class Fft
    {
        public static void Forward(float[] re, float[] im)
        {
            int n = re.Length;
            // 位反转置换
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    float t = re[i]; re[i] = re[j]; re[j] = t;
                    t = im[i]; im[i] = im[j]; im[j] = t;
                }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = -2.0 * Math.PI / len;
                float wr = (float)Math.Cos(ang), wi = (float)Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    float cr = 1f, ci = 0f;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        float xr = re[b] * cr - im[b] * ci;
                        float xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi;
                        re[a] += xr;        im[a] += xi;
                        float nr = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = nr;
                    }
                }
            }
        }
    }

    public static class Dsp
    {
        public const int Rate = 44100;

        // ---------------- 起始点检测（谱通量 + 自适应阈值） ----------------
        /// <summary>返回每个检测到的起始点（秒）。已按最小间隔去重。</summary>
        public static double[] Onsets(float[] y, int sr, int hop, int win, double minGapSec, double sens = 1.0)
        {
            if (y == null || y.Length < win * 2 || hop <= 0) return new double[0];
            int pad = win;
            int total = y.Length + pad * 2;
            int nfr = (total - win) / hop;
            if (nfr < 8) return new double[0];

            int bins = win / 2 + 1;
            var flux = new float[Math.Max(0, nfr - 1)];
            var re = new float[win];
            var im = new float[win];
            var prev = new float[bins];
            var cur = new float[bins];
            var hann = new float[win];
            for (int i = 0; i < win; i++)
                hann[i] = (float)(0.5 - 0.5 * Math.Cos(2.0 * Math.PI * i / (win - 1)));

            for (int f = 0; f < nfr; f++)
            {
                int start = f * hop;
                for (int i = 0; i < win; i++)
                {
                    int idx = start + i - pad;
                    float s = (idx >= 0 && idx < y.Length) ? y[idx] : 0f;
                    re[i] = s * hann[i];
                    im[i] = 0f;
                }
                Fft.Forward(re, im);
                double sum = 0;
                for (int b = 0; b < bins; b++)
                {
                    float mag = (float)Math.Sqrt(re[b] * re[b] + im[b] * im[b]);
                    float lv = (float)Math.Log(1.0 + mag * 100.0);
                    cur[b] = lv;
                    if (f > 0)
                    {
                        float d = lv - prev[b];
                        if (d > 0) sum += d;
                    }
                }
                if (f > 0) flux[f - 1] = (float)sum;
                var t = prev; prev = cur; cur = t;
            }

            // 自适应阈值：过去 32 帧的均值 + 1.1 倍标准差（滑动累计和，O(n)）
            int n = flux.Length;
            if (n < 4) return new double[0];
            var c1 = new double[n + 1];
            var c2 = new double[n + 1];
            for (int i = 0; i < n; i++) { c1[i + 1] = c1[i] + flux[i]; c2[i + 1] = c2[i] + flux[i] * flux[i]; }
            double mean = flux[0];
            for (int i = 0; i < n; i++) mean += flux[i];
            mean /= n;
            if (mean < 1e-9) mean = 1e-9;

            var res = new System.Collections.Generic.List<double>();
            int minGap = Math.Max(1, (int)(minGapSec * sr / hop));
            int last = -1000000;
            for (int i = 1; i < n - 1; i++)
            {
                int lo = Math.Max(0, i - 32);
                int cnt = i - lo;
                double m = (c1[i + 1] - c1[lo]) / cnt;
                double v = (c2[i + 1] - c2[lo]) / cnt - m * m;
                if (v < 0) v = 0;
                double thr = m + 1.1 * Math.Sqrt(v);
                double floorThr = mean * (0.55 / Math.Max(0.2, sens));
                if (thr < floorThr) thr = floorThr;
                if (flux[i] > thr && flux[i] >= flux[i - 1] && flux[i] >= flux[i + 1] && i - last >= minGap)
                {
                    res.Add((i * hop + pad + win / 2) / (double)sr);
                    last = i;
                }
            }
            return res.ToArray();
        }

        // ---------------- 基频估计（自相关） ----------------
        /// <summary>估计 t 时刻的基频（Hz）。不可靠时返回 0。</summary>
        public static double PitchAt(float[] y, int sr, double t, int win,
                                     double loHz = 60, double hiHz = 1200)
        {
            if (y == null) return 0;
            int c = (int)(t * sr) - win / 2;      // 以该时刻为中心
            if (c < 0) c = 0;
            if (c + win > y.Length) c = Math.Max(0, y.Length - win - 1);
            if (c + win > y.Length || win < 64) return 0;

            double energy = 0;
            for (int i = 0; i < win; i++) energy += (double)y[c + i] * y[c + i];
            if (Math.Sqrt(energy / win) < 0.008) return 0;

            // 去直流
            double mean = 0;
            for (int i = 0; i < win; i++) mean += y[c + i];
            mean /= win;

            int lo = Math.Max(2, (int)(sr / Math.Max(1.0, hiHz)));   // lag 下界 = 高频上限
            int hi = Math.Min(win - 1, (int)(sr / Math.Max(1.0, loHz)));
            if (hi <= lo + 2) return 0;

            var seg = new float[win];
            for (int i = 0; i < win; i++) seg[i] = (float)(y[c + i] - mean);

            double best = 0;
            int bestLag = 0;
            double zero = 0;
            for (int i = 0; i < win; i++) zero += (double)seg[i] * seg[i];
            if (zero < 1e-12) return 0;

            for (int lag = lo; lag <= hi; lag++)
            {
                double s = 0;
                int n2 = win - lag;
                for (int i = 0; i < n2; i++) s += (double)seg[i] * seg[i + lag];
                if (s > best) { best = s; bestLag = lag; }
            }
            if (bestLag <= 0 || best < 0.30 * zero) return 0;
            return (double)sr / bestLag;
        }

        public static double HzToSemitone(double hz)
        {
            if (hz <= 1) return 0;
            return 12.0 * Math.Log(hz / 440.0, 2.0) + 9.0;   // A4=440 → +9 半音
        }

        // ---------------- 时间轴偏移标定 ----------------
        /// <summary>
        /// 用全曲标定一个全局偏移（秒），把谱面时间轴对齐到音频起始点。
        /// 评分直接用「有多少音符落在起始点 ±25ms 内」的比例 —— 这就是我们的质量指标本身，
        /// 比中位误差稳得多（少数关卡起始点检测会误检，中位误差会被带偏，最大化命中率不会）。
        /// 实测：能把逐砖中位误差从 ~36ms 压到 ~7ms。
        /// </summary>
        public static double CalibrateOffset(double[] noteTimes, double[] onsets, double searchSec)
        {
            if (noteTimes == null || onsets == null || noteTimes.Length == 0 || onsets.Length < 8)
                return 0;
            const double Tol = 0.025;

            int step = Math.Max(1, (int)(Rate * 0.001));       // 1ms 精度
            int steps = (int)(searchSec * Rate / step);
            // 均匀取样最多 600 个音符，够稳又便宜
            int stride = Math.Max(1, noteTimes.Length / 600);
            var sample = new List<double>(700);
            for (int i = 0; i < noteTimes.Length; i += stride) sample.Add(noteTimes[i]);
            if (sample.Count < 8) return 0;

            double bestRatio = -1, bestOff = 0;
            for (int s = -steps; s <= steps; s++)
            {
                double off = (double)s * step / Rate;
                int hit = 0;
                for (int k = 0; k < sample.Count; k++)
                {
                    double t = sample[k] + off;
                    int j = NearestOnset(onsets, t);
                    if (j >= 0 && Math.Abs(t - onsets[j]) <= Tol) hit++;
                }
                double ratio = (double)hit / sample.Count;
                // 相同命中率时取更小的绝对偏移，避免贴着搜索边界
                if (ratio > bestRatio + 1e-9 ||
                    (Math.Abs(ratio - bestRatio) <= 1e-9 && Math.Abs(off) < Math.Abs(bestOff)))
                {
                    bestRatio = ratio;
                    bestOff = off;
                }
            }
            return bestRatio > 0 ? bestOff : 0;
        }

        /// <summary>
        /// ★求两个「时间点集合」之间的整体偏移：找一个 Δ，让 a 里的点尽量落在 b 里的点附近
        /// （判定：|a[i] - (b[j] + Δ)| &lt;= tol）。返回被命中的 a 点占比最高的那个 Δ。
        ///
        /// 用在「离线算的时间轴」对照「游戏自带的 entryTime 时刻表」：
        /// 两边的砖数可能差几个（起始砖、终点砖、中旋的处理不同），
        /// 所以**不能按下标一一配对**，必须用这种「集合对集合」的方法求偏移，才不会被错位带偏。
        ///
        /// 做法：对每个 a[i]，只把落在 a[i]±searchSec 之内的 b[j] 拿出来，
        /// 把差值丢进 1ms 的直方图；最后取直方图峰值附近的平均值。
        /// 复杂度 O(N × 窗口内点数)，几百个点是毫秒级。
        /// </summary>
        public static double EstimateSetShift(double[] a, double[] b, double searchSec, double tol = 0.012)
        {
            if (a == null || b == null || a.Length < 4 || b.Length < 4) return 0;
            int bins = (int)(searchSec * 2000);            // 0.5ms 一格
            if (bins < 8) bins = 8;
            var hist = new int[bins * 2 + 1];
            double binSec = searchSec / bins;

            int stride = Math.Max(1, a.Length / 800);
            int used = 0;
            for (int i = 0; i < a.Length; i += stride)
            {
                used++;
                // 二分找 b 里 [a[i]-searchSec, a[i]+searchSec] 的区间
                int lo = LowerBound(b, a[i] - searchSec);
                for (int j = lo; j < b.Length && b[j] <= a[i] + searchSec; j++)
                {
                    int k = (int)Math.Round((a[i] - b[j]) / binSec) + bins;
                    if (k >= 0 && k < hist.Length) hist[k]++;
                }
            }
            if (used < 4) return 0;

            int peak = 0;
            for (int k = 1; k < hist.Length; k++) if (hist[k] > hist[peak]) peak = k;
            // 峰值 ±2 格做重心，精度更高
            double num = 0, den = 0;
            for (int k = Math.Max(0, peak - 2); k <= Math.Min(hist.Length - 1, peak + 2); k++)
            { num += hist[k] * k; den += hist[k]; }
            if (den <= 0) return 0;
            double center = num / den;
            return (center - bins) * binSec;
        }

        /// <summary>这个 Δ 下，a 有多少比例的点能在 tol 内找到 b 的对应点（0~1）。</summary>
        public static double SetShiftMatchRate(double[] a, double[] b, double shift, double tol = 0.020)
        {
            if (a == null || b == null || a.Length == 0 || b.Length == 0) return 0;
            int hit = 0, used = 0;
            int stride = Math.Max(1, a.Length / 800);
            for (int i = 0; i < a.Length; i += stride)
            {
                used++;
                double t = a[i] - shift;
                int j = LowerBound(b, t - tol);
                if (j < b.Length && Math.Abs(b[j] - t) <= tol) hit++;
                else if (j > 0 && Math.Abs(b[j - 1] - t) <= tol) hit++;
            }
            return used == 0 ? 0 : (double)hit / used;
        }

        static int LowerBound(double[] arr, double v)
        {
            int lo = 0, hi = arr.Length;
            while (lo < hi) { int m = (lo + hi) / 2; if (arr[m] < v) lo = m + 1; else hi = m; }
            return lo;
        }

        private static int NearestOnset(double[] onsets, double t)
        {            int lo = 0, hi = onsets.Length - 1;
            if (t <= onsets[0]) return Math.Abs(t - onsets[0]) < 0.25 ? 0 : -1;
            if (t >= onsets[hi]) return Math.Abs(t - onsets[hi]) < 0.25 ? hi : -1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (onsets[mid] < t) lo = mid; else hi = mid;
            }
            return (t - onsets[lo] <= onsets[hi] - t) ? lo : hi;
        }

        // ---------------- 冰音 attack 检测 ----------------
        /// <summary>
        /// 找冰音开头「先低再高」的上升起点（秒）。
        /// 实测样本：0ms 起振、5ms 开始明显上升、10ms 达峰 —— 返回的就是这个上升起点。
        /// 合成时把采样起点放在砖块时刻之前这么久，听感上「冰」正好落在砖上。
        /// </summary>
        public static double DetectAttack(float[] pcm, int sr)
        {
            if (pcm == null || pcm.Length < sr / 100) return 0.006;
            int win = Math.Max(1, (int)(sr * 0.002));       // 2ms 窗
            int n = Math.Min(pcm.Length / win, 60);        // 只看前 120ms
            if (n < 4) return 0.006;

            var rms = new double[n];
            double peak = 0;
            for (int i = 0; i < n; i++)
            {
                double s = 0;
                for (int k = 0; k < win; k++) { double v = pcm[i * win + k]; s += v * v; }
                rms[i] = Math.Sqrt(s / win);
                if (rms[i] > peak) peak = rms[i];
            }
            if (peak < 1e-6) return 0.006;
            for (int i = 0; i < n; i++) rms[i] /= peak;

            // 从第一个超过 15% 峰值的点往回找起点（下界必须夹到 0，之前会算出 -1）
            int start = 0;
            for (int i = 1; i < n; i++)
            {
                if (rms[i] > 0.15)
                {
                    while (start < i - 1 && rms[start + 1] > rms[start] * 1.05) start++;
                    break;
                }
            }
            if (start < 0) start = 0;
            double sec = start * 0.002;
            if (sec < 0.001) sec = 0.001;
            if (sec > 0.05) sec = 0.05;
            return sec;
        }

        // ---------------- 冰音包络（用于“少一点/多一点点”的音量塑形） ----------------
        /// <summary>算出冰音的归一化包络，用于按 pressCount 微调音量，避免多押时糊成一团。</summary>
        public static float[] Envelope(float[] pcm, int sr, int points)
        {
            var env = new float[points];
            if (pcm == null || pcm.Length == 0 || points <= 0) return env;
            int per = Math.Max(1, pcm.Length / points);
            for (int i = 0; i < points; i++)
            {
                int s = i * per, e = Math.Min(pcm.Length, s + per);
                double acc = 0; int c = 0;
                for (int k = s; k < e; k++) { acc += (double)pcm[k] * pcm[k]; c++; }
                env[i] = c > 0 ? (float)Math.Sqrt(acc / c) : 0f;
            }
            return env;
        }
    }
}
