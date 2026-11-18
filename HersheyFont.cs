using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Text;

namespace test01_1014
{
    using SubPath = VectorPathItem.SubPath;

    /// <summary>
    /// Hỗ trợ:
    /// 1. Font 1 nét (Hershey Single-Line / Stick Font) có dấu Tiếng Việt để khắc tốc độ cao.
    /// 2. Uốn chữ theo cung tròn (Text on Arc / Curve).
    /// 3. Hàn liền nét chữ / hình giao nhau (Boolean Union / Weld) thành đường cắt thống nhất.
    /// 4. Chuyển đổi các hình cơ bản / văn bản thành VectorPathItem để sửa từng điểm nút (Node Edit).
    /// </summary>
    public static class HersheyFont
    {
        // Chiều cao cơ sở của hệ tọa độ Hershey chuẩn (từ y = -12 đến y = +9 -> cao 21 đơn vị)
        private const float BaseCapHeight = 21f;

        private struct GlyphDef
        {
            public float Left;
            public float Right;
            public List<PointF[]> Strokes;
        }

        // Bảng mã Hershey Simplex (ASCII 32..126), giải mã từ chuỗi vector chuẩn Hershey
        private static readonly Dictionary<char, string> RawSimplex = new()
        {
            [' '] = "JZ",
            ['!'] = "MWRFRT RRYQZR[SZRY",
            ['"'] = "JZNFNM RVFVM",
            ['#'] = "H]SBLb RYBRb RLOZO RKUYU",
            ['$'] = "H\\PBP_ RTBT_ RYIWGTFPFMGKIKKLMMNOOUQWRXSYUYXWZT[P[MZKX",
            ['%'] = "F^[FI[ RNFPHPJOLMMKMIKIIJGLFNFPGSHVHYG[F RWTUUTWTYV[X[ZZ[X[VYTWT",
            ['&'] = "E_\\O\\N[MZMYNXPVUTXRZP[L[JZIYHWHUISJRQNRMSKSIRGPFNGMIMKNNPQUXWZY[[[\\Z\\Y",
            ['\''] = "MWRHQGRFSGSIRKQL",
            ['('] = "KYVBTDRGPKOPOTPYR]T`Vb",
            [')'] = "KYNBPDRGTKUPUTTYR]P`Nb",
            ['*'] = "JZRFRR RMIWO RWIMO",
            ['+'] = "E_RIR[ RIR[R",
            [','] = "MWSZR[QZRYSZS\\R^Q_",
            ['-'] = "E_IR[R",
            ['.'] = "MWRYQZR[SZRY",
            ['/'] = "G][BIb",
            ['0'] = "H\\QFNGLJKOKRLWNZQ[S[VZXWYRYOXJVGSFQF",
            ['1'] = "H\\NJPISFS[",
            ['2'] = "H\\LKLJMHNGPFTFVGWHXJXLWNUQK[Y[",
            ['3'] = "H\\MFXFRNUNWOXPYSYUXXVZS[P[MZLYKW",
            ['4'] = "H\\UFKTZT RUFU[",
            ['5'] = "H\\WFMFLOMNPMSMVNXPYSYUXXVZS[P[MZLYKW",
            ['6'] = "H\\XIWGTFRFOGMJLOLTMXOZR[S[VZXXYUYTXQVOSNRNOOMQLT",
            ['7'] = "H\\YFO[ RKFYF",
            ['8'] = "H\\PFMGLILKMMONSOVPXRYTYWXYWZT[P[MZLYKWKTLRNPQOUNWMXKXIWGTFPF",
            ['9'] = "H\\XMWPURRSQSNRLPKMKLLINGQFRFUGWIXMXRWWUZR[P[MZLX",
            [':'] = "MWRMQNROSNRM RRYQZR[SZRY",
            [';'] = "MWRMQNROSNRM RSZR[QZRYSZS\\R^Q_",
            ['<'] = "F^ZIJRZ[",
            ['='] = "E_IO[O RIU[U",
            ['>'] = "F^JIZRJ[",
            ['?'] = "I[LKLJMHNGPFTFVGWHXJXLWNVORQRT RRYQZR[SZRY",
            ['@'] = "E`WNVLTKQKOLNMMPMSNUPVSVUUVS RQKOMNPNSOUPV RWKVSVUXVZV\\T]Q]O\\L[JYHWGTFQFNGLHJJILHOHRIUJWLYNZQ[T[WZYYZX RXKWSWUXV",
            ['A'] = "I[RFJ[ RRFZ[ RMTWT",
            ['B'] = "G\\KFK[ RKFTFWGXHYJYLXNWOTP RKPTPWQXRYTYWXYWZT[K[",
            ['C'] = "H]ZKYIWGUFQFOGMILKKNKSLVMXOZQ[U[WZYXZV",
            ['D'] = "G\\KFK[ RKFRFUGWIXKYNYSXVWXUZR[K[",
            ['E'] = "H[LFL[ RLFYF RLPTP RL[Y[",
            ['F'] = "HZLFL[ RLFYF RLPTP",
            ['G'] = "H]ZKYIWGUFQFOGMILKKNKSLVMXOZQ[U[WZYXZVZS RUSZS",
            ['H'] = "G]KFK[ RYFY[ RKPYP",
            ['I'] = "NVRFR[",
            ['J'] = "JZVFVVUYTZR[P[NZMYLVLT",
            ['K'] = "G\\KFK[ RYFKT RPOY[",
            ['L'] = "HYLFL[ RL[X[",
            ['M'] = "F^JFJ[ RJFR[ RZFR[ RZFZ[",
            ['N'] = "G]KFK[ RKFY[ RYFY[",
            ['O'] = "G]PFNGLIKKJNJSKVLXNZP[T[VZXXYVZSZNYKXIVGTFPF",
            ['P'] = "G\\KFK[ RKFTFWGXHYJYMXOWPTQKQ",
            ['Q'] = "G]PFNGLIKKJNJSKVLXNZP[T[VZXXYVZSZNYKXIVGTFPF RSWY]",
            ['R'] = "G\\KFK[ RKFTFWGXHYJYLXNWOTPKP RRPY[",
            ['S'] = "H\\YIWGTFPFMGKIKKLMMNOOUQWRXSYUYXWZT[P[MZKX",
            ['T'] = "JZRFR[ RKFYF",
            ['U'] = "G]KFKULXNZQ[S[VZXXYUYF",
            ['V'] = "I[JFR[ RZFR[",
            ['W'] = "F^HFM[ RRFM[ RRFW[ R\\FW[",
            ['X'] = "H\\KFY[ RK[YF",
            ['Y'] = "I[JFRPR[ RZFRP",
            ['Z'] = "H\\YFK[ RKFYF RK[Y[",
            ['['] = "KYOBOb RPBPb ROBVB RObVb",
            ['\\'] = "KYKFY^",
            [']'] = "KYTBTb RUBUb RNBUB RNbUb",
            ['^'] = "JZPLRITL RMORJWO RRJR[",
            ['_'] = "JZJ]Z]",
            ['`'] = "MWSFRGQIQKRLSKRJ",
            ['a'] = "I\\XMX[ RXPVNTMQMONMPLSLUMXOZQ[T[VZXX",
            ['b'] = "H[LFL[ RLPNNPMSMUNWPXSXUWXUZS[P[NZLX",
            ['c'] = "I[XPVNTMQMONMPLSLUMXOZQ[T[VZXX",
            ['d'] = "I\\XFX[ RXPVNTMQMONMPLSLUMXOZQ[T[VZXX",
            ['e'] = "I[LSXSXQWOVNTMQMONMPLSLUMXOZQ[T[VZXX",
            ['f'] = "MYWFUFSGRJR[ ROMVM",
            ['g'] = "I\\XMX]W`VaTbQbOa RXPVNTMQMONMPLSLUMXOZQ[T[VZXX",
            ['h'] = "I\\MFM[ RMQPNRMUMWNXQX[",
            ['i'] = "NVQFRGSFREQF RRMR[",
            ['j'] = "MWRFSGTFSERF RSMS^RaPbNb",
            ['k'] = "IZMFM[ RWMMW RQSX[",
            ['l'] = "NVRFR[",
            ['m'] = "CaGMG[ RGQJNLMOMQNRQR[ RRQUNWMZM\\N]Q][",
            ['n'] = "I\\MMM[ RMQPNRMUMWNXQX[",
            ['o'] = "I\\QMONMPLSLUMXOZQ[T[VZXXYUYSXPVNTMQM",
            ['p'] = "H[LMLb RLPNNPMSMUNWPXSXUWXUZS[P[NZLX",
            ['q'] = "I\\XMXb RXPVNTMQMONMPLSLUMXOZQ[T[VZXX",
            ['r'] = "KXOMO[ ROSPPRNTMWM",
            ['s'] = "I[XPWNTMQMNNMPNRPSUTWUXWXXWZT[Q[NZMX",
            ['t'] = "MYRFRWSZU[W[ ROMVM",
            ['u'] = "I\\MMMWNZP[S[UZXW RXMX[",
            ['v'] = "JZLMR[ RXMR[",
            ['w'] = "G]JMN[ RRMN[ RRMV[ RZMV[",
            ['x'] = "I[MMX[ RMM[X",
            ['y'] = "JZLMR[ RXMR[P_NaLbKb",
            ['z'] = "I[XMM[ RMMXM RM[X[",
            ['{'] = "KYTBRCQDPFPHQJRKSMSOQQ RRCQEQGRISJTLTNSPORSTTVTXSZR[Q]Q_Ra RQSSUSWRYQZP\\P^Q`RaTb",
            ['|'] = "NVRBRb",
            ['}'] = "KYPBRCSDTFTHSJRKQMQOSQ RRCSESGRIQJPLPNQPURQTPVPXQZR[S]S_Ra RSSQUQWRYSZT\\T^S`RaPb",
            ['~'] = "F^IUISJPLONOPPTSVTXTZS[Q ISJQLPNPPQTTVUXUZT[Q[O"
        };

