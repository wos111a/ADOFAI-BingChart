// 冰谱转换核心 —— WAV 读写 + ffmpeg 调用
// 纯 C#，不依赖 Unity，可在游戏外单独测试。
using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace BingChart
{
    /// <summary>16-bit PCM WAV 读写。只支持 PCM 16bit（ADOFAI 的 ogg 也统一转成这个格式）。</summary>
    public static class Wav
    {
        public static int ReadPcm(byte[] b, out float[] pcm, out int channels, out int frames)
        {
            channels = 1; frames = 0; pcm = null;
            if (b == null || b.Length < 44) return 0;
            if (Str(b, 0, 4) != "RIFF" || Str(b, 8, 4) != "WAVE") return 0;

            int pos = 12, rate = 0, bits = 0, dataLen = 0, dataPos = -1;
            while (pos + 8 <= b.Length)
            {
                string id = Str(b, pos, 4);
                if (pos + 8 > b.Length) break;
                int size = BitConverter.ToInt32(b, pos + 4);
                if (size < 0 || pos + 8L + size > b.Length) size = b.Length - pos - 8; // 容错截断
                if (id == "fmt " && pos + 24 <= b.Length)
                {
                    // +8 wFormatTag(2) +10 nChannels(2) +12 nSamplesPerSec(4) +16 byteRate(4) +20 blockAlign(2) +22 bits(2)
                    int format = b[pos + 8];
                    channels = BitConverter.ToUInt16(b, pos + 10);
                    rate = BitConverter.ToInt32(b, pos + 12);
                    bits = BitConverter.ToUInt16(b, pos + 22);
                    if (format != 1) return 0; // 只认 PCM
                }
                else if (id == "data") { dataLen = size; dataPos = pos + 8; }
                pos += 8 + size + (size & 1);
            }
            if (rate <= 0 || bits != 16 || dataPos < 0) return 0;
            if (channels < 1 || channels > 2) return 0;
            frames = dataLen / 2 / channels;
            if (frames <= 0) return 0;

            int total = frames * channels;
            var outp = new float[total];
            int avail = Math.Min(total, (b.Length - dataPos) / 2);
            for (int i = 0; i < avail; i++)
                outp[i] = BitConverter.ToInt16(b, dataPos + i * 2) / 32768f;
            pcm = outp;
            return rate;
        }

        public static float[] ReadFromFile(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            float[] data;
            int rate = ReadPcm(b, out data, out int ch, out int frames);
            if (rate <= 0) return null;
            if (ch == 1) return data;
            var mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float s = 0;
                for (int c = 0; c < ch; c++) s += data[i * ch + c];
                mono[i] = s / ch;
            }
            return mono;
        }

        /// <summary>只读 WAV 头，不解出采样（快速判断时长/采样率）。</summary>
        public static bool Probe(string path, out int rate, out double seconds)
        {
            rate = 0; seconds = 0;
            try
            {
                var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using (fs)
                {
                    var head = new byte[12];
                    if (fs.Read(head, 0, 12) != 12) return false;
                    fs.Position = 0;
                    var b = new byte[Math.Min(fs.Length, 1 << 20)];
                    int rd = fs.Read(b, 0, b.Length);
                    float[] tmp;
                    rate = ReadPcm(new ArraySegment<byte>(b, 0, rd).ToArray(), out tmp, out int ch, out int frames);
                    if (rate > 0) seconds = (double)frames / rate;
                }
            }
            catch { return false; }
            return rate > 0;
        }

        public static void Write(string path, float[] data, int rate, int channels = 1)
        {
            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs, Encoding.ASCII))
            {
                int bytes = data.Length * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + bytes);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)1);                    // PCM
                w.Write((short)channels);
                w.Write(rate);
                w.Write(rate * channels * 2);        // byte rate
                w.Write((short)(channels * 2));      // block align
                w.Write((short)16);                  // bits
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(bytes);
                var buf = new byte[bytes];
                for (int i = 0; i < data.Length; i++)
                {
                    float v = data[i];
                    if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                    short s = (short)Math.Round(v * 32767f);
                    buf[i * 2] = (byte)(s & 0xFF);
                    buf[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
                }
                w.Write(buf);
            }
        }

        private static string Str(byte[] b, int off, int len)
        {
            if (off + len > b.Length) return "";
            return Encoding.ASCII.GetString(b, off, len);
        }
    }

    public class FfmpegResult
    {
        public bool Ok;
        public string Error;
        public string OutputPath;
    }

    /// <summary>调用游戏自带的 ffmpeg 做 ogg 解码 / 编码。C# 侧不引入任何 OGG 库。</summary>
    public static class Ffmpeg
    {
        public static string FindExe(string gameDir)
        {
            if (string.IsNullOrEmpty(gameDir)) return null;
            string p = Path.Combine(gameDir, "ffmpeg", "ffmpeg.exe");
            return File.Exists(p) ? p : null;
        }

        private static FfmpegResult Run(string exe, string args, int timeoutMs)
        {
            var r = new FfmpegResult();
            if (!File.Exists(exe)) { r.Error = "找不到 ffmpeg.exe: " + exe; return r; }
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                using (var p = Process.Start(psi))
                {
                    string err = p.StandardError.ReadToEnd();
                    p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        r.Error = "ffmpeg 超时";
                        return r;
                    }
                    if (p.ExitCode != 0) { r.Error = "ffmpeg 退出码 " + p.ExitCode + ": " + Trim(err); return r; }
                }
                r.Ok = true;
                return r;
            }
            catch (Exception e) { r.Error = e.GetType().Name + ": " + e.Message; return r; }
        }

        /// <summary>任意音频 -> 44100 单声道 16bit PCM wav，返回该 wav 路径</summary>
        public static FfmpegResult ToWav(string exe, string input, string outWav, int timeoutMs = 120000)
        {
            string a = "-v quiet -y -i \"" + input + "\" -ac 1 -ar 44100 -f wav \"" + outWav + "\"";
            var r = Run(exe, a, timeoutMs);
            r.OutputPath = outWav;
            if (r.Ok && !File.Exists(outWav)) { r.Ok = false; r.Error = "ffmpeg 没产出文件"; }
            return r;
        }

        /// <summary>wav -> ogg（q4 默认，ADOFAI 用 q4 足够且体积小）</summary>
        public static FfmpegResult ToOgg(string exe, string inWav, string outOgg, int quality = 4, int timeoutMs = 180000)
        {
            string a = "-v quiet -y -i \"" + inWav + "\" -c:a libvorbis -q:a " + quality + " \"" + outOgg + "\"";
            var r = Run(exe, a, timeoutMs);
            r.OutputPath = outOgg;
            if (r.Ok && !File.Exists(outOgg)) { r.Ok = false; r.Error = "ffmpeg 没产出 ogg"; }
            return r;
        }

        private static string Trim(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ");
            return s.Length <= 300 ? s : s.Substring(s.Length - 300);
        }
    }
}
