// =====================================================================
//  Form1_PrintArea.cs  -  KHỔ IN: vùng khắc thực tế, tỉ lệ 1:1, đèn đỏ định vị
//
//  1) ĐỊNH HÌNH KHÔNG GIAN THỰC TẾ
//     Menu "Khổ in > Cài đặt khổ in...": chọn mẫu ống kính F-theta (70/110/150/175/200 mm), bàn cắt
//     (300/400 mm) hoặc nhập Rộng x Cao tùy ý. Canvas vẽ khung vùng khắc từ gốc thước (0,0), làm tối phần
//     ngoài, đánh dấu gốc toạ độ máy, cảnh báo hình vượt ra ngoài (viền đỏ nét đứt + hỏi khi xuất G-code).
//     Vùng khắc này cũng là vùng dùng cho các lệnh "Căn giữa / Căn chỉnh" ở menu Sắp xếp.
//
//  2) TỈ LỆ BẢN VẼ 1:1
//     - Mọi kích thước trên canvas tính bằng mm theo MmPerPx và G-code ra đúng mm đó (không méo, không phóng).
//     - "Kích thước thật 1:1" (Ctrl+1): zoom sao cho 1 mm trên màn hình = 1 mm thật khi đặt thước lên màn hình.
//     - "Hiệu chuẩn tỉ lệ màn hình": đo đoạn 100 mm bằng thước thật, nhập số đo được, phần mềm tự bù sai lệch.
//     - "Thu phóng vừa vùng khắc" (Ctrl+0).
//     - Chọn gốc toạ độ G-code: góc trái-dưới (GRBL) hoặc tâm (galvo EzCad/BSL/Ruida) và xuất theo vị trí
//       thật trên vùng khắc, để vị trí trên canvas = vị trí trên bàn máy.
//
//  3) ĐÈN ĐỎ ĐỊNH VỊ (RED LIGHT)
//     - "Hiện khung đèn đỏ" (F4): khung đỏ nét đứt chạy quanh biên ngoài của sản phẩm (phần SẼ khắc), kèm kích thước
//       và toạ độ máy. Khung đỏ đặc + cảnh báo nếu vượt vùng khắc.
//     - "Xuất G-code khung đèn đỏ...": file G-code chạy viền khung bằng laser công suất RẤT THẤP, hoặc dùng
//       lệnh bật/tắt đèn đỏ riêng của bo (ô "Lệnh bật/tắt"). Công suất 0 = chạy đầu laser không bắn.
//       Sau này khi có kết nối cổng COM: gán machineSend để gửi thẳng thay vì lưu file.
//
//  Gọi InitPrintArea() 1 lần ở CUỐI InitWorkspaceLayout() (Form1_Tools.cs), sau InitBarcodeAndArray().
//  Cần GCodeCompiler.cs (bản đã đổi partial) và GCodeFrame.cs.
// =====================================================================
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;

namespace test01_1014
{
    public enum PrintOrigin { BottomLeft, Center }

    public class PrintAreaCfg
    {
        public float WidthMm { get; set; } = 400f;
        public float HeightMm { get; set; } = 400f;
        public PrintOrigin Origin { get; set; } = PrintOrigin.BottomLeft;
        public bool AbsolutePositions { get; set; } = true;     // xuất G-code theo vị trí thật trên vùng khắc
        public bool ShowFrame { get; set; } = true;
        public bool DimOutside { get; set; } = true;
        public bool WarnOutside { get; set; } = true;
        public float ScreenCalib { get; set; } = 1f;            // hệ số hiệu chuẩn màn hình

        // Đèn đỏ
        public bool ShowRedLight { get; set; } = false;
        public float RedMarginMm { get; set; } = 0f;
        public float RedPowerPercent { get; set; } = 1f;
        public int RedFeed { get; set; } = 3000;
        public int RedLoops { get; set; } = 1;
        public bool RedCross { get; set; } = false;
        public string RedOnCmd { get; set; } = "";
        public string RedOffCmd { get; set; } = "";
    }

    // ------------------------------------------------------------------
    //  Hộp thoại Cài đặt khổ in
    // ------------------------------------------------------------------
    public class PrintAreaDialog : Form
    {
        private static readonly (string Label, float W, float H)[] Presets =
        {
            ("70 × 70 mm  (lens F-theta)", 70, 70),
            ("110 × 110 mm  (lens F-theta)", 110, 110),
            ("150 × 150 mm  (lens F-theta)", 150, 150),
            ("175 × 175 mm  (lens F-theta)", 175, 175),
            ("200 × 200 mm  (lens F-theta)", 200, 200),
            ("300 × 300 mm", 300, 300),
            ("400 × 400 mm  (bàn cắt)", 400, 400),
        };

        public PrintAreaCfg Result { get; private set; }

        private readonly PrintAreaCfg cfg;
        private readonly int dpi;
        private bool updating;

        private ComboBox cmbPreset = null!;
        private NumericUpDown numW = null!, numH = null!;
        private CheckBox chkAbs = null!, chkFrame = null!, chkDim = null!, chkWarn = null!, chkRedShow = null!, chkCross = null!;
        private RadioButton rbBL = null!, rbCenter = null!;
        private NumericUpDown numMargin = null!, numPower = null!, numFeed = null!, numLoops = null!;
        private TextBox txtOn = null!, txtOff = null!;

        private int Sc(int v) => (int)Math.Round(v * dpi / 96f);