        private static readonly Dictionary<char, GlyphDef> GlyphCache = new();

        private static GlyphDef DecodeHershey(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length < 2)
                return new GlyphDef { Left = -5, Right = 5, Strokes = new List<PointF[]>() };

            float left = code[0] - 'R';
            float right = code[1] - 'R';
            var strokes = new List<PointF[]>();
            var cur = new List<PointF>();

            for (int i = 2; i + 1 < code.Length; i += 2)
            {
                char c1 = code[i];
                char c2 = code[i + 1];
                if (c1 == ' ' && c2 == 'R')
                {
                    if (cur.Count >= 2) strokes.Add(cur.ToArray());
                    cur.Clear();
                    continue;
                }
                float x = c1 - 'R';
                float y = c2 - 'R';
                cur.Add(new PointF(x, y));
            }
            if (cur.Count >= 2) strokes.Add(cur.ToArray());
            return new GlyphDef { Left = left, Right = right, Strokes = strokes };
        }

        // Phân tách ký tự Tiếng Việt sang ký tự gốc ASCII + dấu phụ + dấu thanh
        // accent: 0=none, 1=breve(ă), 2=circumflex(â,ê,ô), 3=horn(ơ,ư), 4=d-bar(đ)
        // tone: 0=none, 1=huyền(\), 2=sắc(/), 3=hỏi(?), 4=ngã(~), 5=nặng(.)
        private static void DecomposeVietnamese(char ch, out char baseChar, out int accent, out int tone)
        {
            accent = 0;
            tone = 0;
            baseChar = ch;

            if (ch == 'đ') { baseChar = 'd'; accent = 4; return; }
            if (ch == 'Đ') { baseChar = 'D'; accent = 4; return; }

            string norm = ch.ToString().Normalize(NormalizationForm.FormD);
            baseChar = norm[0];
            for (int i = 1; i < norm.Length; i++)
            {
                char m = norm[i];
                switch (m)
                {
                    case '\u0306': accent = 1; break; // ă
                    case '\u0302': accent = 2; break; // â, ê, ô
                    case '\u031B': accent = 3; break; // ơ, ư
                    case '\u0300': tone = 1; break;   // huyền
                    case '\u0301': tone = 2; break;   // sắc
                    case '\u0309': tone = 3; break;   // hỏi
                    case '\u0303': tone = 4; break;   // ngã
                    case '\u0323': tone = 5; break;   // nặng
                }
            }
        }

