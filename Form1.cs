using OpenCvSharp;
using OpenCvSharp.Dnn;
using System.Text.Json;
using test01_1014;
using System.Windows.Forms;
using Point = System.Drawing.Point;   // tránh xung đột với OpenCvSharp.Point
using File = System.IO.File;                // tránh nhập nhằng với System.Net.WebRequestMethods.File

namespace test01_1014
{
    public partial class Form1 : Form
    {
        Mat g_srcImage = new Mat();
        Mat g_processedImage = new Mat();
        int g_viewMode = 1;
        int g_thresholdVal = 128;
        int g_blurVal = 1;
        private bool showGrid = true; // Mặc định hiển thị lưới
        private Vectoritem selectedItem = null!;     // Đối tượng đang được chọn
        private bool isDraggingItem = false;         // Cờ kiểm tra đang kéo hình
        private PointF dragOffset;                   // Khoảng cách lệch khi kéo chuột (tọa độ thế giới)
        // Danh sách chứa các hình khối, chữ, ảnh vector trên canvas
        private List<Vectoritem> projectItems = new List<Vectoritem>();

        // ===== Zoom / pan vùng làm việc =====
        private float zoom = 1f;
        private const float MmPerPx = 0.25f;   // 1 px canvas = 0.25 mm
        private const int RulerSize = 24;      // độ dày thước (px màn hình)
        private PointF pan = PointF.Empty;
        private bool isPanning;
        private Point panStart;
        private RectangleF zoomBadge = RectangleF.Empty;   // (đã bỏ nhãn % trên canvas: zoom hiện ở thanh trạng thái; để Empty thì Contains luôn false)

        // ===== Đổi kích thước bằng 8 điểm =====
        private const float MinSize = 5f;
        private int resizeHandle = -1;
        private RectangleF resizeStart;
        private PointF resizeMouseStart;
        private int lineHandle = -1;                 // 0 = đầu mút 1, 1 = đầu mút 2 của Line
        private float resizeRot;                     // góc xoay của hình lúc bắt đầu kéo điểm
        private PointF resizeCenter;                 // tâm hình (toạ độ thế giới) lúc bắt đầu kéo
        private float resizeFontStart;               // cỡ chữ lúc bắt đầu kéo (chỉ dùng cho chữ)
        private bool updatingUI;                     // true khi code đang nạp giá trị lên ô nhập

        // ===== Clipboard / menu chuột phải =====
        private Vectoritem? clipboardItem;
        private PointF contextWorldPos;

        // ===== Undo / Redo =====
        private class UndoState { public List<Vectoritem> Items = new(); public int Selected = -1; }
        private readonly List<UndoState> undoStack = new();
        private readonly List<UndoState> redoStack = new();
        private UndoState? pendingUndo;
        private string? lastCoalesceKey;
        private DateTime lastCoalesceTime;
        private const int MaxUndo = 100;
        // Vẽ icon 1 hình. key: line, rect, circle, diamond, pentagon, hexagon, star
        private enum ViewMode { Original, Edited, LaserPath }
        private bool showRuler = true;
        private int Inset => showRuler ? RulerSize : 0;     // phần bị thước chiếm
        private readonly System.Windows.Forms.Timer filterTimer = new System.Windows.Forms.Timer { Interval = 90 };
        private class ViewTab
        {
            public int Number;
            public int Threshold, Blur;
            public bool Invert;                                                     // đảo màu (âm bản) của ảnh trong tab
            public string Title = "";
            public ViewMode Mode;
            public List<Vectoritem> Items = new();                                  // bản sao cố định của dự án
            public Dictionary<string, System.Drawing.Bitmap> Bitmaps = new();       // ảnh đã xử lý, khóa theo Id
        }

        private readonly List<ViewTab> viewTabs = new();
        private readonly Dictionary<ViewMode, int> viewCounter = new();
        private ViewTab? activeTab;
        private const int MaxViewTabs = 8;
        private ViewTabBar tabBar = null!;
        private bool g_invertTab;                        // đảo màu (âm bản) cho tab Edited / Laser Path
        private CheckBox chkTabInvert = null!;
        private bool updatingTabInvert;
        private bool ReadOnlyView => activeTab != null;      // đang xem = không được sửa
        private static System.Drawing.Bitmap MakeShapeIcon(string key, int size)
        {
            var bmp = new System.Drawing.Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float m = size * 0.15f, s = size - 2 * m;                 // lề và vùng vẽ
            using var pen = new Pen(Color.FromArgb(40, 40, 40), Math.Max(1.5f, size / 14f))
            { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };

            switch (key.ToLowerInvariant())
            {
                case "line": g.DrawLine(pen, m, size - m, size - m, m); break;
                case "rect": g.DrawRectangle(pen, m, m + s * 0.15f, s, s * 0.7f); break;
                case "circle": g.DrawEllipse(pen, m, m, s, s); break;
                default:
                    if (Enum.TryParse<PolygonKind>(key, true, out var kind))
                    {
                        var pts = VectorPolygon.BuildUnit(kind)
                            .Select(p => new PointF(m + p.X * s, m + p.Y * s)).ToArray();
                        g.DrawPolygon(pen, pts);
                    }
                    break;
            }
            return bmp;
        }

        private int IconSize => (int)Math.Round(24 * DeviceDpi / 96f);   // tự to ra theo độ phân giải màn hình

        // Tạo mục menu chỉ có hình, tên hiện khi rê chuột
        private ToolStripMenuItem IconItem(string tip, string key) => new ToolStripMenuItem
        {
            Image = MakeShapeIcon(key, IconSize),
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            ToolTipText = tip
        };

        // Đổi 3 mục có sẵn trong Designer (Line, Rectangle, circle) sang chỉ hiện hình
        private void ApplyShapeIcons()
        {
            void Set(ToolStripMenuItem mi, string key)
            {
                mi.Image = MakeShapeIcon(key, IconSize);
                mi.DisplayStyle = ToolStripItemDisplayStyle.Image;
                mi.ToolTipText = mi.Text;
            }
            Set(lineToolStripMenuItem1, "line");
            Set(rectangleToolStripMenuItem, "rect");
            Set(circleToolStripMenuItem, "circle");
            shapeToolStripMenuItem.DropDown.ImageScalingSize = new System.Drawing.Size(IconSize, IconSize);
        }

        public Form1()
        {
            InitializeComponent();
            // InitCursorCoords();   // đã bỏ: tọa độ chuột chỉ hiện ở thanh trạng thái (Form1_Status.cs)
            InitProjectFile();
            InitSnap();                                            // SNAP lưới + OSNAP (Form1.Snap.cs)
            InitDrawTool();
            exportGCodeToolStripMenuItem.Click += exportGCodeToolStripMenuItem_Click;

            // NumericUpDown mặc định chỉ tối đa 100 -> nới ra để hiện kích thước lớn
            numWidth.Minimum = 1; numWidth.Maximum = 10000;
            numHeight.Minimum = 1; numHeight.Maximum = 10000;

            pictureBox1.MouseWheel += Canvas_MouseWheel;
            BuildCanvasContextMenu();
            AddPolygonMenus();
            ApplyShapeIcons();
            var rulerItem = new ToolStripMenuItem("Ruler") { CheckOnClick = true, Checked = true };
            rulerItem.CheckedChanged += (s, e) => { showRuler = rulerItem.Checked; pictureBox1.Invalidate(); };
            viewToolStripMenuItem.DropDownItems.Add(rulerItem);
            Grid.Checked = true;                                   // hiện dấu tick cho Grid cho đồng bộ
            // Thanh tab ở chân vùng làm việc
            tabBar = new ViewTabBar { Height = (int)Math.Round(30 * DeviceDpi / 96f) };
            tabBar.TabSelected += i => SetActiveTab(i == 0 ? null : viewTabs[i - 1]);
            tabBar.AllowClose = false;                             // chỉ có 3 tab cố định: Original / Edited / Laser Path
            viewTabs.Add(new ViewTab { Mode = ViewMode.Edited, Number = 1, Title = "Đã xử lý" });
            viewTabs.Add(new ViewTab { Mode = ViewMode.LaserPath, Number = 1, Title = "Đường laser" });
            InitTabInvert();
            splitContainer2.Panel1.Controls.Add(tabBar);
            tabBar.SendToBack();
            RefreshTabBar();

            trackBarThreshold.Minimum = 0; trackBarThreshold.Maximum = 255;
            trackBarThreshold.TickFrequency = 32; trackBarThreshold.LargeChange = 16;
            trackBarThreshold.Value = g_thresholdVal;
            trackBarBlur.Minimum = 0; trackBarBlur.Maximum = 10;
            trackBarBlur.TickFrequency = 1; trackBarBlur.LargeChange = 1;
            trackBarBlur.Value = g_blurVal;
            filterTimer.Tick += FilterTimer_Tick;
            UpdateSliderLabels();
            UpdateFilterSlidersVisibility();
            // Ẩn bảng thông số lúc ban đầu khi chưa chọn hình nào
            panelProperties.Visible = false;
            panelProperties.Enabled = false;
            textpanel.Visible = false;
            textpanel.Enabled = false;

        }

