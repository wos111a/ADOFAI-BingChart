// 冰谱合成：谱面时间轴 + 音频分析 -> 新的冰谱音频
//
// 硬性要求：默认必须同时用到「谱面」和「音频」两个来源才允许转换。
//   谱面提供：每砖时间、BPM、轨道角度、Multitap / MultiPlanet 事件
//   音频提供：起始点（标定时间轴）、基频（匀速段音高）、多押的时间验证
// 音频缺失时，只有显式开启「只按轨道转」备用方案才会继续（报告会打星号标记）。
using System;
using System.Collections.Generic;
using System.IO;

namespace BingChart
{
    public class IceOptions
    {
        public float Volume = 0.85f;         // 总输出音量（最后统一乘一次）

        // ★ 混合方式：默认「叠加」——原曲保留，冰音盖在上面
        //   false = 替换，新文件里只有冰音
        public bool KeepOriginal = true;
        public float OriginalVolume = 1.00f;   // 原曲那一层的音量
        public float IceVolume = 0.60f;        // 冰音那一层的音量（用户定的 60%）

        // ★ 冰音裁短：样本本身 280ms，但大部分是拖尾。相邻音符 70% 会重叠 → 糊成一片。
        //   裁短后上一个响完下一个才来，就能听出一个个点。0 = 不裁。
        public double IceLengthMs = 120;

        /// <summary>用户手动微调：把冰音整体前后挪（秒）。正=冰音更晚。</summary>
        public double ExtraOffsetSec = 0;

        // ★ 密集段落自适应降音量：一秒内音符越多，单个冰音越轻，避免堆成噪声。
        //   1.0 = 不调整；越小压制越强。
        public double DensityCompensation = 0.5;
        public double AttackAlignSec = -1;    // <0 = 自动检测冰音 attack
        public double SearchSec = 0.25;

        public double PressSemitone2 = 2.0;
        public double PressSemitone3 = 3.0;
        public double PressSemitone4 = 4.0;

        // ★ 音高规则（用户指定）：
        //   在小球到砖块那一刻，截取原曲那一小段的音高；以「开头部分」的音高作为基准音，
        //   比基准高就升调、低就降调 —— 这样冰音就跟着旋律的起伏走。
        public bool PitchRelativeToOpening = true;
        public int PitchWindowSamples = 2048;     // 取样窗口（采样点）
        public double PitchLowHz = 70;            // 基频检测下限
        public double PitchHighHz = 1200;         // 基频检测上限
        // ★ 压缩系数：原曲音高差 1 个八度 -> 冰音差 PitchCompress 个半音。
        //   实测 12（=精确音程）会让 30~48% 的音符顶到上限、听感很跳；
        //   压到 3 只留方向、去掉幅度，77% 的相邻音符音高相同，成段保持才像音乐。
        public double PitchCompress = 3.0;
        public double PitchMaxSemitone = 7.0;     // 最多升降多少半音
        public int PitchSmooth = 5;               // 音高序列中位滤波窗口（奇数，1=不平滑）
        public double OpeningSeconds = 0.08;      // 用开头百分之几的时长当基准音

        // 旧的「匀速段跟音频」：实测会让音高乱飘，默认关
        public bool UseAudioPitchInConstant = false;
        public double ConstantAudioWeight = 0.90;
        public double ConstantChartWeight = 0.10;
        public double ConstantMaxShift = 2.0;

