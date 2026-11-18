// =====================================================================
//  Form1_Layout.cs  -  BỐ CỤC: thanh công cụ, cột phải, tab xem, xem trước đường tô
//
//  Gồm các phần (tìm nhanh bằng Ctrl+F "PHẦN"):
//    1. Bố cục cửa sổ, thanh công cụ trái, cột phải   (trước đây: Form1_Tools.cs)
//    2. Thanh tab đáy (Bản vẽ / Đã xử lý / Đường laser)   (trước đây: ViewTabBar.cs)
//    3. Xem trước đường tô kín (Fill / Hatch)   (trước đây: Form1_FillPreview.cs)
// =====================================================================

// =====================================================================
// PHẦN 1: Bố cục cửa sổ, thanh công cụ trái, cột phải   (trước đây là file Form1_Tools.cs)
// =====================================================================
// =====================================================================
//  Form1_Tools.cs  -  Thanh công cụ dọc bên trái + bố cục cột phải
//
//  1) THANH CÔNG CỤ TRÁI: chỉ là các nút bấm cho công cụ vẽ có sẵn trong Form1_drawtool.cs
//     (BeginDrawTool / CancelTool / curTool). Vẽ kéo thả, snap, bảng nhập số vẫn do file đó lo.
//       - Nút ứng với công cụ đang dùng (curTool) được tô sáng xanh, tự đổi theo khi chọn
//         công cụ từ menu / chuột phải (đồng bộ mỗi 150 ms)
//       - Chọn = hủy công cụ vẽ, quay về chọn / kéo hình
//       - Đa giác: bấm vào icon = vẽ loại đang chọn, bấm mũi tên = đổi loại
//       - Ảnh: mở hộp chọn ảnh rồi đặt vào giữa vùng đang nhìn
//       - Phím tắt (khi canvas đang giữ focus): V Chọn, L Đường, R Chữ nhật, E Elip, P Đa giác, T Chữ, I Ảnh
//         (Esc = hủy công cụ: đã có sẵn trong Form1_drawtool.cs)
//
//  2) CỘT PHẢI: chuyển bảng thông số PropertyPalette (Form1_Palette.cs) từ cột trái sang bên phải,
//     bỏ cột trái cũ và vùng trống bên phải canvas. Hai thanh trượt Canny / Blur (xem trước ảnh)
//     nằm ở đáy cột phải (kèm ô Đảo màu / âm bản), chỉ hiện khi đang ở tab "Edited".
//
//  Gọi InitWorkspaceLayout() 1 lần trong OnHandleCreated (Form1_Cad.cs), ngay sau InitPalette().
// =====================================================================
namespace test01_1014
{
    using OpenCvSharp.Dnn;
    using System.Drawing.Drawing2D;
    using test01_1014;
    using Size = System.Drawing.Size;   // tránh nhập nhằng với OpenCvSharp.Size
    using Font = System.Drawing.Font;
    using Point = System.Drawing.Point;

    public partial class Form1
    {
        private ToolStrip toolBox = null!;
        private ToolStripButton? undoButton, redoButton;
        private ToolStripSplitButton? polyButton;
        private readonly Dictionary<DrawTool, ToolStripItem> toolButtons = new();
        private PolygonKind polyPick = PolygonKind.Pentagon;
        private DrawTool lastHighlight = (DrawTool)(-1);
        private PolygonKind lastPolyIcon = (PolygonKind)(-1);

        private Panel rightHost = null!;
        private Panel? secPreview;                       // 2 thanh trượt Canny / Blur
        private System.Windows.Forms.Timer? toolTimer;

        private int Scale96(int v) => (int)Math.Round(v * DeviceDpi / 96f);

        // ---- độ rộng bảng thông số: kéo được và được nhớ lại giữa các lần mở ----
        private Splitter rightSplitter = null!;
        private static string RightWidthPath => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "test01_1014", "panelwidth.txt");

        private int LoadRightWidth()
        {
            try
            {
                if (!System.IO.File.Exists(RightWidthPath)) return 0;
                return int.TryParse(System.IO.File.ReadAllText(RightWidthPath).Trim(), out int v) ? v : 0;
            }
            catch { return 0; }
        }