        // =====================================================================
        //  XỬ LÝ ẢNH OPENCV
        // =====================================================================
        private void trackBarThreshold_Scroll(object sender, EventArgs e) => OnFilterSliderChanged();
        private void trackBarBlur_Scroll(object sender, EventArgs e) => OnFilterSliderChanged();

        private void OnFilterSliderChanged()
        {
            g_thresholdVal = trackBarThreshold.Value;
            g_blurVal = trackBarBlur.Value;
            UpdateSliderLabels();
            if (activeTab is { Mode: ViewMode.Edited } t)
            {
                t.Threshold = g_thresholdVal; t.Blur = g_blurVal;
                filterTimer.Stop(); filterTimer.Start();      // chờ 90 ms sau lần kéo cuối rồi mới xử lý lại ảnh
            }
        }

        private void FilterTimer_Tick(object? s, EventArgs e)
        {
            filterTimer.Stop();
            if (activeTab is not { Mode: ViewMode.Edited } t) return;
            BuildTabBitmaps(t);
            RefreshTabBar();
            pictureBox1.Invalidate();
        }

        private void UpdateSliderLabels()
        {
            Canny.Text = $"Threshold: {trackBarThreshold.Value}";
            Blur.Text = $"Blur: {trackBarBlur.Value}";
        }

        // 2 thanh trượt (và nhãn) chỉ hiện khi đang ở tab Edited
        private void UpdateFilterSlidersVisibility()
        {
            bool show = activeTab is { Mode: ViewMode.Edited };
            trackBarThreshold.Visible = trackBarBlur.Visible = Canny.Visible = Blur.Visible = show;
            if (secPreview != null) secPreview.Visible = show;      // khung chứa 2 thanh trượt ở cột phải
        }

        private static string TabTitle(ViewTab t)
        {
            string par = t.Items.Any(i => i is VectorImage) ? $" (T{t.Threshold}/B{t.Blur})" : "";
            return t.Mode switch
            {
                ViewMode.Original => $"Bản vẽ {t.Number}",
                ViewMode.Edited => $"Đã xử lý {t.Number}{par}",
                _ => $"Đường laser {t.Number}{par}"
            };
        }

        // =====================================================================
        //  Ô NHẬP CHỮ / KÍCH THƯỚC (có undo)
        // =====================================================================
        private void txtContent_TextChanged(object sender, EventArgs e)
        {
            if (updatingUI) return;
            if (selectedItem is VectorText txtObj && !isDraggingItem)
            {
                PushUndoCoalesced("text:" + txtObj.Id);
                txtObj.TextContent = txtContent.Text;
                pictureBox1.Invalidate();
            }
        }

        private void numFontSize_ValueChanged(object sender, EventArgs e)
        {
            if (updatingUI) return;
            if (selectedItem is VectorText txtObj)
            {
                PushUndoCoalesced("fsize:" + txtObj.Id);
                txtObj.FontSize = (float)numFontSize.Value;
                pictureBox1.Invalidate();
            }
        }
        private void originalToolStripMenuItem_Click(object sender, EventArgs e) => CreateViewTab(ViewMode.Original);
        private void editedToolStripMenuItem_Click(object sender, EventArgs e) => CreateViewTab(ViewMode.Edited);
        private void laserPathToolStripMenuItem_Click(object sender, EventArgs e) => CreateViewTab(ViewMode.LaserPath);
        private void cmbFontName_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (updatingUI) return;
            if (selectedItem is VectorText txtObj && cmbFontName.SelectedItem != null)
            {
                PushUndoCoalesced("fname:" + txtObj.Id);
                txtObj.FontName = cmbFontName.SelectedItem.ToString()!;
                pictureBox1.Invalidate();
            }
        }

        private void numericUpDown1_ValueChanged(object sender, EventArgs e)
        {
            if (updatingUI || selectedItem == null) return;
            PushUndoCoalesced("size:" + selectedItem.Id);
            selectedItem.Width = (float)numWidth.Value / MmPerPx;
            pictureBox1.Invalidate();
        }

        private void numHeight_ValueChanged(object sender, EventArgs e)
        {
            if (updatingUI || selectedItem == null) return;
            PushUndoCoalesced("size:" + selectedItem.Id);
            selectedItem.Height = (float)numHeight.Value / MmPerPx;
            pictureBox1.Invalidate();
        }

        private void UpdatePropertiesToUI()
        {
            bool isText = selectedItem is VectorText;
            bool isLine = selectedItem is VectorLine;
            bool showShape = selectedItem != null && !isText && !isLine;

            updatingUI = true;
            try
            {
                if (selectedItem != null && !isLine)
                {
                    decimal w = Math.Min(numWidth.Maximum, Math.Max(numWidth.Minimum, (decimal)(selectedItem.Width * MmPerPx)));
                    decimal h = Math.Min(numHeight.Maximum, Math.Max(numHeight.Minimum, (decimal)(selectedItem.Height * MmPerPx)));
                    if (numWidth.Value != w) numWidth.Value = w;
                    if (numHeight.Value != h) numHeight.Value = h;
                }
                if (selectedItem is VectorText t)
                {
                    if (txtContent.Text != t.TextContent) txtContent.Text = t.TextContent;
                    decimal fs = (decimal)t.FontSize;
                    if (fs >= numFontSize.Minimum && fs <= numFontSize.Maximum && numFontSize.Value != fs)
                        numFontSize.Value = fs;
                    if (cmbFontName.Text != t.FontName) cmbFontName.Text = t.FontName;
                }
            }
            finally { updatingUI = false; }

            if (panelProperties.Visible != showShape) { panelProperties.Visible = showShape; panelProperties.Enabled = showShape; }
            if (textpanel.Visible != isText) { textpanel.Visible = isText; textpanel.Enabled = isText; }
        }
        // 3 tab cố định: menu View chỉ CHUYỂN sang tab tương ứng (Original = tab chỉnh sửa, không còn tạo tab mới)
        private void CreateViewTab(ViewMode mode)
        {
            SetActiveTab(mode == ViewMode.Original ? null : viewTabs.FirstOrDefault(v => v.Mode == mode));
        }

        // Chụp lại dự án hiện tại cho tab xem + áp thông số Canny / Blur / Âm bản đang chọn
        private void RefreshViewTab(ViewTab tab)
        {
            tab.Items.Clear();
            foreach (var it in projectItems)
            {
                var c = CloneItem(it, true);
                if (c != null) tab.Items.Add(c);
            }
            tab.Threshold = g_thresholdVal; tab.Blur = g_blurVal; tab.Invert = g_invertTab;
            BuildTabBitmaps(tab);
        }

        // Ô "Đảo màu (âm bản)": được Form1_Tools.cs đặt dưới 2 thanh trượt Canny / Blur (khung này chỉ hiện ở tab Edited)
        private void InitTabInvert()
        {
            chkTabInvert = new CheckBox
            {
                Text = "Đảo màu (âm bản)",
                AutoSize = true,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(40, 40, 40),
                Font = new System.Drawing.Font("Segoe UI", 9f, FontStyle.Bold),
                Padding = new Padding(4, 2, 4, 2)
            };
            chkTabInvert.CheckedChanged += (s, e) =>
            {
                if (updatingTabInvert) return;
                g_invertTab = chkTabInvert.Checked;
                if (activeTab is { Mode: ViewMode.Edited } vt)
                {
                    vt.Invert = g_invertTab;
                    filterTimer.Stop(); filterTimer.Start();        // xử lý lại ảnh sau 90 ms
                }
            };
        }

        private void UpdateTabInvertUi()
        {
            updatingTabInvert = true;
            try { chkTabInvert.Checked = g_invertTab; }
            finally { updatingTabInvert = false; }
        }

        private void BuildTabBitmaps(ViewTab tab)
        {
            foreach (var b in tab.Bitmaps.Values) b.Dispose();      // giải phóng bản cũ khi xử lý lại
            tab.Bitmaps.Clear();
            if (tab.Mode == ViewMode.Original) return;
            foreach (var it in tab.Items)
                if (it is VectorImage vi && vi.LoadedImage != null)
                    tab.Bitmaps[vi.Id] = ProcessImage(vi.LoadedImage, tab.Mode, tab.Threshold, tab.Blur, tab.Invert);
        }