        // 多押判定（按真实 BPM 分档）
        public bool MultiTapUseAudio = true;
        /// <summary>
        /// ★已经拿到游戏自己标的 tapsNeeded 时置 true：**不要再用毫秒间隔启发式**。
        /// 游戏的判定是权威的；启发式会在很多谱面上大量误判（实测能误标 30% 的音符 → 全都加 +2 半音 → 音高一片乱）。
        /// </summary>
        public bool MultitapFromGame = false;
        /// <summary>
        /// ★多押主判据：相邻两块砖的**时间间隔**小于这个毫秒数，就算"几乎同时按两下"。
        ///
        /// 为什么用毫秒而不是角度：官方定义里 Multipress 就是"两个键同时按"/"同时击打两格相邻轨道"，
        /// 而"同时"是个时间概念。角度必须再乘上 BPM 才是时间（原规则要分低/高两档就是这个原因），
        /// 用毫秒一个数就够，而且 BPM 中途变化时自动跟着走。
        ///
        /// 实测（186 个真实自制谱、35050 个砖间隔）：
        ///   &lt;20ms 0.73% / &lt;30ms 1.76% / &lt;40ms 2.78% / &lt;60ms 5.25%
        /// 用户原来那套规则（&lt;240bpm 看 &lt;25°、&gt;=320bpm 看 15~40°）换算出来正好是 15~42ms，
        /// 所以默认取 40ms。
        /// </summary>
        public double MultiTapGapMs = 40.0;
        /// <summary>一串连着的近间隔最多算到几押（对应 双押/三押/四押 三个半音设置）。</summary>
        public int MultiTapMaxCluster = 4;

        public bool ChartOnlyFallback = true;
        public double OnsetSensitivity = 1.0;
        public double OnsetHop = 512;
        public double OnsetWin = 1024;
        public double OnsetMinGap = 0.040;
        public int OggQuality = 4;
        public double DurationTailSec = 2.0;
        /// <summary>
        /// ★只给「测音高取样」用的时间偏移（秒）。
        ///
        /// 背景：时间轴现在**直接用游戏自己算好的逐砖时刻**（`scrFloor.entryTime`，游戏时钟基准），
        /// 而测音高必须去**原曲 ogg 的播放头**基准上取样，两者差一个固定常数
        /// （实测 ≈ +0.07s，就是游戏校准里的 calibration_i）。
        /// 所以：冰音落点用游戏时刻（和砖块严丝合缝），音高取样用「游戏时刻 + 这个偏移」（落在原曲上）。
        /// </summary>
        public double PitchTimeShiftSec = 0.0;
        /// <summary>时间轴来源标签（写进报告）。</summary>
        public string TimelineLabel = "离线解析 .adofai";
        /// <summary>
        /// ★用了游戏自带的逐砖时刻表时置 true：**不要再做「按音频重标定」**。
        /// 因为那些时刻已经是游戏算好的绝对时刻，再按音频起始点去挪一遍反而会把它挪歪
        /// （音频起始点 ≠ 砖块时刻，这是之前反复踩的坑）。
        /// </summary>
        public bool SkipAudioCalibration = false;

        /// <summary>进度上报：参数是「这一步在干嘛」和 0~1 的总进度。mod 用它画实时卡片。</summary>
        public Action<string, float> Progress;
    }

    public class IceNote
    {
        public double Time;
        public double Semitone;
        public int PressCount;
        public bool ConstantSegment;
        public bool FromAudio;
        public double AudioHz;
        public double NeededAngle;
        public double Bpm;
    }

    public class IceChart
    {
        public string SongKey = "";
        public string SongTitle = "";
        public int SampleRate = 44100;
        public double DurationSec;
        public List<IceNote> Notes = new List<IceNote>();
        public double CalibOffsetSec;
        /// <summary>时间轴是从哪来的（写进报告，方便一眼看出有没有用上游戏自带的时刻表）。</summary>
        public string TimelineSource = "离线解析 .adofai";
        public double RefHz;          // 基准音（开头部分的中位音高）
        public int OnsetCount;
        public int MatchedNotes;
        public int ConstantNotes;
        public int MultiNotes;
        public int MaxPress;
        public bool UsedAudio;
        public string Report = "";
    }

    public class BuildResult
    {
        public bool Ok;
        public string Error;
        public IceChart Chart;
    }