        public PrintAreaDialog(PrintAreaCfg current, int dpi)
        {
            this.dpi = dpi;
            cfg = JsonSerializer.Deserialize<PrintAreaCfg>(JsonSerializer.Serialize(current)) ?? new PrintAreaCfg();
            Result = current;

            Text = "Cài đặt khổ in";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            ClientSize = new Size(Sc(500), Sc(640));
            Build();
            Load2();
        }

        private Label Lbl(Control p, string text, int x, int y, int w, int h = 20, bool note = false)
        {
            var l = new Label { Text = text, Left = Sc(x), Top = Sc(y), Width = Sc(w), Height = Sc(h) };
            if (note) { l.ForeColor = Color.DimGray; l.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold); }
            p.Controls.Add(l);
            return l;
        }

        private NumericUpDown Num(Control p, int x, int y, int w, decimal min, decimal max, int dec, decimal inc)
        {
            var n = new NumericUpDown
            {
                Left = Sc(x),
                Top = Sc(y),
                Width = Sc(w),
                Minimum = min,
                Maximum = max,
                DecimalPlaces = dec,
                Increment = inc
            };
            p.Controls.Add(n);
            return n;
        }

        private CheckBox Chk(Control p, string text, int x, int y, int w)
        {
            var c = new CheckBox { Text = text, Left = Sc(x), Top = Sc(y), Width = Sc(w), Height = Sc(22) };
            p.Controls.Add(c);
            return c;
        }

