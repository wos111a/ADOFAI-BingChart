// 图标与控件素材
//
// 冰方块图标优先读 ui/ice.png —— 那是把系统 🧊 emoji（Segoe UI Emoji 字形）
// 1:1 渲染出来的透明 PNG，因为直接写 🧊 在这套 Unity 字体里会显示成方框。
// 读不到就回退到下面程序绘制的立方体，保证任何情况下都有图标可显示。
//
// 齿轮、「文A」语言图标、圆角矩形都是程序绘制，不依赖外部资源。
using System;
using UnityEngine;

namespace BingChart
{
    public static class Icons
    {
        static Texture2D _cube, _cubeBig, _gear, _langA, _langKo, _white;
        public static string LangKoSource = "";

        public static Texture2D White
        {
            get
            {
                if (_white == null) { _white = Solid(Color.white); }
                return _white;
            }
        }

        // 图标优先用 mod 目录 ui/ice.png（把系统 🧊 emoji 1:1 扣成的透明 PNG），
        // 读不到才回退到程序绘制的立方体，保证任何情况下都有图标可显示。
        static string _loadNote = "";

        public static Texture2D Cube
        {
            get
            {
                if (_cube == null) _cube = LoadPng("ice.png") ?? MakeCube(96);
                return _cube;
            }
        }

        public static Texture2D CubeBig
        {
            get
            {
                if (_cubeBig == null) _cubeBig = LoadPng("ice.png") ?? MakeCube(96);
                return _cubeBig;
            }
        }

        /// <summary>图标是从文件加载的还是程序画的（诊断面板会显示）。</summary>
        public static string CubeSource { get { var _ = Cube; return _loadNote; } }

        static Texture2D LoadPng(string fileName)
        {
            try
            {
                string modPath = ModMain.ModPath;
                if (string.IsNullOrEmpty(modPath)) { _loadNote = "程序绘制（ModPath 未就绪）"; return null; }
                string p = System.IO.Path.Combine(modPath, "ui", fileName);
                if (!System.IO.File.Exists(p)) { _loadNote = "程序绘制（找不到 ui/" + fileName + "）"; return null; }
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                t.hideFlags = HideFlags.HideAndDontSave;
                if (!t.LoadImage(System.IO.File.ReadAllBytes(p)))
                {
                    _loadNote = "程序绘制（PNG 解码失败）";
                    return null;
                }
                t.filterMode = FilterMode.Bilinear;
                t.wrapMode = TextureWrapMode.Clamp;
                _loadNote = "emoji 图（ui/" + fileName + " " + t.width + "x" + t.height + "）";
                return t;
            }
            catch (Exception e)
            {
                _loadNote = "程序绘制（" + e.GetType().Name + "）";
                return null;
            }
        }
        public static Texture2D Gear { get { if (_gear == null) _gear = MakeGear(32); return _gear; } }
        public static Texture2D LangA { get { if (_langA == null) _langA = MakeLangA(32); return _langA; } }

        /// <summary>
        /// 韩文「한」。中文字体（微软雅黑/宋体/黑体/等线/游哥特）**都没有韩文字形**，
        /// 只有 malgun.ttf 有；Unity 的系统字体回退能否找到无法保证，找不到就是一个方框。
        /// 所以和 🧊 一样：从韩文字体里把这个字渲染成透明 PNG 带进 mod，用图片画，不依赖字体。
        /// </summary>
        public static Texture2D LangKo
        {
            get
            {
                if (_langKo == null)
                {
                    _langKo = LoadPng("lang_ko.png");
                    LangKoSource = _langKo == null ? "无（会退回文字 KR）" : "图片";
                }
                return _langKo;
            }
        }