    public static class IceChartBuilder
    {
        public static BuildResult Build(ParsedChart chart, string audioPath, string icePcmPath,
                                        string ffmpegPath, string outDir, IceOptions opt)
        {
            var res = new BuildResult();
            if (chart == null || chart.Notes == null || chart.Notes.Count < 2)
            {
                res.Error = "谱面没有可用的音符（缺少 angleData / pathData，或全是非音符砖）";
                return res;
            }
            if (!File.Exists(icePcmPath)) { res.Error = "找不到冰音样本: " + icePcmPath; return res; }

            Report(opt, "1/4 解析谱面…", 0.05f);
            bool noAudio = string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath);
            if (noAudio && !opt.ChartOnlyFallback)
            {
                res.Error = "关卡没有音频，且未开启「没有音频时只按轨道转」备用方案";
                return res;
            }

            // ---------- 冰音 ----------
            float[] ice = Wav.ReadFromFile(icePcmPath);
            int iceRate = Dsp.Rate;
            if (ice == null || ice.Length < 256) { res.Error = "冰音样本读不出来（需要 16-bit PCM WAV）"; return res; }
            double attack = opt.AttackAlignSec >= 0 ? opt.AttackAlignSec : Dsp.DetectAttack(ice, iceRate);
            Report(opt, "2/4 分析音频…（解码 OGG）", 0.15f);

            // ---------- 音频（可选）----------
            float[] pcm = null;
            double audioDur = 0;
            double[] onsets = new double[0];
            double calib = 0;
            int rate = 44100;

