// 冰谱转换核心 —— 离线测试台
// 直接跑真实关卡，逐项断言：解析 / 时间轴 / 标定 / 合成真的有声音 / OGG 可解码
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BingChart;

class Program
{
    static string GameDir = @"D:\SteamLibrary\steamapps\common\A Dance of Fire and Ice";
    static string IceWav = "";
    static int pass = 0, fail = 0;
    static List<string> failures = new List<string>();

    static void Chk(bool cond, string label, string extra = "")
    {
        if (cond) { pass++; Console.WriteLine($"    ✓ {label} {extra}"); }
        else { fail++; failures.Add(label + " " + extra); Console.WriteLine($"    ✗ {label} {extra}"); }
    }

    /// <summary>归一化互相关系数（只取前 60 秒，够用且快）。</summary>
    static double Corr(float[] a, float[] b)
    {
        if (a == null || b == null) return 0;
        int n = Math.Min(Math.Min(a.Length, b.Length), 44100 * 60);
        if (n < 1000) return 0;
        double ma = 0, mb = 0;
        for (int i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
        ma /= n; mb /= n;
        double num = 0, da = 0, db = 0;
        for (int i = 0; i < n; i++)
        {
            double x = a[i] - ma, y = b[i] - mb;
            num += x * y; da += x * x; db += y * y;
        }
        if (da <= 1e-12 || db <= 1e-12) return 0;
        return num / Math.Sqrt(da * db);
    }

    static double Rms(float[] a, int from, int to)
    {
        if (a == null) return 0;
        from = Math.Max(0, from); to = Math.Min(a.Length, to);
        if (to <= from) return 0;
        double s = 0;
        for (int i = from; i < to; i++) s += (double)a[i] * a[i];
        return Math.Sqrt(s / (to - from));
    }

    static int Main(string[] argv)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var modDir = argv.Length > 0 ? argv[0] : @"D:\Program Files\work\ADOFAI冰\冰谱Mod";
        IceWav = Path.Combine(modDir, "audio", "冰.wav");
        string ffmpeg = Path.Combine(GameDir, "ffmpeg", "ffmpeg.exe");
        string outRoot = Path.Combine(Path.GetTempPath(), "BingChartTest");

        Console.WriteLine("=== 冰谱转换核心 · 离线测试台 ===");
        Console.WriteLine($"冰音: {IceWav}  存在={File.Exists(IceWav)}");
        Console.WriteLine($"ffmpeg: {ffmpeg}  存在={File.Exists(ffmpeg)}");
        if (!File.Exists(IceWav)) { Console.WriteLine("冰音不存在，测试无法进行"); return 2; }
        if (!File.Exists(ffmpeg)) { Console.WriteLine("ffmpeg 不存在，测试无法进行"); return 2; }

        // ---------- 区域 1：冰音样本本身 ----------
        Console.WriteLine("\n【区域1】冰音样本");
        Chk(Wav.Probe(IceWav, out int ir, out double isec), "冰音 WAV 可读", $"{ir}Hz {isec:F3}s");
        var ice = Wav.ReadFromFile(IceWav);
        Chk(ice != null && ice.Length > 1000, "冰音采样解出", $"{ice?.Length} 个采样");
        double icePk = 0;
        foreach (float v in ice) icePk = Math.Max(icePk, Math.Abs(v));
        Chk(icePk > 0.05, "冰音不是静音", $"峰值 {icePk:F3}");
        double attack = Dsp.DetectAttack(ice, ir);
        Chk(attack > 0.0005 && attack < 0.05, "attack 自动检测在合理范围", $"{attack * 1000:F1}ms");

        // ---------- 区域 2：谱面解析 ----------
        Console.WriteLine("\n【区域2】谱面解析（88 个真实谱面）");
        var charts = new List<(string name, string path, string dir)>();
        string backup = Path.Combine(GameDir, "Mods", "LevelLibrary", "backup");
        foreach (var d in Directory.GetDirectories(backup))
            foreach (var f in Directory.GetFiles(d))
                if (f.EndsWith(".adofai", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".adofa", StringComparison.OrdinalIgnoreCase))
                    charts.Add((Path.GetFileName(f), f, d));
        int parsed = 0, withNotes = 0;
        foreach (var c in charts)
        {
            try
            {
                var pc = ChartReader.Load(c.path);
                if (pc != null) { parsed++; if (pc.Notes.Count > 0) withNotes++; }
            }
            catch { }
        }
        Console.WriteLine($"    谱面总数 {charts.Count}");
        Chk(parsed >= charts.Count * 0.9, "谱面解析成功率 ≥90%", $"{parsed}/{charts.Count} = {parsed * 100.0 / charts.Count:F1}%");
        Chk(withNotes > 0, "有能算出音符的谱面", $"{withNotes} 个");

        // ---------- dump 模式：对比逐音符数据 ----------
        if (argv.Length > 2 && argv[2] == "dump")
        {
            foreach (var c in charts)
            {
                var pc0 = ChartReader.Load(c.path);
                if (pc0 == null || pc0.Notes.Count < 20) continue;
                Console.WriteLine($"关卡: {c.name}  音符 {pc0.Notes.Count}  砖数 {pc0.TileCount}  ActionCount {pc0.ActionCount}  baseBpm {pc0.BaseBpm}  offset {pc0.OffsetMs}ms");
                Console.WriteLine("idx  angle    bpm      time(s)   need");
                for (int i = 0; i < Math.Min(20, pc0.Notes.Count); i++)
                {
                    var nn = pc0.Notes[i];
                    Console.WriteLine("  tile=" + nn.TileIndex + " angle=" + pc0.Angles[nn.TileIndex] + " bpm=" + nn.Bpm.ToString("F3") + " time=" + nn.Time.ToString("F4") + " need=" + nn.NeededAngle.ToString("F1"));
                }
                Console.WriteLine("末音符时间 " + pc0.Notes[pc0.Notes.Count - 1].Time.ToString("F2") + "s");
                // 按 BPM 分组统计每段耗时，便于和 Python 对账
                var groups = new Dictionary<double, double>();
                for (int i = 1; i < pc0.Notes.Count; i++)
                {
                    double seg = (pc0.Notes[i].Time - pc0.Notes[i - 1].Time) * 1000.0;
                    double key = Math.Round(pc0.Notes[i].Bpm, 2);
                    if (!groups.ContainsKey(key)) groups[key] = 0;
                    groups[key] += seg;
                }
                Console.WriteLine("Bpm 分布(耗时ms):");
                foreach (var kv in groups.OrderByDescending(x => x.Value).Take(10))
                    Console.WriteLine("   bpm=" + kv.Key.ToString("F2") + "  " + kv.Value.ToString("F0") + "ms");
                int sp = 0, hp = 0, pa = 0, tw = 0;
                var rr = Json.Parse(File.ReadAllText(c.path));
                foreach (JVal a in rr.Get("actions").ArrOrEmpty())
                {
                    string et = a.Get("eventType").StrOr("");
                    if (et == "SetSpeed") sp++;
                    else if (et == "Hold") hp++;
                    else if (et == "Pause") pa++;
                    else if (et == "Twirl") tw++;
                }
                Console.WriteLine("事件: SetSpeed " + sp + " / Hold " + hp + " / Pause " + pa + " / Twirl " + tw);
                Console.WriteLine("C# 解析结果: TwirlCount=" + pc0.TwirlCount + " SetSpeedCount=" + pc0.SetSpeedCount
                    + " TriangleCount=" + pc0.TriangleCount);
                Console.Write("前 20 块方向: ");
                for (int i = 0; i < Math.Min(20, pc0.Clockwise.Length); i++)
                    Console.Write(pc0.Clockwise[i] ? "T" : "F");
                Console.WriteLine();
                Console.Write("前 20 块 BPM : ");
                for (int i = 0; i < Math.Min(20, pc0.BpmAt.Length); i++)
                    Console.Write(pc0.BpmAt[i].ToString("F0") + " ");
                Console.WriteLine();
                Console.WriteLine("前 6 条 action 的 floor 解析情况:");
                int sh2 = 0;
                foreach (JVal a in rr.Get("actions").ArrOrEmpty())
                {
                    JVal fv = a.Get("floor");
                    Console.WriteLine("   eventType=" + a.Get("eventType").StrOr("?")
                        + " floorKind=" + (fv == null ? "NULL对象" : fv.K.ToString())
                        + " floorStr=" + (fv == null ? "-" : fv.StrOr("-"))
                        + " floorNum=" + (fv == null ? "-" : fv.NumOr(-999).ToString())
                        + " IntOr=" + a.IntOr(-1));
                    if (++sh2 >= 6) break;
                }
                return 0;
            }
            return 0;
        }

        // ---------- 区域 3：时间轴 + 合成（真实关卡端到端） ----------
        Console.WriteLine("\n【区域3】端到端转换（真实关卡）");
        var opt = new IceOptions();
        int limit = argv.Length > 1 ? int.Parse(argv[1]) : 9999;
        int e2e = 0, e2eOk = 0;
        var durations = new List<double>();
        var alignHits = new List<double>();
        foreach (var c in charts)
        {
            string ogg = Directory.GetFiles(c.dir, "*.ogg").FirstOrDefault();
            if (ogg == null) continue;
            if (e2e >= limit) break;
            e2e++;
            string outDir = Path.Combine(outRoot, Path.GetFileNameWithoutExtension(c.name));
            try
            {
                var pc = ChartReader.Load(c.path);
                if (pc == null || pc.Notes.Count < 20) continue;
                var r = IceChartBuilder.Build(pc, ogg, IceWav, ffmpeg, outDir, opt);
                if (!r.Ok) { Console.WriteLine($"    · {Path.GetFileName(c.name),-30} 跳过: {r.Error}"); continue; }
                e2eOk++;
                durations.Add(r.Chart.DurationSec);
                // 对齐质量：至少 30% 的音符要有音频起始点支撑（实测标定后中位误差 ~7ms）
                double hit = r.Chart.Notes.Count > 0 ? (double)r.Chart.MatchedNotes / r.Chart.Notes.Count : 0;
                if (r.Chart.MatchedNotes > 0 && hit < 0.30)
                    Console.WriteLine($"    ! {Path.GetFileName(c.name),-30} 对齐命中率仅 {hit * 100:F1}%，请复核");
                alignHits.Add(hit);
                if (limit <= 8)
                    Console.WriteLine($"    · {Path.GetFileName(c.name),-26} 音符{r.Chart.Notes.Count,5} 起始点{r.Chart.OnsetCount,5} "
                        + $"偏移{r.Chart.CalibOffsetSec * 1000,7:F1}ms 命中{r.Chart.MatchedNotes,5} 匀速段{r.Chart.ConstantNotes,4} | {r.Chart.Report}");
                if (r.Chart.Notes.Count > 0 && r.Chart.DurationSec < 5.0)
                    Console.WriteLine($"    ! {Path.GetFileName(c.name),-30} 产物时长异常短 {r.Chart.DurationSec:F2}s");
            }
            catch (Exception e)
            {
                Console.WriteLine($"    · {Path.GetFileName(c.name),-30} 异常: {e.GetType().Name}: {e.Message}");
            }
        }
        Console.WriteLine($"    端到端跑了 {e2e} 个关卡，成功 {e2eOk} 个");
        Chk(e2eOk > 0, "至少成功转换 1 个真实关卡", $"{e2eOk} 个");
        if (alignHits.Count > 0)
        {
            double avg = alignHits.Average();
            Chk(avg >= 0.30, "平均对齐命中率 ≥30%（音符有音频起始点支撑）", $"平均 {avg * 100:F1}%  最低 {alignHits.Min() * 100:F1}%");
            Chk(alignHits.Count(x => x >= 0.30) >= alignHits.Count * 0.8, "≥80% 的关卡对齐命中率 ≥30%",
                $"{alignHits.Count(x => x >= 0.30)}/{alignHits.Count} 个达标");
        }

        // 逐个详细检查第一个成功的产物
        string firstOk = null, firstOkDir = null;
        foreach (var c in charts)
        {
            string ogg = Directory.GetFiles(c.dir, "*.ogg").FirstOrDefault();
            if (ogg == null) continue;
            string outDir = Path.Combine(outRoot, Path.GetFileNameWithoutExtension(c.name));
            if (File.Exists(Path.Combine(outDir, "ice.ogg"))) { firstOk = outDir; firstOkDir = c.dir; break; }
        }
        if (firstOk != null)
        {
            Console.WriteLine($"\n【区域4】产物深度检查: {firstOk}");
            string oggPath = Path.Combine(firstOk, "ice.ogg");
            string wavPath = Path.Combine(firstOk, "ice.wav");
            Chk(File.Exists(oggPath), "ice.ogg 生成");
            Chk(new FileInfo(oggPath).Length > 20000, "ice.ogg 体积合理", $"{new FileInfo(oggPath).Length} bytes");

            // 关键：解码回来确认真的有声音（这正是上一版翻车的地方）
            var tmp = Path.Combine(firstOk, "verify.wav");
            var vr = Ffmpeg.ToWav(ffmpeg, oggPath, tmp);
            Chk(vr.Ok, "ice.ogg 可被解码回 WAV", vr.Error);
            float[] back = Wav.ReadFromFile(tmp);
            Chk(back != null && back.Length > 10000, "回解采样数正常", $"{back?.Length}");
            // 音头对齐 / 音符前静音 必须在「替换模式」下检查：
            // 叠加模式里原曲一直在响，本来就不是静音，用叠加产物做这两个断言没有意义。
            {
                string lv4 = null, lg4 = null;
                foreach (var c4 in charts)
                {
                    string o4 = Directory.GetFiles(c4.dir, "*.ogg").FirstOrDefault();
                    if (o4 != null) { lv4 = c4.path; lg4 = o4; break; }
                }
                if (lv4 != null)
                {
                    string soloDir = Path.Combine(outRoot, "solo_for_timing");
                    var rr = IceChartBuilder.Build(ChartReader.Load(lv4), lg4, IceWav, ffmpeg, soloDir,
                        new IceOptions { KeepOriginal = false, IceVolume = 0.85f });
                    Chk(rr.Ok, "【替换模式】单独转换成功", rr.Ok ? rr.Chart.Report : rr.Error);
                    if (rr.Ok)
                    {
                        var soloWav = Wav.ReadFromFile(Path.Combine(soloDir, "ice.wav"));
                        double spk = 0;
                        if (soloWav != null) foreach (float v in soloWav) spk = Math.Max(spk, Math.Abs(v));
                        Chk(spk > 0.05, "【替换模式】产物不是静音", $"峰值 {spk:F3}");

                        string rp2 = Path.Combine(soloDir, "report.txt");
                        double n0 = 0, cal = 0, atk = 0.001;
                        if (File.Exists(rp2))
                            foreach (string ln in File.ReadAllLines(rp2, Encoding.UTF8))
                            {
                                if (!ln.StartsWith("KEY=")) continue;
                                foreach (string one in ln.Substring(4).Split(';'))
                                {
                                    if (one.StartsWith("note0=")) n0 = double.Parse(one.Substring(6), CultureInfo.InvariantCulture);
                                    else if (one.StartsWith("calib=")) cal = double.Parse(one.Substring(6), CultureInfo.InvariantCulture);
                                    else if (one.StartsWith("attack=")) atk = double.Parse(one.Substring(7), CultureInfo.InvariantCulture);
                                }
                            }
                        int sr4 = 44100;
                        double exp = n0 + cal;
                        int c4i = (int)(exp * sr4);
                        double on = -1;
                        for (int i = Math.Max(0, c4i - sr4 / 12); i < Math.Min(soloWav.Length, c4i + sr4 / 12); i++)
                            if (Math.Abs(soloWav[i]) > 0.02) { on = i / (double)sr4; break; }
                        Chk(on > 0, "【替换模式】第一个音符处有冰音起振", on < 0 ? "没找到" : $"{on * 1000:F0}ms");
                        double pre = 0;
                        int pe = (int)((exp - atk - 0.004) * sr4);
                        for (int i = Math.Max(0, pe - sr4 / 20); i < pe; i++) pre = Math.Max(pre, Math.Abs(soloWav[i]));
                        Chk(pre < 0.02, "【替换模式】音符之前是静音（不会提前误触发）", $"峰值 {pre:F4}");
                    }
                }
            }
        }


        // ---------- 区域 5：音高映射规则 ----------
        Console.WriteLine("\n【区域5】音高映射规则");
        Chk(IceChartBuilder.SemitoneForPress(1, opt) == 0, "单押 = 基准音");
        Chk(IceChartBuilder.SemitoneForPress(2, opt) == 2, "双押 = +2 半音");
        Chk(IceChartBuilder.SemitoneForPress(3, opt) == 3, "三押 = +3 半音");
        Chk(IceChartBuilder.SemitoneForPress(4, opt) == 4, "四押以上 = +4 半音");
        Chk(opt.PressSemitone4 <= 4.0, "音高差不超过大三度（不刺耳）");

        // 渲染器：非零输入必须产出非零输出
        {
            var fake = new IceChart { SampleRate = 44100, DurationSec = 1.0 };
            fake.Notes.Add(new IceNote { Time = 0.1, Semitone = 0, PressCount = 1 });
            var small = ice.Length > 4000 ? new float[4000] : ice;
            Array.Copy(ice, small, small.Length);
            var rendered = IceSynth.Render(fake, small, null, new IceOptions { KeepOriginal = false, IceVolume = 0.85f }, out double p2);
            double p2max = 0;
            foreach (float v in rendered) p2max = Math.Max(p2max, Math.Abs(v));
            Chk(p2max > 0.05, "渲染器：非零样本 -> 非零输出", $"峰值 {p2max:F3}");
            Chk(rendered[(int)(0.1 * 44100)] != 0, "渲染器：音符位置确实写入了采样");
            Chk(rendered[0] == 0, "渲染器：音符之前是静音（不会误触发）");
        }

        // 升调 / 降调都要能出声
        foreach (int semi in new[] { -4, 0, 4 })
        {
            var fake = new IceChart { SampleRate = 44100, DurationSec = 0.8 };
            fake.Notes.Add(new IceNote { Time = 0.05, Semitone = semi, PressCount = 1 });
            var r2 = IceSynth.Render(fake, ice, null, new IceOptions { KeepOriginal = false, IceVolume = 0.85f }, out double p3);
            double m = 0;
            foreach (float v in r2) m = Math.Max(m, Math.Abs(v));
            Chk(m > 0.05, $"渲染器：{semi:+#;-#;0} 半音出声", $"峰值 {m:F3}");
        }

        // ---------- 区域 5.5：多押判定（按毫秒） ----------
        // 权威依据：官方 wiki「Multipress = 2 keys at the same time」、
        //           Steam 指南「同时击打两格相邻轨道」、
        //           萌娘百科/bilibili wiki「双押砖块不能 在关卡编辑器复现，为 RJ-X 独有」
        // ⇒ 自制谱里没有双押砖，只能靠「相邻两砖挨得极近」判定，所以主判据是**毫秒间隔**。
        Console.WriteLine("\n【区域5.5】多押判定（按毫秒）");
        {
            ParsedChart Make(double[] times)
            {
                var pc = new ParsedChart { BaseBpm = 180 };
                for (int i = 0; i < times.Length; i++)
                    pc.Notes.Add(new Note { Index = i, TileIndex = i, Time = times[i], Bpm = 180, NeededAngle = 180 });
                return pc;
            }
            var noAudio = new IceOptions { MultiTapGapMs = 40, MultiTapUseAudio = false };

            var pc1 = Make(new[] { 1.000, 1.015, 2.000, 2.200 });
            IceChartBuilder.ComputePressCounts(pc1, null, noAudio);
            Chk(pc1.Notes[0].PressCount == 2, "间隔 15ms -> 双押", "PressCount=" + pc1.Notes[0].PressCount);
            Chk(pc1.Notes[2].PressCount == 1, "间隔 200ms -> 单押", "PressCount=" + pc1.Notes[2].PressCount);

            var pc2 = Make(new[] { 1.000, 1.015, 1.030, 2.000 });
            IceChartBuilder.ComputePressCounts(pc2, null, noAudio);
            Chk(pc2.Notes[0].PressCount == 3 && pc2.Notes[1].PressCount == 3 && pc2.Notes[2].PressCount == 3,
                "三砖每段 15ms -> 三押");

            var pc3 = Make(new[] { 1.000, 1.030 });
            IceChartBuilder.ComputePressCounts(pc3, null, new IceOptions { MultiTapGapMs = 40, MultiTapUseAudio = false });
            Chk(pc3.Notes[0].PressCount == 2, "30ms 间隔 + 阈值 40ms -> 双押");
            var pc4 = Make(new[] { 1.000, 1.030 });
            IceChartBuilder.ComputePressCounts(pc4, null, new IceOptions { MultiTapGapMs = 20, MultiTapUseAudio = false });
            Chk(pc4.Notes[0].PressCount == 1, "30ms 间隔 + 阈值 20ms -> 单押（阈值真的按毫秒走）");

            var pc5 = Make(new[] { 1.000, 1.005, 1.010, 1.015, 1.020, 2.0 });
            IceChartBuilder.ComputePressCounts(pc5, null,
                new IceOptions { MultiTapGapMs = 40, MultiTapMaxCluster = 4, MultiTapUseAudio = false });
            Chk(pc5.Notes[0].PressCount == 4, "五砖连锁截到 4 押", "PressCount=" + pc5.Notes[0].PressCount);
            Chk(pc5.Notes[4].PressCount == 1, "被截掉的那个回到单押");

            var pc6 = Make(new[] { 1.000, 5.000 });
            pc6.Notes[0].Multitap = 3;
            IceChartBuilder.ComputePressCounts(pc6, null, noAudio);
            Chk(pc6.Notes[0].PressCount == 3, "谱面显式 Multitap=3 -> 三押");

            // 真实谱面统计：多押占比应该落在合理区间（实测 <40ms 占 2.78%）
            int totalNotes = 0, multiNotes = 0;
            var gaps = new List<double>();
            foreach (var cc in charts)
            {
                var p = ChartReader.Load(cc.path);
                if (p == null || p.Notes.Count < 2) continue;
                for (int k = 1; k < p.Notes.Count; k++)
                    gaps.Add(p.Notes[k].Time - p.Notes[k - 1].Time);
                IceChartBuilder.ComputePressCounts(p, null, noAudio);
                foreach (var nt in p.Notes) { totalNotes++; if (nt.PressCount >= 2) multiNotes++; }
            }
            gaps.Sort();
            double ratio = totalNotes > 0 ? 100.0 * multiNotes / totalNotes : 0;
            int nUnder40 = 0; foreach (double g in gaps) if (g <= 0.040) nUnder40++;
            Console.WriteLine("    → 相邻音间隔: 共 " + gaps.Count + " 个；"
                + "最小 " + (gaps[0] * 1000).ToString("0.0") + "ms，"
                + "1‰ " + (gaps[gaps.Count / 1000] * 1000).ToString("0.0") + "ms，"
                + "1% " + (gaps[gaps.Count / 100] * 1000).ToString("0.0") + "ms，"
                + "中位 " + (gaps[gaps.Count / 2] * 1000).ToString("0.0") + "ms；"
                + "≤40ms 的占 " + (100.0 * nUnder40 / Math.Max(1, gaps.Count)).ToString("0.00") + "%");
            int nNonPos = 0; foreach (double g in gaps) if (g <= 1e-6) nNonPos++;
            Console.WriteLine("    → 间隔 <=0（时刻重复或倒流）的有 " + nNonPos + " 个");
            Chk(ratio >= 0.1 && ratio <= 10.0, "真实谱面多押占比在 0.1%~10%",
                $"{ratio:F2}%（{multiNotes}/{totalNotes}）");
        }

        // ---------- 区域 5.7：时间集合互相关 + 游戏时刻表接管 ----------
        Console.WriteLine("\n【区域5.7】时间集合互相关（用来对齐「游戏时刻表」和「离线时间轴」）");
        {
            // 造一条「离线」时间轴
            var a = new List<double>();
            double t0 = 2.0;
            for (int i = 0; i < 120; i++) { t0 += 0.20 + (i % 5 == 0 ? 0.15 : 0.0); a.Add(t0); }

            // 「游戏」时刻表 = 离线 - 0.3s，而且故意多 3 个、少 2 个（模拟起始砖/终点砖/中旋处理不同）
            var b = new List<double>();
            for (int i = 0; i < a.Count; i++)
            {
                if (i == 10 || i == 40) continue;
                b.Add(a[i] - 0.300);
            }
            b.Add(0.5); b.Add(1.2); b.Add(999.0);
            b.Sort();

            double sh = Dsp.EstimateSetShift(a.ToArray(), b.ToArray(), 1.5);
            Chk(Math.Abs(sh - 0.300) < 0.006, "整体偏移求出 ≈ +300ms", (sh * 1000).ToString("0.0") + "ms");
            double rt = Dsp.SetShiftMatchRate(a.ToArray(), b.ToArray(), sh, 0.030);
            Chk(rt > 0.95, "吻合率 > 95%（多几个少几个不影响）", (rt * 100).ToString("1") + "%");
            double bad = Dsp.SetShiftMatchRate(a.ToArray(), b.ToArray(), sh + 0.5, 0.030);
            Chk(bad < 0.10, "偏移不对时吻合率会掉下来（能区分对错）", (bad * 100).ToString("1") + "%");
        }

        Console.WriteLine("\n【区域5.8】游戏自带时刻表接管时间轴");
        {
            // 造一条「真值」时间轴，再拿它当「游戏算的」
            int N = 200;
            var truth = new double[N];
            double t = 1.5;
            for (int i = 0; i < N; i++) { t += (i % 7 == 0) ? 0.12 : 0.24; truth[i] = t; }

            // 离线那条故意整体差 -0.07s（模拟游戏 offset/校准），并且第 50 块起有 30ms 精度损失
            var pc = new ParsedChart { BaseBpm = 180 };
            for (int i = 0; i < N; i++)
                pc.Notes.Add(new Note
                {
                    Index = i, TileIndex = i,
                    Time = truth[i] + 0.07,          // 离线(ogg 基准)比游戏时钟晚 70ms
                    Bpm = 180, NeededAngle = 180
                });

            // 游戏时刻表：多一个起始砖(seqID=0, 不可落)、中间多一个装饰砖、少一个终点砖
            var gt = new GameFloorData
            {
                Time = new double[N + 2], Taps = new int[N + 2], Mid = new bool[N + 2],
                Land = new bool[N + 2], Seq = new int[N + 2], Speed = new float[N + 2]
            };
            gt.Time[0] = 0.0; gt.Seq[0] = 0; gt.Land[0] = true; gt.Speed[0] = 1f;
            for (int i = 0; i < N; i++)
            {
                int j = i + 1;
                gt.Time[j] = truth[i]; gt.Seq[j] = j; gt.Land[j] = true; gt.Speed[j] = 1f;
                gt.Taps[j] = (i == 3 || i == 99) ? 2 : 1;            // 两块「双押砖」
            }
            gt.Time[N + 1] = truth[N - 1] + 0.24; gt.Seq[N + 1] = N + 1; gt.Land[N + 1] = true; gt.Speed[N + 1] = 1f;

            double ps; string det;
            string fail = GameTimelineKit.Apply(pc, gt, out ps, out det);
            Chk(fail == null, "游戏时刻表接管成功", fail ?? "");
            Chk(pc.Notes.Count == N + 1, "音符数 = 游戏里可落的砖数", pc.Notes.Count + " 个");
            Chk(Math.Abs(pc.Notes[0].Time - truth[0]) < 1e-9, "落点用的是游戏时刻（不是离线那条）",
                pc.Notes[0].Time.ToString("F4"));
            Chk(Math.Abs(ps - 0.07) < 0.015, "求出「游戏→原曲」偏移 ≈ 70ms（测音高要用）",
                (ps * 1000).ToString("0.0") + "ms");
            Chk(pc.Notes[3].Multitap == 2 && pc.Notes[99].Multitap == 2, "tapsNeeded=2 的砖被标成双押",
                pc.Notes[3].Multitap + " / " + pc.Notes[99].Multitap);
            Chk(pc.Notes[2].Multitap == 0, "tapsNeeded=1 的砖不标双押");
            Chk(pc.Notes[0].Time > 0 && pc.Notes[pc.Notes.Count - 1].Time > 0, "时刻为正");

            // 单位的自动判定：把游戏时刻全换成毫秒，应该自动认出来
            var gt2 = new GameFloorData
            {
                Time = new double[gt.Time.Length], Taps = (int[])gt.Taps.Clone(), Mid = (bool[])gt.Mid.Clone(),
                Land = (bool[])gt.Land.Clone(), Seq = (int[])gt.Seq.Clone(), Speed = (float[])gt.Speed.Clone()
            };
            for (int i = 0; i < gt.Time.Length; i++) gt2.Time[i] = gt.Time[i] * 1000.0;
            var pc2 = ChartReader0();
            double ps2; string det2;
            string fail2 = GameTimelineKit.Apply(pc2, gt2, out ps2, out det2);
            Chk(fail2 == null && Math.Abs(pc2.Notes[0].Time - truth[0]) < 1e-6,
                "毫秒版的游戏时刻表被自动认出来（单位不猜）", fail2 ?? "");

            // 游戏时刻表明显不合理时必须拒绝，不能用
            var gtBad = new GameFloorData
            {
                Time = new double[gt.Time.Length], Taps = (int[])gt.Taps.Clone(), Mid = (bool[])gt.Mid.Clone(),
                Land = (bool[])gt.Land.Clone(), Seq = (int[])gt.Seq.Clone(), Speed = (float[])gt.Speed.Clone()
            };
            for (int i = 0; i < gt.Time.Length; i++) gtBad.Time[i] = gt.Time[i] * 10.0;   // 量级差 10 倍
            var pc3 = ChartReader0();
            double ps3; string det3;
            Chk(GameTimelineKit.Apply(pc3, gtBad, out ps3, out det3) != null,
                "量级离谱的游戏时刻表会被拒绝（不会带歪）");

            ParsedChart ChartReader0()
            {
                var p = new ParsedChart { BaseBpm = 180 };
                for (int i = 0; i < N; i++)
                    p.Notes.Add(new Note { Index = i, TileIndex = i, Time = truth[i] + 0.07, Bpm = 180, NeededAngle = 180 });
                return p;
            }
        }

        // ---------- 区域 5.9：多语言表 ----------
        Console.WriteLine("\n【区域5.9】多语言");
        {
            bool HasHan(string s)
            {
                foreach (char c in s) if (c >= 0x4e00 && c <= 0x9fff) return true;
                return false;
            }
            try
            {
                Loc.Lang = 0;
                Chk(Loc.T("冰谱 设置") == "冰谱 设置", "中文：原样返回");

                // 表里没有的串必须原样返回（优雅降级），不能显示成 key
                Loc.Lang = 1;
                Chk(Loc.T("这个串肯定不在表里xyz") == "这个串肯定不在表里xyz",
                    "表里没有的串原样返回（不会显示成 key）");

                // 抽查关键界面串：英文版不能残留中文
                string[] keys = {
                    "基础设置", "专业设置", "其他设置", "重置", "设置", "语言",
                    "开关与音量", "启用冰谱（关掉后完全不干预）", "冰谱音量", "音色",
                    "试听", "试播冰音层", "转换快捷键", "行为", "反馈 / 交流", "加 QQ 群",
                    "音高：怎么定冰音的高低", "多押：一块砖要按几下", "多押间隔阈值",
                    "冰音听感", "时间轴与播放微调", "播放微调（立刻生效）", "转换",
                    "界面", "维护", "诊断", "重置", "全部恢复默认",
                    "已识别谱面", "正在转换冰谱…", "冰谱 出错了", "知道了", "我已知晓",
                    "冰谱 Mod 怎么用", "不再提醒（下次不弹这个）",
                };
                Loc.Lang = 1;
                int enOk = 0, enBad = 0;
                foreach (string k in keys)
                {
                    string v = Loc.T(k);
                    if (v != k && !HasHan(v)) enOk++; else enBad++;
                }
                Chk(enBad == 0, "英文：抽查的界面串全部翻译且无残留中文", enOk + "/" + keys.Length);

                Loc.Lang = 2;
                int krOk = 0;
                foreach (string k in keys) if (Loc.T(k) != k) krOk++;
                Chk(krOk >= keys.Length * 8 / 10, "韩文：抽查的界面串大部分已翻译", krOk + "/" + keys.Length);

                // 滑条读数里的单位也要跟着换
                Loc.Lang = 1;
                string ro = Loc.T("±7 半音");
                Chk(!HasHan(ro), "滑条读数里的单位会跟着翻译", ro);
                Loc.Lang = 0;
                Chk(Loc.T("±7 半音") == "±7 半音", "切回中文后单位恢复");
            }
            catch (Exception ex)
            {
                Chk(false, "多语言表能正常加载（重复 key 会在这里炸）", ex.GetType().Name + ": " + ex.Message);
            }
            finally { Loc.Lang = 0; }
        }

        // ---------- 区域 6：拒绝条件（必须结合谱面+音频） ----------
        Console.WriteLine("\n【区域6】拒绝条件");
        {
            var r = IceChartBuilder.Build(null, "x", IceWav, ffmpeg, outRoot, opt);
            Chk(!r.Ok && r.Error.Contains("音符"), "谱面为空 -> 拒绝", r.Error);
        }
        {
            // 音频缺失：默认走「只按轨道转」备用方案
            var pc0 = ChartReader.Load(charts.First(c => File.Exists(c.path)).path);
            var r0 = IceChartBuilder.Build(pc0, @"Z:\不存在.ogg", IceWav, ffmpeg, outRoot, opt);
            Chk(r0.Ok, "音频不存在 + 允许兜底 -> 按轨道转换成功", r0.Ok ? r0.Chart.Report : r0.Error);
            Chk(r0.Ok && !r0.Chart.UsedAudio, "兜底转换标记为「未使用音频」");
            Chk(r0.Ok && r0.Chart.OnsetCount == 0, "兜底转换不做起始点标定");

            // 关掉备用方案 -> 必须明确拒绝
            var strict = new IceOptions { ChartOnlyFallback = false };
            var r1 = IceChartBuilder.Build(pc0, @"Z:\不存在.ogg", IceWav, ffmpeg, outRoot, strict);
            Chk(!r1.Ok && r1.Error.Contains("备用方案"), "音频不存在 + 禁用兜底 -> 明确拒绝", r1.Error);
        }

        // ---------- 区域 7：叠加 vs 替换 ----------
        Console.WriteLine("\n\n【区域7】叠加 / 替换 两种混合模式");
        {
            // 找一个「谱面+音频」齐全的关卡
            string lv = null, lg = null;
            foreach (var c in charts)
            {
                string o = Directory.GetFiles(c.dir, "*.ogg").FirstOrDefault();
                if (o != null) { lv = c.path; lg = o; break; }
            }
            if (lv == null) { Chk(false, "找不到可用于混合测试的关卡"); }
            else
            {
                var pc = ChartReader.Load(lv);
                string baseDir = Path.Combine(outRoot, "mix_overlay");
                string repDir = Path.Combine(outRoot, "mix_replace");

                var optOv = new IceOptions { KeepOriginal = true, OriginalVolume = 1f, IceVolume = 0.6f };
                var optRp = new IceOptions { KeepOriginal = false, IceVolume = 0.6f };
                var rOv = IceChartBuilder.Build(pc, lg, IceWav, ffmpeg, baseDir, optOv);
                var rRp = IceChartBuilder.Build(pc, lg, IceWav, ffmpeg, repDir, optRp);
                Chk(rOv.Ok, "叠加模式转换成功", rOv.Ok ? rOv.Chart.Report : rOv.Error);
                Chk(rRp.Ok, "替换模式转换成功", rRp.Ok ? rRp.Chart.Report : rRp.Error);

                if (rOv.Ok && rRp.Ok)
                {
                    // 解回 WAV 做相关性分析：叠加版应当与原曲高度相关，替换版应当几乎不相关
                    string oW = Path.Combine(baseDir, "chk.wav");
                    string rW = Path.Combine(repDir, "chk.wav");
                    string sW = Path.Combine(baseDir, "orig.wav");
                    bool ok1 = Ffmpeg.ToWav(ffmpeg, Path.Combine(baseDir, "ice.ogg"), oW).Ok;
                    bool ok2 = Ffmpeg.ToWav(ffmpeg, Path.Combine(repDir, "ice.ogg"), rW).Ok;
                    bool ok3 = Ffmpeg.ToWav(ffmpeg, lg, sW).Ok;
                    Chk(ok1 && ok2 && ok3, "三份音频都能解回 WAV");

                    var ov = Wav.ReadFromFile(oW);
                    var rp = Wav.ReadFromFile(rW);
                    var og = Wav.ReadFromFile(sW);

                    double cOv = Corr(ov, og), cRp = Corr(rp, og);
                    Console.WriteLine($"    与原曲的相关度: 叠加={cOv:F4}   替换={cRp:F4}");
                    Chk(cOv > 0.60, "叠加版里能听到原曲（相关度显著）", $"corr={cOv:F4}");
                    Chk(cRp < 0.50, "替换版与原曲不相关（原曲确实被拿掉了）", $"corr={cRp:F4}");
                    Chk(cOv - cRp > 0.4, "两种模式差异显著", $"差 {cOv - cRp:F4}");

                    // 冰音层在两种模式里都要有：查第一个音符附近是否比原曲多出能量
                    int sr = 44100;
                    double n0 = pc.Notes[0].Time;
                    int a = (int)((n0 + 0.010) * sr), b = (int)((n0 + 0.090) * sr);
                    double eOv = Rms(ov, a, b), eRp = Rms(rp, a, b), eOg = Rms(og, a, b);
                    Console.WriteLine($"    第一个音符处 RMS: 原曲={eOg:F4}  叠加={eOv:F4}  替换={eRp:F4}");
                    Chk(eOv > eOg, "叠加版在该处比原曲更响（冰音叠上去了）");
                    Chk(eRp > 0.005, "替换版在该处仍有声音（冰音在）");

                    // 叠加版不能削顶失真
                    double pk = 0; foreach (float v in ov) pk = Math.Max(pk, Math.Abs(v));
                    Chk(pk <= 1.001, "叠加版没有超出 0dB（不会削顶失真）", $"峰值 {pk:F4}");
                }
            }
        }

        // ---------- 汇总 ----------
        Console.WriteLine("\n=== 汇总 ===");
        Console.WriteLine($"通过 {pass} 项，失败 {fail} 项");
        foreach (var f in failures) Console.WriteLine("  失败: " + f);
        return fail == 0 ? 0 : 1;
    }
}