        private static GlyphDef GetGlyph(char ch)
        {
            lock (GlyphCache)
            {
                if (GlyphCache.TryGetValue(ch, out var cached)) return cached;

                DecomposeVietnamese(ch, out char baseCh, out int accent, out int tone);
                if (!RawSimplex.TryGetValue(baseCh, out string? raw))
                    raw = RawSimplex.TryGetValue(char.ToUpperInvariant(baseCh), out var up) ? up : RawSimplex['?'];

                GlyphDef g = DecodeHershey(raw);
                var strokes = new List<PointF[]>(g.Strokes.Select(s => (PointF[])s.Clone()));

                bool isUpper = char.IsUpper(baseCh);
                float topY = isUpper ? -14f : -7f;
                float cx = (g.Left + g.Right) * 0.5f;

                // Thêm dấu phụ Tiếng Việt (1 nét)
                if (accent == 4) // đ / Đ
                {
                    if (isUpper)
                        strokes.Add(new[] { new PointF(g.Left - 1f, -2f), new PointF(cx + 1f, -2f) });
                    else
                        strokes.Add(new[] { new PointF(cx - 2f, -8f), new PointF(g.Right + 1f, -8f) });
                }
                else if (accent == 1) // ă (dấu trăng)
                {
                    strokes.Add(new[]
                    {
                        new PointF(cx - 3.5f, topY - 4f),
                        new PointF(cx - 1.5f, topY - 2f),
                        new PointF(cx + 1.5f, topY - 2f),
                        new PointF(cx + 3.5f, topY - 4f)
                    });
                    topY -= 4.5f;
                }
                else if (accent == 2) // â, ê, ô (dấu mũ)
                {
                    strokes.Add(new[]
                    {
                        new PointF(cx - 3.5f, topY - 1.5f),
                        new PointF(cx, topY - 4.5f),
                        new PointF(cx + 3.5f, topY - 1.5f)
                    });
                    topY -= 5f;
                }
                else if (accent == 3) // ơ, ư (dấu móc)
                {
                    float hx = g.Right - 2f;
                    float hy = isUpper ? -11f : -4f;
                    strokes.Add(new[]
                    {
                        new PointF(hx, hy - 3f),
                        new PointF(hx + 2.5f, hy - 2f),
                        new PointF(hx + 2f, hy + 0.5f),
                        new PointF(hx, hy + 1f)
                    });
                }

                // Thêm dấu thanh Tiếng Việt (1 nét)
                switch (tone)
                {
                    case 1: // huyền
                        strokes.Add(new[] { new PointF(cx - 2.5f, topY - 4.5f), new PointF(cx + 1.5f, topY - 1.5f) });
                        break;
                    case 2: // sắc
                        strokes.Add(new[] { new PointF(cx - 1.5f, topY - 1.5f), new PointF(cx + 2.5f, topY - 4.5f) });
                        break;
                    case 3: // hỏi
                        strokes.Add(new[]
                        {
                            new PointF(cx - 1.5f, topY - 5f),
                            new PointF(cx + 1.5f, topY - 4.5f),
                            new PointF(cx + 1f, topY - 2.5f),
                            new PointF(cx - 0.5f, topY - 1.5f)
                        });
                        break;
                    case 4: // ngã
                        strokes.Add(new[]
                        {
                            new PointF(cx - 3f, topY - 2.5f),
                            new PointF(cx - 1f, topY - 4.5f),
                            new PointF(cx + 1f, topY - 2f),
                            new PointF(cx + 3f, topY - 4f)
                        });
                        break;
                    case 5: // nặng
                        strokes.Add(new[]
                        {
                            new PointF(cx - 0.6f, 13.5f),
                            new PointF(cx + 0.6f, 13.5f),
                            new PointF(cx + 0.6f, 14.5f),
                            new PointF(cx - 0.6f, 14.5f),
                            new PointF(cx - 0.6f, 13.5f)
                        });
                        break;
                }

                var res = new GlyphDef { Left = g.Left, Right = g.Right, Strokes = strokes };
                GlyphCache[ch] = res;
                return res;
            }
        }