        private void SaveRightWidth()
        {
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(RightWidthPath)!);
                System.IO.File.WriteAllText(RightWidthPath, rightHost.Width.ToString());
            }
            catch { }
        }

        // ------------------------------------------------------------------
        //  Bố cục: thanh công cụ trái | canvas | bảng thông số phải
        // ------------------------------------------------------------------
        private void InitWorkspaceLayout()
        {
            if (toolBox != null) return;                  // chỉ chạy 1 lần
            if (propPalette == null) InitPalette();

            InitToolbox();                                // tự Controls.Add(toolBox)

            // ---- cột phải: bảng thông số (Fill) + thanh trượt xem trước (Bottom) ----
            rightHost = new Panel { Dock = DockStyle.Right, Width = 320 };
            secPreview = BuildPreviewSection();
            rightHost.Controls.Add(secPreview);

            splitContainer1.Panel1.Controls.Remove(propPalette);
            propPalette.Dock = DockStyle.Fill;
            rightHost.Controls.Add(propPalette);
            propPalette.BringToFront();                   // Fill phải nằm trên cùng thứ tự dock

            Controls.Add(rightHost);

            // Thanh kéo giữa canvas và bảng thông số: kéo chuột trái/phải để đổi độ rộng bảng
            rightSplitter = new Splitter
            {
                Dock = DockStyle.Right,
                Width = Scale96(5),
                MinSize = Scale96(240),            // bảng không hẹp hơn mức này
                MinExtra = Scale96(300),           // canvas luôn còn ít nhất chừng này
                BackColor = Color.FromArgb(214, 218, 224),
                Cursor = Cursors.VSplit
            };
            rightSplitter.MouseEnter += (s, e) => rightSplitter.BackColor = Color.FromArgb(0, 120, 215);
            rightSplitter.MouseLeave += (s, e) => rightSplitter.BackColor = Color.FromArgb(214, 218, 224);
            rightSplitter.SplitterMoved += (s, e) => SaveRightWidth();
            Controls.Add(rightSplitter);
            rightHost.SendToBack();                // rightHost dock trước -> thanh kéo nằm sát bên trái nó

            int savedW = LoadRightWidth();
            if (savedW > 0) rightHost.Width = Math.Clamp(savedW, Scale96(240), Math.Max(Scale96(240), ClientSize.Width - Scale96(300)));

            // Thứ tự dock: menu trước -> 2 thanh bên -> canvas (Fill) sau cùng
            menuStrip1.SendToBack();
            splitContainer1.BringToFront();

            splitContainer1.Panel1Collapsed = true;       // bỏ cột trái cũ (palette đã chuyển đi)
            splitContainer2.Panel2Collapsed = true;       // bỏ vùng trống bên phải canvas

            if (activeTab is { Mode: ViewMode.Edited }) secPreview.Visible = true;

            // ---- Dọn menu cho gọn (các mục trùng với thanh công cụ / bảng phải) ----
            menuStrip1.Items.Remove(exportGCodeToolStripMenuItem);                 // bỏ khỏi thanh trên...
            exportGCodeToolStripMenuItem.Text = "Xuất G-code / Mô phỏng...";
            exportGCodeToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.E;
            fiieToolStripMenuItem.DropDownItems.Insert(                            // ...đưa vào Tệp, trước dòng kẻ + Thoát
                fiieToolStripMenuItem.DropDownItems.Count - 2, exportGCodeToolStripMenuItem);

            shapeToolStripMenuItem.Visible = false;                                // Edit chỉ còn Undo / Redo
            textToolStripMenuItem.Visible = false;
            imageToolStripMenuItem.Visible = false;
            foreach (ToolStripItem it in toolStripMenuItem1.DropDownItems)         // dòng kẻ thừa sau Redo
                if (it is ToolStripSeparator) it.Visible = false;

            toolTimer = new System.Windows.Forms.Timer { Interval = 150 };
            toolTimer.Tick += (s, e) => SyncToolHighlight();
            toolTimer.Start();

            InitArrangeAndMenus();      // menu Sắp xếp + Việt hóa tên menu (Form1_Arrange.cs)
            InitStatusBar();            // thanh trạng thái dưới cùng (Form1_Status.cs)
            InitBarcodeAndArray();      // mã vạch + tạo mảng bản sao (Form1_Array.cs)
            InitPrintArea();            // <-- thêm: Khổ in (Form1_PrintArea.cs)
            InitLayers();               // bảng LỚP (layer) theo màu (Form1_Layers.cs)
        }

        // 2 thanh trượt cũ (Canny / Blur) chuyển từ cột trái sang đáy cột phải
        private Panel BuildPreviewSection()
        {
            var p = new Panel
            {
                Dock = DockStyle.Bottom,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Visible = false,
                BackColor = Color.White
            };
            var hdr = new Label
            {
                Text = "XỬ LÝ ẢNH (XEM TRƯỚC)",
                Dock = DockStyle.Top,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(222, 229, 240),
                ForeColor = Color.FromArgb(40, 60, 100)
            };
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Padding = new Padding(8, 4, 8, 8)
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (Control c in new Control[] { Canny, trackBarThreshold, Blur, trackBarBlur })
            {
                c.Dock = DockStyle.Fill;
                c.Margin = new Padding(3);
                t.Controls.Add(c);
            }
            chkTabInvert.Dock = DockStyle.Fill;                 // ô Đảo màu (âm bản) nằm dưới 2 thanh trượt
            chkTabInvert.Margin = new Padding(3, 8, 3, 3);
            t.Controls.Add(chkTabInvert);
            p.Controls.Add(t);      // thêm t trước, hdr sau -> hdr được dock trước (nằm trên cùng)
            p.Controls.Add(hdr);
            return p;
        }

        // ------------------------------------------------------------------
        //  Dựng thanh công cụ
        // ------------------------------------------------------------------
        private void InitToolbox()
        {
            int icon = IconSize;
            int cw = icon + Scale96(26), ch = icon + Scale96(14);

            toolBox = new ToolStrip
            {
                Dock = DockStyle.Left,
                LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow,
                GripStyle = ToolStripGripStyle.Hidden,
                CanOverflow = false,
                ImageScalingSize = new Size(icon, icon),
                BackColor = Color.FromArgb(243, 243, 243),
                Padding = new Padding(Scale96(4), Scale96(8), Scale96(4), Scale96(4)),
                Renderer = new ToolRenderer()
            };

            ToolStripButton NewBtn(string tip, System.Drawing.Image img, Action onClick)
            {
                var b = new ToolStripButton
                {
                    Image = img,
                    ToolTipText = tip,
                    DisplayStyle = ToolStripItemDisplayStyle.Image,
                    ImageScaling = ToolStripItemImageScaling.None,
                    AutoSize = false,
                    Size = new Size(cw, ch),
                    Margin = new Padding(0, 2, 0, 2)
                };
                b.Click += (s, e) => onClick();
                toolBox.Items.Add(b);
                return b;
            }

            undoButton = NewBtn("Hoàn tác (Ctrl+Z)", MakeToolIcon("undo", icon), Undo);
            redoButton = NewBtn("Làm lại (Ctrl+Y)", MakeToolIcon("redo", icon), Redo);
            toolBox.Items.Add(new ToolStripSeparator());

            toolButtons[DrawTool.None] = NewBtn("Chọn / di chuyển (V)", MakeToolIcon("select", icon), PickSelect);
            nodeEditBtn = NewBtn("Sửa điểm nút / đường dẫn (N)", MakeToolIcon("node", icon), ToggleNodeEditMode);
            toolBox.Items.Add(new ToolStripSeparator());
            toolButtons[DrawTool.Line] = NewBtn("Đường thẳng (L)", MakeShapeIcon("line", icon), () => PickTool(DrawTool.Line));
            toolButtons[DrawTool.Rectangle] = NewBtn("Hình chữ nhật (R)", MakeShapeIcon("rect", icon), () => PickTool(DrawTool.Rectangle));
            toolButtons[DrawTool.Ellipse] = NewBtn("Hình tròn / elip (E)", MakeShapeIcon("circle", icon), () => PickTool(DrawTool.Ellipse));

            polyButton = new ToolStripSplitButton
            {
                Image = MakeShapeIcon(polyPick.ToString(), icon),
                ToolTipText = "Đa giác (P)",
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                ImageScaling = ToolStripItemImageScaling.None,
                AutoSize = false,
                Size = new Size(cw, ch),
                Margin = new Padding(0, 2, 0, 2)
            };
            polyButton.ButtonClick += (s, e) => PickTool(DrawTool.Polygon);
            foreach (var (name, kind) in PolygonList)
            {
                var mi = IconItem(name, name);
                var k = kind;
                mi.Click += (s, e) => { polyPick = k; PickTool(DrawTool.Polygon); };
                polyButton.DropDownItems.Add(mi);
            }
            toolButtons[DrawTool.Polygon] = polyButton;
            toolBox.Items.Add(polyButton);

            toolBox.Items.Add(new ToolStripSeparator());
            toolButtons[DrawTool.Text] = NewBtn("Chữ (T)", MakeToolIcon("text", icon), () => PickTool(DrawTool.Text));

            // Ảnh không phải "công cụ kéo thả": bấm là mở hộp chọn ảnh ngay (không tô sáng)
            NewBtn("Chèn ảnh (I)", MakeToolIcon("image", icon), PickImage);
            NewBtn("Tạo mã vạch / QR (B)", MakeToolIcon("barcode", icon), () => PickBarcode(null));
            toolBox.Items.Add(new ToolStripSeparator());
            NewBtn("Hàn liền đường cắt / chữ (W)", MakeToolIcon("weld", icon), WeldSelectedItems);
            NewBtn("Tạo mảng bản sao (Ctrl+Shift+A)", MakeToolIcon("array", icon), CreateArray);

            Controls.Add(toolBox);
            SyncToolHighlight();
        }

        // ------------------------------------------------------------------
        //  Hành động của các nút (chuyển cho Form1_drawtool.cs)
        // ------------------------------------------------------------------
        private void PickSelect()
        {
            if (ReadOnlyView) SetActiveTab(null);
            if (nodeEditMode) SetNodeEditMode(false);
            CancelTool();
            SyncToolHighlight();
            pictureBox1.Focus();
        }

        private void PickTool(DrawTool t)
        {
            if (nodeEditMode) SetNodeEditMode(false);
            BeginDrawTool(t, polyPick);       // tự thoát tab xem, hủy công cụ cũ, hiện bảng nhập số
            SyncToolHighlight();
        }

        private void PickImage()
        {
            if (ReadOnlyView) SetActiveTab(null);
            CancelTool();
            AddImageAt(ViewCenter());
            SyncToolHighlight();
        }

        // Tô sáng nút ứng với curTool (gọi định kỳ từ toolTimer và sau mỗi lần bấm nút)
        private void SyncToolHighlight()
        {
            if (toolBox == null) return;
            if (undoButton != null) undoButton.Enabled = !ReadOnlyView && undoStack.Count > 0;
            if (redoButton != null) redoButton.Enabled = !ReadOnlyView && redoStack.Count > 0;
            DrawTool now = curTool;

            // đang vẽ đa giác: icon của nút đa giác đổi theo loại đang dùng
            if (now == DrawTool.Polygon && polyButton != null && curPoly != lastPolyIcon)
            {
                polyPick = curPoly;
                polyButton.Image = MakeShapeIcon(curPoly.ToString(), IconSize);
                lastPolyIcon = curPoly;
                toolBox.Invalidate();
            }

            if (now == lastHighlight && (nodeEditBtn == null || (nodeEditBtn.Tag != null) == nodeEditMode)) return;
            lastHighlight = now;
            foreach (var kv in toolButtons)
                kv.Value.Tag = (!nodeEditMode && kv.Key == now) ? "active" : null;     // ToolRenderer đọc Tag để tô sáng
            if (nodeEditBtn != null)
                nodeEditBtn.Tag = nodeEditMode ? "active" : null;
            toolBox.Invalidate();
        }

        // Phím tắt công cụ (chỉ khi canvas đang giữ focus và không đang gõ chữ)
        private bool TryToolHotkey(Keys key)
        {
            if (toolBox == null || IsTextEditing() || !pictureBox1.Focused) return false;
            switch (key)
            {
                case Keys.V: PickSelect(); return true;
                case Keys.N: ToggleNodeEditMode(); return true;
                case Keys.S when nodeEditMode: SmoothSelectedNode(); return true;
                case Keys.W: WeldSelectedItems(); return true;
                case Keys.L: PickTool(DrawTool.Line); return true;
                case Keys.R: PickTool(DrawTool.Rectangle); return true;
                case Keys.E: PickTool(DrawTool.Ellipse); return true;
                case Keys.P: PickTool(DrawTool.Polygon); return true;
                case Keys.T: PickTool(DrawTool.Text); return true;
                case Keys.I: PickImage(); return true;
                case Keys.B: PickBarcode(null); return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        //  Icon cho Chọn / Chữ / Ảnh (Đường / Chữ nhật / Tròn / Đa giác dùng MakeShapeIcon có sẵn)
        // ------------------------------------------------------------------
        private static System.Drawing.Bitmap MakeToolIcon(string key, int size)
        {
            var bmp = new System.Drawing.Bitmap(size, size);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            float m = size * 0.15f, s = size - 2 * m;
            using var pen = new Pen(Color.FromArgb(40, 40, 40), Math.Max(1.5f, size / 14f)) { LineJoin = LineJoin.Round };

            switch (key)
            {
                case "select":      // mũi tên con trỏ
                    var arrow = new[]
                    {
                        new PointF(m + s * 0.12f, m), new PointF(m + s * 0.12f, m + s * 0.88f),
                        new PointF(m + s * 0.34f, m + s * 0.68f), new PointF(m + s * 0.52f, m + s),
                        new PointF(m + s * 0.67f, m + s * 0.92f), new PointF(m + s * 0.49f, m + s * 0.62f),
                        new PointF(m + s * 0.78f, m + s * 0.60f)
                    };
                    g.FillPolygon(Brushes.White, arrow);
                    g.DrawPolygon(pen, arrow);
                    break;

                case "node":        // đường cong kèm 3 điểm nút vuông (Node Edit)
                    {
                        var p1 = new PointF(m + s * 0.12f, m + s * 0.82f);
                        var p2 = new PointF(m + s * 0.48f, m + s * 0.18f);
                        var p3 = new PointF(m + s * 0.88f, m + s * 0.68f);
                        g.DrawLines(pen, new[] { p1, p2, p3 });
                        float nr = Math.Max(2.2f, s * 0.12f);
                        foreach (var np in new[] { p1, p2, p3 })
                        {
                            g.FillRectangle(Brushes.White, np.X - nr, np.Y - nr, nr * 2, nr * 2);
                            g.DrawRectangle(pen, np.X - nr, np.Y - nr, nr * 2, nr * 2);
                        }
                    }
                    break;

                case "weld":        // 2 vòng tròn giao nhau hàn liền
                    g.DrawEllipse(pen, m + s * 0.04f, m + s * 0.18f, s * 0.58f, s * 0.58f);
                    g.DrawEllipse(pen, m + s * 0.38f, m + s * 0.18f, s * 0.58f, s * 0.58f);
                    break;

                case "undo":
                case "redo":      // mũi tên cong quay lại / đi tới
                    if (key == "redo") { g.TranslateTransform(size, 0); g.ScaleTransform(-1, 1); }
                    g.DrawArc(pen, m, m + s * 0.25f, s, s * 0.8f, 200, 250);
                    g.FillPolygon(new SolidBrush(pen.Color), new[]
                    {
                        new PointF(m + s * 0.02f, m + s * 0.05f), new PointF(m + s * 0.02f, m + s * 0.55f),
                        new PointF(m + s * 0.45f, m + s * 0.30f)
                    });
                    break;

                case "text":        // chữ T
                    using (var f = new Font("Segoe UI", size * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var br = new SolidBrush(Color.FromArgb(40, 40, 40)))
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString("T", f, br, new RectangleF(0, 0, size, size), sf);
                    break;

                case "barcode":     // các vạch dọc to nhỏ
                    {
                        float[] bx = { 0.00f, 0.10f, 0.22f, 0.30f, 0.44f, 0.54f, 0.68f, 0.78f, 0.90f };
                        float[] bwid = { 0.06f, 0.08f, 0.04f, 0.10f, 0.06f, 0.10f, 0.05f, 0.08f, 0.10f };
                        for (int i = 0; i < bx.Length; i++)
                            g.FillRectangle(Brushes.Black, m + s * bx[i], m + s * 0.05f, Math.Max(1.2f, s * bwid[i]), s * 0.9f);
                    }
                    break;

                case "array":       // lưới 3x3 ô vuông nhỏ
                    for (int i = 0; i < 3; i++)
                        for (int j = 0; j < 3; j++)
                        {
                            var cell = new RectangleF(m + s * (0.02f + j * 0.35f), m + s * (0.02f + i * 0.35f), s * 0.26f, s * 0.26f);
                            if (i == 0 && j == 0) g.FillRectangle(Brushes.Black, cell);
                            else g.DrawRectangle(pen, cell.X, cell.Y, cell.Width, cell.Height);
                        }
                    break;

                case "image":       // khung ảnh + núi + mặt trời
                    g.DrawRectangle(pen, m, m + s * 0.1f, s, s * 0.8f);
                    g.DrawLines(pen, new[]
                    {
                        new PointF(m + s * 0.05f, m + s * 0.82f), new PointF(m + s * 0.38f, m + s * 0.45f),
                        new PointF(m + s * 0.58f, m + s * 0.68f), new PointF(m + s * 0.72f, m + s * 0.55f),
                        new PointF(m + s * 0.95f, m + s * 0.82f)
                    });
                    g.DrawEllipse(pen, m + s * 0.68f, m + s * 0.2f, s * 0.15f, s * 0.15f);
                    break;
            }
            return bmp;
        }

        private static GraphicsPath RoundRect(Rectangle r, int rad)
        {
            int d = Math.Max(2, rad * 2);
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ------------------------------------------------------------------
        //  Vẽ nút: đang dùng = nền xanh nhạt + viền xanh, rê chuột = nền xám
        // ------------------------------------------------------------------
        private sealed class ToolRenderer : ToolStripProfessionalRenderer
        {
            public ToolRenderer() { RoundedEdges = false; }

            private static void PaintButton(Graphics g, ToolStripItem it)
            {
                bool active = it.Tag is string s && s == "active";
                bool hot = it.Selected || it.Pressed;
                if (!active && !hot) return;

                var r = new Rectangle(1, 1, it.Width - 3, it.Height - 3);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = RoundRect(r, 6);
                using (var fill = new SolidBrush(active ? Color.FromArgb(205, 228, 252) : Color.FromArgb(226, 226, 226)))
                    g.FillPath(fill, path);
                if (active)
                    using (var pen = new Pen(Color.FromArgb(0, 120, 215), 1.6f))
                        g.DrawPath(pen, path);
            }

            protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e) =>
                PaintButton(e.Graphics, e.Item);

            protected override void OnRenderSplitButtonBackground(ToolStripItemRenderEventArgs e)
            {
                PaintButton(e.Graphics, e.Item);
                if (e.Item is ToolStripSplitButton sb)
                    OnRenderArrow(new ToolStripArrowRenderEventArgs(e.Graphics, sb, sb.DropDownButtonBounds,
                                  Color.FromArgb(90, 90, 90), ArrowDirection.Down));
            }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using var b = new SolidBrush(e.ToolStrip.BackColor);
                e.Graphics.FillRectangle(b, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using var p = new Pen(Color.FromArgb(215, 215, 215));
                e.Graphics.DrawLine(p, e.ToolStrip.Width - 1, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height);
            }
        }
    }
}

// =====================================================================
// PHẦN 2: Thanh tab đáy (Bản vẽ / Đã xử lý / Đường laser)   (trước đây là file ViewTabBar.cs)
// =====================================================================
namespace test01_1014
{
    using Size = System.Drawing.Size;   // tránh nhập nhằng với OpenCvSharp.Size
    using Font = System.Drawing.Font;
    using Point = System.Drawing.Point;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    // Thanh tab vẽ bằng code: tab 0 = "Edit" (không xóa được), các tab sau có nút ×
    public class ViewTabBar : Control
    {
        private List<string> titles = new() { "Edit" };
        private int selected;
        private int hoverClose = -1;
        private readonly List<Rectangle> tabRects = new();
        private readonly List<Rectangle> closeRects = new();

        public bool AllowClose { get; set; } = true;      // false = tab cố định, không có nút ×
        public event Action<int>? TabSelected;
        public event Action<int>? TabClosed;

        public ViewTabBar()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            Dock = DockStyle.Bottom;
            Height = 30;
            BackColor = Color.FromArgb(232, 232, 232);
            Font = new Font("Segoe UI", 9f);
        }

        public void SetTabs(IEnumerable<string> newTitles, int newSelected)
        {
            titles = newTitles.ToList();
            selected = newSelected;
            hoverClose = -1;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            tabRects.Clear(); closeRects.Clear();

            int pad = 12, x = 4, cs = Math.Max(8, (int)(Height * 0.28f));
            for (int i = 0; i < titles.Count; i++)
            {
                bool closable = AllowClose && i > 0, sel = i == selected;
                var tw = TextRenderer.MeasureText(g, titles[i], Font, new Size(int.MaxValue, Height), TextFormatFlags.NoPadding);
                int w = pad + tw.Width + (closable ? 8 + cs : 0) + pad;
                var r = new Rectangle(x, 0, w, Height);
                tabRects.Add(r);

                using (var bg = new SolidBrush(sel ? Color.White : Color.FromArgb(214, 214, 214)))
                    g.FillRectangle(bg, r);
                if (sel)
                    using (var acc = new SolidBrush(Color.FromArgb(0, 120, 215)))
                        g.FillRectangle(acc, r.X, r.Y, r.Width, 3);

                TextRenderer.DrawText(g, titles[i], Font, new Rectangle(r.X + pad, 0, tw.Width + 2, Height),
                    sel ? Color.FromArgb(30, 30, 30) : Color.FromArgb(90, 90, 90),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                if (closable)
                {
                    var c = new Rectangle(r.Right - pad - cs, (Height - cs) / 2, cs, cs);
                    closeRects.Add(c);
                    if (hoverClose == i)
                        using (var hb = new SolidBrush(Color.FromArgb(235, 190, 190)))
                            g.FillEllipse(hb, c.X - 4, c.Y - 4, c.Width + 8, c.Height + 8);
                    using (var pen = new Pen(Color.FromArgb(70, 70, 70), 1.5f))
                    {
                        g.DrawLine(pen, c.Left, c.Top, c.Right, c.Bottom);
                        g.DrawLine(pen, c.Left, c.Bottom, c.Right, c.Top);
                    }
                }
                else closeRects.Add(Rectangle.Empty);

                x += w + 2;
            }
        }

        private int HitClose(Point p)
        {
            if (!AllowClose) return -1;
            for (int i = 1; i < closeRects.Count; i++)
            {
                var r = closeRects[i]; r.Inflate(5, 5);
                if (r.Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitClose(e.Location);
            if (h != hoverClose) { hoverClose = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverClose = -1; Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            for (int i = 0; i < tabRects.Count; i++)
            {
                if (!tabRects[i].Contains(e.Location)) continue;
                if (AllowClose && i > 0 && (e.Button == MouseButtons.Middle ||
                              (e.Button == MouseButtons.Left && HitClose(e.Location) == i)))
                    TabClosed?.Invoke(i);               // bấm × hoặc chuột giữa = xóa tab
                else if (e.Button == MouseButtons.Left)
                    TabSelected?.Invoke(i);
                return;
            }
        }
    }
}

// =====================================================================
// PHẦN 3: Xem trước đường tô kín (Fill / Hatch)   (trước đây là file Form1_FillPreview.cs)
// =====================================================================
// =====================================================================
//  Form1_FillPreview.cs  -  Xem đường TÔ KÍN (Fill / Hatch) ngay trên canvas và các tab xem
//
//  - Tab Bản vẽ / Đã xử lý : đường tô vẽ mờ bằng màu của lớp. "Tô kín" chỉ hiện đường tô (không nét viền);
//                            "Tô kín + viền" hiện đường tô + nét viền của hình
//  - Tab Đường laser       : vẽ ĐÚNG các đường laser sẽ chạy: đường tô màu đỏ; đường VIỀN (tô kín + viền) màu xanh, nét dày hơn
//  Đường tô do chính GCodeCompiler tạo ra (cùng thuật toán với G-code) và được lưu đệm theo nội dung đối tượng.
//  Khi khoảng cách tô nhỏ (vd 0.1 mm) các đường quá dày để thấy trên màn hình -> CHỈ khi hiển thị, vẽ thưa bớt
//  (tối thiểu ~4 px giữa 2 đường); G-code vẫn tô đúng khoảng cách.
// =====================================================================
namespace test01_1014
{
    using Point = System.Drawing.Point;
    using Size = System.Drawing.Size;
    using Font = System.Drawing.Font;

    public partial class Form1
    {
        private sealed class FillParts
        {
            public int Sig;
            public List<PointF[]> Fill = new();
            public List<PointF[]> Outline = new();
        }

        private readonly Dictionary<string, FillParts?> fillCache = new();
        private static readonly Color OutlineBlue = Color.FromArgb(30, 70, 200);

        private static bool CanFill(Vectoritem it) =>
            it.FillMode != 0 && it is not VectorText { SingleLineFont: true } &&
            (it is VectorRectangle || it is VectorEllipse || it is VectorPolygon || it is VectorText || it is VectorPathItem);

        private static int FillSig(Vectoritem it)
        {
            var h = new HashCode();
            h.Add(it.X); h.Add(it.Y); h.Add(it.Width); h.Add(it.Height);
            h.Add(it.Rotation); h.Add(it.FlipX); h.Add(it.FlipY); h.Add(it.LaserPower);
            h.Add(it.FillMode); h.Add(it.FillIntervalMm); h.Add(it.FillAngle); h.Add(it.FillCross); h.Add(it.FillBidir);
            if (it is VectorText t)
            {
                h.Add(t.TextContent); h.Add(t.FontName); h.Add(t.FontSize); h.Add(t.FontBold); h.Add(t.FontItalic);
                h.Add(t.SingleLineFont); h.Add(t.WeldText); h.Add(t.ArcRadiusMm); h.Add(t.CharSpacingMm);
            }
            if (it is VectorPolygon p) h.Add((int)p.Kind);
            if (it is VectorPathItem pi) h.Add(pi.SubPaths.Count);
            return h.ToHashCode();
        }

        private FillParts? FillPartsFor(Vectoritem it)
        {
            if (!CanFill(it)) return null;
            int sig = FillSig(it);
            if (fillCache.TryGetValue(it.Id, out var c) && c != null && c.Sig == sig) return c;
            if (c == null && fillCache.ContainsKey(it.Id) && fillCache_sig.TryGetValue(it.Id, out var s0) && s0 == sig) return null;

            if (fillCache.Count > 300) { fillCache.Clear(); fillCache_sig.Clear(); }
            FillParts? res = null;
            try
            {
                var cfg = new GCodeSettings { MmPerPixel = MmPerPx, MaxS = Math.Max(1, gcodeCfg.MaxS), CurveToleranceMm = gcodeCfg.CurveToleranceMm };
                var parts = new GCodeCompiler(cfg).GetFillParts(it);
                if (parts != null)
                {
                    res = new FillParts { Sig = sig };
                    res.Fill = parts.Value.Fill.Where(p => p.Count >= 2).Select(p => p.ToArray()).ToList();
                    res.Outline = parts.Value.Outline.Where(p => p.Count >= 2).Select(p => p.ToArray()).ToList();
                }
            }
            catch { res = null; }
            fillCache[it.Id] = res;
            fillCache_sig[it.Id] = sig;
            return res;
        }
        private readonly Dictionary<string, int> fillCache_sig = new();

        // Vẽ các đường tô (không xoay thêm vì đã xoay sẵn). Thưa bớt khi hiển thị nếu quá dày. false = không có gì để vẽ
        private bool DrawFillLines(Graphics g, Vectoritem it, Color color, int alpha, float widthPx)
        {
            var fp = FillPartsFor(it);
            if (fp == null || (fp.Fill.Count == 0 && fp.Outline.Count == 0)) return false;
            if (fp.Fill.Count > 0)
            {
                float stepPx = (float)Math.Max(0.01, it.FillIntervalMm / MmPerPx * zoom);      // khoảng cách 2 đường tô trên màn hình
                int k = Math.Max(1, (int)Math.Ceiling(4f / stepPx));
                if (it.FillCross) k = Math.Max(1, k);                                   // tô chéo: mỗi hướng thưa như nhau
                using var pen = new Pen(Color.FromArgb(alpha, color), Math.Max(0.5f, widthPx) / zoom);
                for (int i = 0; i < fp.Fill.Count; i += k)
                {
                    if (fp.Fill.Count / k > 30000) break;
                    g.DrawLines(pen, fp.Fill[i]);
                }
            }
            return true;
        }

        // Tab Đường laser: đỏ = đường tô; xanh dày hơn = đường viền (chỉ "Tô kín + viền"). false -> vẽ nét viền như thường
        private bool DrawLaserFill(Graphics g, Vectoritem it)
        {
            if (!DrawFillLines(g, it, Color.Crimson, 210, 1f)) return false;
            var fp = FillPartsFor(it);
            if (fp != null && fp.Outline.Count > 0)
            {
                using var pen = new Pen(OutlineBlue, 2.2f / zoom) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
                foreach (var p in fp.Outline) g.DrawLines(pen, p);
            }
            return true;
        }

        // Tab Bản vẽ / Đã xử lý và canvas chính. Trả về true nếu hình CHỈ cần hiện đường tô (Tô kín, chưa chọn) -> bỏ DrawItem
        private bool DrawFillPreview(Graphics g, Vectoritem it)
        {
            if (it.FillMode == 0) return false;
            if (!DrawFillLines(g, it, LayerColors.Of(it.LayerId), 120, 1f)) return false;
            return it.FillMode == 1 && !it.IsSelected;
        }
    }
}