        // Xám -> Blur -> Threshold. Edited: giữ ảnh đen/trắng. Laser: lấy biên của ảnh đen/trắng đó
        private System.Drawing.Bitmap ProcessImage(System.Drawing.Image src, ViewMode mode, int threshold, int blur, bool invert = false)
        {
            float sc = Math.Min(1f, 2000f / Math.Max(src.Width, src.Height));
            int w = Math.Max(1, (int)(src.Width * sc)), h = Math.Max(1, (int)(src.Height * sc));
            using var flat = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(flat))
            {
                g.Clear(Color.White);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, w, h);
            }

            using var bgr = OpenCvSharp.Extensions.BitmapConverter.ToMat(flat);
            using var gray = new Mat();
            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

            using var smooth = new Mat();
            if (blur > 0)
            {
                int k = 2 * blur + 1;                       // mức 1,2,3... -> kernel 3,5,7...
                Cv2.GaussianBlur(gray, smooth, new OpenCvSharp.Size(k, k), 0);
            }
            else gray.CopyTo(smooth);

            using var bin = new Mat();
            Cv2.Threshold(smooth, bin, threshold, 255, ThresholdTypes.Binary);
            if (invert) Cv2.BitwiseNot(bin, bin);               // âm bản: đen <-> trắng

            using var result = new Mat();
            if (mode == ViewMode.Edited) bin.CopyTo(result);
            else
            {
                using var edges = new Mat();
                Cv2.Canny(bin, edges, 50, 150);

                // Nền trắng, nét biên đỏ (cùng màu Crimson với đường viền hình và chữ)
                using var red = new Mat(edges.Size(), MatType.CV_8UC3, new Scalar(255, 255, 255));
                red.SetTo(new Scalar(60, 20, 220), edges);       // OpenCV dùng thứ tự BGR, tức là RGB (220, 20, 60)
                red.CopyTo(result);
            }
            return OpenCvSharp.Extensions.BitmapConverter.ToBitmap(result);
        }

        private void SetActiveTab(ViewTab? tab)
        {
            activeTab = tab;
            CancelInteractions();
            if (tab != null)
            {
                foreach (var o in projectItems) o.IsSelected = false;
                selectedItem = null!;
                UpdatePropertiesToUI();
            }
            toolStripMenuItem1.Enabled = tab == null;

            if (tab != null) RefreshViewTab(tab);                // tab xem luôn phản ánh dự án MỚI NHẤT ở tab Original
            UpdateTabInvertUi();
            UpdateFilterSlidersVisibility();
            RefreshTabBar();
            pictureBox1.Invalidate();
        }
        private void RefreshTabBar()
        {
            var titles = new List<string> { "Bản vẽ", "Đã xử lý", "Đường laser" };
            tabBar.SetTabs(titles, activeTab == null ? 0 : viewTabs.IndexOf(activeTab) + 1);
        }

        private void CloseViewTab(int index)
        {
            var t = viewTabs[index - 1];
            bool wasActive = t == activeTab;
            viewTabs.Remove(t);
            foreach (var b in t.Bitmaps.Values) b.Dispose();
            t.Bitmaps.Clear();
            if (wasActive) SetActiveTab(null);                 // xóa tab đang xem -> về tab Edit
            else RefreshTabBar();
        }

        // ----- Vẽ nội dung tab -----
        private void DrawViewTab(Graphics g, ViewTab tab)
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            foreach (var it in tab.Items)
            {
                if (it.Hidden) continue;
                if (it is VectorImage vi && tab.Bitmaps.TryGetValue(vi.Id, out var bmp))
                    g.DrawImage(bmp, vi.X, vi.Y, vi.Width, vi.Height);
                else if (tab.Mode == ViewMode.LaserPath && it is not VectorImage)
                {
                    // đường tô kín = đúng các đường laser sẽ chạy; không tô được thì vẽ nét viền như cũ
                    if (!DrawLaserFill(g, it))
                        DrawItem(g, it, (gg, ii) => DrawLaserOutline(gg, ii));
                }
                else
                {
                    if (!DrawFillPreview(g, it)) DrawItem(g, it);
                }
            }
        }

        private void DrawLaserOutline(Graphics g, Vectoritem it)
        {
            using var pen = new Pen(Color.Crimson, 1.5f / zoom);          // nét mảnh, không dày lên khi zoom
            switch (it)
            {
                case VectorRectangle: g.DrawRectangle(pen, it.X, it.Y, it.Width, it.Height); break;
                case VectorEllipse: g.DrawEllipse(pen, it.X, it.Y, it.Width, it.Height); break;
                case VectorLine l: g.DrawLine(pen, l.X, l.Y, l.X2, l.Y2); break;
                case VectorPolygon p: g.DrawPolygon(pen, p.GetPoints()); break;
                case VectorPathItem pi: DrawPathItemLaser(g, pen, pi); break;
                case VectorText t: DrawTextOutline(g, pen, t); break;
            }
        }

        private static void DrawPathItemLaser(Graphics g, Pen pen, VectorPathItem pi)
        {
            foreach (var sp in pi.SubPaths)
            {
                if (sp.Points.Count < 2) continue;
                var pts = sp.Points.ToArray();
                if (sp.IsClosed && pts.Length >= 3) g.DrawPolygon(pen, pts);
                else g.DrawLines(pen, pts);
            }
        }

        // Chữ chỉ vẽ phần viền (đúng cách GCodeCompiler khắc chữ)
        private static void DrawTextOutline(Graphics g, Pen pen, VectorText t)
        {
            if (string.IsNullOrEmpty(t.TextContent)) return;
            if (t.HasCustomPath)
            {
                var subPaths = HersheyFont.BuildTextSubPaths(t, MmPerPx, 0.25f);
                foreach (var sp in subPaths)
                {
                    if (sp.Points.Count < 2) continue;
                    var pts = sp.Points.ToArray();
                    if (sp.IsClosed && pts.Length >= 3) g.DrawPolygon(pen, pts);
                    else g.DrawLines(pen, pts);
                }
                return;
            }
            FontFamily fam;
            try { fam = new FontFamily(t.FontName); } catch { fam = new FontFamily("Arial"); }
            using (fam)
            using (var gp = new System.Drawing.Drawing2D.GraphicsPath())
            {
                FontStyle style = fam.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold;
                gp.AddString(t.TextContent, fam, (int)style, t.FontSize * 96f / 72f,
                             new PointF(t.X, t.Y), StringFormat.GenericDefault);
                g.DrawPath(pen, gp);
            }
        }
        private void Form1_Load(object sender, EventArgs e)
        {
            foreach (FontFamily font in FontFamily.Families)
            {
                cmbFontName.Items.Add(font.Name);
            }
            if (cmbFontName.Items.Count > 0)
                cmbFontName.SelectedIndex = 0;
        }

        // =====================================================================
        //  ZOOM / PAN
        // =====================================================================
        private PointF ScreenToWorld(Point p) => new PointF((p.X - pan.X) / zoom, (p.Y - pan.Y) / zoom);

        private PointF ViewCenter() => ScreenToWorld(new Point(pictureBox1.Width / 2, pictureBox1.Height / 2));

        private void Canvas_MouseWheel(object? s, MouseEventArgs e)
        {
            float old = zoom;
            zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.15f : 1 / 1.15f), 0.1f, 20f);
            pan.X = e.X - (e.X - pan.X) * zoom / old;   // zoom quanh con trỏ
            pan.Y = e.Y - (e.Y - pan.Y) * zoom / old;
            pictureBox1.Invalidate();
        }

        // =====================================================================
        //  ĐỔI KÍCH THƯỚC BẰNG 8 ĐIỂM
        // =====================================================================
        // Cho phép đổi kích thước cả hình đã xoay, ảnh và chữ (chữ: kéo góc = đổi cỡ chữ)
        private bool CanResize(Vectoritem it) =>
            it is VectorRectangle || it is VectorEllipse || it is VectorImage || it is VectorPolygon || it is VectorText || it is VectorPathItem;

        // Hướng (trong khung gốc) của từng điểm kéo: 0=TL 1=T 2=TR 3=R 4=BR 5=B 6=BL 7=L
        private static readonly (int X, int Y)[] HandleDir =
            { (-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0) };

        // 8 điểm kéo nằm trên khung ĐÃ XOAY (toạ độ thế giới)
        private PointF[] GetHandlePoints(Vectoritem it) =>
            HandlePointsOf(new RectangleF(it.X, it.Y, it.Width, it.Height), it.Rotation);

        private static PointF[] HandlePointsOf(RectangleF rc, float rot)
        {
            float l = rc.Left, t = rc.Top, r = rc.Right, b = rc.Bottom;
            float cx = (l + r) / 2, cy = (t + b) / 2;
            var pts = new[] {
                new PointF(l, t), new PointF(cx, t), new PointF(r, t), new PointF(r, cy),
                new PointF(r, b), new PointF(cx, b), new PointF(l, b), new PointF(l, cy) };
            if (rot != 0f)
                for (int i = 0; i < pts.Length; i++) pts[i] = RotateAround(pts[i], cx, cy, rot);
            return pts;
        }

        private int HitHandle(PointF w)
        {
            if (selectedItem == null || !CanResize(selectedItem) || MultiSelected) return -1;
            var pts = GetHandlePoints(selectedItem);
            bool onlyCorners = selectedItem is VectorText;       // chữ: chỉ kéo 4 góc (đổi cỡ chữ)
            float tol = 7f / zoom;
            for (int i = 0; i < pts.Length; i++)
            {
                if (onlyCorners && i % 2 == 1) continue;
                if (Math.Abs(w.X - pts[i].X) <= tol && Math.Abs(w.Y - pts[i].Y) <= tol) return i;
            }
            return -1;
        }

        private int HitLineHandle(PointF w)
        {
            if (selectedItem is not VectorLine l || MultiSelected) return -1;
            float tol = 7f / zoom;
            if (Math.Abs(w.X - l.X) <= tol && Math.Abs(w.Y - l.Y) <= tol) return 0;
            if (Math.Abs(w.X - l.X2) <= tol && Math.Abs(w.Y - l.Y2) <= tol) return 1;
            return -1;
        }

        private static Cursor HandleCursor(int h) => h switch
        {
            0 or 4 => Cursors.SizeNWSE,
            2 or 6 => Cursors.SizeNESW,
            1 or 5 => Cursors.SizeNS,
            3 or 7 => Cursors.SizeWE,
            _ => Cursors.Default
        };

        // Con trỏ theo hướng THẬT của điểm kéo trên màn hình (có tính góc xoay)
        private Cursor HandleCursorFor(int h)
        {
            if (h < 0) return Cursors.Default;
            float rot = selectedItem?.Rotation ?? 0f;
            var (dx, dy) = HandleDir[h];
            double a = Math.Atan2(dy, dx) * 180.0 / Math.PI + rot;
            a = ((a % 180.0) + 180.0) % 180.0;
            int q = (int)Math.Round(a / 45.0) % 4;               // 0: ngang, 1: chéo \, 2: dọc, 3: chéo /
            return q switch
            {
                0 => Cursors.SizeWE,
                1 => Cursors.SizeNWSE,
                2 => Cursors.SizeNS,
                _ => Cursors.SizeNESW
            };
        }

        // Đo kích thước chữ giống hệt lúc vẽ (VectorText.Draw dùng MeasureString)
        private SizeF MeasureText(VectorText t)
        {
            if (t.HasCustomPath)
            {
                HersheyFont.BuildTextSubPaths(t, MmPerPx, 0.3f);
                return new SizeF(t.Width, t.Height);
            }
            using var g = pictureBox1.CreateGraphics();
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            using var f = t.MakeFont();
            return g.MeasureString(t.TextContent, f);
        }

        // w = vị trí chuột (toạ độ thế giới, có thể đã snap).
        // Cách làm: đưa chuột về khung CHƯA XOAY của hình lúc bắt đầu kéo, tính khung mới như hình thẳng,
        // rồi đặt lại sao cho ĐIỂM NEO (góc / cạnh đối diện) đứng yên trên màn hình.
        private void ApplyResize(PointF w)
        {
            var it = selectedItem;
            if (it == null) return;

            float rot = resizeRot;
            PointF c0 = resizeCenter;
            PointF m = rot != 0f ? RotateAround(w, c0.X, c0.Y, -rot) : w;
            PointF m0 = rot != 0f ? RotateAround(resizeMouseStart, c0.X, c0.Y, -rot) : resizeMouseStart;
            float dx = m.X - m0.X, dy = m.Y - m0.Y;

            float sw = resizeStart.Width, sh = resizeStart.Height;
            float scx = resizeStart.Left + sw / 2f, scy = resizeStart.Top + sh / 2f;
            int h = resizeHandle;
            bool mL = h == 0 || h == 6 || h == 7, mR = h == 2 || h == 3 || h == 4;
            bool mT = h == 0 || h == 1 || h == 2, mB = h == 4 || h == 5 || h == 6;
            bool corner = h % 2 == 0;
            var txt = it as VectorText;

            // điểm neo (đứng yên) trong khung gốc: góc / cạnh đối diện
            float ax = mL ? resizeStart.Right : mR ? resizeStart.Left : scx;
            float ay = mT ? resizeStart.Bottom : mB ? resizeStart.Top : scy;

            // Chữ luôn co giãn đều (đổi cỡ chữ). Hình khác: khóa tỉ lệ (nút xích) hoặc giữ Shift.
            bool keep = txt != null || IsRatioLocked(it) || (ModifierKeys & Keys.Shift) != 0;
            if (sw < 0.001f || sh < 0.001f) keep = false;

            float l, t, r, b, nw, nh;
            if (keep)
            {
                float scale;
                if (corner)
                {
                    float cxp = (mL ? resizeStart.Left : resizeStart.Right) + dx;
                    float cyp = (mT ? resizeStart.Top : resizeStart.Bottom) + dy;
                    float wRaw = mL ? ax - cxp : cxp - ax;
                    float hRaw = mT ? ay - cyp : cyp - ay;
                    scale = Math.Max(wRaw / sw, hRaw / sh);
                }
                else if (h == 3 || h == 7)                       // cạnh phải / trái: cao tự theo, giữ nguyên tâm dọc
                {
                    float edge = (mL ? resizeStart.Left : resizeStart.Right) + dx;
                    scale = (mL ? ax - edge : edge - ax) / sw;
                }
                else                                             // cạnh trên / dưới: rộng tự theo, giữ nguyên tâm ngang
                {
                    float edge = (mT ? resizeStart.Top : resizeStart.Bottom) + dy;
                    scale = (mT ? ay - edge : edge - ay) / sh;
                }
                scale = Math.Max(scale, MinSize / Math.Min(sw, sh));
                nw = sw * scale; nh = sh * scale;

                if (txt != null)
                {
                    txt.FontSize = Math.Clamp(resizeFontStart * scale, 1f, 1000f);
                    SizeF sz = MeasureText(txt);
                    nw = sz.Width; nh = sz.Height;
                }
                l = mL ? ax - nw : mR ? ax : scx - nw / 2f;
                t = mT ? ay - nh : mB ? ay : scy - nh / 2f;
                r = l + nw; b = t + nh;
            }
            else
            {
                l = resizeStart.Left; t = resizeStart.Top; r = resizeStart.Right; b = resizeStart.Bottom;
                if (mL) l += dx;
                if (mR) r += dx;
                if (mT) t += dy;
                if (mB) b += dy;
                if (r - l < MinSize) { if (mL) l = r - MinSize; else r = l + MinSize; }
                if (b - t < MinSize) { if (mT) t = b - MinSize; else b = t + MinSize; }
                nw = r - l; nh = b - t;
            }

            // Đặt lại: tâm mới (thế giới) = điểm neo (thế giới) - R * (neo - tâm mới) trong khung gốc
            float vx = ax - (l + r) / 2f, vy = ay - (t + b) / 2f;
            double rad = rot * Math.PI / 180.0, cs = Math.Cos(rad), sn = Math.Sin(rad);
            PointF aw = rot != 0f ? RotateAround(new PointF(ax, ay), c0.X, c0.Y, rot) : new PointF(ax, ay);
            float cwx = aw.X - (float)(vx * cs - vy * sn);
            float cwy = aw.Y - (float)(vx * sn + vy * cs);

            float newX = cwx - nw / 2f, newY = cwy - nh / 2f;
            if (it is VectorPathItem pi)
            {
                pi.Scale(nw, nh, newX, newY);
            }
            else
            {
                it.Width = nw; it.Height = nh;
                it.X = newX; it.Y = newY;
            }

            updatingUI = true;   // tránh numeric bắn ngược lại gán Width/Height
            try
            {
                numWidth.Value = Math.Min(numWidth.Maximum, Math.Max(numWidth.Minimum, (decimal)(it.Width * MmPerPx)));
                numHeight.Value = Math.Min(numHeight.Maximum, Math.Max(numHeight.Minimum, (decimal)(it.Height * MmPerPx)));
            }
            finally { updatingUI = false; }
        }

        // =====================================================================
        //  VẼ CANVAS
        // =====================================================================
        private void pictureBox1_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            g.TranslateTransform(pan.X, pan.Y);
            g.ScaleTransform(zoom, zoom);

            if (showGrid)
            {
                int gs = 20;
                var tl = ScreenToWorld(Point.Empty);
                var br = ScreenToWorld(new Point(pictureBox1.Width, pictureBox1.Height));
                int x0 = (int)Math.Floor(tl.X / gs) * gs, y0 = (int)Math.Floor(tl.Y / gs) * gs;
                using (var gridPen = new Pen(Color.FromArgb(225, 225, 225), 1f / zoom))
                {
                    for (int x = x0; x <= br.X; x += gs) g.DrawLine(gridPen, x, tl.Y, x, br.Y);
                    for (int y = y0; y <= br.Y; y += gs) g.DrawLine(gridPen, tl.X, y, br.X, y);
                }
            }

            DrawSnapDots(g);                                       // chấm giao điểm lưới snap
            if (activeTab != null) DrawViewTab(g, activeTab);
            else foreach (var item in projectItems) { if (item.Hidden) continue; if (!DrawFillPreview(g, item)) DrawItem(g, item); }

            // 8 điểm kéo (kích thước cố định trên màn hình dù zoom bao nhiêu) - nằm trên khung ĐÃ XOAY
            if (selectedItem != null && CanResize(selectedItem) && !MultiSelected)
            {
                float hs = 8f / zoom;
                var hpts = GetHandlePoints(selectedItem);
                if (selectedItem.Rotation != 0f)           // viền khung nghiêng để thấy rõ hình đang xoay
                {
                    using var fp = new Pen(Color.FromArgb(150, 0, 120, 215), 1f / zoom)
                    { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                    g.DrawPolygon(fp, new[] { hpts[0], hpts[2], hpts[4], hpts[6] });
                }
                bool onlyCorners = selectedItem is VectorText;
                using var hp = new Pen(Color.Blue, 1f / zoom);
                for (int i = 0; i < hpts.Length; i++)
                {
                    if (onlyCorners && i % 2 == 1) continue;
                    var rc = new RectangleF(hpts[i].X - hs / 2, hpts[i].Y - hs / 2, hs, hs);
                    g.FillRectangle(Brushes.White, rc);
                    g.DrawRectangle(hp, rc.X, rc.Y, rc.Width, rc.Height);
                }
            }

            // 2 ô vuông ở đầu mút đường thẳng
            if (selectedItem is VectorLine sl && !MultiSelected)
            {
                float hs = 8f / zoom;
                using var hp = new Pen(Color.Blue, 1f / zoom);
                foreach (var p in new[] { new PointF(sl.X, sl.Y), new PointF(sl.X2, sl.Y2) })
                {
                    var rc = new RectangleF(p.X - hs / 2, p.Y - hs / 2, hs, hs);
                    g.FillRectangle(Brushes.White, rc);
                    g.DrawRectangle(hp, rc.X, rc.Y, rc.Width, rc.Height);
                }
            }

            DrawSnapMarker(g);                                     // dấu điểm snap

            // Vẽ ngoài transform: thước + chữ báo chế độ xem
            g.ResetTransform();
            if (showRuler) DrawRulers(g);
            if (activeTab != null)
            {
                using var viewFont = new System.Drawing.Font("Segoe UI", 9f, FontStyle.Bold);
                string viewMsg = "Chế độ xem (chỉ đọc): " + activeTab.Title;
                var viewSz = g.MeasureString(viewMsg, viewFont);
                using var viewBg = new SolidBrush(Color.FromArgb(225, 255, 244, 200));
                g.FillRectangle(viewBg, Inset + 8, Inset + 8, viewSz.Width + 8, viewSz.Height + 4);
                g.DrawString(viewMsg, viewFont, Brushes.SaddleBrown, Inset + 12, Inset + 10);
            }
            // Nhãn % zoom và tọa độ chuột không vẽ trên canvas nữa: chỉ hiện ở thanh trạng thái dưới cùng
            DrawSnapOverlay(g);                                    // nhãn SNAP / kích thước khi kéo
        }
        private void ResetView()
        {
            zoom = 1f;
            pan = new PointF(RulerSize + 10, RulerSize + 10);
            pictureBox1.Invalidate();
        }
        private void DrawRulers(Graphics g)
        {
            float ppm = zoom / MmPerPx;                          // pixel màn hình trên 1 mm
            float[] steps = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000 };
            float major = steps[steps.Length - 1];
            foreach (float s in steps) if (s * ppm >= 60f) { major = s; break; }   // vạch lớn cách nhau >= 60 px
            float minor = major / 5f;
            bool drawMinor = minor * ppm >= 4f;

            using var back = new SolidBrush(Color.FromArgb(245, 245, 245));
            using var tick = new Pen(Color.FromArgb(120, 120, 120));
            using var border = new Pen(Color.FromArgb(190, 190, 190));
            using var font = new System.Drawing.Font("Segoe UI", 7f);

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            g.FillRectangle(back, 0, 0, pictureBox1.Width, RulerSize);
            g.FillRectangle(back, 0, 0, RulerSize, pictureBox1.Height);
            g.DrawLine(border, 0, RulerSize, pictureBox1.Width, RulerSize);
            g.DrawLine(border, RulerSize, 0, RulerSize, pictureBox1.Height);

            // thước ngang
            int k0 = (int)Math.Floor((RulerSize - pan.X) / ppm / minor);
            int k1 = (int)Math.Ceiling((pictureBox1.Width - pan.X) / ppm / minor);
            for (int k = k0; k <= k1; k++)
            {
                float mm = k * minor;
                int x = (int)Math.Round(pan.X + mm * ppm);
                if (x < RulerSize) continue;
                bool isMajor = k % 5 == 0;
                if (!isMajor && !drawMinor) continue;
                g.DrawLine(tick, x, RulerSize - (isMajor ? 12 : 5), x, RulerSize);
                if (isMajor) g.DrawString(Math.Round((double)mm, 2).ToString("0.##"), font, Brushes.DimGray, x + 2, 1);
            }

            // thước dọc
            int j0 = (int)Math.Floor((RulerSize - pan.Y) / ppm / minor);
            int j1 = (int)Math.Ceiling((pictureBox1.Height - pan.Y) / ppm / minor);
            for (int k = j0; k <= j1; k++)
            {
                float mm = k * minor;
                int y = (int)Math.Round(pan.Y + mm * ppm);
                if (y < RulerSize) continue;
                bool isMajor = k % 5 == 0;
                if (!isMajor && !drawMinor) continue;
                g.DrawLine(tick, RulerSize - (isMajor ? 12 : 5), y, RulerSize, y);
                if (isMajor) g.DrawString(Math.Round((double)mm, 2).ToString("0.##"), font, Brushes.DimGray, 1, y + 1);
            }

            g.FillRectangle(back, 0, 0, RulerSize, RulerSize);   // ô góc
            g.DrawString("mm", font, Brushes.DimGray, 2, 6);
        }
        // =====================================================================
        //  CHUỘT
        // =====================================================================
        private void pictureBox1_MouseDown(object sender, MouseEventArgs e)
        {
            if (showRuler && (e.X < RulerSize || e.Y < RulerSize)) return;
            if (ReadOnlyView)
            {
                if (e.Button == MouseButtons.Left && zoomBadge.Contains(e.Location)) { ResetView(); return; }
                if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle)
                { isPanning = true; panStart = e.Location; }
                return;
            }
            if (DrawToolMouseDown(e)) return; // đang dùng công cụ vẽ
            if (e.Button == MouseButtons.Right)
            {
                contextWorldPos = ScreenToWorld(e.Location);
                var prevSel = selectedItem;
                SelectItemAt(contextWorldPos);
                if (prevSel != selectedItem) UpdatePropertiesToUI();
                pictureBox1.Invalidate();
                return;
            }
            if (e.Button == MouseButtons.Middle)
            { isPanning = true; panStart = e.Location; return; }
            if (e.Button != MouseButtons.Left) return;

            // Bấm vào nhãn % = về 100%
            if (zoomBadge.Contains(e.Location))
            { zoom = 1f; pan = PointF.Empty; pictureBox1.Invalidate(); return; }

            PointF w = ScreenToWorld(e.Location);

            // 1) Ô vuông đầu mút của đường thẳng
            int lh = HitLineHandle(w);
            if (lh >= 0)
            {
                isDraggingItem = false;
                lineHandle = lh;
                pendingUndo = TakeSnapshot();
                return;
            }

            // 2) 8 điểm kéo của hình/ảnh (phải đứng TRƯỚC đoạn chọn/kéo hình)
            int h = HitHandle(w);
            if (h >= 0)
            {
                isDraggingItem = false;
                resizeHandle = h;
                resizeStart = new RectangleF(selectedItem.X, selectedItem.Y, selectedItem.Width, selectedItem.Height);
                resizeRot = selectedItem.Rotation;
                resizeCenter = new PointF(selectedItem.X + selectedItem.Width / 2f, selectedItem.Y + selectedItem.Height / 2f);
                resizeFontStart = selectedItem is VectorText rt ? rt.FontSize : 0f;
                resizeMouseStart = w;
                pendingUndo = TakeSnapshot();
                return;
            }

            // 3) Chọn / kéo hình
            var prev = selectedItem;
            SelectItemAt(w);
            if (selectedItem != null)
            {
                isDraggingItem = true;
                dragOffset = new PointF(w.X - selectedItem.X, w.Y - selectedItem.Y);
                pendingUndo = TakeSnapshot();
            }
            if (selectedItem != prev) UpdatePropertiesToUI();
            pictureBox1.Invalidate();
        }

        private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            if (DrawToolMouseMove(e)) return;
            if (isPanning)
            {
                pan.X += e.X - panStart.X; pan.Y += e.Y - panStart.Y;
                panStart = e.Location;
                pictureBox1.Invalidate();
                return;
            }

            PointF w = ScreenToWorld(e.Location);

            if (lineHandle >= 0 && selectedItem is VectorLine ln)
            {
                CommitPendingUndo();
                if ((ModifierKeys & Keys.Shift) != 0)      // Shift: khóa góc theo bội số 45°
                {
                    float ox = lineHandle == 0 ? ln.X2 : ln.X, oy = lineHandle == 0 ? ln.Y2 : ln.Y;
                    double ang = Math.Atan2(w.Y - oy, w.X - ox);
                    double snapped = Math.Round(ang / (Math.PI / 4)) * (Math.PI / 4);
                    double len = Math.Sqrt((w.X - ox) * (w.X - ox) + (w.Y - oy) * (w.Y - oy));
                    if (snGridOn) len = Math.Round(len / SnStepPx) * SnStepPx;   // Shift: dài theo bội số của bước snap
                    w = new PointF(ox + (float)(len * Math.Cos(snapped)), oy + (float)(len * Math.Sin(snapped)));
                    snMarker = SnNone;
                }
                else w = SnapLinePoint(ln, w);                                     // snap lưới / OSNAP
                if (lineHandle == 0) { ln.X = w.X; ln.Y = w.Y; } else { ln.X2 = w.X; ln.Y2 = w.Y; }
                SnLineInfo(ln, e.Location);
                pictureBox1.Invalidate();
                return;
            }

            if (resizeHandle >= 0)                          // đang đổi kích thước -> không bao giờ kéo hình
            {
                CommitPendingUndo();
                ApplyResize(SnapResizeMouse(w));
                SnSizeInfo(selectedItem, e.Location);
                pictureBox1.Invalidate();
                return;
            }

            if (isDraggingItem && resizeHandle < 0 && lineHandle < 0 && selectedItem != null)
            {
                float nx = w.X - dragOffset.X, ny = w.Y - dragOffset.Y;
                var sp = SnapMoveTopLeft(selectedItem, nx, ny);      // snap lưới / OSNAP
                nx = sp.X; ny = sp.Y;
                if (nx != selectedItem.X || ny != selectedItem.Y)
                {
                    CommitPendingUndo();
                    selectedItem.MoveTo(nx, ny);
                    SnMoveInfo(selectedItem, e.Location);
                    pictureBox1.Invalidate();
                }
                return;
            }

            pictureBox1.Cursor = HitLineHandle(w) >= 0 ? Cursors.Cross : HandleCursorFor(HitHandle(w));
        }

        private void pictureBox1_MouseUp(object sender, MouseEventArgs e)
        {
            isPanning = false; isDraggingItem = false;
            resizeHandle = -1; lineHandle = -1;
            pendingUndo = null;
        }

        private void SelectItemAt(PointF w)
        {
            VectorLine.HitTolerance = 6f / zoom;
            selectedItem = null!;
            foreach (var o in projectItems) o.IsSelected = false;
            for (int i = projectItems.Count - 1; i >= 0; i--)
            {
                if (HitTestItem(projectItems[i], w))
                {
                    selectedItem = projectItems[i];
                    selectedItem.IsSelected = true;
                    ExpandGroup(selectedItem);       // hình thuộc nhóm -> chọn cả nhóm
                    break;
                }
            }
        }

        // =====================================================================
        //  THÊM / XÓA / COPY / PASTE
        // =====================================================================
        private void AddItem(Vectoritem it)
        {
            SnapNewItem(it);                 // góc trên-trái nhảy vào điểm lưới
            if (it.LayerId < 0) AssignToLayer(it, curLayer);   // đối tượng mới -> lớp đang chọn (bản dán / sao chép giữ lớp cũ)
            PushUndo();
            foreach (var o in projectItems) o.IsSelected = false;
            it.IsSelected = true;
            selectedItem = it;
            projectItems.Add(it);
            UpdatePropertiesToUI();
            pictureBox1.Invalidate();
        }

        private void AddRectangleAt(PointF p) => BeginDrawTool(DrawTool.Rectangle);
        private void AddEllipseAt(PointF p) => BeginDrawTool(DrawTool.Ellipse);
        private static readonly (string Name, PolygonKind Kind)[] PolygonList =
{
    ("Diamond", PolygonKind.Diamond), ("Pentagon", PolygonKind.Pentagon),
    ("Hexagon", PolygonKind.Hexagon), ("Star", PolygonKind.Star)};
        private void AddPolygonAt(PolygonKind kind, PointF p) => BeginDrawTool(DrawTool.Polygon, kind);
        private void AddLineAt(PointF p) => BeginDrawTool(DrawTool.Line);

        private void AddTextAt(PointF p) => BeginDrawTool(DrawTool.Text);

        private void AddImageAt(PointF p)
        {
            using var ofd = new OpenFileDialog { Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*" };
            if (ofd.ShowDialog() != DialogResult.OK) return;

            System.Drawing.Image img;
            using (var tmp = new System.Drawing.Bitmap(ofd.FileName))
                img = new System.Drawing.Bitmap(tmp);          // copy ra để không khóa file

            float sc = Math.Min(300f / img.Width, 300f / img.Height);
            AddItem(new VectorImage(img) { X = p.X, Y = p.Y, Width = img.Width * sc, Height = img.Height * sc });
        }

        private Vectoritem? CloneItem(Vectoritem src, bool snapshot = false)
        {
            Vectoritem? c = src switch
            {
                VectorRectangle => new VectorRectangle(),
                VectorEllipse => new VectorEllipse(),
                VectorLine l => new VectorLine { X2 = l.X2, Y2 = l.Y2 },
                VectorPolygon pg => new VectorPolygon(pg.Kind),
                VectorPathItem pi => new VectorPathItem
                {
                    SubPaths = pi.SubPaths.Select(sp => new VectorPathItem.SubPath
                    {
                        Points = new List<PointF>(sp.Points),
                        IsClosed = sp.IsClosed
                    }).ToList()
                },
                VectorText t => new VectorText { TextContent = t.TextContent, FontName = t.FontName, FontSize = t.FontSize },
                VectorBarcode bc when bc.LoadedImage != null =>          // phải đứng TRƯỚC VectorImage (lớp cha)
                    new VectorBarcode(snapshot ? bc.LoadedImage : new System.Drawing.Bitmap(bc.LoadedImage),
                                      bc.Content, bc.Kind, bc.Quiet, bc.ShowText),
                VectorImage im when im.LoadedImage != null =>
                    new VectorImage(snapshot ? im.LoadedImage : new System.Drawing.Bitmap(im.LoadedImage)),
                _ => null
            };
            if (c == null) return null;
            c.Name = src.Name;
            c.X = src.X; c.Y = src.Y; c.Width = src.Width; c.Height = src.Height;
            c.LaserPower = src.LaserPower; c.LaserSpeed = src.LaserSpeed; c.Passes = src.Passes;
            CopyItemExtras(src, c);
            c.IsSelected = false;
            if (snapshot) { c.Id = src.Id; c.GroupId = src.GroupId; }    // undo giữ nhóm; sao chép thì tách nhóm (Form1_Select.cs tự gán lại)
            return c;
        }

        private void CopySelected()
        {
            if (MultiSelected) { CopyGroup(); return; }
            clipboardGroup.Clear();
            if (selectedItem != null) clipboardItem = CloneItem(selectedItem);
        }

        private void DeleteSelected()
        {
            if (selectedItem == null && !projectItems.Any(i => i.IsSelected)) return;
            PushUndo();
            projectItems.RemoveAll(i => i.IsSelected || i == selectedItem);   // xóa cả nhóm đang chọn
            selectedItem = null!;
            UpdatePropertiesToUI();
            pictureBox1.Invalidate();
        }

        private void CutSelected() { CopySelected(); DeleteSelected(); }

        private void PasteAtCursor()
        {
            if (PasteGroupAt(contextWorldPos)) return;       // đã sao chép nhiều hình
            if (clipboardItem == null) return;
            var c = CloneItem(clipboardItem);
            if (c == null) return;
            c.MoveTo(contextWorldPos.X, contextWorldPos.Y);
            AddItem(c);
        }

        // =====================================================================
        //  UNDO / REDO
        // =====================================================================
        private UndoState TakeSnapshot()
        {
            var s = new UndoState();
            foreach (var it in projectItems)
            {
                var c = CloneItem(it, true);
                if (c == null) continue;
                if (it == selectedItem) s.Selected = s.Items.Count;
                s.Items.Add(c);
            }
            return s;
        }

        private void PushUndo()
        {
            undoStack.Add(TakeSnapshot());
            if (undoStack.Count > MaxUndo) undoStack.RemoveAt(0);
            redoStack.Clear();
            lastCoalesceKey = null;
        }

        // Gộp các thay đổi liên tiếp (gõ chữ, bấm mũi tên numeric) thành 1 bước undo
        private void PushUndoCoalesced(string key)
        {
            var now = DateTime.UtcNow;
            if (lastCoalesceKey == key && (now - lastCoalesceTime).TotalMilliseconds < 1000)
            { lastCoalesceTime = now; return; }
            PushUndo();
            lastCoalesceKey = key;
            lastCoalesceTime = now;
        }

        // Dùng cho kéo / đổi kích thước: chỉ lưu khi chuột thật sự di chuyển
        private void CommitPendingUndo()
        {
            if (pendingUndo == null) return;
            undoStack.Add(pendingUndo);
            if (undoStack.Count > MaxUndo) undoStack.RemoveAt(0);
            redoStack.Clear();
            lastCoalesceKey = null;
            pendingUndo = null;
        }

        private void CancelInteractions()
        {
            isDraggingItem = false; isPanning = false;
            resizeHandle = -1; lineHandle = -1; pendingUndo = null;
        }

        private void RestoreState(UndoState st)
        {
            projectItems.Clear();
            projectItems.AddRange(st.Items);
            foreach (var it in projectItems) it.IsSelected = false;
            selectedItem = null!;
            if (st.Selected >= 0 && st.Selected < projectItems.Count)
            { selectedItem = projectItems[st.Selected]; selectedItem.IsSelected = true; }
            SyncLayersFromItems();          // Hoàn tác / Làm lại có thể đổi thông số đối tượng -> lớp theo đó
            UpdatePropertiesToUI();
            pictureBox1.Invalidate();
        }

        private void Undo()
        {
            if (undoStack.Count == 0) return;
            CancelInteractions();
            redoStack.Add(TakeSnapshot());
            var st = undoStack[^1]; undoStack.RemoveAt(undoStack.Count - 1);
            RestoreState(st);
            lastCoalesceKey = null;
        }

        private void Redo()
        {
            if (redoStack.Count == 0) return;
            CancelInteractions();
            undoStack.Add(TakeSnapshot());
            var st = redoStack[^1]; redoStack.RemoveAt(redoStack.Count - 1);
            RestoreState(st);
            lastCoalesceKey = null;
        }

        // Đang gõ trong ô nhập thì để ô đó tự xử lý Ctrl+Z / Ctrl+C...
        private bool IsTextEditing()
        {
            Control? c = ActiveControl;
            while (c is ContainerControl cc && cc.ActiveControl != null) c = cc.ActiveControl;
            return c is TextBoxBase || c is ComboBox;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (TryToolHotkey(keyData)) return true;       // V L R E P T I = chọn công cụ
            if (!ReadOnlyView && !IsTextEditing())
            {
                if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
                if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z))
                { Redo(); return true; }

                if (keyData == (Keys.Control | Keys.A))        // chọn tất cả (kể cả khi canvas chưa có focus; đang gõ trong ô nhập thì để ô đó tự xử lý)
                { if (curTool != DrawTool.None) CancelTool(); pictureBox1.Focus(); SelectAll(); return true; }
                if (keyData == Keys.Escape && pictureBox1.Focused && curTool == DrawTool.None)
                {
                    if (nodeEditMode) { SetNodeEditMode(false); return true; }
                    if (projectItems.Any(i => i.IsSelected)) { DeselectAll(); return true; }
                }

                if (pictureBox1.Focused)
                {
                    if ((keyData == Keys.Delete || keyData == Keys.Back) && nodeEditMode && DeleteSelectedNode()) return true;
                    if (keyData == Keys.Delete) { DeleteSelected(); return true; }
                    if (keyData == (Keys.Control | Keys.C)) { CopySelected(); return true; }
                    if (keyData == (Keys.Control | Keys.X)) { CutSelected(); return true; }
                    if (keyData == (Keys.Control | Keys.V))
                    { contextWorldPos = ViewCenter(); PasteAtCursor(); return true; }
                }
            }
            if (keyData == (Keys.Control | Keys.D0))        // Ctrl+0 = về 100%
            { zoom = 1f; pan = PointF.Empty; pictureBox1.Invalidate(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // =====================================================================
        //  MENU CHUỘT PHẢI + Undo/Redo trong menu Edit
        // =====================================================================
        private ToolStripMenuItem miCut = null!, miCopy = null!, miPaste = null!, miDelete = null!;

        private void BuildCanvasContextMenu()
        {
            var menu = new ContextMenuStrip();
            var miUndo = new ToolStripMenuItem("Undo") { ShortcutKeyDisplayString = "Ctrl+Z" };
            var miRedo = new ToolStripMenuItem("Redo") { ShortcutKeyDisplayString = "Ctrl+Y" };
            miUndo.Click += (s, e) => Undo();
            miRedo.Click += (s, e) => Redo();

            miCopy = new ToolStripMenuItem("Copy");
            miCut = new ToolStripMenuItem("Cut");
            miPaste = new ToolStripMenuItem("Paste");
            miDelete = new ToolStripMenuItem("Delete");
            miCopy.Click += (s, e) => CopySelected();
            miCut.Click += (s, e) => CutSelected();
            miPaste.Click += (s, e) => PasteAtCursor();
            miDelete.Click += (s, e) => DeleteSelected();

            var miSelAll = new ToolStripMenuItem("Chọn tất cả") { ShortcutKeyDisplayString = "Ctrl+A" };
            miSelAll.Click += (s, e) => { if (curTool != DrawTool.None) CancelTool(); SelectAll(); };
            var insert = new ToolStripMenuItem("Insert");
            var shape = new ToolStripMenuItem("Shape");
            var line = IconItem("Line", "line");
            var rect = IconItem("Rectangle", "rect");
            var circle = IconItem("Circle", "circle"); line.Click += (s, e) => AddLineAt(contextWorldPos);
            rect.Click += (s, e) => AddRectangleAt(contextWorldPos);
            circle.Click += (s, e) => AddEllipseAt(contextWorldPos);
            shape.DropDownItems.AddRange(new ToolStripItem[] { line, rect, circle });
            foreach (var (name, kind) in PolygonList)
            {
                var k = kind;
                var mi = IconItem(name, name);            // mới
                mi.Click += (s, e) => AddPolygonAt(k, contextWorldPos);
                shape.DropDownItems.Add(mi);
            }
            var text = new ToolStripMenuItem("Text");
            var image = new ToolStripMenuItem("Image");
            text.Click += (s, e) => AddTextAt(contextWorldPos);
            image.Click += (s, e) => AddImageAt(contextWorldPos);
            insert.DropDownItems.AddRange(new ToolStripItem[] { shape, text, image });

            var miNodeEdit = new ToolStripMenuItem("Sửa điểm nút (Node Edit)") { ShortcutKeyDisplayString = "N" };
            miNodeEdit.Click += (s, e) => ToggleNodeEditMode();
            var miWeld = new ToolStripMenuItem("Hàn liền đường cắt / chữ (Weld)") { ShortcutKeyDisplayString = "W" };
            miWeld.Click += (s, e) => WeldSelectedItems();
            var miToPath = new ToolStripMenuItem("Chuyển thành đường dẫn (Path)");
            miToPath.Click += (s, e) => ConvertSelectedToPath(true);
            var miTraceImg = new ToolStripMenuItem("Tách nét Vector từ ảnh (Potrace)...");
            miTraceImg.Click += (s, e) => TraceSelectedImage();

            menu.Items.AddRange(new ToolStripItem[] {
                miUndo, miRedo, new ToolStripSeparator(),
                miCopy, miCut, miPaste, miDelete, miSelAll, new ToolStripSeparator(),
                miNodeEdit, miWeld, miToPath, miTraceImg, new ToolStripSeparator(), insert });

            menu.Opening += (s, e) =>
            {
                if (ReadOnlyView) { e.Cancel = true; return; }
                bool has = selectedItem != null;
                miUndo.Enabled = undoStack.Count > 0;
                miRedo.Enabled = redoStack.Count > 0;
                miCopy.Enabled = miCut.Enabled = miDelete.Enabled = has;
                miPaste.Enabled = clipboardItem != null;
                miNodeEdit.Checked = nodeEditMode;
                miNodeEdit.Enabled = has && selectedItem is not VectorImage;
                miWeld.Enabled = has && selectedItem is not VectorImage;
                miToPath.Enabled = has && selectedItem is not VectorImage && selectedItem is not VectorPathItem;
                miTraceImg.Enabled = has && selectedItem is VectorImage;
            };
            pictureBox1.ContextMenuStrip = menu;

            // Undo/Redo trong menu Edit
            var eUndo = new ToolStripMenuItem("Undo") { ShortcutKeyDisplayString = "Ctrl+Z" };
            var eRedo = new ToolStripMenuItem("Redo") { ShortcutKeyDisplayString = "Ctrl+Y" };
            eUndo.Click += (s, e) => Undo();
            eRedo.Click += (s, e) => Redo();
            toolStripMenuItem1.DropDownItems.Insert(0, eUndo);
            toolStripMenuItem1.DropDownItems.Insert(1, eRedo);
            var eSelAll = new ToolStripMenuItem("Chọn tất cả") { ShortcutKeyDisplayString = "Ctrl+A" };
            eSelAll.Click += (s, e) => { if (curTool != DrawTool.None) CancelTool(); SelectAll(); };
            toolStripMenuItem1.DropDownItems.Insert(2, new ToolStripSeparator());
            toolStripMenuItem1.DropDownItems.Insert(3, eSelAll);
            toolStripMenuItem1.DropDownItems.Insert(4, new ToolStripSeparator());
            toolStripMenuItem1.DropDownOpening += (s, e) =>
            {
                eUndo.Enabled = undoStack.Count > 0;
                eRedo.Enabled = redoStack.Count > 0;
            };
        }

        // =====================================================================
        //  MENU EDIT (chèn vào giữa vùng đang nhìn thấy)
        // =====================================================================
        private void tectangleToolStripMenuItem_Click(object sender, EventArgs e)
        { var c = ViewCenter(); AddRectangleAt(new PointF(c.X - 60, c.Y - 40)); }

        private void circleToolStripMenuItem_Click(object sender, EventArgs e)
        { var c = ViewCenter(); AddEllipseAt(new PointF(c.X - 50, c.Y - 50)); }

        // Designer gắn handler này cho cả mục "shape" lẫn "Line" -> chỉ xử lý khi bấm đúng "Line"
        private void lineToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (sender != lineToolStripMenuItem1) return;
            var c = ViewCenter(); AddLineAt(new PointF(c.X - 60, c.Y));
        }

        private void textToolStripMenuItem_Click(object sender, EventArgs e)
        { var c = ViewCenter(); AddTextAt(new PointF(c.X - 75, c.Y - 20)); }

        private void imageToolStripMenuItem_Click(object sender, EventArgs e)
        { var c = ViewCenter(); AddImageAt(new PointF(c.X - 100, c.Y - 100)); }

        // =====================================================================
        //  FILE: MỞ / LƯU / MỚI
        // =====================================================================
        private void openToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            if (ReadOnlyView) SetActiveTab(null);
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Laser Project (*.lzr)|*.lzr|JSON File (*.json)|*.json|All Files (*.*)|*.*";
                ofd.Title = "Mở file dự án khắc Laser";

                if (ofd.ShowDialog() != DialogResult.OK) return;
                try
                {
                    string jsonString = File.ReadAllText(ofd.FileName);
                    ProjectDataDTO? dto = JsonSerializer.Deserialize<ProjectDataDTO>(jsonString);

                    if (dto != null && dto.Items != null)
                    {
                        PushUndo();
                        projectItems.Clear();
                        selectedItem = null!;

                        foreach (var d in dto.Items)
                        {
                            Vectoritem? it = null;
                            switch (d.Type)
                            {
                                case "Text":
                                    it = new VectorText
                                    {
                                        TextContent = string.IsNullOrEmpty(d.TextContent) ? "Văn bản" : d.TextContent,
                                        FontName = string.IsNullOrEmpty(d.FontName) ? "Arial" : d.FontName,
                                        FontSize = d.FontSize > 0 ? d.FontSize : 18f
                                    };
                                    break;
                                case "Rectangle": it = new VectorRectangle(); break;
                                case "Ellipse": it = new VectorEllipse(); break;
                                case "Line": it = new VectorLine { X2 = d.X2, Y2 = d.Y2 }; break;
                                case "Polygon":
                                    if (Enum.TryParse<PolygonKind>(d.Shape, out var pk)) it = new VectorPolygon(pk);
                                    break;
                                case "Image":
                                    if (!string.IsNullOrEmpty(d.ImageBase64))
                                    {
                                        byte[] imageBytes = Convert.FromBase64String(d.ImageBase64);
                                        using var ms = new MemoryStream(imageBytes);
                                        using var tmp = System.Drawing.Image.FromStream(ms);
                                        it = new VectorImage(new System.Drawing.Bitmap(tmp));
                                    }
                                    break;
                            }
                            if (it == null) continue;
                            it.X = d.X; it.Y = d.Y;
                            if (it is not VectorText) { it.Width = d.Width; it.Height = d.Height; }
                            it.LaserPower = d.LaserPower; it.LaserSpeed = d.LaserSpeed; it.Passes = d.Passes;
                            projectItems.Add(it);
                        }

                        UpdatePropertiesToUI();
                        pictureBox1.Invalidate();
                        MessageBox.Show("Mở file dự án thành công!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Không thể mở file dự án: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void saveToolStripMenuItem1_Click(object? sender, EventArgs e)
        {
            using (SaveFileDialog sfd = new SaveFileDialog())
            {
                sfd.Filter = "Laser Project (*.lzr)|*.lzr|JSON File (*.json)|*.json";
                sfd.Title = "Lưu dự án khắc Laser";

                if (sfd.ShowDialog() != DialogResult.OK) return;
                try
                {
                    ProjectDataDTO dto = new ProjectDataDTO();

                    foreach (var item in projectItems)
                    {
                        VectorItemDTO d = new VectorItemDTO
                        {
                            X = item.X,
                            Y = item.Y,
                            Width = item.Width,
                            Height = item.Height,
                            LaserPower = item.LaserPower,
                            LaserSpeed = item.LaserSpeed,
                            Passes = item.Passes
                        };

                        if (item is VectorText txt)
                        {
                            d.Type = "Text";
                            d.TextContent = txt.TextContent;
                            d.FontName = txt.FontName;
                            d.FontSize = txt.FontSize;
                        }
                        else if (item is VectorImage img && img.LoadedImage != null)
                        {
                            d.Type = "Image";
                            using (MemoryStream ms = new MemoryStream())
                            {
                                img.LoadedImage.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                                d.ImageBase64 = Convert.ToBase64String(ms.ToArray());
                            }
                        }
                        else if (item is VectorRectangle) d.Type = "Rectangle";
                        else if (item is VectorEllipse) d.Type = "Ellipse";
                        else if (item is VectorLine ln) { d.Type = "Line"; d.X2 = ln.X2; d.Y2 = ln.Y2; }
                        else if (item is VectorPolygon pg) { d.Type = "Polygon"; d.Shape = pg.Kind.ToString(); }
                        else continue;

                        dto.Items.Add(d);
                    }

                    string jsonString = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(sfd.FileName, jsonString);

                    MessageBox.Show("Lưu dự án thành công!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Lỗi khi lưu dự án: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void NewtoolStripMenuItem1_Click(object? sender, EventArgs e)
        {
            if (ReadOnlyView) SetActiveTab(null);
            if (projectItems.Count > 0)
            {
                DialogResult result = MessageBox.Show(
                    "Bạn có chắc chắn muốn tạo dự án mới không? Mọi thay đổi chưa lưu sẽ bị mất.",
                    "Xác nhận tạo mới",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question
                );
                if (result == DialogResult.No) return;
            }

            PushUndo();                      // lỡ bấm nhầm vẫn Ctrl+Z lại được
            projectItems.Clear();
            selectedItem = null!;
            UpdatePropertiesToUI();
            pictureBox1.Invalidate();

            MessageBox.Show("Đã tạo dự án mới thành công!", "Thông báo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        private void ProcessAndRender()
        {
            if (g_srcImage == null || g_srcImage.Empty()) return;

            if (pictureBox1.Image != null) pictureBox1.Image.Dispose();   // giải phóng ảnh cũ
            pictureBox1.Image = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(g_srcImage);
        }

        // "Save As" trong Designer đang gắn handler này: mở ảnh để xử lý bằng OpenCV
        private void saveToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Image Files|*.jpg;*.png;*.bmp|All Files|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    g_srcImage = Cv2.ImRead(ofd.FileName);
                    ProcessAndRender();
                }
            }
        }

        // =====================================================================
        //  VIEW / TRACKBAR / EXPORT
        // =====================================================================

        private void Grid_Click(object sender, EventArgs e)
        {
            showGrid = !showGrid;
            Grid.Checked = showGrid;
            pictureBox1.Invalidate();
        }

        private void exportGCodeToolStripMenuItem_Click(object? sender, EventArgs e)
        {
            if (projectItems.Count == 0) { MessageBox.Show("Canvas đang trống."); return; }
            if (!ConfirmInsideArea()) return;                 // cảnh báo nếu hình vượt vùng khắc
            var toExport = ExportItems();                     // chỉ các lớp bật "Xuất", chạy theo thứ tự lớp
            if (toExport.Count == 0)
            {
                MessageBox.Show(this, "Không có đối tượng nào ở lớp đang bật \"Xuất\".\nHãy bật \"Xuất\" cho ít nhất một lớp có đối tượng.", "Xuất G-code");
                return;
            }
            using (var f = new GCodePreviewForm(toExport, BuildGCodeSettings()))
                f.ShowDialog(this);
            SyncLayersFromItems();                            // form G-code cho chỉnh riêng từng đối tượng -> lớp cập nhật theo
        }
        private void AddPolygonMenus()
        {
            foreach (var (name, kind) in PolygonList)
            {
                var k = kind;
                var mi = IconItem(name, name);
                mi.Click += (s, e) => { var c = ViewCenter(); AddPolygonAt(k, new PointF(c.X - 50, c.Y - 50)); };
                shapeToolStripMenuItem.DropDownItems.Add(mi);
            }
        }

        // =====================================================================
        //  Các handler rỗng mà Form1.Designer.cs đang tham chiếu (giữ lại để không lỗi build)
        // =====================================================================
        private void menuStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e) { }
        private void fiieToolStripMenuItem_Click(object sender, EventArgs e) { }
        private void toolStripMenuItem1_Click(object sender, EventArgs e) { }
        private void splitContainer1_Panel1_Paint_1(object sender, PaintEventArgs e) { }
        private void pictureBox1_Click_2(object sender, EventArgs e) { }
        private void label1_Click_1(object sender, EventArgs e) { }
        private void label1_Click_2(object sender, EventArgs e) { }
        private void label1_Click_3(object sender, EventArgs e) { }
        private void panelProperties_Paint(object sender, PaintEventArgs e) { }

        private void label6_Click(object sender, EventArgs e)
        {

        }

        private void label9_Click(object sender, EventArgs e)
        {

        }
    }
}