        public static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c); t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        // ---------------- 淡蓝色立方体冰块（🧊 的样子）----------------
        public static Texture2D MakeCube(int s)
        {
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
            t.hideFlags = HideFlags.HideAndDontSave;
            var buf = new Color32[s * s];
            for (int i = 0; i < s * s; i++) buf[i] = new Color32(0, 0, 0, 0);

            // 六个轮廓点（等距视角的立方体）
            float[,] V = {
                {0.50f, 0.045f},   // 0 上顶点
                {0.075f,0.275f},   // 1 左上
                {0.925f,0.275f},   // 2 右上
                {0.50f, 0.505f},   // 3 中心
                {0.075f,0.735f},   // 4 左下
                {0.925f,0.735f},   // 5 右下
                {0.50f, 0.962f},   // 6 下顶点
            };

            Color32 cTop = new Color32(214, 240, 255, 255);   // 顶面最亮
            Color32 cLeft = new Color32(140, 205, 245, 255);  // 左面中等
            Color32 cRight = new Color32(88, 160, 216, 255);  // 右面最暗
            Color32 cEdge = new Color32(38, 84, 132, 255);    // 棱线

            int[] topPoly = { 0, 2, 3, 1 };
            int[] leftPoly = { 1, 3, 6, 4 };
            int[] rightPoly = { 3, 2, 5, 6 };

            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float fx = (x + 0.5f) / s, fy = (y + 0.5f) / s;
                    // 注意贴图 y 轴向上，这里把 fy 翻过来更直观
                    float uy = 1f - fy;
                    Color32 col; bool hit = false;
                    if (InPoly(fx, uy, V, topPoly)) { col = cTop; hit = true; }
                    else if (InPoly(fx, uy, V, leftPoly)) { col = cLeft; hit = true; }
                    else if (InPoly(fx, uy, V, rightPoly)) { col = cRight; hit = true; }
                    else col = new Color32(0, 0, 0, 0);

                    if (hit)
                    {
                        // 左上加一块高光
                        float hx = fx - 0.34f, hy = uy - 0.26f;
                        float hd = Mathf.Sqrt(hx * hx + hy * hy);
                        if (hd < 0.085f)
                        {
                            float k = 1f - hd / 0.085f;
                            col = Lerp32(col, new Color32(255, 255, 255, 255), k * 0.85f);
                        }
                    }
                    // 棱线：靠近轮廓线就压深
                    if (hit && NearAnyEdge(fx, uy, V, 1.6f / s))
                        col = cEdge;

                    buf[y * s + x] = col;
                }

