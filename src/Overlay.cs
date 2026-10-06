// 游戏内界面：全屏设置页 / 首次操作说明 / 实时状态卡片 / 提示条
//
// 设计要点：
//  · 所有尺寸和字号都按屏幕高度缩放（S 系数）—— 之前字号写死 15px，在 717x528 的小窗口里显得很小
//  · 背景是程序画的渐变 + 飘雪粒子，不依赖任何图片
//  · 冰方块和韩文「한」是预渲染的透明 PNG（从系统字体扣的），不靠字体渲染
//  · 状态卡片固定在左上角（用户要求）
using System;
using System.Collections.Generic;
using UnityEngine;

namespace BingChart
{
    public static class Overlay
    {
        // ---------- 配色 ----------
        static readonly Color CBg0 = new Color(0.055f, 0.075f, 0.115f, 1f);
        static readonly Color CBg1 = new Color(0.105f, 0.145f, 0.215f, 1f);
        static readonly Color CPanel = new Color(0.10f, 0.13f, 0.185f, 0.95f);
        static readonly Color CPanel2 = new Color(0.145f, 0.185f, 0.255f, 0.95f);
        static readonly Color CLine = new Color(0.40f, 0.72f, 0.92f, 0.45f);
        static readonly Color CAccent = new Color(0.50f, 0.86f, 1.00f, 1f);
        static readonly Color CText = new Color(0.91f, 0.95f, 0.99f, 1f);
        static readonly Color CMuted = new Color(0.60f, 0.68f, 0.78f, 1f);

        // 首次说明：深蓝底 + 暖色描边（不再整块红，用户反馈"瘆人"）
        static readonly Color CHintBg = new Color(0.10f, 0.125f, 0.175f, 0.97f);
        static readonly Color CHintEdge = new Color(1.00f, 0.72f, 0.42f, 0.85f);

        // 卡片
        static readonly Color CCardBg = new Color(0.085f, 0.105f, 0.145f, 0.94f);
        static readonly Color CErrBg = new Color(0.26f, 0.10f, 0.10f, 0.96f);
        static readonly Color CBad = new Color(1.00f, 0.62f, 0.50f, 1f);

        // ---------- UI 缩放 ----------
        static float S = 1f;
        static int Px(float baseSize) { return Mathf.Max(10, Mathf.RoundToInt(baseSize * S)); }
        static float Fp(float basePx) { return basePx * S; }

        // ---------- 资源 ----------
        static Texture2D texWhite, texSpark;
        static GUIStyle stTitle, stH1, stBody, stSmall, stBtn, stBtnHover, stTab, stTabOn;
        static bool inited;
        static int builtScaleStep = -1;

        static void Ensure()
        {
            if (inited) return;
            texWhite = NewTex(Color.white);
            texSpark = NewTex(new Color(1, 1, 1, 0.85f));

            stTitle = new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            stTitle.normal.textColor = CText;
            stH1 = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
            stH1.normal.textColor = CAccent;
            stBody = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.UpperLeft, wordWrap = true, richText = true };
            stBody.normal.textColor = CText;
            stSmall = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.UpperLeft, wordWrap = true };
            stSmall.normal.textColor = CMuted;

            // ★★ 关键修复：按钮和标签的文字样式**必须基于 GUI.skin.label**。
            //   上一版基于 GUI.skin.button —— 那个样式自带一张系统默认按钮背景图，
            //   再套到 GUI.Label 上就会在我们自己画的圆角底上**又叠一层灰框**，
            //   看起来就是"按钮和整个 UI 不是一个风格"。
            stBtn = new GUIStyle(GUI.skin.label) { fontSize = 17, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = true };
            stBtn.normal.textColor = CText;
            stBtnHover = new GUIStyle(stBtn); stBtnHover.normal.textColor = Color.white;
            stTab = new GUIStyle(GUI.skin.label) { fontSize = 19, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            stTab.normal.textColor = CMuted;
            stTabOn = new GUIStyle(stTab); stTabOn.normal.textColor = Color.white;
            inited = true;
        }

        static void EnsureScaledStyles()
        {
            int step = Mathf.RoundToInt(S * 20f);
            if (step == builtScaleStep) return;
            builtScaleStep = step;
            stTitle.fontSize = Px(34);
            stH1.fontSize = Px(22);
            stBody.fontSize = Px(17);
            stSmall.fontSize = Px(15);
            stBtn.fontSize = Px(17);
            stBtnHover.fontSize = Px(17);
            stTab.fontSize = Px(19);
            stTabOn.fontSize = Px(19);
        }

