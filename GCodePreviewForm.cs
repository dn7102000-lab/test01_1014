// =====================================================================
//  GCodePreviewForm.cs  -  Xem trước + MÔ PHỎNG + điều chỉnh G-code
//  Project: test01_1014 (.NET 8, WinForms)
//
//  Cách dùng (trong Form1):
//      using var f = new GCodePreviewForm(projectItems,
//                        new GCodeSettings { CanvasHeightPx = pictureBox1.Height });
//      f.ShowDialog(this);
//
//  Chức năng:
//   - Sinh G-code từ projectItems (dùng GCodeCompiler.cs)
//   - Chỉnh công suất / tốc độ / số lần chạy CỦA TỪNG ĐỐI TƯỢNG (ghi thẳng vào Vectoritem)
//   - Chỉnh chung: mm/pixel, S tối đa, bước quét ảnh, đen/trắng + ngưỡng, đảo ảnh
//   - Mô phỏng chạy: Play/Pause, tốc độ x1..x5000, kéo thanh tiến độ, đầu laser chạy,
//     dòng lệnh đang chạy được bôi chọn trong ô code
//   - Zoom (lăn chuột), kéo (giữ chuột), double-click = fit
//   - Lưu file .nc / .gcode
// =====================================================================
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace test01_1014
{
    // ------------------------------------------------------------------
    //  Đoạn thẳng đã parse từ G-code
    // ------------------------------------------------------------------
    public class Seg
    {
        public float X1, Y1, X2, Y2;
        public bool LaserOn;
        public int S;
        public int Feed;    // mm/phút
        public int Line;    // chỉ số dòng trong G-code (từ 0)
        public int Kind;    // 0 = thường / đường tô, 1 = đường VIỀN của "Tô kín + viền" (vẽ màu xanh)
    }

    public static class GCodeParser
    {
        static readonly Regex RxWord = new(@"([A-Z])\s*(-?\d*\.?\d+)", RegexOptions.Compiled);
        static readonly Regex RxParen = new(@"\(.*?\)", RegexOptions.Compiled);

        public static List<Seg> Parse(IEnumerable<string> lines, int rapidFeed)
        {
            var res = new List<Seg>();
            float x = 0, y = 0;
            int s = 0, feed = 1000, mode = 0, ln = -1, tag = 0;
            bool abs = true, laser = false;

            foreach (var raw in lines)
            {
                ln++;
                string line = raw;
                string tr = raw.TrimStart();
                if (tr.StartsWith("; outline")) tag = 1;
                else if (tr.StartsWith("; fill") || tr.StartsWith("; [")) tag = 0;
                int sc = line.IndexOf(';');
                if (sc >= 0) line = line.Substring(0, sc);
                line = RxParen.Replace(line.ToUpperInvariant(), "").Trim();
                if (line.Length == 0) continue;

                float nx = x, ny = y;
                bool moved = false;
                foreach (Match m in RxWord.Matches(line))
                {
                    char c = m.Groups[1].Value[0];
                    float v = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    switch (c)
                    {
                        case 'G':
                            if (v == 0) mode = 0;
                            else if (v == 1) mode = 1;
                            else if (v == 90) abs = true;
                            else if (v == 91) abs = false;
                            break;
                        case 'M':
                            if (v == 3 || v == 4) laser = true;
                            else if (v == 5) laser = false;
                            break;
                        case 'S': s = (int)v; break;
                        case 'F': feed = Math.Max(1, (int)v); break;
                        case 'X': nx = abs ? v : x + v; moved = true; break;
                        case 'Y': ny = abs ? v : y + v; moved = true; break;
                    }
                }
                if (moved)
                {
                    res.Add(new Seg
                    {
                        X1 = x, Y1 = y, X2 = nx, Y2 = ny,
                        LaserOn = mode == 1 && laser && s > 0,
                        S = s,
                        Feed = mode == 0 ? Math.Max(1, rapidFeed) : feed,
                        Line = ln,
                        Kind = tag
                    });
                    x = nx; y = ny;
                }
            }
            return res;
        }
    }

    // ------------------------------------------------------------------
    //  Canvas vẽ G-code: 2 lớp bitmap (kế hoạch + đã khắc) để mô phỏng mượt
    // ------------------------------------------------------------------
    public class GCodeCanvas : Control
    {
        List<Seg> segs = new();
        float scale = 5f;
        PointF offset = new(20, 20);
        Point last;
        bool drag;
        int maxS = 1;

        Bitmap? planBmp, burnBmp;
        bool dirty = true;
        int drawnUpTo = 0;
        int progIdx = 0;
        float progFrac = 0f;
        bool showHead = false;

        bool showRapid = true, paper = true;
        float beam = 0.1f;

        public bool ShowRapid { get => showRapid; set { showRapid = value; dirty = true; Invalidate(); } }
        public bool PaperMode { get => paper; set { paper = value; dirty = true; Invalidate(); } }
        public float BeamWidth { get => beam; set { beam = value; dirty = true; Invalidate(); } }

        public GCodeCanvas()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.Black;
        }

        public void Load(List<Seg> s, bool fit)
        {
            segs = s ?? new List<Seg>();
            maxS = Math.Max(1, segs.Count == 0 ? 1 : segs.Max(g => g.S));
            progIdx = segs.Count;       // mặc định: hiện kết quả hoàn tất
            progFrac = 0;
            showHead = false;
            dirty = true;
            if (fit) FitView(); else Invalidate();
        }

        public void FitView()
        {
            if (segs.Count == 0 || Width < 50 || Height < 50) { Invalidate(); return; }
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var g in segs)
            {
                minX = Math.Min(minX, Math.Min(g.X1, g.X2)); maxX = Math.Max(maxX, Math.Max(g.X1, g.X2));
                minY = Math.Min(minY, Math.Min(g.Y1, g.Y2)); maxY = Math.Max(maxY, Math.Max(g.Y1, g.Y2));
            }
            float w = Math.Max(0.001f, maxX - minX), h = Math.Max(0.001f, maxY - minY);
            scale = Math.Min((Width - 40) / w, (Height - 40) / h);
            offset = new PointF(20 - minX * scale, 20 - minY * scale);
            dirty = true;
            Invalidate();
        }

        PointF ToScreen(float x, float y)
        {
            return new PointF(offset.X + x * scale, Height - (offset.Y + y * scale));
        }

        void SetupG(Graphics g)
        {
            g.SmoothingMode = segs.Count > 20000 ? SmoothingMode.HighSpeed : SmoothingMode.AntiAlias;
        }

        Pen NewBurnPen()
        {
            return new Pen(Color.Black, Math.Max(1f, beam * scale))
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
        }

        void DrawBurn(Graphics g, Pen pen, Seg s)
        {
            if (!s.LaserOn) return;
            int i = Math.Min(255, s.S * 255 / maxS);
            if (paper)
            {
                int v = 235 - 235 * i / 255;           // công suất càng cao càng đen
                pen.Color = Color.FromArgb(v, v, v);
            }
            else
            {
                int c = 60 + 195 * i / 255;
                pen.Color = Color.FromArgb(c, c / 4, 0);
            }
            if (s.Kind == 1)                         // đường viền của "Tô kín + viền": màu riêng để phân biệt với đường tô
                pen.Color = paper ? Color.FromArgb(30, 70, 200) : Color.FromArgb(0, 200, 255);
            g.DrawLine(pen, ToScreen(s.X1, s.Y1), ToScreen(s.X2, s.Y2));
        }

        /// <summary>idx = số đoạn đã chạy xong, frac = phần trăm đoạn đang chạy (0..1).</summary>
        public void SetProgress(int idx, float frac, bool head)
        {
            idx = Math.Clamp(idx, 0, segs.Count);
            showHead = head;
            progFrac = frac;

            if (dirty || idx < drawnUpTo)
            {
                progIdx = idx;
                dirty = true;                       // sẽ dựng lại ở lần vẽ tới
            }
            else if (idx > drawnUpTo && burnBmp != null)
            {
                using (var g = Graphics.FromImage(burnBmp))
                using (var pen = NewBurnPen())
                {
                    SetupG(g);
                    for (int i = drawnUpTo; i < idx; i++) DrawBurn(g, pen, segs[i]);
                }
                drawnUpTo = idx;
                progIdx = idx;
            }
            else
            {
                progIdx = idx;
            }
            Invalidate();
        }

        void EnsureLayers()
        {
            if (Width < 2 || Height < 2) return;
            if (planBmp == null || planBmp.Width != Width || planBmp.Height != Height)
            {
                planBmp?.Dispose();
                burnBmp?.Dispose();
                planBmp = new Bitmap(Width, Height);
                burnBmp = new Bitmap(Width, Height);
                dirty = true;
            }
            if (!dirty) return;

            // Lớp kế hoạch: nền + trục + đường dự kiến
            using (var g = Graphics.FromImage(planBmp))
            {
                g.Clear(paper ? Color.FromArgb(245, 240, 230) : Color.Black);
                SetupG(g);

                var o = ToScreen(0, 0);
                using (var ax = new Pen(paper ? Color.FromArgb(170, 170, 220) : Color.FromArgb(60, 60, 140)))
                {
                    g.DrawLine(ax, o.X, 0, o.X, Height);
                    g.DrawLine(ax, 0, o.Y, Width, o.Y);
                }

                bool drawPlanned = progIdx < segs.Count;    // đang mô phỏng -> hiện đường dự kiến mờ
                using (var rapid = new Pen(paper ? Color.FromArgb(190, 190, 210) : Color.FromArgb(70, 70, 70)) { DashStyle = DashStyle.Dot })
                using (var plan = new Pen(paper ? Color.FromArgb(220, 205, 205) : Color.FromArgb(80, 35, 35), 1f))
                {
                    foreach (var s in segs)
                    {
                        if (s.LaserOn)
                        {
                            if (drawPlanned) g.DrawLine(plan, ToScreen(s.X1, s.Y1), ToScreen(s.X2, s.Y2));
                        }
                        else if (showRapid)
                        {
                            g.DrawLine(rapid, ToScreen(s.X1, s.Y1), ToScreen(s.X2, s.Y2));
                        }
                    }
                }
            }

            // Lớp đã khắc: vẽ các đoạn 0..progIdx-1
            using (var g = Graphics.FromImage(burnBmp!))
            using (var pen = NewBurnPen())
            {
                g.Clear(Color.Transparent);
                SetupG(g);
                for (int i = 0; i < progIdx && i < segs.Count; i++) DrawBurn(g, pen, segs[i]);
            }
            drawnUpTo = progIdx;
            dirty = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            EnsureLayers();
            if (planBmp == null || burnBmp == null) return;
            var g = e.Graphics;
            g.DrawImageUnscaled(planBmp, 0, 0);
            g.DrawImageUnscaled(burnBmp, 0, 0);

            if (showHead && segs.Count > 0)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float hx, hy;
                if (progIdx < segs.Count)
                {
                    var cur = segs[progIdx];
                    hx = cur.X1 + (cur.X2 - cur.X1) * progFrac;
                    hy = cur.Y1 + (cur.Y2 - cur.Y1) * progFrac;
                    if (cur.LaserOn && progFrac > 0)
                    {
                        var part = new Seg { X1 = cur.X1, Y1 = cur.Y1, X2 = hx, Y2 = hy, LaserOn = true, S = cur.S };
                        using (var pen = NewBurnPen()) DrawBurn(g, pen, part);
                    }
                }
                else
                {
                    var l = segs[segs.Count - 1];
                    hx = l.X2; hy = l.Y2;
                }
                var p = ToScreen(hx, hy);
                using (var pen = new Pen(Color.Red, 2f))
                {
                    g.DrawEllipse(pen, p.X - 6, p.Y - 6, 12, 12);
                    g.DrawLine(pen, p.X - 11, p.Y, p.X + 11, p.Y);
                    g.DrawLine(pen, p.X, p.Y - 11, p.X, p.Y + 11);
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Focus(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float k = e.Delta > 0 ? 1.15f : 1 / 1.15f;
            float ky = Height - e.Y;
            offset.X = e.X - (e.X - offset.X) * k;
            offset.Y = ky - (ky - offset.Y) * k;
            scale *= k;
            dirty = true;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e) { drag = true; last = e.Location; }
        protected override void OnMouseUp(MouseEventArgs e) { drag = false; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!drag) return;
            offset.X += e.X - last.X;
            offset.Y -= e.Y - last.Y;
            last = e.Location;
            dirty = true;
            Invalidate();
        }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { base.OnMouseDoubleClick(e); FitView(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { planBmp?.Dispose(); burnBmp?.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // ------------------------------------------------------------------
    //  Form xem trước / mô phỏng / điều chỉnh
    // ------------------------------------------------------------------
    public class GCodePreviewForm : Form
    {
        static readonly double[] Mults = { 1, 5, 10, 50, 200, 1000, 5000 };

        readonly List<Vectoritem> _items;
        readonly GCodeSettings _cfg;
        readonly System.Windows.Forms.Timer _simTimer;
        readonly System.Windows.Forms.Timer _debounce;
        readonly Stopwatch _sw = new();

        string _code = "";
        List<Seg> _segs = new();
        double[] _cum = Array.Empty<double>();
        double _total, _simTime;
        bool _playing, _suppress, _pendingFit;
        int _lastLine = -1, _shownLines;

        // controls
        ListBox lstItems = null!;
        NumericUpDown nPower = null!, nSpeed = null!, nPass = null!;
        NumericUpDown nMm = null!, nMaxS = null!, nStep = null!, nThr = null!, nRapid = null!;
        CheckBox chkBin = null!, chkInv = null!, chkOptimize = null!, chkInsideOut = null!, chkPaper = null!, chkRapid = null!, chkFollow = null!;
        TextBox txtCode = null!;
        GCodeCanvas canvas = null!;
        Button btnPlay = null!, btnStop = null!;
        ComboBox cbSpeed = null!;
        TrackBar tbProg = null!;
        Label lblInfo = null!, lblTime = null!;

        public GCodePreviewForm(List<Vectoritem> items, GCodeSettings? settings = null)
        {
            _items = items;
            _cfg = settings ?? new GCodeSettings();

            // Tự co giãn theo DPI màn hình: vị trí / kích thước viết bằng px ở 96 DPI sẽ được nhân theo tỉ lệ thật,
            // nên chữ (đã phóng theo hệ thống) không còn bị cắt.
            AutoScaleDimensions = new System.Drawing.SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;

            Text = "G-code - Xem trước, mô phỏng & điều chỉnh";
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);       // chữ đậm cho dễ nhìn (ô code vẫn dùng Consolas)
            Width = 1300; Height = 800;      // AutoScale nhân thêm theo DPI
            StartPosition = FormStartPosition.CenterParent;

            _simTimer = new System.Windows.Forms.Timer { Interval = 33 };
            _simTimer.Tick += SimTick;
            _debounce = new System.Windows.Forms.Timer { Interval = 350 };
            _debounce.Tick += (s, e) => { Regenerate(_pendingFit); _pendingFit = false; };

            BuildUi();

            Shown += (s, e) =>
            {
                if (lstItems.Items.Count > 0) lstItems.SelectedIndex = 0;
                Regenerate(true);
            };
            FormClosed += (s, e) => { _simTimer.Stop(); _debounce.Stop(); };
        }

        // ================= UI =================
        void BuildUi()
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 340 };
            var left = new Panel { Dock = DockStyle.Left, Width = 322, Padding = new Padding(4), AutoScroll = true };
            Controls.Add(split);
            Controls.Add(left);

            // --- ô code (trái của split) ---
            txtCode = new TextBox
            {
                Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, ReadOnly = true,
                HideSelection = false, Dock = DockStyle.Fill, Font = new Font("Consolas", 9f)
            };
            split.Panel1.Controls.Add(txtCode);

            // --- canvas + thanh mô phỏng (phải của split) ---
            canvas = new GCodeCanvas { Dock = DockStyle.Fill };
            split.Panel2.Controls.Add(canvas);

            var bar = new Panel { Dock = DockStyle.Bottom, Height = 82 };
            split.Panel2.Controls.Add(bar);

            btnPlay = new Button { Left = 8, Top = 6, Width = 150, Height = 30, Text = "▶ Chạy mô phỏng" };
            btnPlay.Click += (s, e) => TogglePlay();
            btnStop = new Button { Left = 164, Top = 6, Width = 130, Height = 30, Text = "⏹ Hiện kết quả" };
            btnStop.Click += (s, e) => ShowFinal();
            var lblSp = new Label { Left = 306, Top = 12, Width = 70, Text = "Tốc độ sim:" };
            cbSpeed = new ComboBox { Left = 378, Top = 8, Width = 70, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var m in Mults) cbSpeed.Items.Add("x" + m.ToString(CultureInfo.InvariantCulture));
            cbSpeed.SelectedIndex = 3;
            lblTime = new Label { Left = 462, Top = 12, Width = 200, Text = "00:00 / 00:00" };
            tbProg = new TrackBar
            {
                Left = 8, Top = 40, Width = bar.Width - 20, Minimum = 0, Maximum = 1000,
                TickStyle = TickStyle.None, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            tbProg.Scroll += (s, e) =>
            {
                _simTime = tbProg.Value / 1000.0 * _total;
                UpdateSim(true);
            };
            bar.Controls.AddRange(new Control[] { btnPlay, btnStop, lblSp, cbSpeed, lblTime, tbProg });

            // --- panel trái: thông số ---
            int y = 4;
            left.Controls.Add(new Label { Left = 8, Top = y, Width = 290, Text = "ĐỐI TƯỢNG (chọn để chỉnh riêng):", Font = new Font(Font, FontStyle.Bold) });
            y += 22;
            lstItems = new ListBox { Left = 8, Top = y, Width = 290, Height = 110 };
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                string extra = it is VectorText t ? " - " + t.TextContent : "";
                lstItems.Items.Add((i + 1) + ". " + it.Name + extra);
            }
            lstItems.SelectedIndexChanged += (s, e) => LoadItemParams();
            left.Controls.Add(lstItems);
            y += 116;

            nPower = AddNum(left, "Công suất (%)", ref y, 80, 0, 100, 0, 5);
            nSpeed = AddNum(left, "Tốc độ F (mm/phút)", ref y, 1000, 1, 30000, 0, 100);
            nPass = AddNum(left, "Số lần chạy", ref y, 1, 1, 50, 0, 1);
            nPower.ValueChanged += (s, e) => ItemParamChanged();
            nSpeed.ValueChanged += (s, e) => ItemParamChanged();
            nPass.ValueChanged += (s, e) => ItemParamChanged();

            var btnAll = new Button { Left = 8, Top = y, Width = 290, Height = 28, Text = "Áp dụng 3 thông số cho TẤT CẢ" };
            btnAll.Click += (s, e) => ApplyToAll();
            left.Controls.Add(btnAll);
            y += 40;

            left.Controls.Add(new Label { Left = 8, Top = y, Width = 290, Text = "THÔNG SỐ CHUNG:", Font = new Font(Font, FontStyle.Bold) });
            y += 22;
            nMm = AddNum(left, "mm trên mỗi pixel canvas", ref y, (decimal)_cfg.MmPerPixel, 0.01m, 10m, 3, 0.01m);
            nMm.Enabled = false;
            nMaxS = AddNum(left, "S tối đa của firmware", ref y, _cfg.MaxS, 1, 100000, 0, 100);
            nStep = AddNum(left, "Bước dòng quét ảnh (mm)", ref y, (decimal)_cfg.RasterStepMm, 0.02m, 2m, 2, 0.02m);
            nThr = AddNum(left, "Ngưỡng đen/trắng (0-255)", ref y, _cfg.ImageThreshold, 0, 255, 0, 5);
            nRapid = AddNum(left, "Tốc độ G0 để ước tính (mm/phút)", ref y, _cfg.RapidFeedForEstimate, 100, 60000, 0, 500);

            nMm.ValueChanged += (s, e) => ScheduleRegen(true);
            nMaxS.ValueChanged += (s, e) => ScheduleRegen(false);
            nStep.ValueChanged += (s, e) => ScheduleRegen(false);
            nThr.ValueChanged += (s, e) => ScheduleRegen(false);
            nRapid.ValueChanged += (s, e) => ScheduleRegen(false);

            chkBin = AddChk(left, "Ảnh đen/trắng (1-bit)", ref y, _cfg.ImageBinary);
            chkInv = AddChk(left, "Đảo ảnh (khắc phần sáng)", ref y, _cfg.ImageInvert);
            chkOptimize = AddChk(left, "Tối ưu đường chạy G0 (TSP / Nearest)", ref y, _cfg.OptimizeToolpath);
            chkInsideOut = AddChk(left, "Cắt lỗ trong trước, viền ngoài sau", ref y, _cfg.CutInsideOut);
            chkBin.CheckedChanged += (s, e) => ScheduleRegen(false);
            chkInv.CheckedChanged += (s, e) => ScheduleRegen(false);
            chkOptimize.CheckedChanged += (s, e) => ScheduleRegen(false);
            chkInsideOut.CheckedChanged += (s, e) => ScheduleRegen(false);

            chkPaper = AddChk(left, "Xem kiểu giấy (đen = vết khắc)", ref y, true);
            chkRapid = AddChk(left, "Hiện đường di chuyển nhanh (G0)", ref y, true);
            chkFollow = AddChk(left, "Bôi chọn dòng lệnh đang chạy", ref y, true);
            chkPaper.CheckedChanged += (s, e) => canvas.PaperMode = chkPaper.Checked;
            chkRapid.CheckedChanged += (s, e) => canvas.ShowRapid = chkRapid.Checked;
            y += 6;

            var btnFit = new Button { Left = 8, Top = y, Width = 140, Height = 30, Text = "Fit view" };
            btnFit.Click += (s, e) => canvas.FitView();
            var btnSave = new Button { Left = 156, Top = y, Width = 142, Height = 30, Text = "Lưu G-code..." };
            btnSave.Click += (s, e) => SaveCode();
            left.Controls.Add(btnFit);
            left.Controls.Add(btnSave);
            y += 40;

            lblInfo = new Label { Left = 8, Top = y, Width = 290, Height = 150, Text = "..." };
            left.Controls.Add(lblInfo);
        }

        static NumericUpDown AddNum(Control p, string label, ref int y, decimal val, decimal min, decimal max, int dec, decimal inc)
        {
            p.Controls.Add(new Label { Left = 8, Top = y, Width = 290, Text = label });
            y += 20;
            var n = new NumericUpDown
            {
                Left = 8, Top = y, Width = 290, DecimalPlaces = dec,
                Minimum = min, Maximum = max, Increment = inc
            };
            n.Value = Math.Min(max, Math.Max(min, val));
            p.Controls.Add(n);
            y += 30;
            return n;
        }

        static CheckBox AddChk(Control p, string text, ref int y, bool chk)
        {
            var c = new CheckBox { Left = 8, Top = y, Width = 290, Text = text, Checked = chk };
            p.Controls.Add(c);
            y += 24;
            return c;
        }

        // ================= Thông số từng đối tượng =================
        void LoadItemParams()
        {
            int i = lstItems.SelectedIndex;
            if (i < 0 || i >= _items.Count) return;
            var it = _items[i];
            _suppress = true;
            nPower.Value = Math.Min(nPower.Maximum, Math.Max(nPower.Minimum, it.LaserPower));
            nSpeed.Value = Math.Min(nSpeed.Maximum, Math.Max(nSpeed.Minimum, it.LaserSpeed));
            nPass.Value = Math.Min(nPass.Maximum, Math.Max(nPass.Minimum, it.Passes));
            _suppress = false;
        }

        void ItemParamChanged()
        {
            if (_suppress) return;
            int i = lstItems.SelectedIndex;
            if (i < 0 || i >= _items.Count) return;
            var it = _items[i];
            it.LaserPower = (int)nPower.Value;
            it.LaserSpeed = (int)nSpeed.Value;
            it.Passes = (int)nPass.Value;
            ScheduleRegen(false);
        }

        void ApplyToAll()
        {
            foreach (var it in _items)
            {
                it.LaserPower = (int)nPower.Value;
                it.LaserSpeed = (int)nSpeed.Value;
                it.Passes = (int)nPass.Value;
            }
            ScheduleRegen(false);
        }

        // ================= Sinh G-code =================
        void ScheduleRegen(bool fit)
        {
            if (_suppress) return;
            _pendingFit |= fit;
            _debounce.Stop();
            _debounce.Start();
        }

        void ApplyCfg()
        {
            _cfg.MmPerPixel = (float)nMm.Value;
            _cfg.MaxS = (int)nMaxS.Value;
            _cfg.RasterStepMm = (double)nStep.Value;
            _cfg.ImageBinary = chkBin.Checked;
            _cfg.ImageThreshold = (int)nThr.Value;
            _cfg.ImageInvert = chkInv.Checked;
            _cfg.OptimizeToolpath = chkOptimize.Checked;
            _cfg.CutInsideOut = chkInsideOut.Checked;
            _cfg.RapidFeedForEstimate = (int)nRapid.Value;
        }

        void Regenerate(bool fit)
        {
            _debounce.Stop();
            _simTimer.Stop();
            _playing = false;
            btnPlay.Text = "▶ Chạy mô phỏng";

            try
            {
                Cursor = Cursors.WaitCursor;
                ApplyCfg();
                GCodeResult res = new GCodeCompiler(_cfg).Compile(_items);
                _code = res.Code;
                ShowCode(_code);

                _segs = GCodeParser.Parse(_code.Split('\n'), _cfg.RapidFeedForEstimate);
                bool hasRaster = _items.Any(it => it is VectorImage);
                canvas.BeamWidth = hasRaster ? (float)_cfg.RasterStepMm : 0.1f;
                canvas.Load(_segs, fit);
                BuildTimeline();

                lblInfo.Text = string.Format(CultureInfo.InvariantCulture,
                    "Dòng lệnh: {0}\nĐoạn: {1}\nKích thước: {2:0.#} x {3:0.#} mm\nQuãng đường cắt (G1): {4:0.#} mm\nQuãng đường chạy không (G0): {5:0.#} mm\nƯớc tính: {6:0.##} phút",
                    res.LineCount, _segs.Count, res.WidthMm, res.HeightMm, res.CutLengthMm, res.RapidLengthMm, res.EstimatedMinutes);
            }
            catch (Exception ex)
            {
                lblInfo.Text = "Lỗi: " + ex.Message;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        void ShowCode(string code)
        {
            const int maxShown = 30000;
            var lines = code.Split('\n');
            _lastLine = -1;
            if (lines.Length > maxShown)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < maxShown; i++) sb.Append(lines[i].TrimEnd('\r')).Append("\r\n");
                sb.Append("; ... (rút gọn khi hiển thị, tổng ").Append(lines.Length)
                  .Append(" dòng. File lưu vẫn đầy đủ)");
                txtCode.Text = sb.ToString();
                _shownLines = maxShown;
            }
            else
            {
                txtCode.Text = code;
                _shownLines = lines.Length;
            }
        }

        // ================= Mô phỏng =================
        void BuildTimeline()
        {
            int n = _segs.Count;
            _cum = new double[n];
            double t = 0;
            for (int i = 0; i < n; i++)
            {
                var s = _segs[i];
                double dx = s.X2 - s.X1, dy = s.Y2 - s.Y1;
                double len = Math.Sqrt(dx * dx + dy * dy);
                t += len / (Math.Max(1, s.Feed) / 60.0);    // giây
                _cum[i] = t;
            }
            _total = t;
            _simTime = _total;                              // trạng thái ban đầu: hoàn tất
            UpdateTimeLabels();
            tbProg.Value = tbProg.Maximum;
        }

        int FindIdx(double t)
        {
            int lo = 0, hi = _cum.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_cum[mid] <= t) lo = mid + 1; else hi = mid;
            }
            return lo;   // đoạn đầu tiên chưa chạy xong
        }

        void TogglePlay()
        {
            if (_segs.Count == 0) return;
            if (_playing)
            {
                _playing = false;
                _simTimer.Stop();
                btnPlay.Text = "▶ Chạy tiếp";
                return;
            }
            if (_simTime >= _total) _simTime = 0;           // chạy lại từ đầu
            _playing = true;
            btnPlay.Text = "⏸ Tạm dừng";
            _sw.Restart();
            _simTimer.Start();
            UpdateSim(true);
        }

        void ShowFinal()
        {
            _playing = false;
            _simTimer.Stop();
            btnPlay.Text = "▶ Chạy mô phỏng";
            _simTime = _total;
            UpdateSim(false);
        }

        void SimTick(object? sender, EventArgs e)
        {
            double dt = _sw.Elapsed.TotalSeconds;
            _sw.Restart();
            double mult = Mults[Math.Max(0, cbSpeed.SelectedIndex)];
            _simTime += dt * mult;
            if (_simTime >= _total)
            {
                _simTime = _total;
                _playing = false;
                _simTimer.Stop();
                btnPlay.Text = "▶ Chạy lại";
            }
            UpdateSim(true);
        }

        void UpdateSim(bool head)
        {
            int idx = FindIdx(_simTime);
            float frac = 1f;
            if (idx < _segs.Count)
            {
                double start = idx > 0 ? _cum[idx - 1] : 0;
                double dur = _cum[idx] - start;
                frac = dur > 0 ? (float)Math.Clamp((_simTime - start) / dur, 0, 1) : 1f;
            }
            canvas.SetProgress(idx, frac, head);

            tbProg.Value = _total > 0 ? Math.Clamp((int)(_simTime / _total * 1000), 0, 1000) : 0;
            UpdateTimeLabels();

            if (chkFollow.Checked && idx < _segs.Count) HighlightLine(_segs[idx].Line);
        }

        void UpdateTimeLabels()
        {
            lblTime.Text = Fmt(_simTime) + " / " + Fmt(_total);
        }

        static string Fmt(double sec)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, sec));
            return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}";
        }

        void HighlightLine(int line)
        {
            if (line == _lastLine || line < 0 || line >= _shownLines) return;
            _lastLine = line;
            int s = txtCode.GetFirstCharIndexFromLine(line);
            if (s < 0) return;
            int e = txtCode.GetFirstCharIndexFromLine(line + 1);
            if (e < 0) e = txtCode.TextLength;
            txtCode.Select(s, Math.Max(0, e - s));
            txtCode.ScrollToCaret();
        }

        // ================= Lưu =================
        void SaveCode()
        {
            if (string.IsNullOrEmpty(_code)) return;
            using var d = new SaveFileDialog
            {
                Filter = "G-code (*.nc)|*.nc|G-code (*.gcode)|*.gcode|Text (*.txt)|*.txt",
                FileName = "output.nc"
            };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(d.FileName, _code, new UTF8Encoding(false));
            MessageBox.Show("Đã lưu: " + d.FileName, "Lưu G-code");
        }
    }
}