        // =====================================================================
        //  Tạo danh sách SubPath cho VectorText (hỗ trợ Hershey 1 nét, Arc, Weld)
        // =====================================================================
        public static List<SubPath> BuildTextSubPaths(VectorText t, float mmPerPx = 0.25f, float curveTolPx = 0.25f)
        {
            if (string.IsNullOrEmpty(t.TextContent)) return new List<SubPath>();

            float emPx = t.FontSize * 96f / 72f;
            float charSpacePx = t.CharSpacingMm / Math.Max(0.01f, mmPerPx);
            bool hasArc = Math.Abs(t.ArcRadiusMm) >= 1f;
            float arcRadiusPx = t.ArcRadiusMm / Math.Max(0.01f, mmPerPx);

            List<SubPath> paths = t.SingleLineFont
                ? BuildHersheyPaths(t, emPx, charSpacePx, hasArc, arcRadiusPx)
                : BuildTrueTypePaths(t, emPx, charSpacePx, hasArc, arcRadiusPx, curveTolPx);

            // Nếu đang ở chế độ tùy chỉnh (1 nét, uốn cong, hàn chữ, giãn chữ), căn khung bao khớp (t.X, t.Y, t.Width, t.Height)
            if (paths.Count > 0 && t.HasCustomPath)
            {
                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                bool any = false;
                foreach (var sp in paths)
                {
                    foreach (var p in sp.Points)
                    {
                        if (p.X < minX) minX = p.X;
                        if (p.Y < minY) minY = p.Y;
                        if (p.X > maxX) maxX = p.X;
                        if (p.Y > maxY) maxY = p.Y;
                        any = true;
                    }
                }
                if (any)
                {
                    float dx = t.X - minX;
                    float dy = t.Y - minY;
                    if (Math.Abs(dx) > 0.01f || Math.Abs(dy) > 0.01f)
                    {
                        foreach (var sp in paths)
                        {
                            for (int i = 0; i < sp.Points.Count; i++)
                                sp.Points[i] = new PointF(sp.Points[i].X + dx, sp.Points[i].Y + dy);
                        }
                    }
                    t.Width = Math.Max(4f, maxX - minX);
                    t.Height = Math.Max(4f, maxY - minY);
                }
            }

            return paths;
        }

        private static List<SubPath> BuildHersheyPaths(VectorText t, float emPx, float charSpacePx, bool hasArc, float arcRadiusPx)
        {
            float scale = emPx / BaseCapHeight;
            float shear = t.FontItalic ? -0.22f : 0f;
            var lines = t.TextContent.Replace("\r\n", "\n").Split('\n');

            // Thu thập nét theo toạ độ cục bộ (gốc 0,0 tại góc trên-trái dòng đầu)
            var rawStrokes = new List<List<PointF>>();
            float maxLineW = 0f;
            float lineStep = emPx * 1.35f;

            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                float curX = 0f;
                float baseY = li * lineStep + 14f * scale; // đẩy đỉnh chữ xuống dưới Y=0

                foreach (char ch in line)
                {
                    GlyphDef g = GetGlyph(ch);
                    float adv = (g.Right - g.Left) * scale + charSpacePx;
                    float offsetX = curX - g.Left * scale;

                    foreach (var st in g.Strokes)
                    {
                        var pts = new List<PointF>(st.Length);
                        foreach (var p in st)
                        {
                            float lx = offsetX + (p.X + p.Y * shear) * scale;
                            float ly = baseY + p.Y * scale;
                            pts.Add(new PointF(lx, ly));
                        }
                        if (pts.Count >= 2) rawStrokes.Add(pts);

                        // Nếu in đậm (Bold) trong chế độ 1 nét: thêm nét song song dịch nhẹ để đậm nét
                        if (t.FontBold && pts.Count >= 2)
                        {
                            float boldShift = Math.Max(0.6f, scale * 0.65f);
                            var pts2 = pts.Select(p => new PointF(p.X + boldShift, p.Y)).ToList();
                            rawStrokes.Add(pts2);
                        }
                    }
                    curX += Math.Max(scale * 2f, adv);
                }
                if (curX > maxLineW) maxLineW = curX;
            }

            var result = new List<SubPath>();
            float baselineY = 14f * scale + 9f * scale; // chân chữ (y = +9 trong Hershey)

            foreach (var st in rawStrokes)
            {
                // Chia nhỏ đoạn thẳng nếu uốn cong theo cung tròn để đường cong mượt
                List<PointF> densified = hasArc ? SubdividePolyline(st, 2.5f) : st;
                var transformed = new List<PointF>(densified.Count);
                foreach (var p in densified)
                {
                    PointF wp = hasArc
                        ? MapPointToArc(p, maxLineW * 0.5f, baselineY, arcRadiusPx, t.X, t.Y)
                        : new PointF(t.X + p.X, t.Y + p.Y);
                    transformed.Add(wp);
                }
                result.Add(new SubPath { Points = transformed, IsClosed = false });
            }