            if (!noAudio)
            {
                string tmpDir = Path.Combine(Path.GetTempPath(), "BingChart_b_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(tmpDir);
                try
                {
                    string wavPath = Path.Combine(tmpDir, "src.wav");
                    var fr = Ffmpeg.ToWav(ffmpegPath, audioPath, wavPath);
                    if (!fr.Ok) { res.Error = "音频解码失败：" + fr.Error; return res; }

                    float[] raw; int ch, frames;
                    rate = Wav.ReadPcm(File.ReadAllBytes(wavPath), out raw, out ch, out frames);
                    if (rate <= 0) { res.Error = "解码后的 WAV 读不出来（不是 16-bit PCM）"; return res; }
                    if (ch == 1) pcm = raw;
                    else
                    {
                        pcm = new float[frames];
                        for (int i = 0; i < frames; i++)
                        {
                            float s = 0;
                            for (int c = 0; c < ch; c++) s += raw[i * ch + c];
                            pcm[i] = s / ch;
                        }
                    }
                    audioDur = (double)frames / rate;
                    if (audioDur < 1.0)
                    {
                        res.Error = "音频太短（" + audioDur.ToString("0.00") + "s），不像是完整歌曲";
                        return res;
                    }

                    Report(opt, "2/4 分析音频…（检测节拍点）", 0.30f);
                    onsets = Dsp.Onsets(pcm, rate, (int)opt.OnsetHop, (int)opt.OnsetWin,
                                        opt.OnsetMinGap, opt.OnsetSensitivity);
                    if (onsets.Length < 8)
                    {
                        res.Error = "音频里检测不到足够的起始点（" + onsets.Length + " 个），无法标定，拒绝转换";
                        return res;
                    }

                    Report(opt, "3/4 对齐时间轴…（已检出 " + onsets.Length + " 个节拍点）", 0.45f);
                    if (opt.SkipAudioCalibration)
                    {
                        // ★用的是游戏自带的绝对时刻，**不再**按音频起始点去挪它。
                        //   音频起始点 ≠ 砖块时刻，硬挪只会把它挪歪（这是之前反复踩的坑）。
                        calib = opt.ExtraOffsetSec;
                        Report(opt, "3/4 时间轴来自游戏，跳过音频标定", 0.45f);
                    }
                    else
                    {
                        var nt = new double[chart.Notes.Count];
                        for (int i = 0; i < nt.Length; i++) nt[i] = chart.Notes[i].Time;
                        calib = Dsp.CalibrateOffset(nt, onsets, opt.SearchSec) + opt.ExtraOffsetSec;
                    }
                    if (Math.Abs(calib) > opt.SearchSec && !opt.SkipAudioCalibration)
                    {
                        res.Error = "时间轴标定失败（偏移 " + (calib * 1000).ToString("0")
                                    + "ms 超出搜索范围），拒绝转换";
                        return res;
                    }
                }
                finally
                {
                    try { Directory.Delete(tmpDir, true); } catch { }
                }
            }

            // ---------- 逐音符 ----------
            var iceChart = new IceChart
            {
                SampleRate = rate,
                SongTitle = chart.Song,
                CalibOffsetSec = calib,
                TimelineSource = opt.TimelineLabel,
                OnsetCount = onsets.Length,
                UsedAudio = !noAudio,
                SongKey = MakeKey(chart)
            };

            ComputePressCounts(chart, onsets, opt);

            // ---- 第一步：把每个音符处的原曲基频测出来 ----
            int nn = chart.Notes.Count;
            var hzArr = new double[nn];
            var hasHz = new bool[nn];
            if (!noAudio)
            {
                for (int i = 0; i < nn; i++)
                {
                    // 以音符时刻为中心取窗口（小时刻也要有足够样本）
                    double hz = Dsp.PitchAt(pcm, rate, chart.Notes[i].Time + calib + opt.PitchTimeShiftSec,
                                            Math.Max(1024, opt.PitchWindowSamples),
                                            opt.PitchLowHz, opt.PitchHighHz);
                    if (hz > opt.PitchLowHz * 0.9 && hz < opt.PitchHighHz * 1.1)
                    { hzArr[i] = hz; hasHz[i] = true; }
                }
            }

            // ---- 第二步：中位滤波，去掉单点跳变 ----
            if (opt.PitchSmooth > 2)
            {
                int h = opt.PitchSmooth / 2;
                var tmp = new List<double>();
                for (int i = 0; i < nn; i++)
                {
                    tmp.Clear();
                    for (int k = Math.Max(0, i - h); k <= Math.Min(nn - 1, i + h); k++)
                        if (hasHz[k]) tmp.Add(hzArr[k]);
                    if (tmp.Count > 0) { tmp.Sort(); hzArr[i] = tmp[tmp.Count / 2]; hasHz[i] = true; }
                }
            }

            // ---- 第三步：基准音 = 开头一段的中位音高 ----
            double refHz = 0;
            if (!noAudio && opt.PitchRelativeToOpening)
            {
                var early = new List<double>();
                // 基准音取样也走「原曲 ogg 的播放头」基准
                double baseT = chart.Notes[0].Time + opt.PitchTimeShiftSec;
                double until = baseT + audioDur * opt.OpeningSeconds;
                for (int i = 0; i < nn && (chart.Notes[i].Time + opt.PitchTimeShiftSec) <= until; i++)
                    if (hasHz[i]) early.Add(hzArr[i]);
                if (early.Count >= 3) { early.Sort(); refHz = early[early.Count / 2]; }
                else
                {
                    var all = new List<double>();
                    for (int i = 0; i < nn; i++) if (hasHz[i]) all.Add(hzArr[i]);
                    if (all.Count >= 3) { all.Sort(); refHz = all[all.Count / 2]; }
                }
            }
            iceChart.RefHz = refHz;

            // ---- 第四步：逐音符定音高 ----
            int matched = 0, constant = 0, maxPress = 1, multi = 0;
            for (int i = 0; i < nn; i++)
            {
                Note n = chart.Notes[i];
                double t = n.Time + calib - attack;
                if (t < 0) t = 0;

                var inm = new IceNote
                {
                    Time = t,
                    PressCount = Math.Max(1, n.PressCount),
                    ConstantSegment = n.ConstantBpm,
                    NeededAngle = n.NeededAngle,
                    Bpm = n.Bpm
                };
                if (inm.PressCount > maxPress) maxPress = inm.PressCount;
                if (inm.PressCount >= 2) multi++;

                double chartSemi = SemitoneForPress(inm.PressCount, opt);

                if (opt.PitchRelativeToOpening && refHz > 0 && hasHz[i])
                {
                    // ★ 音高 = 那一刻原曲的音高，相对「开头基准音」的高低（压缩后）
                    double rel = opt.PitchCompress * Math.Log(hzArr[i] / refHz, 2.0);
                    double lim = opt.PitchMaxSemitone;
                    if (rel > lim) rel = lim; else if (rel < -lim) rel = -lim;
                    inm.AudioHz = hzArr[i];
                    inm.Semitone = rel + chartSemi;      // 相对音高 + 多押加成
                }
                else
                {
                    inm.Semitone = chartSemi;             // 测不到基频就只按押数
                }

                if (!noAudio && HasOnsetNear(onsets, n.Time + calib + opt.PitchTimeShiftSec, 0.030))
                { inm.FromAudio = true; matched++; }
                iceChart.Notes.Add(inm);
            }
            iceChart.MatchedNotes = matched;
            iceChart.ConstantNotes = constant;
            iceChart.MaxPress = maxPress;
            iceChart.MultiNotes = multi;

            // ---------- 渲染 + 编码 ----------
            Report(opt, "4/4 合成冰谱…（" + chart.Notes.Count + " 个音符）", 0.70f);
            double total = chart.Notes[chart.Notes.Count - 1].Time + calib + opt.DurationTailSec;
            if (total < audioDur) total = audioDur;
            iceChart.DurationSec = total;

            float[] outPcm = IceSynth.Render(iceChart, ice, pcm, opt, out double peak);
            if (peak < 0.0005) { res.Error = "合成结果几乎无声（峰值 " + peak.ToString("0.0000") + "），已中止"; return res; }

            try { Directory.CreateDirectory(outDir); }
            catch (Exception e) { res.Error = "无法创建产物目录: " + e.Message; return res; }

            string outWav = Path.Combine(outDir, "ice.wav");
            Wav.Write(outWav, outPcm, rate);

            // ★ 另导出一份「只有冰音层」的文件。
            //   游戏内不再替换游戏的音频，而是用我们自己的音源播这一份、并锁到游戏的播放进度上，
            //   这样能绕开「替换 AudioClip 导致起播延迟不稳定」的问题。
            {
                var soloOpt = new IceOptions
                {
                    KeepOriginal = false,
                    IceVolume = opt.IceVolume,
                    IceLengthMs = opt.IceLengthMs,
                    DensityCompensation = opt.DensityCompensation,
                    Volume = opt.Volume
                };
                float[] solo = IceSynth.Render(iceChart, ice, null, soloOpt, out double soloPeak);
                if (soloPeak > 0.0005)
                    Wav.Write(Path.Combine(outDir, "ice_only.wav"), solo, rate);
            }
            Report(opt, "4/4 导出 OGG…", 0.88f);
            string outOgg = Path.Combine(outDir, "ice.ogg");
            var er = Ffmpeg.ToOgg(ffmpegPath, outWav, outOgg, opt.OggQuality);
            if (!er.Ok) { res.Error = "OGG 编码失败：" + er.Error; return res; }

            iceChart.Report = string.Format(
                "{0} | 时间轴 {1} | 音符 {2} | 起始点 {3} | 对齐命中 {4} | 匀速段 {5} | 多押砖 {6} | 最高押数 {7} | " +
                "偏移 {8:F1}ms | attack {9:F1}ms | 时长 {10:F2}s | 峰值 {11:F3} | 基准音 {12:F0}Hz",
                noAudio ? "★无音频，仅按轨道转换"
                        : (opt.KeepOriginal ? "叠加(原曲" + (opt.OriginalVolume * 100).ToString("0")
                                              + "% + 冰音" + (opt.IceVolume * 100).ToString("0") + "%)"
                                            : "替换(只有冰音" + (opt.IceVolume * 100).ToString("0") + "%)"),
                iceChart.TimelineSource,
                iceChart.Notes.Count, iceChart.OnsetCount, iceChart.MatchedNotes,
                iceChart.ConstantNotes, multi, maxPress, calib * 1000.0, attack * 1000.0,
                total, peak, iceChart.RefHz);

            try
            {
                File.WriteAllText(Path.Combine(outDir, "report.txt"),
                    string.Format("KEY=note0={0:F6};calib={1:F6};attack={2:F6};notes={3};onsets={4};matched={5};constant={6};multi={7};maxpress={8};dur={9:F3};peak={10:F4};audio={11}\n{12}",
                        chart.Notes[0].Time, calib, attack, iceChart.Notes.Count, iceChart.OnsetCount,
                        iceChart.MatchedNotes, iceChart.ConstantNotes, multi, maxPress, total, peak,
                        noAudio ? 0 : 1, iceChart.Report),
                    new System.Text.UTF8Encoding(false));
            }
            catch { }

            Report(opt, "完成", 1f);
            res.Ok = true;
            res.Chart = iceChart;
            return res;
        }