            // 外轮廓再描一圈，边缘更利落
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float fx = (x + 0.5f) / s, uy = 1f - (y + 0.5f) / s;
                    if (OnOutline(fx, uy, V, 1.2f / s))
                        buf[y * s + x] = cEdge;
                }

            t.SetPixels32(buf); t.Apply();
            return t;
        }

        static bool InPoly(float x, float y, float[,] V, int[] idx)
        {
            bool inside = false;
            int n = idx.Length;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = V[idx[i], 0], yi = V[idx[i], 1];
                float xj = V[idx[j], 0], yj = V[idx[j], 1];
                if (((yi > y) != (yj > y)) && (x < (xj - xi) * (y - yi) / (yj - yi) + xi))
                    inside = !inside;
            }
            return inside;
        }

        static float DistToSeg(float px_, float py, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float len2 = dx * dx + dy * dy;
            float t = len2 <= 1e-9f ? 0f : Mathf.Clamp01(((px_ - ax) * dx + (py - ay) * dy) / len2);
            float cx = ax + t * dx, cy = ay + t * dy;
            return Mathf.Sqrt((px_ - cx) * (px_ - cx) + (py - cy) * (py - cy));
        }

        static bool NearAnyEdge(float x, float y, float[,] V, float tol)
        {
            int[,] E = { {0,1},{0,2},{1,3},{2,3},{3,4},{3,5},{4,6},{5,6} };
            for (int i = 0; i < 8; i++)
                if (DistToSeg(x, y, V[E[i,0],0], V[E[i,0],1], V[E[i,1],0], V[E[i,1],1]) <= tol)
                    return true;
            return false;
        }

        static bool OnOutline(float x, float y, float[,] V, float tol)
        {
            int[,] O = { {0,1},{0,2},{1,4},{2,5},{4,6},{5,6} };
            for (int i = 0; i < 6; i++)
                if (DistToSeg(x, y, V[O[i,0],0], V[O[i,0],1], V[O[i,1],0], V[O[i,1],1]) <= tol)
                    return true;
            return false;
        }

        static Color32 Lerp32(Color32 a, Color32 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color32(
                (byte)(a.r + (b.r - a.r) * t),
                (byte)(a.g + (b.g - a.g) * t),
                (byte)(a.b + (b.b - a.b) * t),
                255);
        }

        // ---------------- 齿轮（设置）----------------
        public static Texture2D MakeGear(int s)
        {
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
            t.hideFlags = HideFlags.HideAndDontSave;
            var buf = new Color32[s * s];
            float c = s * 0.5f, R = s * 0.40f, rIn = s * 0.155f;
            var col = new Color32(226, 242, 255, 255);
            var hole = new Color32(20, 41, 66, 255);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = x - c + 0.5f, dy = y - c + 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    float tooth = R * (0.84f + 0.16f * Mathf.Cos(ang * 8f));
                    Color32 v = new Color32(0, 0, 0, 0);
                    if (d <= tooth && d >= rIn) v = col;
                    else if (d < rIn * 0.55f) v = hole;
                    buf[y * s + x] = v;
                }
            t.SetPixels32(buf); t.Apply();
            t.filterMode = FilterMode.Bilinear;
            return t;
        }

        // ---------------- Windows 风格「文A」语言图标 ----------------
        public static Texture2D MakeLangA(int s)
        {
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
            t.hideFlags = HideFlags.HideAndDontSave;
            var buf = new Color32[s * s];
            var frame = new Color32(226, 242, 255, 255);
            var glyph = new Color32(150, 205, 240, 255);
            int ox = (int)(s * 0.08f), oy = (int)(s * 0.22f);
            int w = (int)(s * 0.84f), h = (int)(s * 0.56f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int lx = x - ox, ly = y - oy;
                    Color32 v = new Color32(0, 0, 0, 0);
                    if (lx >= 0 && ly >= 0 && lx < w && ly < h)
                    {
                        bool edge = lx < 2 || ly < 2 || lx >= w - 2 || ly >= h - 2;
                        if (edge) v = frame;
                        else if (ly > h * 0.28f && ly < h * 0.72f) v = glyph;   // A 的横杠区
                    }
                    // 竖笔画：A 的两条斜边 + 右侧小竖
                    float mid = w * 0.42f;
                    float rel = (ly - h * 0.20f) / Mathf.Max(1f, h * 0.60f);
                    if (lx >= 0 && ly >= 0 && lx < w && ly < h && rel >= 0f && rel <= 1f)
                    {
                        int barW = Mathf.Max(2, (int)(w * 0.10f));
                        int cx = (int)(mid - rel * w * 0.30f);
                        if (Mathf.Abs(lx - cx) < barW / 2) v = glyph;
                        cx = (int)(mid + rel * w * 0.30f);
                        if (Mathf.Abs(lx - cx) < barW / 2) v = glyph;
                    }
                    buf[y * s + x] = v;
                }
            t.SetPixels32(buf); t.Apply();
            t.filterMode = FilterMode.Bilinear;
            return t;
        }

        // ---------------- 圆角矩形贴图 ----------------
        /// <summary>画一个圆角矩形。逐行扫描，每行算左右内缩量，稳定不会画歪。</summary>
        public static void RoundRect(Rect r, float radius, Color col)
        {
            if (r.width <= 0f || r.height <= 0f) return;
            var old = GUI.color;
            GUI.color = col;
            var white = White;
            radius = Mathf.Max(0f, Mathf.Min(radius, Mathf.Min(r.width, r.height) * 0.5f));
            if (radius < 0.5f)
            {
                GUI.DrawTexture(r, white);
                GUI.color = old;
                return;
            }
            int rows = Mathf.Max(1, Mathf.CeilToInt(r.height));
            for (int i = 0; i < rows; i++)
            {
                float y = i + 0.5f;                       // 行中心（相对矩形顶）
                float dy = 0f;
                if (y < radius) dy = radius - y;
                else if (y > r.height - radius) dy = y - (r.height - radius);
                float inset = 0f;
                if (dy > 0f)
                {
                    float k = radius * radius - dy * dy;
                    inset = radius - (k > 0f ? Mathf.Sqrt(k) : 0f);
                }
                float x0 = r.x + inset;
                float w = r.width - inset * 2f;
                if (w <= 0f) continue;
                GUI.DrawTexture(new Rect(x0, r.y + i, w, 1f), white);
            }
            GUI.color = old;
        }

        /// <summary>
        /// 圆角矩形 + 圆角描边。用「先画大一圈的边框色，再盖上内部填色」实现，
        /// 这样四角是真圆角。之前那版只画四条直边，角上是方的（看起来像括号）。
        /// </summary>
        public static void RoundPanel(Rect r, float radius, Color fill, float border, Color borderCol)
        {
            if (border > 0f)
                RoundRect(new Rect(r.x - border, r.y - border, r.width + border * 2f, r.height + border * 2f),
                          radius + border, borderCol);
            RoundRect(r, radius, fill);
        }
    }
}