            return result;
        }

        private static List<SubPath> BuildTrueTypePaths(VectorText t, float emPx, float charSpacePx, bool hasArc, float arcRadiusPx, float curveTolPx)
        {
            FontFamily fam;
            try { fam = new FontFamily(t.FontName); }
            catch { fam = new FontFamily("Arial"); }

            using (fam)
            {
                FontStyle want = (t.FontBold ? FontStyle.Bold : 0) | (t.FontItalic ? FontStyle.Italic : 0);
                FontStyle style = fam.IsStyleAvailable(want) ? want
                                : fam.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular
                                : fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold
                                : FontStyle.Italic;

                bool needPerChar = hasArc || Math.Abs(charSpacePx) > 0.05f || t.WeldText;

                if (!needPerChar)
                {
                    using var gp = new GraphicsPath();
                    gp.AddString(t.TextContent, fam, (int)style, emPx, new PointF(t.X, t.Y), StringFormat.GenericDefault);
                    return ExtractSubPaths(gp, curveTolPx);
                }

                // Xử lý từng ký tự để hỗ trợ giãn cách chữ, uốn cong theo cung, và hàn chữ (Weld)
                var charShapes = new List<List<SubPath>>();
                var lines = t.TextContent.Replace("\r\n", "\n").Split('\n');
                float lineStep = emPx * 1.25f;

                using var bmp = new Bitmap(1, 1);
                using var g = Graphics.FromImage(bmp);
                using var measureFont = new Font(fam, emPx, style, GraphicsUnit.Pixel);
                var sf = new StringFormat(StringFormat.GenericTypographic)
                {
                    FormatFlags = StringFormatFlags.MeasureTrailingSpaces
                };

                // Tính tổng chiều rộng dòng đầu để căn tâm cung tròn
                float totalW = 0f;
                if (lines.Length > 0)
                {
                    foreach (char c in lines[0])
                    {
                        float cw = c == ' ' ? emPx * 0.32f : g.MeasureString(c.ToString(), measureFont, 10000, sf).Width;
                        totalW += cw + charSpacePx;
                    }
                }

                float baselineY = emPx * 0.85f;

                for (int li = 0; li < lines.Length; li++)
                {
                    string line = lines[li];
                    float curX = 0f;
                    float curY = li * lineStep;

                    foreach (char c in line)
                    {
                        float cw = c == ' ' ? emPx * 0.32f : g.MeasureString(c.ToString(), measureFont, 10000, sf).Width;
                        if (!char.IsWhiteSpace(c))
                        {
                            using var gp = new GraphicsPath();
                            gp.AddString(c.ToString(), fam, (int)style, emPx, new PointF(0f, 0f), sf);
                            var sub = ExtractSubPaths(gp, curveTolPx);

                            if (hasArc)
                            {
                                // Xoay và đặt ký tự vuông góc với tiếp tuyến cung tròn
                                float charCenterX = curX + cw * 0.5f;
                                foreach (var sp in sub)
                                {
                                    for (int i = 0; i < sp.Points.Count; i++)
                                    {
                                        PointF lp = new PointF(curX + sp.Points[i].X, curY + sp.Points[i].Y);
                                        sp.Points[i] = MapPointToArc(lp, totalW * 0.5f, baselineY, arcRadiusPx, t.X, t.Y);
                                    }
                                }
                            }
                            else
                            {
                                foreach (var sp in sub)
                                {
                                    for (int i = 0; i < sp.Points.Count; i++)
                                    {
                                        sp.Points[i] = new PointF(t.X + curX + sp.Points[i].X, t.Y + curY + sp.Points[i].Y);
                                    }
                                }
                            }

                            if (sub.Count > 0) charShapes.Add(sub);
                        }
                        curX += cw + charSpacePx;
                    }
                }

                if (t.WeldText && charShapes.Count > 1)
                {
                    return WeldShapeGroups(charShapes);
                }

                return charShapes.SelectMany(s => s).ToList();
            }
        }

        // Ánh xạ 1 điểm (x, y) từ hệ tọa độ dòng chữ phẳng sang cung tròn bán kính R (px)
        // R > 0: uốn vòm xuống (tâm cung ở dưới chữ); R < 0: uốn võng lên (tâm cung ở trên chữ)
        private static PointF MapPointToArc(PointF p, float centerX, float baselineY, float arcRadiusPx, float originX, float originY)
        {
            float dx = p.X - centerX;
            float dy = baselineY - p.Y; // khoảng cách từ chân chữ hướng lên trên
            float absR = Math.Max(8f, Math.Abs(arcRadiusPx));

            if (arcRadiusPx > 0)
            {
                // Tâm cung nằm phía dưới chân chữ một đoạn absR
                float theta = dx / absR; // góc lệch so với trục đứng (radian)
                float r = Math.Max(1f, absR + dy);
                float wx = originX + centerX + r * MathF.Sin(theta);
                float wy = originY + baselineY + absR - r * MathF.Cos(theta);
                return new PointF(wx, wy);
            }
            else
            {
                // Tâm cung nằm phía trên chân chữ một đoạn absR
                float theta = dx / absR;
                float r = Math.Max(1f, absR - dy);
                float wx = originX + centerX + r * MathF.Sin(theta);
                float wy = originY + baselineY - absR + r * MathF.Cos(theta);
                return new PointF(wx, wy);
            }
        }

        private static List<PointF> SubdividePolyline(List<PointF> pts, float maxSegLen)
        {
            var res = new List<PointF>();
            if (pts.Count == 0) return res;
            res.Add(pts[0]);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                PointF a = pts[i], b = pts[i + 1];
                float dx = b.X - a.X, dy = b.Y - a.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                int steps = Math.Max(1, (int)MathF.Ceiling(dist / maxSegLen));
                for (int s = 1; s <= steps; s++)
                {
                    float t = (float)s / steps;
                    res.Add(new PointF(a.X + dx * t, a.Y + dy * t));
                }
            }
            return res;
        }

        public static List<SubPath> ExtractSubPaths(GraphicsPath gp, float flattenTol = 0.25f)
        {
            var res = new List<SubPath>();
            if (gp.PointCount < 2) return res;

            using var flatPath = (GraphicsPath)gp.Clone();
            using var m = new Matrix();
            flatPath.Flatten(m, Math.Max(0.02f, flattenTol));

            if (flatPath.PointCount < 2) return res;
            var pts = flatPath.PathPoints;
            using var iter = new GraphicsPathIterator(flatPath);
            while (iter.NextSubpath(out int start, out int end, out bool closed) > 0)
            {
                var list = new List<PointF>(end - start + 1);
                for (int i = start; i <= end; i++)
                {
                    if (list.Count == 0 || Distance(list[^1], pts[i]) > 0.02f)
                        list.Add(pts[i]);
                }
                if (list.Count >= 2 && closed && Distance(list[0], list[^1]) <= 0.05f)
                    list.RemoveAt(list.Count - 1);

                if (list.Count >= 2)
                    res.Add(new SubPath { Points = list, IsClosed = closed });
            }
            return res;
        }

        // =====================================================================
        //  HÀN LIỀN NÉT (BOOLEAN UNION / WELD) CÁC NHÓM ĐƯỜNG VIỀN KÍN
        // =====================================================================
        private struct Seg
        {
            public PointF A;
            public PointF B;
            public int GroupIdx;
        }

        /// <summary>
        /// Hợp nhất (Boolean Union) nhiều nhóm đường контур (mỗi nhóm là 1 chữ cái hoặc 1 đối tượng)
        /// bằng cách tìm giao điểm đoạn thẳng, loại bỏ các đoạn nằm bên trong nhóm khác, và nối lại thành đường cắt kín.
        /// </summary>
        public static List<SubPath> WeldShapeGroups(List<List<SubPath>> groups)
        {
            if (groups.Count == 0) return new List<SubPath>();
            if (groups.Count == 1) return groups[0];

            // Chuẩn hóa chiều đa giác trong từng nhóm: đường bao ngoài cùng chiều kim đồng hồ (diện tích > 0 trên màn hình), lỗ ngược chiều
            var cleanGroups = new List<List<List<PointF>>>();
            foreach (var grp in groups)
            {
                var loops = new List<List<PointF>>();
                foreach (var sp in grp)
                {
                    if (sp.Points.Count < 3) continue;
                    var pts = new List<PointF>(sp.Points);
                    if (Distance(pts[0], pts[^1]) < 0.05f) pts.RemoveAt(pts.Count - 1);
                    if (pts.Count >= 3) loops.Add(pts);
                }
                NormalizeGroupOrientation(loops);
                if (loops.Count > 0) cleanGroups.Add(loops);
            }

            if (cleanGroups.Count <= 1)
                return groups.SelectMany(g => g).ToList();

            // Tạo danh sách tất cả các cạnh có hướng
            var rawSegs = new List<Seg>();
            var groupBounds = new RectangleF[cleanGroups.Count];

            for (int gi = 0; gi < cleanGroups.Count; gi++)
            {
                float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                foreach (var loop in cleanGroups[gi])
                {
                    int n = loop.Count;
                    for (int i = 0; i < n; i++)
                    {
                        PointF a = loop[i];
                        PointF b = loop[(i + 1) % n];
                        if (Distance(a, b) < 0.01f) continue;
                        rawSegs.Add(new Seg { A = a, B = b, GroupIdx = gi });
                        if (a.X < minX) minX = a.X; if (a.Y < minY) minY = a.Y;
                        if (a.X > maxX) maxX = a.X; if (a.Y > maxY) maxY = a.Y;
                    }
                }
                groupBounds[gi] = new RectangleF(minX - 1f, minY - 1f, Math.Max(1f, maxX - minX + 2f), Math.Max(1f, maxY - minY + 2f));
            }

            // Tìm giao điểm giữa các cạnh thuộc các nhóm khác nhau để chia nhỏ cạnh
            int segCount = rawSegs.Count;
            var splitParams = new List<float>[segCount];
            for (int i = 0; i < segCount; i++) splitParams[i] = new List<float> { 0f, 1f };

            for (int i = 0; i < segCount; i++)
            {
                var s1 = rawSegs[i];
                for (int j = i + 1; j < segCount; j++)
                {
                    var s2 = rawSegs[j];
                    if (s1.GroupIdx == s2.GroupIdx) continue;
                    if (!SegBoundsOverlap(s1.A, s1.B, s2.A, s2.B)) continue;

                    if (TryIntersectSegments(s1.A, s1.B, s2.A, s2.B, out float t1, out float t2))
                    {
                        if (t1 > 0.001f && t1 < 0.999f) splitParams[i].Add(t1);
                        if (t2 > 0.001f && t2 < 0.999f) splitParams[j].Add(t2);
                    }
                }
            }

            // Giữ lại các đoạn con KHÔNG nằm bên trong bất kỳ nhóm nào khác
            var keptSegs = new List<(PointF A, PointF B)>();
            for (int i = 0; i < segCount; i++)
            {
                var s = rawSegs[i];
                var ts = splitParams[i];
                ts.Sort();

                for (int k = 0; k < ts.Count - 1; k++)
                {
                    float t0 = ts[k], t1 = ts[k + 1];
                    if (t1 - t0 < 0.001f) continue;

                    PointF p0 = Lerp(s.A, s.B, t0);
                    PointF p1 = Lerp(s.A, s.B, t1);
                    if (Distance(p0, p1) < 0.01f) continue;

                    PointF mid = new PointF((p0.X + p1.X) * 0.5f, (p0.Y + p1.Y) * 0.5f);
                    bool insideOther = false;
                    for (int gj = 0; gj < cleanGroups.Count; gj++)
                    {
                        if (gj == s.GroupIdx) continue;
                        if (!groupBounds[gj].Contains(mid)) continue;
                        if (IsPointInsideGroup(mid, cleanGroups[gj]))
                        {
                            insideOther = true;
                            break;
                        }
                    }

                    if (!insideOther)
                        keptSegs.Add((p0, p1));
                }
            }

            // Nối các đoạn thẳng còn lại thành các SubPath khép kín
            return StitchSegments(keptSegs);
        }

        private static void NormalizeGroupOrientation(List<List<PointF>> loops)
        {
            for (int i = 0; i < loops.Count; i++)
            {
                PointF testPt = loops[i][0];
                int depth = 0;
                for (int j = 0; j < loops.Count; j++)
                {
                    if (i == j) continue;
                    if (PointInPolygon(testPt, loops[j])) depth++;
                }
                bool isOuter = (depth % 2) == 0;
                double area = SignedArea(loops[i]);
                // Quy ước: đường ngoài có area > 0, đường lỗ có area < 0
                if ((isOuter && area < 0) || (!isOuter && area > 0))
                    loops[i].Reverse();
            }
        }

        private static double SignedArea(List<PointF> pts)
        {
            double sum = 0;
            for (int i = 0, n = pts.Count; i < n; i++)
            {
                PointF a = pts[i], b = pts[(i + 1) % n];
                sum += (double)a.X * b.Y - (double)b.X * a.Y;
            }
            return sum * 0.5;
        }

        private static bool IsPointInsideGroup(PointF p, List<List<PointF>> groupLoops)
        {
            int hits = 0;
            foreach (var loop in groupLoops)
            {
                if (PointInPolygon(p, loop)) hits++;
            }
            return (hits % 2) == 1;
        }

        private static bool PointInPolygon(PointF p, List<PointF> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                PointF pi = poly[i], pj = poly[j];
                if (((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                    (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y + 1e-12f) + pi.X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static bool SegBoundsOverlap(PointF a1, PointF b1, PointF a2, PointF b2)
        {
            float minX1 = Math.Min(a1.X, b1.X) - 0.05f, maxX1 = Math.Max(a1.X, b1.X) + 0.05f;
            float minY1 = Math.Min(a1.Y, b1.Y) - 0.05f, maxY1 = Math.Max(a1.Y, b1.Y) + 0.05f;
            float minX2 = Math.Min(a2.X, b2.X), maxX2 = Math.Max(a2.X, b2.X);
            float minY2 = Math.Min(a2.Y, b2.Y), maxY2 = Math.Max(a2.Y, b2.Y);
            return maxX1 >= minX2 && maxX2 >= minX1 && maxY1 >= minY2 && maxY2 >= minY1;
        }

        private static bool TryIntersectSegments(PointF p1, PointF p2, PointF p3, PointF p4, out float t, out float u)
        {
            t = u = 0f;
            float rX = p2.X - p1.X, rY = p2.Y - p1.Y;
            float sX = p4.X - p3.X, sY = p4.Y - p3.Y;
            float rxs = rX * sY - rY * sX;
            if (Math.Abs(rxs) < 1e-6f) return false;

            float qpX = p3.X - p1.X, qpY = p3.Y - p1.Y;
            t = (qpX * sY - qpY * sX) / rxs;
            u = (qpX * rY - qpY * rX) / rxs;
            return t >= -0.001f && t <= 1.001f && u >= -0.001f && u <= 1.001f;
        }

        private static List<SubPath> StitchSegments(List<(PointF A, PointF B)> segs)
        {
            var result = new List<SubPath>();
            int n = segs.Count;
            if (n == 0) return result;

            bool[] used = new bool[n];
            const float snapTol = 0.25f;

            for (int i = 0; i < n; i++)
            {
                if (used[i]) continue;
                used[i] = true;

                var pts = new List<PointF> { segs[i].A, segs[i].B };
                while (true)
                {
                    PointF tail = pts[^1];
                    if ( pts.Count >= 3 && Distance(tail, pts[0]) <= snapTol)
                    {
                        pts.RemoveAt(pts.Count - 1);
                        break;
                    }

                    int bestIdx = -1;
                    float bestDist = snapTol;
                    for (int j = 0; j < n; j++)
                    {
                        if (used[j]) continue;
                        float d = Distance(tail, segs[j].A);
                        if (d <= bestDist)
                        {
                            bestDist = d;
                            bestIdx = j;
                        }
                    }

                    if (bestIdx < 0)
                    {
                        // Thử nối ngược nếu cần
                        for (int j = 0; j < n; j++)
                        {
                            if (used[j]) continue;
                            float d = Distance(tail, segs[j].B);
                            if (d <= bestDist)
                            {
                                bestDist = d;
                                bestIdx = j;
                                used[bestIdx] = true;
                                pts.Add(segs[bestIdx].A);
                                goto NextStep;
                            }
                        }
                        break;
                    }

                    used[bestIdx] = true;
                    pts.Add(segs[bestIdx].B);
                NextStep:;
                }

                if (pts.Count >= 3)
                {
                    result.Add(new SubPath
                    {
                        Points = SimplifyCollinear(pts, 0.04f),
                        IsClosed = true
                    });
                }
            }

            return result;
        }

        private static List<PointF> SimplifyCollinear(List<PointF> pts, float tol)
        {
            if (pts.Count <= 4) return pts;
            var res = new List<PointF>(pts.Count);
            int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                PointF prev = res.Count > 0 ? res[^1] : pts[(i - 1 + n) % n];
                PointF cur = pts[i];
                PointF next = pts[(i + 1) % n];
                if (PointToSegmentDistance(cur, prev, next) > tol)
                    res.Add(cur);
            }
            return res.Count >= 3 ? res : pts;
        }

        public static float PointToSegmentDistance(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-8f) return Distance(p, a);
            float t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0f, 1f);
            float projX = a.X + t * dx, projY = a.Y + t * dy;
            return Distance(p, new PointF(projX, projY));
        }

        private static PointF Lerp(PointF a, PointF b, float t) =>
            new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        private static float Distance(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        // =====================================================================
        //  CHUYỂN ĐỔI HÌNH CƠ BẢN / CHỮ THÀNH VectorPathItem (ĐỂ SỬA ĐIỂM NÚT)
        // =====================================================================
        public static VectorPathItem? ConvertToPathItem(Vectoritem it, float mmPerPx = 0.25f)
        {
            var pathItem = new VectorPathItem
            {
                Name = it.Name + " (Đường dẫn)",
                LayerId = it.LayerId,
                LaserPower = it.LaserPower,
                LaserSpeed = it.LaserSpeed,
                Passes = it.Passes,
                FillMode = it.FillMode,
                FillIntervalMm = it.FillIntervalMm,
                FillAngle = it.FillAngle,
                FillCross = it.FillCross,
                FillBidir = it.FillBidir,
                Hidden = it.Hidden,
                Locked = it.Locked,
                GroupId = it.GroupId
            };

            List<SubPath> subPaths = ExtractItemSubPaths(it, mmPerPx);
            if (subPaths.Count == 0) return null;

            pathItem.SubPaths = subPaths;
            pathItem.RecalculateBounds();
            return pathItem;
        }

        public static List<SubPath> ExtractItemSubPaths(Vectoritem it, float mmPerPx = 0.25f)
        {
            var subPaths = new List<SubPath>();

            switch (it)
            {
                case VectorPathItem vpi:
                    foreach (var sp in vpi.SubPaths)
                        subPaths.Add(new SubPath { Points = new List<PointF>(sp.Points), IsClosed = sp.IsClosed });
                    break;

                case VectorRectangle r:
                    subPaths.Add(new SubPath
                    {
                        IsClosed = true,
                        Points = new List<PointF>
                        {
                            new(r.X, r.Y),
                            new(r.X + r.Width, r.Y),
                            new(r.X + r.Width, r.Y + r.Height),
                            new(r.X, r.Y + r.Height)
                        }
                    });
                    break;

                case VectorEllipse el:
                    {
                        int n = 48;
                        float cx = el.X + el.Width / 2f, cy = el.Y + el.Height / 2f;
                        float rx = el.Width / 2f, ry = el.Height / 2f;
                        var pts = new List<PointF>(n);
                        for (int i = 0; i < n; i++)
                        {
                            double a = 2 * Math.PI * i / n;
                            pts.Add(new PointF(cx + (float)(rx * Math.Cos(a)), cy + (float)(ry * Math.Sin(a))));
                        }
                        subPaths.Add(new SubPath { Points = pts, IsClosed = true });
                    }
                    break;

                case VectorPolygon poly:
                    subPaths.Add(new SubPath
                    {
                        Points = poly.GetPoints().ToList(),
                        IsClosed = true
                    });
                    break;

                case VectorLine ln:
                    subPaths.Add(new SubPath
                    {
                        Points = new List<PointF> { new(ln.X, ln.Y), new(ln.X2, ln.Y2) },
                        IsClosed = false
                    });
                    break;

                case VectorText vt:
                    subPaths = BuildTextSubPaths(vt, mmPerPx, 0.25f);
                    break;
            }

            // Áp dụng phép xoay / lật của đối tượng trực tiếp vào tọa độ các điểm nút
            if (it is not VectorLine && (it.Rotation != 0f || it.FlipX || it.FlipY))
            {
                float cx = it.X + it.Width / 2f, cy = it.Y + it.Height / 2f;
                double rad = it.Rotation * Math.PI / 180.0;
                float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);

                foreach (var sp in subPaths)
                {
                    for (int i = 0; i < sp.Points.Count; i++)
                    {
                        PointF p = sp.Points[i];
                        float dx = p.X - cx, dy = p.Y - cy;
                        if (it.FlipX) dx = -dx;
                        if (it.FlipY) dy = -dy;
                        float rx = cx + dx * cos - dy * sin;
                        float ry = cy + dx * sin + dy * cos;
                        sp.Points[i] = new PointF(rx, ry);
                    }
                }
            }

            return subPaths;
        }

        /// <summary>
        /// Hàn (Weld / Boolean Union) nhiều đối tượng đang chọn thành 1 VectorPathItem duy nhất.
        /// </summary>
        public static VectorPathItem? WeldItemsToPath(List<Vectoritem> items, float mmPerPx = 0.25f)
        {
            if (items == null || items.Count == 0) return null;

            var groups = new List<List<SubPath>>();
            var openPaths = new List<SubPath>();

            foreach (var it in items)
            {
                var sps = ExtractItemSubPaths(it, mmPerPx);
                var closed = sps.Where(s => s.IsClosed && s.Points.Count >= 3).ToList();
                var open = sps.Where(s => !s.IsClosed || s.Points.Count < 3).ToList();
                if (closed.Count > 0) groups.Add(closed);
                openPaths.AddRange(open);
            }

            var welded = WeldShapeGroups(groups);
            welded.AddRange(openPaths);
            if (welded.Count == 0) return null;

            var first = items[0];
            var res = new VectorPathItem
            {
                Name = "Đường hàn (Welded)",
                LayerId = first.LayerId,
                LaserPower = first.LaserPower,
                LaserSpeed = first.LaserSpeed,
                Passes = first.Passes,
                FillMode = first.FillMode,
                FillIntervalMm = first.FillIntervalMm,
                FillAngle = first.FillAngle,
                FillCross = first.FillCross,
                FillBidir = first.FillBidir,
                SubPaths = welded
            };
            res.RecalculateBounds();
            return res;
        }
    }
}