        static void Report(IceOptions o, string step, float p)
        {
            if (o != null && o.Progress != null)
            {
                try { o.Progress(step, p); } catch { }
            }
        }

        /// <summary>
        /// 判定「多押」（一块砖要按两下 / 多下）。
        ///
        /// 权威依据（查过官方 wiki / Steam 指南 / 萌娘百科 / bilibili wiki）：
        ///   · 官方 wiki Game Mechanics：「Multipress = 2 keys at the same time」
        ///   · Steam 指南：「多次按键!! 在同时击打两格相邻轨道的时候出现」
        ///   · 萌娘百科 / bilibili wiki：「双押砖块：该砖块内还有一个小砖块，需要同时进行 2 次点击；
        ///      不能在关卡编辑器复现；为官方 RJ-X 关卡独有」
        ///   ⇒ **自制谱里根本没有"双押砖块"这种砖**。玩家能遇到的"多押"只有一种来源：
        ///     相邻两块砖挨得极近，近到必须几乎同时点两下。
        ///
        /// ★注意（我踩过的坑）：angleData 里的 0 **不是**"两砖同一时刻"。
        /// 它是相对角度，游戏用的是累加出来的 neededAngle（归一化到 (0,360]，0 会变成 360），
        /// 所以 neededAngle 永远不为 0 —— 自制谱里造不出严格同时的两块砖。
        ///
        /// 判定顺序：
        ///   1. 谱面显式标记的 Multitap 事件（若有）优先
        ///   2. 真正"同一时刻"落砖（防御性保留，实际极罕见）
        ///   3. ★主判据：相邻两砖时间间隔小于 MultiTapGapMs（默认 40ms）
        ///   4. 可选：结合音频再确认一次（原始需求要求"必须结合谱面和音频两种因素"）
        /// </summary>
        public static void ComputePressCounts(ParsedChart chart, double[] onsets, IceOptions opt)
        {
            var notes = chart.Notes;
            for (int i = 0; i < notes.Count; i++) { notes[i].PressCount = 1; notes[i].GeometricTap = false; }
            if (notes.Count < 2) return;

            // ★游戏已经给了权威押数 → 只沿用，不再跑启发式
            if (opt.MultitapFromGame)
            {
                for (int i = 0; i < notes.Count; i++)
                    notes[i].PressCount = Math.Max(1, Math.Min(Math.Max(2, opt.MultiTapMaxCluster), notes[i].Multitap));
                return;
            }

            int maxCluster = Math.Max(2, opt.MultiTapMaxCluster);
            const double near = 0.002;      // "同一时刻"的容差（2ms）

            // ---- 1) + 2)：显式事件优先；顺带线性扫一遍同时刻落砖 ----
            int k2 = 0;
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                if (n.Multitap >= 2) n.PressCount = Math.Max(n.PressCount, Math.Min(maxCluster, n.Multitap));
                if (k2 < i) k2 = i;
                while (k2 + 1 < notes.Count && notes[k2 + 1].Time - notes[i].Time <= near) k2++;
                int same = k2 - i + 1;
                if (same > 1) n.PressCount = Math.Max(n.PressCount, Math.Min(maxCluster, same));
            }