        static Texture2D NewTex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c); t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        // ---------- 飘雪粒子 ----------
        class Flake { public float X, Y, Z, R, Spd, Ph; }
        static List<Flake> flakes;
        static float lastT;
        public static bool ParticlesEnabled = true;

        static void TickParticles()
        {
            if (flakes == null)
            {
                flakes = new List<Flake>();
                var rnd = new System.Random(12345);
                for (int i = 0; i < 90; i++)
                    flakes.Add(new Flake
                    {
                        X = (float)rnd.NextDouble(),
                        Y = (float)rnd.NextDouble(),
                        Z = 0.35f + (float)rnd.NextDouble() * 0.65f,
                        R = 1.2f + (float)rnd.NextDouble() * 2.6f,
                        Spd = 0.012f + (float)rnd.NextDouble() * 0.05f,
                        Ph = (float)rnd.NextDouble() * 6.28f
                    });
            }
            float t = Time.unscaledTime;
            float dt = Mathf.Clamp(t - lastT, 0f, 0.1f);
            lastT = t;
            foreach (var f in flakes)
            {
                f.Y -= f.Spd * f.Z * dt;
                f.X += Mathf.Sin(t * 0.7f + f.Ph) * 0.05f * dt * f.Z;
                if (f.Y < -0.05f) { f.Y = 1.05f; f.X = UnityEngine.Random.value; }
                if (f.X < -0.05f) f.X = 1.05f; else if (f.X > 1.05f) f.X = -0.05f;
            }
        }

        static void DrawBackdrop(float dim)
        {
            var r = new Rect(0, 0, Screen.width, Screen.height);
            GUI.color = CBg0; GUI.DrawTexture(r, texWhite);
            GUI.color = new Color(CBg1.r, CBg1.g, CBg1.b, 0.5f);
            GUI.DrawTexture(new Rect(0, Screen.height * 0.40f, Screen.width, Screen.height * 0.60f), texWhite);
            GUI.color = Color.white;

            if (ParticlesEnabled)
            {
                TickParticles();
                foreach (var f in flakes)
                {
                    float sz = f.R * f.Z * S;
                    GUI.color = new Color(0.85f, 0.95f, 1f, 0.10f + 0.30f * f.Z);
                    GUI.DrawTexture(new Rect(f.X * Screen.width, f.Y * Screen.height, sz, sz), texSpark);
                }
            }
            if (dim > 0.001f)
            {
                GUI.color = new Color(0f, 0f, 0f, dim);
                GUI.DrawTexture(r, texWhite);
            }
            GUI.color = Color.white;
        }

        // ---------- 通用控件 ----------
        static void Panel(Rect r, Color bg, float border = 0f, Color? borderCol = null)
        {
            if (border > 0f)
                Icons.RoundRect(new Rect(r.x - border, r.y - border, r.width + border * 2, r.height + border * 2),
                                Mathf.Max(4f, Fp(10f) + border), borderCol ?? CLine);
            Icons.RoundRect(r, Fp(10f), bg);
        }

        static void Icon(Rect r)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(r, Icons.CubeBig);
        }

        // ---------- 按钮样式 ----------
        // 统一走这一套：都画「圆角底 + 2px 描边」，描边颜色区分用途。
        // 文字一律用基于 GUI.skin.label 的样式（绝不带系统按钮背景）。
        static readonly Color CBtnFace = new Color(0.155f, 0.195f, 0.260f, 1f);
        static readonly Color CBtnFaceH = new Color(0.215f, 0.275f, 0.365f, 1f);
        static readonly Color CBtnEdge = new Color(0.40f, 0.72f, 0.92f, 0.42f);
        static readonly Color CBtnPriFace = new Color(0.145f, 0.375f, 0.560f, 1f);
        static readonly Color CBtnPriFaceH = new Color(0.195f, 0.490f, 0.710f, 1f);
        static readonly Color CBtnDanFace = new Color(0.520f, 0.140f, 0.160f, 1f);
        static readonly Color CBtnDanFaceH = new Color(0.720f, 0.200f, 0.200f, 1f);
        static readonly Color CBtnDanEdge = new Color(1.00f, 0.45f, 0.42f, 0.90f);

        public enum Btn { Normal, Primary, Danger, Flat }

        static bool Button(Rect r, string text, bool primary = false)
        {
            return Button(r, text, primary ? Btn.Primary : Btn.Normal);
        }

        static bool Button(Rect r, string text, Btn kind)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            Color fill, edge;
            switch (kind)
            {
                case Btn.Primary:
                    fill = hover ? CBtnPriFaceH : CBtnPriFace; edge = CAccent; break;
                case Btn.Danger:
                    fill = hover ? CBtnDanFaceH : CBtnDanFace; edge = CBtnDanEdge; break;
                case Btn.Flat:
                    fill = hover ? new Color(0.20f, 0.25f, 0.33f, 0.85f) : new Color(0f, 0f, 0f, 0f);
                    edge = hover ? CBtnEdge : new Color(0f, 0f, 0f, 0f); break;
                default:
                    fill = hover ? CBtnFaceH : CBtnFace; edge = hover ? CAccent : CBtnEdge; break;
            }
            Icons.RoundPanel(r, Fp(9f), fill, Fp(2f), edge);
            var st = new GUIStyle(stBtn) { fontSize = Px(17) };
            st.normal.textColor = (kind == Btn.Danger) ? new Color(1f, 0.93f, 0.93f, 1f) : CText;
            if (hover) st.normal.textColor = Color.white;
            GUI.Label(r, Loc.T(text), st);
            if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            { Event.current.Use(); return true; }
            return false;
        }

        /// <summary>画一个叉（两条 45° 的粗棒）。用户要求右上角用「红色的好看的 X」。</summary>
        static void DrawCross(Rect r, Color col, float thick)
        {
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(45f, r.center);
            Icons.RoundRect(new Rect(r.x, r.center.y - thick * 0.5f, r.width, thick), thick * 0.5f, col);
            GUI.matrix = m;
            GUIUtility.RotateAroundPivot(-45f, r.center);
            Icons.RoundRect(new Rect(r.x, r.center.y - thick * 0.5f, r.width, thick), thick * 0.5f, col);
            GUI.matrix = m;
        }

        static bool CloseX(Rect r)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            Color face = hover ? new Color(0.88f, 0.24f, 0.26f, 1f) : new Color(0.62f, 0.16f, 0.20f, 1f);
            Icons.RoundPanel(r, Fp(10f), face, Fp(2f),
                             hover ? new Color(1f, 0.70f, 0.68f, 1f) : new Color(1f, 0.45f, 0.42f, 0.75f));
            float ins = r.width * 0.28f;
            DrawCross(new Rect(r.x + ins, r.y + ins, r.width - ins * 2f, r.height - ins * 2f),
                      Color.white, Mathf.Max(2f, Fp(3.4f)));
            if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            { Event.current.Use(); return true; }
            return false;
        }

        static void DrawCheck(Rect box, bool val)
        {
            float bs = box.width;
            Icons.RoundRect(box, Fp(5f), val ? CAccent : new Color(0.25f, 0.30f, 0.38f, 1f));
            if (val)
            {
                GUI.color = new Color(0.05f, 0.10f, 0.16f, 1f);
                GUI.DrawTexture(new Rect(box.x + bs * 0.18f, box.y + bs * 0.46f, bs * 0.5f, bs * 0.12f), texWhite);
                GUI.DrawTexture(new Rect(box.x + bs * 0.36f, box.y + bs * 0.22f, bs * 0.12f, bs * 0.45f), texWhite);
                GUI.color = Color.white;
            }
        }

        static bool ToggleRow(Rect r, ref bool val)
        {
            float bs = Fp(26f);
            var box = new Rect(r.x, r.y + (r.height - bs) * 0.5f, bs, bs);
            bool hover = r.Contains(Event.current.mousePosition);
            DrawCheck(box, val);
            if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            { Event.current.Use(); val = !val; return true; }
            return false;
        }

        static bool CheckRow(Rect r, string label, ref bool val)
        {
            float bs = Fp(26f);
            var box = new Rect(r.x, r.y + (r.height - bs) * 0.5f, bs, bs);
            bool hover = r.Contains(Event.current.mousePosition);
            DrawCheck(box, val);
            GUI.Label(new Rect(r.x + bs + Fp(10f), r.y, r.width - bs - Fp(10f), r.height), Loc.T(label), stBody);
            if (hover && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            { Event.current.Use(); val = !val; return true; }
            return false;
        }

        static float Slider(Rect r, float val, float min, float max)
        {
            float h = Fp(8f);
            var back = new Rect(r.x, r.y + r.height / 2 - h / 2, r.width, h);
            Icons.RoundRect(back, h / 2, new Color(0.20f, 0.25f, 0.33f, 1f));
            float t = Mathf.InverseLerp(min, max, val);
            if (t > 0.001f)
                Icons.RoundRect(new Rect(back.x, back.y, back.width * t, back.height), h / 2, CAccent);
            float kx = back.x + back.width * t;
            float ks = Fp(18f);
            Icons.RoundRect(new Rect(kx - ks / 2, r.y + (r.height - ks) / 2, ks, ks), Fp(5f), Color.white);
            Icons.RoundRect(new Rect(kx - ks * 0.3f, r.y + (r.height - ks * 0.6f) / 2, ks * 0.6f, ks * 0.6f),
                            Fp(3f), CAccent);

            var e = Event.current;
            bool down = e.type == EventType.MouseDown && r.Contains(e.mousePosition);
            bool drag = e.type == EventType.MouseDrag && r.Contains(e.mousePosition) && e.button == 0;
            if (down || drag)
            {
                float nt = Mathf.Clamp01((e.mousePosition.x - back.x) / Mathf.Max(1f, back.width));
                float nv = Mathf.Lerp(min, max, nt);
                if (!Mathf.Approximately(nv, val)) { val = nv; GUI.changed = true; }
                e.Use();
            }
            return val;
        }

        // ---------- 状态 ----------
        static float toastUntil;
        static string toastMsg = "";
        static float openAnim, hintAnim;
        static int tab;

        public static void Toast(string msg, float sec = 3.2f)
        {
            toastMsg = msg;   // 显示时统一过 Loc.T，调用点不用改
            toastUntil = Time.unscaledTime + sec;
        }

        public static void Draw()
        {
            // ★ 下限设 1.0：小窗口（如 717x528）也绝不低于设计基准。
            //   之前下限 0.8，而 528/900=0.59 被卡到 0.8，正文只剩 14px，跟原来写死 15px 没区别，
            //   所以用户仍然嫌小。
            S = Mathf.Clamp(Screen.height / 900f, 1.0f, 1.8f);
            Ensure();
            EnsureScaledStyles();

            float dt = Time.unscaledDeltaTime;
            openAnim = Mathf.MoveTowards(openAnim, ModMain.UiSettingsOpen ? 1f : 0f, dt * 5f);
            hintAnim = Mathf.MoveTowards(hintAnim, ModMain.UiHintOpen ? 1f : 0f, dt * 6f);

            if (openAnim > 0.002f) { DrawSettingsPage(openAnim); return; }
            if (hintAnim > 0.002f) { DrawFirstHint(hintAnim); return; }

            DrawCard();
            if (Time.unscaledTime < toastUntil) DrawToast();
        }

        static void DrawToast()
        {
            // 和状态卡片同一条左上角线（用户要求：弹窗统一放左上角）
            float w = Fp(480f), h = Fp(48f);
            var r = new Rect(Fp(16f), Fp(16f), w, h);
            Panel(r, CPanel2, Fp(2f), CLine);
            GUI.Label(r, Loc.T(toastMsg), stBody);
        }

        // ---------- 实时状态卡片（左上角）----------
        static void DrawCard()
        {
            int mode = ModMain.CardMode;
            if (mode == 0) return;

            float pad = Fp(14f), lineH = Fp(24f);
            float w = Fp(500f);
            int bodyLines = mode == 3 ? 3 : (mode == 2 ? 2 : 3);
            float extra = mode == 2 ? Fp(36f) : (mode == 3 ? Fp(48f) : 0f);
            float h = pad + Fp(38f) + bodyLines * lineH + extra + pad;
            var r = new Rect(Fp(16f), Fp(16f), w, h);

            Panel(r, mode == 3 ? CErrBg : CCardBg, Fp(2f),
                  mode == 3 ? new Color(CBad.r, CBad.g, CBad.b, 0.6f) : CLine);
            Icon(new Rect(r.x + pad, r.y + pad, Fp(32f), Fp(32f)));

            var stH = new GUIStyle(stBody) { fontSize = Px(21), fontStyle = FontStyle.Bold };
            var stL = new GUIStyle(stSmall) { fontSize = Px(16) };
            var stL2 = new GUIStyle(stBody) { fontSize = Px(16) };

            string title = mode == 3 ? Loc.T("冰谱 出错了") : (mode == 2 ? Loc.T("正在转换冰谱…") : Loc.T("已识别谱面"));
            GUI.Label(new Rect(r.x + pad + Fp(42f), r.y + pad, w - pad * 2 - Fp(46f), Fp(34f)), title, stH);

            float lx = r.x + pad + Fp(42f);
            float lw = w - pad * 2 - Fp(46f);
            float ly = r.y + pad + Fp(38f);

            if (mode == 1)
            {
                string song = ModMain.CurrentSongTitle.Length > 0 ? ModMain.CurrentSongTitle : ModMain.CurrentSongKey;
                GUI.Label(new Rect(lx, ly, lw, lineH), song, stL2); ly += lineH;
                string cr = Loc.T(ModMain.CurrentChartPath.Length > 0 ? "谱面 已找到" : "谱面 缺失");
                string au = Loc.T(ModMain.CurrentAudioPath.Length > 0 ? "音频 已找到" : "音频 缺失（只按轨道转）");
                GUI.Label(new Rect(lx, ly, lw, lineH), cr + "    " + au + "    " + ModMain.CurrentNoteCount + Loc.T(" 个音符"), stL);
                ly += lineH;
                GUI.Label(new Rect(lx, ly, lw, lineH), Loc.T("按 ") + ModMain.HotkeyText() + Loc.T(" 开始转换"), stL);
            }
            else if (mode == 2)
            {
                GUI.Label(new Rect(lx, ly, lw, lineH), Loc.T(ModMain.ConvertStep), stL2);
                ly += lineH + Fp(4f);
                var bar = new Rect(lx, ly, lw, Fp(14f));
                Icons.RoundRect(bar, Fp(7f), new Color(0.20f, 0.25f, 0.33f, 1f));
                float fw = bar.width * Mathf.Clamp01(ModMain.ConvertProgress01);
                if (fw > 2f) Icons.RoundRect(new Rect(bar.x, bar.y, fw, bar.height), Fp(7f), CAccent);
                ly += Fp(20f);
                GUI.Label(new Rect(lx, ly, lw, lineH), Loc.T("已有的可以先玩原曲，转完自动接上"), stL);
            }
            else
            {
                GUI.Label(new Rect(lx, ly, lw, lineH), Loc.T("原曲不受影响，可以继续玩"), stL); ly += lineH;
                GUI.Label(new Rect(lx, ly, lw, Fp(46f)), Loc.T(ModMain.LastError), stL2);
                ly += Fp(48f);
                if (Button(new Rect(r.xMax - pad - Fp(110f), ly, Fp(110f), Fp(34f)), "知道了"))
                    ModMain.DismissError();
            }
        }

        // ---------- 首次操作说明 ----------
        static void DrawFirstHint(float a)
        {
            DrawBackdrop(0.55f * a);

            float w = Mathf.Min(Screen.width * 0.80f, Fp(880f));
            float h = Mathf.Min(Screen.height * 0.90f, Fp(500f));
            var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

            Panel(r, CHintBg, Fp(3f), CHintEdge);
            Icon(new Rect(r.x + Fp(28), r.y + Fp(24), Fp(64), Fp(64)));

            var stBig = new GUIStyle(stBody) { fontSize = Px(38), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(r.x + Fp(106), r.y + Fp(26), w - Fp(140), Fp(46)), Loc.T("冰谱 Mod 怎么用"), stBig);
            var stSub = new GUIStyle(stSmall) { fontSize = Px(16), alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(r.x + Fp(108), r.y + Fp(76), w - Fp(140), Fp(24)), Loc.T("第一次使用，说明一下就这几条"), stSub);

            var stLine = new GUIStyle(stBody) { fontSize = Px(20) };
            var stNote = new GUIStyle(stSmall) { fontSize = Px(16) };
            float ly = r.y + Fp(118);
            float lh = Fp(36);
            string hot = ModMain.HotkeyText();
            string[] lines = {
                Loc.T("进关卡后按 <color=#7FD4F5><b>") + hot + Loc.T("</b></color> 开始把原曲转成冰谱"),
                Loc.T("转换在后台跑（长曲子几十秒），期间照常玩原曲"),
                Loc.T("转完后原曲自动静音、改放冰谱 —— 砖块怎么落，冰就怎么响"),
                Loc.T("不想转就什么都不按，原曲一点不变"),
                Loc.T("退出关卡自动还原，%temp% 里的产物一并清掉")
            };
            for (int k = 0; k < lines.Length; k++)
            {
                GUI.Label(new Rect(r.x + Fp(36), ly, Fp(30), lh), (k + 1).ToString(),
                          new GUIStyle(stLine) { alignment = TextAnchor.MiddleCenter });
                GUI.Label(new Rect(r.x + Fp(72), ly, w - Fp(110), lh), lines[k], stLine);
                ly += lh;
            }
            ly += Fp(6);
            GUI.Label(new Rect(r.x + Fp(36), ly, w - Fp(72), Fp(30)),
                Loc.T("快捷键、音量、音色都在 UMM 面板的「设置」里。"), stNote);
            ly += Fp(26);
            // ★ 红字提醒：在关卡编辑器里测试时，退出千万别保存，否则会把改过的谱面存下来
            var stRed = new GUIStyle(stSmall) { fontSize = Px(17), fontStyle = FontStyle.Bold };
            stRed.normal.textColor = new Color(1.00f, 0.55f, 0.45f, 1f);
            GUI.Label(new Rect(r.x + Fp(36), ly, w - Fp(72), Fp(30)),
                Loc.T("★ 在关卡编辑器里测试的话，退出谱子时记得选「放弃保存」"), stRed);

            float by = r.yMax - Fp(72);
            CheckRow(new Rect(r.x + Fp(36), by, Fp(400), Fp(36)), "不再提醒（下次不弹这个）", ref ModMain.HintNeverAgain);
            if (Button(new Rect(r.xMax - Fp(236), by - Fp(6), Fp(196), Fp(50)), "我已知晓", true))
            {
                ModMain.UiHintOpen = false;
                ModMain.AcknowledgeHint();
            }
            GUI.color = Color.white;
        }

        // ---------- 全屏设置页 ----------
        static readonly string[] Langs = { "中", "EN", "KR" };
        static readonly string[] LangTips = { "简体中文", "English", "한국어" };

        static float scroll;                 // 设置页内容滚动量
        static float lastContentH, lastViewH;

        static void DrawSettingsPage(float a)
        {
            DrawBackdrop(0.3f * a);

            float topH = Fp(104f);
            Panel(new Rect(0, 0, Screen.width, topH), new Color(CBg0.r, CBg0.g, CBg0.b, 0.94f));
            Icon(new Rect(Fp(22), Fp(18), Fp(52), Fp(52)));
            var stBig = new GUIStyle(stBody) { fontSize = Px(30), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            GUI.Label(new Rect(Fp(88), Fp(14), Fp(520), Fp(40)), Loc.T("冰谱 设置"), stBig);
            GUI.Label(new Rect(Fp(90), Fp(58), Fp(660), Fp(24)),
                      Loc.T("A Dance of Fire and Ice · 把每一颗砖换成冰音"), stSmall);

            // 右上角：语言选择 + 红色 X（用户要求把「关闭」换成 X）
            float xs = Fp(46f);
            var xr = new Rect(Screen.width - Fp(22f) - xs, Fp(24f), xs, xs);
            DrawLanguagePicker(new Rect(xr.x - Fp(320f) - Fp(14f), Fp(24f), Fp(320f), Fp(44f)));
            if (CloseX(xr)) ModMain.UiSettingsOpen = false;

            string[] tabs = { "基础设置", "专业设置", "其他设置" };
            float tw = Fp(210f), tg = Fp(14f);
            float tx = (Screen.width - (tw * 3 + tg * 2)) * 0.5f;
            for (int i = 0; i < 3; i++)
            {
                var b = new Rect(tx + (tw + tg) * i, topH + Fp(14f), tw, Fp(48f));
                bool sel = tab == i;
                bool hov = b.Contains(Event.current.mousePosition);
                Icons.RoundPanel(b, Fp(10f),
                                 sel ? new Color(0.20f, 0.45f, 0.64f, 1f)
                                     : (hov ? CBtnFaceH : new Color(0.125f, 0.155f, 0.205f, 1f)),
                                 Fp(2f),
                                 sel ? CAccent : (hov ? CBtnEdge : new Color(0.32f, 0.40f, 0.52f, 0.55f)));
                GUI.Label(b, Loc.T(tabs[i]), sel ? stTabOn : stTab);
                if (hov && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                { tab = i; scroll = 0f; Event.current.Use(); }
            }

            float areaTop = topH + Fp(76f);
            float areaH = Screen.height - areaTop - Fp(18f);
            float areaW = Mathf.Min(Screen.width * 0.88f, Fp(1000f));
            var area = new Rect((Screen.width - areaW) * 0.5f, areaTop, areaW, areaH);
            Panel(area, CPanel, Fp(2f), CLine);
            Icon(new Rect(area.x + Fp(20), area.y + Fp(16), Fp(34), Fp(34)));
            GUI.Label(new Rect(area.x + Fp(64), area.y + Fp(14), Fp(500), Fp(36)), Loc.T(tabs[tab]), stH1);

            float icw = area.width - Fp(52f);
            var view = new Rect(area.x + Fp(26), area.y + Fp(62), icw, area.height - Fp(80));

            // ★滚动：以前内容多了直接被切掉、看不到下面（用户截图里「行为」以下全被裁了）
            var e = Event.current;
            if (view.Contains(e.mousePosition) && e.type == EventType.ScrollWheel)
            { scroll -= e.delta.y * Fp(32f); e.Use(); }

            GUI.BeginGroup(view);
            float cy = -scroll;
            float cw = icw - Fp(16f);
            if (tab == 0) BasicTab(0, ref cy, cw);
            else if (tab == 1) ProTab(0, ref cy, cw);
            else OtherTab(0, ref cy, cw);
            lastContentH = cy + scroll;
            lastViewH = view.height;
            GUI.EndGroup();

            float maxScroll = Mathf.Max(0f, lastContentH - view.height);
            scroll = Mathf.Clamp(scroll, 0f, maxScroll);

            if (maxScroll > 1f)
            {
                var track = new Rect(view.xMax - Fp(6f), view.y, Fp(6f), view.height);
                Icons.RoundRect(track, track.width * 0.5f, new Color(0.20f, 0.25f, 0.33f, 0.85f));
                float kh = Mathf.Max(Fp(44f), view.height * view.height / Mathf.Max(1f, lastContentH));
                float kt = scroll / maxScroll;
                Icons.RoundRect(new Rect(track.x, track.y + (view.height - kh) * kt, track.width, kh),
                                track.width * 0.5f, CAccent);
            }
        }

        static void DrawLanguagePicker(Rect r)
        {
            Panel(r, CPanel2, Fp(2f), CLine);
            var gl = new Rect(r.x + Fp(10f), r.y + r.height / 2 - Fp(9f), Fp(18f), Fp(18f));
            GUI.color = Color.white; GUI.DrawTexture(gl, texWhite);
            GUI.color = new Color(0.28f, 0.34f, 0.44f, 1f);
            GUI.DrawTexture(new Rect(gl.x + Fp(3f), gl.y + Fp(3f), gl.width - Fp(6f), Fp(2f)), texWhite);
            GUI.DrawTexture(new Rect(gl.x + Fp(3f), gl.y + Fp(8f), gl.width - Fp(6f), Fp(2f)), texWhite);
            GUI.DrawTexture(new Rect(gl.x + Fp(3f), gl.y + Fp(13f), (gl.width - Fp(6f)) * 0.6f, Fp(2f)), texWhite);
            GUI.color = Color.white;

            float cw = (r.width - Fp(46f)) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var b = new Rect(r.x + Fp(36f) + cw * i, r.y + Fp(5f), cw - Fp(4f), r.height - Fp(10f));
                bool sel = ModMain.LangIndex == i;
                bool hov = b.Contains(Event.current.mousePosition);
                Icons.RoundPanel(b, Fp(7f),
                                 sel ? new Color(0.24f, 0.45f, 0.62f, 1f)
                                     : (hov ? new Color(0.30f, 0.36f, 0.45f, 1f) : new Color(0, 0, 0, 0)),
                                 sel ? Fp(2f) : 0f, CAccent);
                if (i == 2 && Icons.LangKo != null)
                {
                    // 韩文用预渲染的透明图（中文字体没有韩文字形，直接用字会变方框）
                    float g = Fp(26f);
                    GUI.color = sel ? Color.white : CMuted;
                    GUI.DrawTexture(new Rect(b.x + (b.width - g) * 0.5f, b.y + (b.height - g) * 0.5f, g, g), Icons.LangKo);
                    GUI.color = Color.white;
                }
                else
                {
                    var st = new GUIStyle(stSmall) { alignment = TextAnchor.MiddleCenter, fontSize = Px(16) };
                    st.normal.textColor = sel ? Color.white : CMuted;
                    if (sel) st.fontStyle = FontStyle.Bold;
                    GUI.Label(b, Langs[i], st);
                }
                if (hov && Event.current.type == EventType.MouseDown && Event.current.button == 0)
                { ModMain.LangIndex = i; ModMain.ApplyLanguage(); Event.current.Use(); }
            }
        }

        // ---------- 设置页布局助手 ----------
        static void Head(string t, float x, ref float y, float w, string resetAction = null)
        {
            y += Fp(14f);
            float bw = Fp(88f), bh = Fp(30f);
            float tw = w - (resetAction != null ? bw + Fp(12f) : 0f);
            GUI.Label(new Rect(x, y + Fp(2f), tw, Fp(30f)), Loc.T(t), stH1);
            if (resetAction != null)
            {
                if (Button(new Rect(x + w - bw, y, bw, bh), "重置", Btn.Flat))
                    ModMain.UiAction = resetAction;
            }
            y += Fp(38f);
        }

        static void Note(string s, float x, ref float y, float w)
        {
            float cpl = Mathf.Max(20f, w / (Px(14) * 0.62f));
            int rows = 1 + (int)(s.Length / cpl);
            float h = Fp(21f) * rows;
            GUI.Label(new Rect(x, y, w, h + Fp(8f)), Loc.T(s), stSmall);
            y += h + Fp(10f);
        }

        static void TextRow(string label, string value, float x, ref float y, float w)
        {
            GUI.Label(new Rect(x, y, w, Fp(24f)), Loc.T(label), stBody);
            y += Fp(24f);
            GUI.Label(new Rect(x + Fp(16f), y, w - Fp(16f), Fp(24f)), Loc.T(value), stSmall);
            y += Fp(30f);
        }

        static void Toggle(string label, ref bool val, float x, ref float y, float w)
        {
            GUI.Label(new Rect(x, y, w, Fp(26f)), Loc.T(label), stBody);
            y += Fp(26f);
            var r = new Rect(x, y, w, Fp(30f));
            bool nv = val;
            if (ToggleRow(r, ref nv)) val = nv;
            y += Fp(36f);
        }

        static void Slide(string label, ref float val, float min, float max, string readout,
                          float x, ref float y, float w)
        {
            GUI.Label(new Rect(x, y, w * 0.64f, Fp(24f)), Loc.T(label), stBody);
            GUI.Label(new Rect(x + w * 0.64f, y, w * 0.36f, Fp(24f)), Loc.T(readout), stSmall);
            y += Fp(26f);
            var r = new Rect(x, y, w, Fp(26f));
            float nv = Slider(r, val, min, max);
            if (!Mathf.Approximately(nv, val)) val = nv;
            y += Fp(34f);
        }

        static void BtnRow(string[] labels, float x, ref float y, float w)
        {
            BtnRow(labels, x, ref y, w, Btn.Normal);
        }

        static void BtnRow(string[] labels, float x, ref float y, float w, Btn kind)
        {
            float bw = (w - Fp(10f) * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                var r = new Rect(x + (bw + Fp(10f)) * i, y, bw, Fp(38f));
                if (Button(r, labels[i], kind)) ModMain.UiAction = labels[i];   // 动作键用未翻译原文
            }
            y += Fp(46f);
        }

        static void SlideInt(string label, ref int val, int min, int max, string readout,
                             float x, ref float y, float w)
        {
            float f = val;
            Slide(label, ref f, min, max, readout, x, ref y, w);
            val = Mathf.Clamp(Mathf.RoundToInt(f), min, max);
        }

        // ---------- 三个标签页的内容 ----------
        // 用户要求：只留 基础设置 / 专业设置 / 其他设置 三页，每组带「重置」，另加总的「全部恢复默认」。

        // ===== 基础设置：普通用户日常要动的都在这里 =====
        static void BasicTab(float x, ref float y, float w)
        {
            var c = ModMain.cfg;
            Head("开关与音量", x, ref y, w, "重置基础");
            Toggle("启用冰谱（关掉后完全不干预）", ref c.Enabled, x, ref y, w);
            Slide("冰谱音量", ref c.IceVolume, 0f, 1.5f, (c.IceVolume * 100f).ToString("0") + "%", x, ref y, w);
            Toggle("叠加：原曲保留 + 叠冰音（关掉 = 只放冰音）", ref c.MixOverlay, x, ref y, w);
            Slide("原曲音量", ref c.OriginalVolume, 0f, 1.5f, (c.OriginalVolume * 100f).ToString("0") + "%", x, ref y, w);
            Note("★ 判断「冰有没有踩准」时，建议先把叠加关掉、只听冰音 —— 没被原曲盖住才能一个点一个点数。对准了再打开。", x, ref y, w);

            Head("音色", x, ref y, w);
            TextRow("当前音色", c.IceFile, x, ref y, w);
            BtnRow(new[] { "切换音色", "试听", "试播冰音层" }, x, ref y, w);
            Note("「试听」放的是冰音样本本身（一小段）；「试播冰音层」放的是这个谱子转出来的纯冰音"
               + "（不接游戏时钟、不管原曲）。如果试播有声音、进关卡却没声音，问题就在同步那一段，不在音频。", x, ref y, w);

            Head("转换快捷键", x, ref y, w);
            TextRow("主键 / 副键", c.HotkeyMain + "  +  " + c.HotkeySub, x, ref y, w);
            BtnRow(new[] { "改主键", "改副键" }, x, ref y, w);
            Note(ModMain.CaptureMode == 0 ? "在游戏画面上直接按一个键即可改绑" : "现在按一个键…（Esc 取消）", x, ref y, w);

            Head("行为", x, ref y, w);
            Toggle("转换完成后自动切到冰谱", ref c.AutoSwitch, x, ref y, w);
            Toggle("球失败时显示操作说明", ref c.ShowHintOnFail, x, ref y, w);
            TextRow("首次提示状态", ModMain.HintStateText(), x, ref y, w);
            BtnRow(new[] { "重新打开首次提示" }, x, ref y, w);
            Note("「知道了」只是关掉本次启动的提示，下次开游戏还会弹；想永久不再出现，要在提示里勾「不再提醒」。", x, ref y, w);

            Head("反馈 / 交流", x, ref y, w);
            BtnRow(new[] { "加 QQ 群" }, x, ref y, w);
            Note("遇到问题、想要新功能、想反馈冰音好不好听，都可以进群说。群号 807651876。", x, ref y, w);
            TextRow("作者 / 版本", "By wos111(机人)　v" + ModMain.Version, x, ref y, w);
        }

        // ===== 专业设置：调音高 / 多押 / 听感 / 同步 =====
        static void ProTab(float x, ref float y, float w)
        {
            var c = ModMain.cfg;

            Head("音高：怎么定冰音的高低", x, ref y, w, "重置音高");
            Note("规则：在小球到砖块的那一刻，测原曲那一小段的音高，拿「开头部分」当基准音。"
               + "比基准高就升调、低就降调 —— 这样冰音跟着旋律起伏走。", x, ref y, w);
            Toggle("按原曲音高定调（关掉 = 所有冰音同调）", ref c.PitchRelative, x, ref y, w);
            Slide("升降幅度", ref c.PitchCompress, 0f, 12f,
                  c.PitchCompress.ToString("0.#") + "（1 个八度差多少半音）", x, ref y, w);
            Note("12 = 精确音程，实测太跳（约 30% 的音符顶到上限）；"
               + "3 只保留方向、去掉幅度，约 77% 的相邻音符音高相同，成段保持更像音乐。", x, ref y, w);
            Slide("最多升降", ref c.PitchMaxSemitone, 1f, 12f, "±" + c.PitchMaxSemitone.ToString("0.#") + " 半音", x, ref y, w);
            Slide("基准音取样位置", ref c.OpeningPercent, 2f, 30f, "开头 " + c.OpeningPercent.ToString("0") + "%", x, ref y, w);
            Slide("基频下限", ref c.PitchLowHz, 40f, 400f, c.PitchLowHz.ToString("0") + " Hz", x, ref y, w);
            Slide("基频上限", ref c.PitchHighHz, 400f, 3000f, c.PitchHighHz.ToString("0") + " Hz", x, ref y, w);
            Note("基频范围太低会混进贝斯、太高会混进泛音，都不稳。", x, ref y, w);

            Head("多押：一块砖要按几下", x, ref y, w, "重置多押");
            Note("官方定义：「同时按两个键 / 同时击打两格相邻轨道」。而「双押砖块」是官方 RJ-X 关卡"
               + "独有的砖，**自制谱根本造不出来**。所以自制谱里的多押只有一种来源："
               + "相邻两块砖挨得极近，近到必须几乎同时点两下。", x, ref y, w);
            Note("★ 这里直接用「时间间隔」判，比用角度准：角度还得乘上 BPM 才是时间。"
               + "实测 186 个真实自制谱、35050 个砖间隔：<20ms 占 0.73%，<30ms 占 1.76%，"
               + "<40ms 占 2.78%，<60ms 占 5.25%。", x, ref y, w);
            Slide("多押间隔阈值", ref c.MultiTapGapMs, 5f, 120f, c.MultiTapGapMs.ToString("0") + "ms", x, ref y, w);
            Note("两块砖隔得比这个值还近，就算「几乎同时按两下」。BPM 中途变化会自动跟着走，不用分档。"
               + "想要最接近原声的重音就调到 20~25ms；想多抓一些就 40~60ms。", x, ref y, w);
            SlideInt("最多算到几押", ref c.MultiTapMaxCluster, 2, 4, c.MultiTapMaxCluster.ToString("0") + " 押", x, ref y, w);
            Toggle("结合音频验证（这一簇附近得有原曲重音才算）", ref c.MultiTapUseAudio, x, ref y, w);

            Head("多押加成（押得越多音越高）", x, ref y, w);
            Slide("双押", ref c.PressSemitone2, 0f, 8f, c.PressSemitone2.ToString("0.#") + " 半音", x, ref y, w);
            Slide("三押", ref c.PressSemitone3, 0f, 8f, c.PressSemitone3.ToString("0.#") + " 半音", x, ref y, w);
            Slide("四押及以上", ref c.PressSemitone4, 0f, 8f, c.PressSemitone4.ToString("0.#") + " 半音", x, ref y, w);

            Head("冰音听感", x, ref y, w, "重置听感");
            Note("冰音样本本身 0.28 秒长，但大部分是拖尾。音符密集时前后会重叠，听起来就是一片连续的"
               + "啪啦声、数不出点。裁短后上一个响完下一个才来，就能听出一个个点。设成 0 = 不裁。", x, ref y, w);
            Slide("冰音长度", ref c.IceLengthMs, 0f, 280f,
                  c.IceLengthMs <= 0 ? "不裁（280ms）" : c.IceLengthMs.ToString("0") + "ms", x, ref y, w);
            Slide("密集段落压制强度", ref c.DensityCompensation, 0f, 1f,
                  c.DensityCompensation.ToString("0.00") + "（一秒内音符越多，单个冰音越轻）", x, ref y, w);

            Head("时间轴与播放微调", x, ref y, w, "重置同步");
            Slide("对齐微调（转换时用）", ref c.ExtraOffsetMs, -3000f, 3000f,
                  (c.ExtraOffsetMs >= 0 ? "+" : "") + c.ExtraOffsetMs.ToString("0") + "ms", x, ref y, w);
            Note("范围 ±3 秒。听感偏早就往右调（冰音更晚），偏晚就往左调。**改完要重新转换**。", x, ref y, w);
            Slide("播放微调（立刻生效）", ref c.PlaybackOffsetMs, -500f, 500f,
                  (c.PlaybackOffsetMs >= 0 ? "+" : "") + c.PlaybackOffsetMs.ToString("0") + "ms", x, ref y, w);
            Note("冰音和游戏音乐是两份独立播放的，播放那一层可能整体差一点点。这里挪的是播放位置，"
               + "**改完立刻生效、不用重转**。正数 = 冰音更晚出来。左上角卡片会实时显示「偏差 XXXms」，"
               + "照着那个数往反方向调就行。", x, ref y, w);

            Head("转换", x, ref y, w, "重置转换");
            Toggle("没有音频时只按轨道转（备用方案）", ref c.ChartOnlyFallback, x, ref y, w);
            Slide("结尾额外保留", ref c.TailSeconds, 0f, 6f, c.TailSeconds.ToString("0.0") + " 秒", x, ref y, w);
            Slide("起始点检测灵敏度", ref c.OnsetSensitivity, 0.3f, 2.5f, c.OnsetSensitivity.ToString("0.00") + "×", x, ref y, w);
        }

        // ===== 其他设置：界面偏好 / 维护 / 诊断 / 重置 =====
        static void OtherTab(float x, ref float y, float w)
        {
            var c = ModMain.cfg;

            Head("界面", x, ref y, w, "重置界面");
            TextRow("语言", LangTips[Mathf.Clamp(c.LangIndex, 0, 2)] + "（右上角切换）", x, ref y, w);
            Toggle("设置页背景飘雪", ref ParticlesEnabled, x, ref y, w);
            Note("飘雪只是装饰，卡的话关掉能省一点性能。", x, ref y, w);

            Head("维护", x, ref y, w);
            BtnRow(new[] { "清除 %temp% 缓存", "打开产物目录" }, x, ref y, w);
            Note("转换产物放在 %temp%\\BingChart\\<关卡名>\\ 里，离开关卡时会自动删掉，"
               + "按一次「清除」可以连根目录一起清。", x, ref y, w);

            Head("诊断", x, ref y, w);
            Note(ModMain.DiagText(), x, ref y, w);
            Note("进关卡后 mod 的日志写在游戏目录上一级的 Player.log 里，"
               + "路径：%USERPROFILE%\\AppData\\LocalLow\\7th Beat Games\\A Dance of Fire and Ice\\Player.log", x, ref y, w);

            Head("重置", x, ref y, w);
            Note("点下面这个按钮，**所有设置**都会回到出厂默认（语言和「不再提醒」会保留）。", x, ref y, w);
            BtnRow(new[] { "全部恢复默认" }, x, ref y, w, Btn.Danger);
        }
    }
}