        private void Build()
        {
            var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var bar = new Panel { Dock = DockStyle.Bottom, Height = Sc(52) };
            Controls.Add(body);
            Controls.Add(bar);

            GroupBox Group(string title, int top, int height)
            {
                var g = new GroupBox { Text = title, Left = Sc(12), Top = Sc(top), Width = Sc(458), Height = Sc(height) };
                body.Controls.Add(g);
                return g;
            }

            // ---- 1. Khổ in ----
            var ga = Group("1. Khổ in / vùng khắc thực tế", 8, 168);
            Lbl(ga, "Mẫu có sẵn:", 14, 28, 100);
            cmbPreset = new ComboBox { Left = Sc(120), Top = Sc(25), Width = Sc(318), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var p in Presets) cmbPreset.Items.Add(p.Label);
            cmbPreset.Items.Add("Tùy chỉnh");
            ga.Controls.Add(cmbPreset);
            Lbl(ga, "Rộng (mm):", 14, 62, 100);
            numW = Num(ga, 120, 59, 120, 10, 5000, 1, 5);
            Lbl(ga, "Cao (mm):", 14, 94, 100);
            numH = Num(ga, 120, 91, 120, 10, 5000, 1, 5);
            Lbl(ga, "Lens F-theta: nhập đúng trường quét ghi trên ống kính. Bàn cắt: phần máy di chuyển được.",
                14, 122, 430, 36, true);
            cmbPreset.SelectedIndexChanged += (s, e) =>
            {
                if (updating || cmbPreset.SelectedIndex < 0 || cmbPreset.SelectedIndex >= Presets.Length) return;
                updating = true;
                numW.Value = (decimal)Presets[cmbPreset.SelectedIndex].W;
                numH.Value = (decimal)Presets[cmbPreset.SelectedIndex].H;
                updating = false;
            };
            EventHandler sizeChanged = (s, e) => { if (!updating) SyncPresetFromSize(); };
            numW.ValueChanged += sizeChanged;
            numH.ValueChanged += sizeChanged;

            // ---- 2. Gốc toạ độ ----
            var gb = Group("2. Gốc toạ độ & xuất G-code", 184, 142);
            chkAbs = Chk(gb, "Xuất G-code theo vị trí thật trên vùng khắc", 14, 24, 430);
            Lbl(gb, "Tắt = gốc (0,0) là góc dưới-trái của cả bản vẽ (đặt gốc máy ngay tại vật liệu).", 30, 46, 410, 32, true);
            rbBL = new RadioButton { Text = "Gốc ở góc trái-dưới  (GRBL, LightBurn)", Left = Sc(30), Top = Sc(82), Width = Sc(400), Height = Sc(22) };
            rbCenter = new RadioButton { Text = "Gốc ở tâm vùng khắc  (galvo EzCad / BSL / Ruida)", Left = Sc(30), Top = Sc(106), Width = Sc(400), Height = Sc(22) };
            gb.Controls.Add(rbBL); gb.Controls.Add(rbCenter);
            chkAbs.CheckedChanged += (s, e) => { rbBL.Enabled = rbCenter.Enabled = chkAbs.Checked; };

            // ---- 3. Hiển thị ----
            var gc = Group("3. Hiển thị trên canvas", 334, 104);
            chkFrame = Chk(gc, "Hiện khung vùng khắc + dấu gốc toạ độ", 14, 22, 430);
            chkDim = Chk(gc, "Làm tối phần nằm ngoài vùng khắc", 14, 46, 430);
            chkWarn = Chk(gc, "Cảnh báo khi hình vượt vùng khắc (hỏi lại khi xuất G-code)", 14, 70, 436);

            // ---- 4. Đèn đỏ ----
            var gd = Group("4. Đèn đỏ định vị (Red Light)", 446, 262);
            chkRedShow = Chk(gd, "Hiện khung đèn đỏ (biên ngoài sản phẩm) trên canvas   [F4]", 14, 22, 436);
            Lbl(gd, "Lề quanh sản phẩm (mm):", 14, 54, 190);
            numMargin = Num(gd, 220, 51, 100, 0, 100, 1, 0.5m);
            Lbl(gd, "Công suất đèn đỏ (%):", 14, 84, 190);
            numPower = Num(gd, 220, 81, 100, 0, 100, 1, 0.5m);
            Lbl(gd, "0 = chỉ chạy đầu laser, không bắn", 328, 80, 125, 40, true);
            Lbl(gd, "Tốc độ chạy khung (mm/phút):", 14, 114, 206);
            numFeed = Num(gd, 220, 111, 100, 100, 60000, 0, 500);
            Lbl(gd, "Số vòng chạy khung:", 14, 144, 190);
            numLoops = Num(gd, 220, 141, 100, 1, 20, 0, 1);
            chkCross = Chk(gd, "Thêm chữ thập ở tâm khung", 14, 170, 430);
            Lbl(gd, "Lệnh bật đèn đỏ riêng:", 14, 200, 190);
            txtOn = new TextBox { Left = Sc(220), Top = Sc(197), Width = Sc(220) };
            Lbl(gd, "Lệnh tắt đèn đỏ riêng:", 14, 228, 190);
            txtOff = new TextBox { Left = Sc(220), Top = Sc(225), Width = Sc(220) };
            gd.Controls.Add(txtOn); gd.Controls.Add(txtOff);
            // chú thích đặt ngoài nhóm cho khỏi chật
            var hint = new Label
            {
                Text = "Để trống 2 ô lệnh = dùng laser công suất thấp. Công suất 1% vẫn có thể để lại vết trên một số vật liệu: " +
                       "thử trên phế liệu hoặc đặt 0.",
                Left = Sc(14),
                Top = Sc(714),
                Width = Sc(440),
                Height = Sc(50),
                ForeColor = Color.DimGray,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            body.Controls.Add(hint);

            // ---- nút ----
            var ok = new Button { Text = "OK", Left = Sc(290), Top = Sc(10), Width = Sc(90), Height = Sc(30), DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Hủy", Left = Sc(390), Top = Sc(10), Width = Sc(90), Height = Sc(30), DialogResult = DialogResult.Cancel };
            ok.Click += (s, e) => Collect();
            bar.Controls.Add(ok); bar.Controls.Add(cancel);
            AcceptButton = ok; CancelButton = cancel;
        }

        private void SyncPresetFromSize()
        {
            int idx = Presets.Length;     // "Tùy chỉnh"
            for (int i = 0; i < Presets.Length; i++)
                if (Math.Abs((float)numW.Value - Presets[i].W) < 0.01f && Math.Abs((float)numH.Value - Presets[i].H) < 0.01f) { idx = i; break; }
            updating = true;
            cmbPreset.SelectedIndex = idx;
            updating = false;
        }

        private void Load2()
        {
            updating = true;
            numW.Value = Math.Min(numW.Maximum, Math.Max(numW.Minimum, (decimal)cfg.WidthMm));
            numH.Value = Math.Min(numH.Maximum, Math.Max(numH.Minimum, (decimal)cfg.HeightMm));
            updating = false;
            SyncPresetFromSize();

            chkAbs.Checked = cfg.AbsolutePositions;
            rbBL.Checked = cfg.Origin == PrintOrigin.BottomLeft;
            rbCenter.Checked = cfg.Origin == PrintOrigin.Center;
            rbBL.Enabled = rbCenter.Enabled = cfg.AbsolutePositions;

            chkFrame.Checked = cfg.ShowFrame; chkDim.Checked = cfg.DimOutside; chkWarn.Checked = cfg.WarnOutside;

            chkRedShow.Checked = cfg.ShowRedLight;
            numMargin.Value = Math.Min(numMargin.Maximum, Math.Max(numMargin.Minimum, (decimal)cfg.RedMarginMm));
            numPower.Value = Math.Min(numPower.Maximum, Math.Max(numPower.Minimum, (decimal)cfg.RedPowerPercent));
            numFeed.Value = Math.Min(numFeed.Maximum, Math.Max(numFeed.Minimum, cfg.RedFeed));
            numLoops.Value = Math.Min(numLoops.Maximum, Math.Max(numLoops.Minimum, cfg.RedLoops));
            chkCross.Checked = cfg.RedCross;
            txtOn.Text = cfg.RedOnCmd; txtOff.Text = cfg.RedOffCmd;
        }

        private void Collect()
        {
            cfg.WidthMm = (float)numW.Value; cfg.HeightMm = (float)numH.Value;
            cfg.AbsolutePositions = chkAbs.Checked;
            cfg.Origin = rbCenter.Checked ? PrintOrigin.Center : PrintOrigin.BottomLeft;
            cfg.ShowFrame = chkFrame.Checked; cfg.DimOutside = chkDim.Checked; cfg.WarnOutside = chkWarn.Checked;
            cfg.ShowRedLight = chkRedShow.Checked;
            cfg.RedMarginMm = (float)numMargin.Value;
            cfg.RedPowerPercent = (float)numPower.Value;
            cfg.RedFeed = (int)numFeed.Value;
            cfg.RedLoops = (int)numLoops.Value;
            cfg.RedCross = chkCross.Checked;
            cfg.RedOnCmd = txtOn.Text.Trim(); cfg.RedOffCmd = txtOff.Text.Trim();
            Result = cfg;
        }
    }

    // ------------------------------------------------------------------
    //  Gắn vào Form1
    // ------------------------------------------------------------------
    public partial class Form1
    {
        private PrintAreaCfg printCfg = new();
        private bool printInited, paMenuSync;
        private ToolStripMenuItem miPaShow = null!, miPaRed = null!;
        private ToolStripStatusLabel? stArea;
        private System.Windows.Forms.Timer? redTimer;
        private float redPhase;
        private RectangleF? redRectPx;
        private int redSig = int.MinValue;

        // Sau này (kết nối cổng COM / GRBL): gán hàm gửi G-code vào đây; trả về true nếu đã gửi.
        private Func<string, bool>? machineSend;

        private static string PrintCfgPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "test01_1014", "printarea.json");

        private RectangleF AreaPx => new RectangleF(0, 0, printCfg.WidthMm / MmPerPx, printCfg.HeightMm / MmPerPx);

        // Vùng khắc dùng cho menu Sắp xếp / Căn chỉnh (xem Form1_Arrange.cs)
        private SizeF PrintAreaSizeMm => new SizeF(printCfg.WidthMm, printCfg.HeightMm);

        // =================================================================
        //  Khởi tạo
        // =================================================================
        private void InitPrintArea()
        {
            if (printInited) return;
            printInited = true;
            LoadPrintCfg();

            var root = new ToolStripMenuItem("Khổ in");
            var miSet = new ToolStripMenuItem("Cài đặt khổ in...");
            miSet.Click += (s, e) => OpenPrintAreaDialog();

            miPaShow = new ToolStripMenuItem("Hiện vùng khắc trên canvas") { CheckOnClick = true, Checked = printCfg.ShowFrame };
            miPaShow.CheckedChanged += (s, e) =>
            {
                if (paMenuSync) return;
                printCfg.ShowFrame = miPaShow.Checked; SavePrintCfg(); pictureBox1.Invalidate();
            };

            var miFit = new ToolStripMenuItem("Thu phóng vừa vùng khắc") { ShortcutKeys = Keys.Control | Keys.D0 };
            miFit.Click += (s, e) => ZoomFitArea();
            var miActual = new ToolStripMenuItem("Kích thước thật 1:1") { ShortcutKeys = Keys.Control | Keys.D1 };
            miActual.Click += (s, e) => ZoomActualSize();
            var miCal = new ToolStripMenuItem("Hiệu chuẩn tỉ lệ màn hình...");
            miCal.Click += (s, e) => CalibrateScreen();

            miPaRed = new ToolStripMenuItem("Hiện khung đèn đỏ (biên sản phẩm)") { CheckOnClick = true, Checked = printCfg.ShowRedLight, ShortcutKeys = Keys.F4 };
            miPaRed.CheckedChanged += (s, e) =>
            {
                if (paMenuSync) return;
                printCfg.ShowRedLight = miPaRed.Checked; SavePrintCfg(); RedLightChanged();
            };
            var miRedExp = new ToolStripMenuItem("Xuất G-code khung đèn đỏ...");
            miRedExp.Click += (s, e) => ExportRedLight();

            root.DropDownItems.AddRange(new ToolStripItem[]
            {
                miSet, miPaShow, new ToolStripSeparator(),
                miFit, miActual, miCal, new ToolStripSeparator(),
                miPaRed, miRedExp
            });

            int idx = arrangeRoot != null ? menuStrip1.Items.IndexOf(arrangeRoot) : -1;
            if (idx < 0) idx = menuStrip1.Items.IndexOf(viewToolStripMenuItem);
            menuStrip1.Items.Insert(idx >= 0 ? idx + 1 : menuStrip1.Items.Count, root);

            // Ô "Khổ in" trên thanh trạng thái do InitStatusBar (Form1_Status.cs) tạo; ở đây chỉ cập nhật chữ
            UpdateAreaStatus();

            redTimer = new System.Windows.Forms.Timer { Interval = 110 };
            redTimer.Tick += (s, e) =>
            {
                if (!printCfg.ShowRedLight || ReadOnlyView) return;
                redPhase = (redPhase + 0.6f) % 7f;                    // chạy nét đứt như đèn đỏ đang quét
                var r = RedRect();
                if (r != null) pictureBox1.Invalidate(RedScreenRect(r.Value));
            };
            redTimer.Start();

            pictureBox1.OverlayPaint += OnPrintAreaPaint;

            InitMachine();                                  // menu "Máy" + điều khiển cổng COM (Form1_Machine.cs)
            InitObjectList();                               // tab "Đối tượng" ở cột phải (Form1_ObjectList.cs)
        }

        private void UpdateAreaStatus()
        {
            if (stArea != null)
                stArea.Text = $"Khổ in: {printCfg.WidthMm:0.##} × {printCfg.HeightMm:0.##} mm";
        }

        private void SyncPrintMenus()
        {
            paMenuSync = true;
            miPaShow.Checked = printCfg.ShowFrame;
            miPaRed.Checked = printCfg.ShowRedLight;
            paMenuSync = false;
        }

        private void RedLightChanged()
        {
            redSig = int.MinValue;
            pictureBox1.Invalidate();
        }

        private void OpenPrintAreaDialog()
        {
            using var dlg = new PrintAreaDialog(printCfg, DeviceDpi);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            bool sizeChanged = Math.Abs(dlg.Result.WidthMm - printCfg.WidthMm) > 0.001f ||
                               Math.Abs(dlg.Result.HeightMm - printCfg.HeightMm) > 0.001f;
            dlg.Result.ScreenCalib = printCfg.ScreenCalib;      // hiệu chuẩn có hộp thoại riêng
            printCfg = dlg.Result;
            SavePrintCfg();
            SyncPrintMenus();
            UpdateAreaStatus();
            redSig = int.MinValue;
            if (sizeChanged) ZoomFitArea(); else pictureBox1.Invalidate();
        }

        private void LoadPrintCfg()
        {
            try
            {
                if (!File.Exists(PrintCfgPath)) return;
                var c = JsonSerializer.Deserialize<PrintAreaCfg>(File.ReadAllText(PrintCfgPath));
                if (c == null) return;
                c.WidthMm = Math.Clamp(c.WidthMm, 10f, 5000f);
                c.HeightMm = Math.Clamp(c.HeightMm, 10f, 5000f);
                c.ScreenCalib = Math.Clamp(c.ScreenCalib, 0.3f, 3f);
                printCfg = c;
            }
            catch { /* file hỏng thì dùng mặc định */ }
        }

        private void SavePrintCfg()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PrintCfgPath)!);
                File.WriteAllText(PrintCfgPath, JsonSerializer.Serialize(printCfg));
            }
            catch { }
        }

        // =================================================================
        //  Thu phóng: vừa vùng khắc / kích thước thật 1:1 / hiệu chuẩn
        // =================================================================
        // Pixel màn hình trên 1 mm THẬT (DPI Windows báo, nhân hệ số hiệu chuẩn người dùng)
        private float PxPerMmReal() => DeviceDpi / 25.4f * printCfg.ScreenCalib;

        private void ZoomFitArea()
        {
            if (ReadOnlyView) SetActiveTab(null);
            var a = AreaPx;
            float availW = Math.Max(50, pictureBox1.Width - Inset - 40);
            float availH = Math.Max(50, pictureBox1.Height - Inset - 40);
            zoom = Math.Clamp(Math.Min(availW / a.Width, availH / a.Height), 0.1f, 20f);
            pan = new PointF(Inset + (pictureBox1.Width - Inset - a.Width * zoom) / 2f,
                             Inset + (pictureBox1.Height - Inset - a.Height * zoom) / 2f);
            pictureBox1.Invalidate();
        }

        private void ZoomActualSize()
        {
            // 1 mm thật = PxPerMmReal() px màn hình; canvas có zoom/MmPerPx px màn hình trên 1 mm
            float nz = Math.Clamp(PxPerMmReal() * MmPerPx, 0.1f, 20f);
            var c = new Point(pictureBox1.Width / 2, pictureBox1.Height / 2);
            var w = ScreenToWorld(c);                  // giữ nguyên điểm đang ở giữa màn hình
            zoom = nz;
            pan = new PointF(c.X - w.X * zoom, c.Y - w.Y * zoom);
            pictureBox1.Invalidate();
        }

        private void CalibrateScreen()
        {
            float a = PxPerMmReal();
            int lenPx = (int)Math.Round(100f * a);
            using var f = new Form
            {
                Text = "Hiệu chuẩn tỉ lệ màn hình",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                AutoScaleMode = AutoScaleMode.None,
                ClientSize = new Size(Math.Max(Scale96(470), lenPx + Scale96(70)), Scale96(268)),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            var info = new Label
            {
                Text = "Đặt thước thật lên màn hình, đo đoạn đen bên dưới (đang là 100 mm theo cài đặt hiện tại) " +
                       "rồi nhập số đo được. Phần mềm sẽ bù lại để \"Kích thước thật 1:1\" khớp thước.",
                Bounds = new Rectangle(Scale96(14), Scale96(10), f.ClientSize.Width - Scale96(28), Scale96(56))
            };
            var ruler = new Panel
            {
                Bounds = new Rectangle(Scale96(14), Scale96(72), f.ClientSize.Width - Scale96(28), Scale96(56)),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            ruler.Paint += (s, e) =>
            {
                var g = e.Graphics;
                int x0 = Scale96(16), y = ruler.Height / 2;
                using var p = new Pen(Color.Black, 2f);
                g.DrawLine(p, x0, y, x0 + lenPx, y);
                for (int i = 0; i <= 10; i++)                          // vạch mỗi 10 mm
                {
                    int x = x0 + (int)Math.Round(i * 10 * a);
                    int h = (i == 0 || i == 10) ? Scale96(12) : Scale96(6);
                    g.DrawLine(p, x, y - h, x, y + h);
                }
                using var fnt = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                g.DrawString("100 mm", fnt, Brushes.DimGray, x0 + lenPx / 2 - Scale96(22), y + Scale96(10));
            };
            var lbl = new Label { Text = "Đo được (mm):", Bounds = new Rectangle(Scale96(14), Scale96(146), Scale96(110), Scale96(24)) };
            var num = new NumericUpDown
            {
                DecimalPlaces = 1,
                Minimum = 20,
                Maximum = 500,
                Value = 100,
                Increment = 0.5m,
                Bounds = new Rectangle(Scale96(130), Scale96(143), Scale96(100), Scale96(26))
            };
            var cur = new Label
            {
                Text = $"Hệ số hiện tại: {printCfg.ScreenCalib:0.###}",
                Bounds = new Rectangle(Scale96(250), Scale96(146), Scale96(200), Scale96(24)),
                ForeColor = Color.DimGray
            };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(f.ClientSize.Width - Scale96(190), Scale96(222), Scale96(80), Scale96(30)) };
            var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(f.ClientSize.Width - Scale96(100), Scale96(222), Scale96(80), Scale96(30)) };
            var reset = new Button { Text = "Đặt lại mặc định", Bounds = new Rectangle(Scale96(14), Scale96(222), Scale96(140), Scale96(30)) };
            reset.Click += (s, e) => { printCfg.ScreenCalib = 1f; SavePrintCfg(); f.DialogResult = DialogResult.Cancel; f.Close(); pictureBox1.Invalidate(); };
            f.Controls.AddRange(new Control[] { info, ruler, lbl, num, cur, ok, cancel, reset });
            f.AcceptButton = ok; f.CancelButton = cancel;

            if (f.ShowDialog(this) != DialogResult.OK) return;
            printCfg.ScreenCalib = Math.Clamp(printCfg.ScreenCalib * 100f / (float)num.Value, 0.3f, 3f);
            SavePrintCfg();
            pictureBox1.Invalidate();
        }

        // =================================================================
        //  Cài đặt G-code theo khổ in + kiểm tra vượt vùng
        // =================================================================
        // Dùng chung gcodeCfg (Form1_AutoSave.cs) để S tối đa, bước quét... được nhớ giữa các lần mở
        private GCodeSettings BuildGCodeSettings()
        {
            var c = gcodeCfg;
            c.MmPerPixel = MmPerPx;                              // khóa tỉ lệ canvas -> mm: KHÔNG đổi ở nơi khác
            float wPx = printCfg.WidthMm / MmPerPx, hPx = printCfg.HeightMm / MmPerPx;
            c.OriginAtJobCorner = !printCfg.AbsolutePositions;
            c.CanvasHeightPx = hPx;
            if (printCfg.Origin == PrintOrigin.Center) { c.AbsOriginXpx = wPx / 2f; c.AbsOriginYpx = hPx / 2f; }
            else { c.AbsOriginXpx = 0f; c.AbsOriginYpx = hPx; }
            return c;
        }

        private FrameOptions BuildFrameOptions() => new FrameOptions
        {
            MarginMm = printCfg.RedMarginMm,
            PowerPercent = printCfg.RedPowerPercent,
            Feed = printCfg.RedFeed,
            Loops = printCfg.RedLoops,
            Cross = printCfg.RedCross,
            OnCmd = printCfg.RedOnCmd,
            OffCmd = printCfg.RedOffCmd
        };

        private bool RectOutsideArea(RectangleF b)
        {
            var a = AreaPx;
            float eps = 0.01f / MmPerPx;                          // dung sai 0.01 mm
            return b.Left < a.Left - eps || b.Top < a.Top - eps || b.Right > a.Right + eps || b.Bottom > a.Bottom + eps;
        }

        private List<Vectoritem> ItemsOutsideArea()
        {
            var res = new List<Vectoritem>();
            foreach (var it in ExportItems())              // chỉ tính các lớp bật "Xuất"
                if (RectOutsideArea(ItemBounds(it))) res.Add(it);
            return res;
        }

        // Gọi trước khi mở form xuất G-code. true = tiếp tục
        private bool ConfirmInsideArea()
        {
            if (!printCfg.WarnOutside) return true;
            var o = ItemsOutsideArea();
            if (o.Count == 0) return true;
            var rs = MessageBox.Show(this,
                $"Có {o.Count} đối tượng nằm ngoài vùng khắc {printCfg.WidthMm:0.##} × {printCfg.HeightMm:0.##} mm.\n\n" +
                "Phần nằm ngoài sẽ không khắc được và có thể làm máy chạy quá hành trình.\n\nVẫn tiếp tục xuất G-code?",
                "Vượt vùng khắc", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            return rs == DialogResult.Yes;
        }

        // =================================================================
        //  Đèn đỏ
        // =================================================================
        // Khung bao (px) của phần sẽ khắc + lề; lưu đệm theo nội dung dự án
        private RectangleF? RedRect()
        {
            int sig = HashCode.Combine(ComputeContentSignature(), printCfg.RedMarginMm, gcodeCfg.MaxS);
            if (sig != redSig)
            {
                redSig = sig;
                redRectPx = null;
                try
                {
                    var bb = new GCodeCompiler(BuildGCodeSettings()).GetBoundsPx(ExportItems());
                    if (bb is RectangleF r)
                    {
                        float m = printCfg.RedMarginMm / MmPerPx;
                        r.Inflate(m, m);
                        redRectPx = r;
                    }
                }
                catch { redRectPx = null; }
            }
            return redRectPx;
        }

        private Rectangle RedScreenRect(RectangleF r)
        {
            var sr = new RectangleF(pan.X + r.X * zoom, pan.Y + r.Y * zoom, r.Width * zoom, r.Height * zoom);
            return Rectangle.Inflate(Rectangle.Ceiling(sr), 14, 14);
        }

        // Toạ độ máy (mm) của 1 điểm canvas, theo gốc đã chọn; chỉ có nghĩa khi xuất theo vị trí thật
        private PointF PxToMachineMm(PointF p)
        {
            float k = MmPerPx;
            var a = AreaPx;
            return printCfg.Origin == PrintOrigin.Center
                ? new PointF((p.X - a.Width / 2f) * k, (a.Height / 2f - p.Y) * k)
                : new PointF(p.X * k, (a.Height - p.Y) * k);
        }

        private void ExportRedLight()
        {
            if (projectItems.Count == 0) { MessageBox.Show(this, "Canvas đang trống.", "Đèn đỏ"); return; }
            try
            {
                var comp = new GCodeCompiler(machine?.IsOpen == true ? BuildMachineSettings() : BuildGCodeSettings());   // đã nối máy: toạ độ theo "Bắt đầu từ"
                GCodeResult res = comp.CompileFrame(ExportItems(), BuildFrameOptions());

                var rr = RedRect();
                if (printCfg.WarnOutside && rr is RectangleF r0 && RectOutsideArea(r0) &&
                    MessageBox.Show(this, "Khung đèn đỏ vượt ra ngoài vùng khắc. Vẫn xuất?", "Vượt vùng khắc",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;

                if (machineSend != null && machineSend(res.Code))      // đã kết nối máy: gửi thẳng
                    return;

                using var sfd = new SaveFileDialog
                {
                    Filter = "G-code (*.nc)|*.nc|G-code (*.gcode)|*.gcode|Text (*.txt)|*.txt",
                    FileName = "redlight_frame.nc",
                    Title = "Lưu G-code khung đèn đỏ"
                };
                if (sfd.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(sfd.FileName, res.Code, new UTF8Encoding(false));

                string mode = !string.IsNullOrWhiteSpace(printCfg.RedOnCmd) ? "lệnh đèn đỏ riêng"
                            : printCfg.RedPowerPercent > 0 ? $"laser công suất thấp {printCfg.RedPowerPercent:0.#}%"
                            : "không bắn laser";
                MessageBox.Show(this,
                    $"Đã lưu khung {res.WidthMm:0.##} × {res.HeightMm:0.##} mm ({printCfg.RedLoops} vòng, {mode}).\n" +
                    $"Ước tính {res.EstimatedMinutes * 60:0} giây.",
                    "Khung đèn đỏ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Không tạo được khung đèn đỏ:\n" + ex.Message, "Đèn đỏ",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // =================================================================
        //  Vẽ lớp phủ: vùng khắc + đèn đỏ
        // =================================================================
        private void OnPrintAreaPaint(object? sender, PaintEventArgs e)
        {
            if (ReadOnlyView || (!printCfg.ShowFrame && !printCfg.ShowRedLight)) return;
            var g = e.Graphics;
            var area = AreaPx;
            var outside = printCfg.ShowFrame && printCfg.WarnOutside ? ItemsOutsideArea() : new List<Vectoritem>();
            RectangleF? red = printCfg.ShowRedLight ? RedRect() : null;

            var state = g.Save();
            try
            {
                g.SetClip(new Rectangle(Inset, Inset, Math.Max(0, pictureBox1.Width - Inset),
                                        Math.Max(0, pictureBox1.Height - Inset)), CombineMode.Intersect);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(pan.X, pan.Y);
                g.ScaleTransform(zoom, zoom);

                if (printCfg.ShowFrame) PaintAreaWorld(g, area, outside);
                if (red is RectangleF rr) PaintRedWorld(g, rr);
            }
            finally { g.Restore(state); }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (printCfg.ShowFrame) PaintAreaLabels(g, area, outside.Count);
            if (red is RectangleF rl) PaintRedLabel(g, rl);
        }

        private void PaintAreaWorld(Graphics g, RectangleF area, List<Vectoritem> outside)
        {
            if (printCfg.DimOutside)       // làm tối phần ngoài vùng khắc
            {
                var tl = ScreenToWorld(Point.Empty);
                var br = ScreenToWorld(new Point(pictureBox1.Width, pictureBox1.Height));
                var st = g.Save();
                using (var hole = new Region(area)) g.ExcludeClip(hole);   // Graphics.ExcludeClip không nhận RectangleF
                using var dim = new SolidBrush(Color.FromArgb(70, 60, 60, 70));
                g.FillRectangle(dim, tl.X - 2, tl.Y - 2, br.X - tl.X + 4, br.Y - tl.Y + 4);
                g.Restore(st);
            }

            using (var pen = new Pen(Color.FromArgb(210, 0, 90, 170), 2f / zoom))
                g.DrawRectangle(pen, area.X, area.Y, area.Width, area.Height);

            // Dấu gốc toạ độ máy
            float len = 16f / zoom;
            using var cap = new AdjustableArrowCap(3, 4);
            using var op = new Pen(Color.FromArgb(220, 200, 40, 40), 2f / zoom);
            if (printCfg.Origin == PrintOrigin.Center)
            {
                float cx = area.X + area.Width / 2f, cy = area.Y + area.Height / 2f;
                g.DrawLine(op, cx - len, cy, cx + len, cy);
                g.DrawLine(op, cx, cy - len, cx, cy + len);
                g.DrawEllipse(op, cx - len / 3f, cy - len / 3f, 2f * len / 3f, 2f * len / 3f);
            }
            else
            {
                op.CustomEndCap = cap;
                g.DrawLine(op, area.Left, area.Bottom, area.Left + len * 1.8f, area.Bottom);     // +X
                g.DrawLine(op, area.Left, area.Bottom, area.Left, area.Bottom - len * 1.8f);     // +Y (hướng lên)
            }

            // Hình vượt vùng: khung đỏ nét đứt
            if (outside.Count > 0)
            {
                using var wp = new Pen(Color.FromArgb(230, 220, 30, 30), 1.5f / zoom) { DashStyle = DashStyle.Dash };
                foreach (var it in outside)
                {
                    var b = ItemBounds(it);
                    g.DrawRectangle(wp, b.X, b.Y, b.Width, b.Height);
                }
            }
        }

        private void PaintAreaLabels(Graphics g, RectangleF area, int outsideCount)
        {
            float sx = pan.X + area.Left * zoom, sy = pan.Y + area.Top * zoom;
            bool actual = Math.Abs(zoom - PxPerMmReal() * MmPerPx) < 0.004f * zoom;

            string title = $"Vùng khắc {printCfg.WidthMm:0.##} × {printCfg.HeightMm:0.##} mm" + (actual ? "   ● 1:1" : "");
            using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
            {
                var sz = g.MeasureString(title, f);
                float ty = sy - sz.Height - 4;
                if (ty < Inset + 2) ty = sy + 4;                              // hết chỗ phía trên thì đặt vào trong
                float tx = Math.Max(Inset + 4, sx + 2);
                using var bg = new SolidBrush(Color.FromArgb(225, 255, 255, 255));
                g.FillRectangle(bg, tx - 2, ty - 1, sz.Width + 6, sz.Height + 2);
                g.DrawString(title, f, actual ? Brushes.DarkGreen : Brushes.RoyalBlue, tx, ty);
            }

            // Nhãn gốc toạ độ
            using (var f = new Font("Segoe UI", 8.5f))
            {
                float ox, oy;
                if (printCfg.Origin == PrintOrigin.Center)
                { ox = pan.X + (area.Left + area.Width / 2f) * zoom + 8; oy = pan.Y + (area.Top + area.Height / 2f) * zoom + 6; }
                else
                { ox = pan.X + area.Left * zoom + 6; oy = pan.Y + area.Bottom * zoom + 4; }
                g.DrawString(printCfg.AbsolutePositions ? "0,0" : "gốc theo bản vẽ", f, Brushes.Firebrick, ox, oy);
            }

            if (outsideCount > 0)
            {
                string w = $"⚠ {outsideCount} đối tượng vượt ra ngoài vùng khắc";
                using var f = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                var sz = g.MeasureString(w, f);
                float x = pictureBox1.Width - sz.Width - 14, y = Inset + 8;
                using var bg = new SolidBrush(Color.FromArgb(235, 255, 235, 235));
                using var bd = new Pen(Color.Firebrick);
                g.FillRectangle(bg, x - 6, y - 3, sz.Width + 12, sz.Height + 6);
                g.DrawRectangle(bd, x - 6, y - 3, sz.Width + 12, sz.Height + 6);
                g.DrawString(w, f, Brushes.Firebrick, x, y);
            }
        }

        private void PaintRedWorld(Graphics g, RectangleF r)
        {
            bool over = RectOutsideArea(r);
            float w = (over ? 2.6f : 1.8f) / zoom;

            using (var tint = new SolidBrush(Color.FromArgb(over ? 26 : 14, 255, 0, 0)))
                g.FillRectangle(tint, r);

            using (var pen = new Pen(Color.FromArgb(240, over ? 255 : 230, 20, 20), w)
            { DashPattern = new float[] { 4f, 3f }, DashOffset = redPhase })
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);

            // 4 góc đậm (dễ thấy điểm mốc khi nhìn lên máy)
            float L = Math.Min(14f / zoom, Math.Min(r.Width, r.Height) / 3f);
            using var cp = new Pen(Color.FromArgb(255, 220, 0, 0), w * 1.8f);
            g.DrawLines(cp, new[] { new PointF(r.Left, r.Top + L), new PointF(r.Left, r.Top), new PointF(r.Left + L, r.Top) });
            g.DrawLines(cp, new[] { new PointF(r.Right - L, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Top + L) });
            g.DrawLines(cp, new[] { new PointF(r.Right, r.Bottom - L), new PointF(r.Right, r.Bottom), new PointF(r.Right - L, r.Bottom) });
            g.DrawLines(cp, new[] { new PointF(r.Left + L, r.Bottom), new PointF(r.Left, r.Bottom), new PointF(r.Left, r.Bottom - L) });
        }

        private void PaintRedLabel(Graphics g, RectangleF r)
        {
            bool over = RectOutsideArea(r);
            string t = $"Đèn đỏ {r.Width * MmPerPx:0.##} × {r.Height * MmPerPx:0.##} mm";
            if (printCfg.AbsolutePositions)
            {
                var a = PxToMachineMm(new PointF(r.Left, r.Top));
                var b = PxToMachineMm(new PointF(r.Right, r.Bottom));
                t += $"   X {Math.Min(a.X, b.X):0.#} → {Math.Max(a.X, b.X):0.#}   Y {Math.Min(a.Y, b.Y):0.#} → {Math.Max(a.Y, b.Y):0.#}";
            }
            if (over) t += "   ⚠ VƯỢT VÙNG KHẮC";

            using var f = new Font("Segoe UI", 9f, FontStyle.Bold);
            var sz = g.MeasureString(t, f);
            float sx = pan.X + r.Left * zoom, sy = pan.Y + r.Bottom * zoom + 6;
            if (sy + sz.Height > pictureBox1.Height - 30) sy = pan.Y + r.Top * zoom - sz.Height - 6;
            sx = Math.Max(Inset + 4, Math.Min(sx, pictureBox1.Width - sz.Width - 8));
            using var bg = new SolidBrush(Color.FromArgb(230, 255, 245, 245));
            g.FillRectangle(bg, sx - 2, sy - 1, sz.Width + 6, sz.Height + 2);
            g.DrawString(t, f, Brushes.Firebrick, sx, sy);
        }
    }
}