            // ---- 3) 主判据：把"连着挨得极近"的一串砖聚成一簇，簇的大小就是押数 ----
            double gap = Math.Max(1.0, opt.MultiTapGapMs) / 1000.0;
            int a = 0;
            while (a < notes.Count)
            {
                int b = a;
                while (b + 1 < notes.Count
                       && (b + 1 - a) < maxCluster
                       && notes[b + 1].Time - notes[b].Time > 1e-6
                       && notes[b + 1].Time - notes[b].Time <= gap)
                    b++;
                int size = b - a + 1;
                if (size >= 2 && PressesConfirmByAudio(onsets, notes, a, b, opt))
                {
                    for (int k = a; k <= b; k++)
                    {
                        notes[k].PressCount = Math.Max(notes[k].PressCount, size);
                        notes[k].GeometricTap = true;
                    }
                }
                a = Math.Max(b + 1, a + 1);   // 一定要跳过整簇，否则最后一簇会被重复吞
            }
        }

        /// <summary>可选：用音频起始点确认这一簇确实是个重音（"必须结合谱面和音频两种因素"）。</summary>
        static bool PressesConfirmByAudio(double[] onsets, System.Collections.Generic.List<Note> notes,
                                          int a, int b, IceOptions opt)
        {
            if (!opt.MultiTapUseAudio) return true;
            if (onsets == null || onsets.Length == 0) return true;   // 没有音频就不拦
            for (int k = a; k <= b; k++)
                if (HasOnsetNear(onsets, notes[k].Time, 0.09)) return true;
            return false;
        }

        public static bool HasOnsetNear(double[] onsets, double t, double tol)
        {
            if (onsets == null || onsets.Length == 0) return false;
            int lo = 0, hi = onsets.Length - 1;
            if (t < onsets[0]) return onsets[0] - t <= tol;
            if (t > onsets[hi]) return t - onsets[hi] <= tol;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (onsets[mid] < t) lo = mid; else hi = mid;
            }
            return (t - onsets[lo] <= tol) || (onsets[hi] - t <= tol);
        }

        public static double SemitoneForPress(int press, IceOptions o)
        {
            if (press <= 1) return 0;
            if (press == 2) return o.PressSemitone2;
            if (press == 3) return o.PressSemitone3;
            return o.PressSemitone4;
        }

        public static string MakeKey(ParsedChart c)
        {
            string s = !string.IsNullOrEmpty(c.Song) ? c.Song : Path.GetFileNameWithoutExtension(c.FilePath);
            var sb = new System.Text.StringBuilder();
            foreach (char ch in s)
                if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch > 127) sb.Append(ch);
            string k = sb.ToString().Trim();
            return k.Length > 60 ? k.Substring(0, 60) : k;
        }
    }

    public static class IceSynth
    {
        /// <summary>
        /// 混音。默认「叠加」：先把原曲整条铺底，再按每个音符的音高把冰音叠上去。
        /// KeepOriginal=false 时只铺冰音（替换模式）。
        /// </summary>
        public static float[] Render(IceChart chart, float[] ice, float[] original, IceOptions opt, out double peak)
        {
            int sr = chart.SampleRate;
            int n = (int)(chart.DurationSec * sr) + sr;
            var buf = new float[n];
            if (ice == null || ice.Length == 0) { peak = 0; return buf; }
            float volume = opt != null ? opt.IceVolume : 0.6f;

            // ---- 底层：原曲 ----
            if (opt != null && opt.KeepOriginal && original != null && original.Length > 0)
            {
                int n0 = Math.Min(n, original.Length);
                float ov = opt.OriginalVolume;
                for (int i = 0; i < n0; i++) buf[i] += original[i] * ov;
            }

            // 裁短后的样本长度（0 = 用整条）
            int sampleLen = (int)(opt != null ? opt.IceLengthMs : 120);
            int iceLen = (opt != null && opt.IceLengthMs <= 0) || sampleLen <= 0
                ? ice.Length
                : Math.Min(ice.Length, (int)(sampleLen / 1000.0 * Dsp.Rate));
            if (iceLen < 64) iceLen = Math.Min(ice.Length, 64);
            // 淡出，避免裁断处爆音
            int fade = Math.Min((int)(Dsp.Rate * 0.012), iceLen / 4);
            var iceTrim = new float[iceLen];
            for (int i = 0; i < iceLen; i++)
            {
                float g = 1f;
                if (fade > 0 && i >= iceLen - fade) g = (iceLen - i) / (float)fade;
                iceTrim[i] = ice[i] * g;
            }

            // 局部密度 -> 增益（用前后 0.35 秒内的音符数衡量）
            int ni = 0;
            foreach (IceNote note in chart.Notes)
            {
                int near = 0;
                for (int k = Math.Max(0, ni - 40); k < Math.Min(chart.Notes.Count, ni + 41); k++)
                    if (Math.Abs(chart.Notes[k].Time - note.Time) < 0.35) near++;
                float densityGain = 1f;
                if (opt != null && opt.DensityCompensation > 0 && near > 2)
                {
                    // 密到每秒 4 个以上开始压；用幂曲线平滑过渡
                    double dens = near / 0.7;
                    densityGain = (float)Math.Pow(Math.Max(0.15, Math.Min(1.0, 3.0 / Math.Max(3.0, dens))),
                                                  opt.DensityCompensation);
                }
                double ratio = Math.Pow(2.0, note.Semitone / 12.0);
                if (ratio < 0.25) ratio = 0.25;
                if (ratio > 4.0) ratio = 4.0;

                int outLen = (int)(iceTrim.Length / ratio);
                if (outLen < 8) continue;
                int pos = (int)(note.Time * sr);
                if (pos < 0) pos = 0;
                if (pos >= n) continue;
                float amp = volume * densityGain / (float)Math.Sqrt(Math.Max(1, note.PressCount));
                ni++;

                if (ratio >= 1.0)
                {
                    float step = (float)ratio;
                    for (int i = 0; i < outLen; i++)
                    {
                        int i0 = (int)(i * step);
                        if (i0 >= iceTrim.Length) break;
                        int i1 = i0 + 1;
                        float a = iceTrim[i0];
                        float b = i1 < iceTrim.Length ? 0.5f * (iceTrim[i0] + iceTrim[i1]) : a;
                        int d = pos + i;
                        if (d >= n) break;
                        buf[d] += (a * 0.6f + b * 0.4f) * amp;
                    }
                }
                else
                {
                    float inv = (float)(1.0 / ratio);
                    for (int i = 0; i < outLen; i++)
                    {
                        float fi = i * inv;
                        int i0 = (int)fi;
                        if (i0 >= iceTrim.Length) break;
                        int i1 = i0 + 1;
                        float a = iceTrim[i0];
                        float b = i1 < iceTrim.Length ? iceTrim[i1] : a;
                        int d = pos + i;
                        if (d >= n) break;
                        buf[d] += (a + (b - a) * (fi - i0)) * amp;
                    }
                }
            }

            // 先量「未夹」的真实峰值。超过 0.99 就整体降增益 —— 这样不会有任何削顶失真，
            // 只是整体略安静一点。之前的顺序是「先硬夹到 ±1 再降增益」，被夹掉的波形已经失真了。
            double truePeak = 0;
            for (int i = 0; i < n; i++)
            {
                double a = Math.Abs(buf[i]);
                if (a > truePeak) truePeak = a;
            }
            if (truePeak > 0.99)
            {
                float g = (float)(0.99 / truePeak);
                for (int i = 0; i < n; i++) buf[i] *= g;
            }
            // 最后兜底夹一次（正常路径下不会触发，防 NaN/Inf 之类的意外）
            peak = 0;
            for (int i = 0; i < n; i++)
            {
                float v = buf[i];
                if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                buf[i] = v;
                double a = Math.Abs(v);
                if (a > peak) peak = a;
            }
            return buf;
        }
    }
}
