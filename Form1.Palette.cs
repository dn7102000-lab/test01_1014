using OpenCvSharp.Dnn;
using System.Drawing.Drawing2D;
using test01_1014;
using Size = System.Drawing.Size;   // tránh nhập nhằng với OpenCvSharp.Size
using Font = System.Drawing.Font;
using Point = System.Drawing.Point;

// =====================================================================
//  Form1.Palette.cs  -  Bảng THÔNG SỐ đối tượng kiểu AutoCAD (Properties palette)
//
//  Thay cho các ô Rộng/Cao + chữ cũ ở cột trái. Chia thành các mục rõ ràng:
//    ĐỐI TƯỢNG                : tên loại hình đang chọn
//    VỊ TRÍ & KÍCH THƯỚC (mm) : X, Y, Rộng, Cao (+ Khóa tỉ lệ)          [chữ nhật, tròn, đa giác, ảnh, chữ]
//                               Điểm đầu/cuối X,Y, Dài, Góc              [đường thẳng]
//    CHỮ                      : Nội dung, Font, Cỡ chữ                    [văn bản]
//    LASER                    : Công suất %, Tốc độ F, Số lần chạy        [mọi đối tượng]
//  Sửa ở đây là hình trên canvas đổi ngay (có Undo). Kéo thả trên canvas thì các ô số chạy theo.
//  Toạ độ tính bằng mm theo thước trên canvas (gốc ở góc thước, Y hướng xuống).
// =====================================================================

namespace test01_1014
{
    // ------------------------------------------------------------------
    //  Control bảng thông số
    // ------------------------------------------------------------------
    public class PropertyPalette : Panel
    {
        public float MmPerPx { get; set; } = 0.25f;

        public event Action<string>? BeforeEdit;     // sắp sửa (để Form1 lưu Undo; key dùng gộp các lần sửa liên tiếp)
        public event Action? Edited;                 // đã sửa -> vẽ lại canvas
        public event Action? ExportClicked;
        public event Action? BarcodeEditClicked;     // nút "Sửa nội dung / loại mã" của mã vạch
        public event Action? NodeEditClicked;        // nút "Sửa điểm nút (Node Edit)"
        public event Action? SmoothNodeClicked;      // nút "Bo tròn đỉnh đang chọn"
        public event Action? WeldClicked;            // nút "Hàn liền đường cắt (Weld)"
        public event Action? TraceImageClicked;      // nút "Tách nét Vector (Potrace)..." của ảnh
        public event Action? DeleteClicked;
        public Func<List<(string Name, Color Col)>>? LayersGet;     // danh sách lớp (tên + màu) do Form1 cung cấp
        public event Action<int>? LayerPicked;                      // chọn lớp khác cho đối tượng đang chọn
        public event Action<Vectoritem>? FillApplied;    // sửa mục TÔ KÍN -> Form1 chép sang các đối tượng cùng được chọn
        public event Action<Vectoritem>? LaserApplied;   // sửa mục LASER -> Form1 chép sang các đối tượng cùng được chọn
        private int extraSelected;
        public int ExtraSelected                          // số đối tượng chọn thêm ngoài đối tượng chính
        {
            get => extraSelected;
            set
            {
                if (extraSelected == value) return;
                extraSelected = value;
                if (!built) return;
                SyncLock(); UpdateEnabled(); Relayout(); RefreshValues();
            }
        }            // số đối tượng chọn thêm ngoài đối tượng chính

        private sealed class Row
        {
            public Label Lbl = null!;
            public Control Ctl = null!;
            public Func<Vectoritem, bool> Vis = null!;
        }

        private readonly List<(Label Header, List<Row> Rows)> sections = new();
        private readonly Dictionary<string, NumericUpDown> nums = new();
        private readonly Dictionary<string, Label> labels = new();
        private readonly Label lblName, lblEmpty;
        private readonly ChainToggle chain;
        private readonly TextBox txtContent;
        private readonly ComboBox cmbFont;
        private readonly Button btnExport, btnDelete, btnBarcode, btnNodeEdit, btnSmoothNode, btnWeldItems, btnTraceImage;
        private readonly CheckBox chkBold, chkItalic, chkInvert, chkSingleLine, chkWeldText;
        private readonly Panel pnlStyle;
        private readonly ComboBox cmbEngrave, cmbFill, cmbLayer;
        private List<Color> layerCols = new();
        private string layerSig = "";
        private readonly CheckBox chkFillBi, chkFillCross;
        private bool fillVisCache;

        private Vectoritem? item;
        private bool updating;
        private readonly bool built;

        // --- móc nối với Form1 ---
        public Func<Vectoritem, bool>? LockGet;                    // trạng thái khóa tỉ lệ của đối tượng (Form1 nhớ theo Id)
        public Action<Vectoritem, bool>? LockSet;
        public Func<Vectoritem, RectangleF>? ItemBoundsPx;        // khung bao (có tính xoay) của 1 đối tượng, px
        public Func<RectangleF>? GroupBoundsPx;                   // khung bao của cả nhóm đang chọn, px
        public Action<string, float>? GroupNumber;                // sửa X / Y / Rộng / Cao của cả nhóm (đơn vị px)

        private bool IsLineSolo(Vectoritem i) => i is VectorLine && ExtraSelected == 0;
        private bool IsTextSolo(Vectoritem i) => i is VectorText && ExtraSelected == 0;
        private bool IsBoxOrGroup(Vectoritem i) => i is not VectorLine || ExtraSelected > 0;
        private bool ChainVisible() => item != null && (ExtraSelected > 0 || (item is not VectorLine && item is not VectorText));

        private void SyncLock()
        {
            if (item == null) return;
            bool old = updating;
            updating = true;
            try { chain.Checked = LockGet?.Invoke(item) ?? (item is VectorImage); }
            finally { updating = old; }
        }

        private void UpdateEnabled()
        {
            if (item == null) return;
            bool en = ExtraSelected > 0 || item is not VectorText;    // chữ: rộng/cao do font quyết định
            nums["W"].Enabled = nums["H"].Enabled = en;
        }

        private void SetLabelTexts(Vectoritem it)
        {
            bool grp = ExtraSelected > 0, rot = !grp && it.Rotation != 0f && it is not VectorLine;
            labels["X"].Text = grp ? "X (nhóm)" : rot ? "X (bao)" : "X";
            labels["Y"].Text = grp ? "Y (nhóm)" : rot ? "Y (bao)" : "Y";
            labels["W"].Text = grp ? "Rộng (nhóm)" : "Rộng";
            labels["H"].Text = grp ? "Cao (nhóm)" : "Cao";
        }

        // Dời hình sao cho góc trên-trái của KHUNG BAO (hình đã xoay) hoặc của hình (chưa xoay) về đúng toạ độ nhập
        private void MoveBoxTo(float? x, float? y)
        {
            if (item == null) return;
            if (item.Rotation != 0f && item is not VectorLine && ItemBoundsPx != null)
            {
                var bb = ItemBoundsPx(item);
                item.MoveTo(item.X + (x.HasValue ? x.Value - bb.X : 0f), item.Y + (y.HasValue ? y.Value - bb.Y : 0f));
            }
            else item.MoveTo(x ?? item.X, y ?? item.Y);
        }

        private static bool IsLine(Vectoritem i) => i is VectorLine;
        private static bool IsText(Vectoritem i) => i is VectorText;
        private static bool IsBox(Vectoritem i) => i is not VectorLine;                         // mọi thứ có khung Rộng x Cao
        private static bool IsResizable(Vectoritem i) => i is not VectorLine && i is not VectorText;
        private static bool Any(Vectoritem i) => true;
        private static bool IsShape(Vectoritem i) => i is VectorRectangle || i is VectorEllipse || i is VectorPolygon || i is VectorPathItem;   // hình xoay được
        private static bool IsImage(Vectoritem i) => i is VectorImage;
        private static bool IsFillable(Vectoritem i) => (i is VectorRectangle || i is VectorEllipse || i is VectorPolygon || i is VectorText || i is VectorPathItem) && i is not VectorText { SingleLineFont: true };   // hình kín + chữ
        private static bool FillOn(Vectoritem i) => IsFillable(i) && i.FillMode != 0;

        public PropertyPalette()
        {
            AutoScroll = true;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);     // chữ đậm cho dễ nhìn; mọi ô con kế thừa

            lblName = new Label
            {
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                BackColor = Color.FromArgb(40, 90, 160),
                ForeColor = Color.White
            };
            lblEmpty = new Label
            {
                Text = "Chưa chọn đối tượng.\n\nClick một hình trên canvas để chỉnh thông số, " +
                       "hoặc chọn công cụ vẽ (Shape / Text) rồi kéo thả trên canvas.",
                ForeColor = Color.DimGray
            };
            Controls.Add(lblName);
            Controls.Add(lblEmpty);

            // ---- VỊ TRÍ & KÍCH THƯỚC ----
            // ---- LỚP (đối tượng này thuộc lớp nào; đổi ở đây = chuyển sang lớp khác) ----
            var lay = NewSection("LỚP KHẮC (LAYER)");
            var lblLay = new Label { Text = "Lớp khắc" };
            cmbLayer = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 20 };
            cmbLayer.DrawItem += (s, e) =>
            {
                if (e.Index < 0 || e.Index >= cmbLayer.Items.Count) return;
                e.DrawBackground();
                var r = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 3, 14, e.Bounds.Height - 6);
                using (var b = new SolidBrush(e.Index < layerCols.Count ? layerCols[e.Index] : Color.Gray)) e.Graphics.FillRectangle(b, r);
                e.Graphics.DrawRectangle(Pens.Gray, r);
                TextRenderer.DrawText(e.Graphics, cmbLayer.Items[e.Index]?.ToString() ?? "", e.Font ?? Font,
                    new Rectangle(r.Right + 6, e.Bounds.Y, e.Bounds.Width - r.Width - 14, e.Bounds.Height),
                    e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
            cmbLayer.SelectedIndexChanged += (s, e) =>
            {
                if (updating || item == null || cmbLayer.SelectedIndex < 0) return;
                LayerPicked?.Invoke(cmbLayer.SelectedIndex);
            };
            Controls.Add(lblLay); Controls.Add(cmbLayer);
            lay.Add(new Row { Lbl = lblLay, Ctl = cmbLayer, Vis = Any });

            var pos = NewSection("VỊ TRÍ & KÍCH THƯỚC (mm) · gốc = góc trên-trái");
            AddNum(pos, "X", "X", IsBoxOrGroup, -100000, 100000, 2, 1);
            AddNum(pos, "Y", "Y", IsBoxOrGroup, -100000, 100000, 2, 1);
            AddNum(pos, "W", "Rộng", IsBoxOrGroup, 0.25m, 100000, 2, 1);
            AddNum(pos, "H", "Cao", IsBoxOrGroup, 0.25m, 100000, 2, 1);

            chain = new ChainToggle { Visible = false };
            chain.CheckedChanged += (s, e) => { if (!updating && item != null) LockSet?.Invoke(item, chain.Checked); };
            Controls.Add(chain);
            AddNum(pos, "Rot", "Xoay (°)", i => i is not VectorLine && ExtraSelected == 0, -360, 360, 1, 1);

            AddNum(pos, "X1", "Điểm đầu X", IsLineSolo, -100000, 100000, 2, 1);
            AddNum(pos, "Y1", "Điểm đầu Y", IsLineSolo, -100000, 100000, 2, 1);
            AddNum(pos, "X2", "Điểm cuối X", IsLineSolo, -100000, 100000, 2, 1);
            AddNum(pos, "Y2", "Điểm cuối Y", IsLineSolo, -100000, 100000, 2, 1);
            AddNum(pos, "Len", "Chiều dài", IsLineSolo, 0, 100000, 2, 1);
            AddNum(pos, "Ang", "Góc (°, 0 = sang phải)", IsLineSolo, 0, 360, 1, 5);

            // ---- CHỮ ----
            var txt = NewSection("CHỮ");
            var lblC = new Label { Text = "Nội dung" };
            txtContent = new TextBox();
            Controls.Add(lblC); Controls.Add(txtContent);
            txt.Add(new Row { Lbl = lblC, Ctl = txtContent, Vis = IsTextSolo });

            var lblF = new Label { Text = "Font" };
            cmbFont = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var f in FontFamily.Families) cmbFont.Items.Add(f.Name);
            Controls.Add(lblF); Controls.Add(cmbFont);
            txt.Add(new Row { Lbl = lblF, Ctl = cmbFont, Vis = IsTextSolo });

            AddNum(txt, "FS", "Cỡ chữ (pt)", IsTextSolo, 1, 1000, 1, 1);

            var lblSt = new Label { Text = "Kiểu" };
            chkBold = new CheckBox
            {
                Text = "B",
                Appearance = Appearance.Button,
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(0, 0, 60, 28),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            chkItalic = new CheckBox
            {
                Text = "I",
                Appearance = Appearance.Button,
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(68, 0, 60, 28),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold | FontStyle.Italic)
            };
            pnlStyle = new Panel();
            pnlStyle.Controls.Add(chkBold); pnlStyle.Controls.Add(chkItalic);
            Controls.Add(lblSt); Controls.Add(pnlStyle);
            txt.Add(new Row { Lbl = lblSt, Ctl = pnlStyle, Vis = IsTextSolo });

            var lblSL = new Label();
            chkSingleLine = new CheckBox { Text = "Font 1 nét (Hershey - khắc nhanh)", AutoSize = true };
            Controls.Add(lblSL); Controls.Add(chkSingleLine);
            txt.Add(new Row { Lbl = lblSL, Ctl = chkSingleLine, Vis = IsTextSolo });

            var lblWT = new Label();
            chkWeldText = new CheckBox { Text = "Hàn liền nét chữ giao nhau (Weld)", AutoSize = true };
            Controls.Add(lblWT); Controls.Add(chkWeldText);
            txt.Add(new Row { Lbl = lblWT, Ctl = chkWeldText, Vis = i => i is VectorText vt && !vt.SingleLineFont && ExtraSelected == 0 });

            AddNum(txt, "ArcR", "Uốn cong R (mm, 0=thẳng)", IsTextSolo, -5000, 5000, 1, 5);
            AddNum(txt, "CSp", "Giãn chữ (mm)", IsTextSolo, -50, 100, 2, 0.2m);

            // ---- ĐƯỜNG DẪN & SỬA NÚT (NODE EDIT / WELD) ----
            var nod = NewSection("ĐƯỜNG DẪN & ĐIỂM NÚT (NODE / WELD)");
            var lblNE = new Label();
            btnNodeEdit = new Button { Text = "Sửa điểm nút / đường dẫn (Phím N)", Height = 30 };
            btnNodeEdit.Click += (s, e) => NodeEditClicked?.Invoke();
            Controls.Add(lblNE); Controls.Add(btnNodeEdit);
            nod.Add(new Row { Lbl = lblNE, Ctl = btnNodeEdit, Vis = i => i is not VectorImage && ExtraSelected == 0 });

            var lblSM = new Label();
            btnSmoothNode = new Button { Text = "Bo tròn đỉnh đang chọn (Phím S)", Height = 30 };
            btnSmoothNode.Click += (s, e) => SmoothNodeClicked?.Invoke();
            Controls.Add(lblSM); Controls.Add(btnSmoothNode);
            nod.Add(new Row { Lbl = lblSM, Ctl = btnSmoothNode, Vis = i => i is VectorPathItem && ExtraSelected == 0 });

            var lblWD = new Label();
            btnWeldItems = new Button { Text = "Hàn gộp thành đường cắt (Phím W)", Height = 30 };
            btnWeldItems.Click += (s, e) => WeldClicked?.Invoke();
            Controls.Add(lblWD); Controls.Add(btnWeldItems);
            nod.Add(new Row { Lbl = lblWD, Ctl = btnWeldItems, Vis = i => i is not VectorImage });

            // ---- MÃ VẠCH / QR ----
            var bar = NewSection("MÃ VẠCH / QR");
            var lblBar = new Label { Text = "Nội dung / loại mã" };
            btnBarcode = new Button { Text = "Sửa nội dung / loại mã…" };
            btnBarcode.Click += (s, e) => BarcodeEditClicked?.Invoke();
            Controls.Add(lblBar); Controls.Add(btnBarcode);
            bar.Add(new Row { Lbl = lblBar, Ctl = btnBarcode, Vis = i => i is VectorBarcode && ExtraSelected == 0 });

            // ---- KHẮC ẢNH ----
            var img = NewSection("KHẮC ẢNH");
            var lblM = new Label { Text = "Chế độ" };
            cmbEngrave = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cmbEngrave.Items.AddRange(new object[] { "Theo cài đặt G-code", "Ảnh xám (nhiều mức)", "Đen trắng (1-bit)" });
            Controls.Add(lblM); Controls.Add(cmbEngrave);
            img.Add(new Row { Lbl = lblM, Ctl = cmbEngrave, Vis = IsImage });

            AddNum(img, "Th", "Ngưỡng (0-255)", i => i is VectorImage v && v.EngraveMode == 2, 0, 255, 0, 1);

            var lblInv = new Label();
            chkInvert = new CheckBox { Text = "Đảo màu (khắc vùng sáng)", AutoSize = true };
            Controls.Add(lblInv); Controls.Add(chkInvert);
            img.Add(new Row { Lbl = lblInv, Ctl = chkInvert, Vis = i => i is VectorImage v && v.EngraveMode != 0 });

            var lblTr = new Label();
            btnTraceImage = new Button { Text = "Tách nét Vector / Potrace (Cắt viền)…", Height = 30 };
            btnTraceImage.Click += (s, e) => TraceImageClicked?.Invoke();
            Controls.Add(lblTr); Controls.Add(btnTraceImage);
            img.Add(new Row { Lbl = lblTr, Ctl = btnTraceImage, Vis = i => i is VectorImage && ExtraSelected == 0 });

            // ---- TÔ KÍN (riêng từng đối tượng; sửa lớp thì đè lại theo lớp) ----
            var fil = NewSection("TÔ KÍN (FILL) - RIÊNG ĐỐI TƯỢNG");
            var lblFm = new Label { Text = "Chế độ" };
            cmbFill = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            cmbFill.Items.AddRange(new object[] { "Đường viền", "Tô kín", "Tô kín + viền" });
            Controls.Add(lblFm); Controls.Add(cmbFill);
            fil.Add(new Row { Lbl = lblFm, Ctl = cmbFill, Vis = IsFillable });
            AddNum(fil, "FI", "Cách dòng (mm)", FillOn, 0.02m, 5, 2, 0.01m);
            AddNum(fil, "FA", "Góc quét (°)", FillOn, -360, 360, 0, 5);
            var lblFb = new Label();
            chkFillBi = new CheckBox { Text = "Quét 2 chiều", AutoSize = true };
            Controls.Add(lblFb); Controls.Add(chkFillBi);
            fil.Add(new Row { Lbl = lblFb, Ctl = chkFillBi, Vis = FillOn });
            var lblFc = new Label();
            chkFillCross = new CheckBox { Text = "Quét chéo (thêm lượt vuông góc)", AutoSize = true };
            Controls.Add(lblFc); Controls.Add(chkFillCross);
            fil.Add(new Row { Lbl = lblFc, Ctl = chkFillCross, Vis = FillOn });

            // ---- LASER ----
            var las = NewSection("LASER");
            AddNum(las, "Pw", "Công suất (%)", Any, 0, 100, 0, 5);
            AddNum(las, "Sp", "Tốc độ F (mm/phút)", Any, 1, 60000, 0, 100);
            AddNum(las, "Ps", "Số lần chạy", Any, 1, 100, 0, 1);

            // ---- nút ----
            btnExport = new Button { Text = "Xuất G-code / Mô phỏng…", Height = 32 };
            btnDelete = new Button { Text = "Xóa đối tượng", Height = 32 };
            btnExport.Click += (s, e) => ExportClicked?.Invoke();
            btnDelete.Click += (s, e) => DeleteClicked?.Invoke();
            Controls.Add(btnExport); Controls.Add(btnDelete);

            // ---- sự kiện sửa nội dung chữ / font / khóa tỉ lệ ----
            txtContent.TextChanged += (s, e) =>
            {
                if (updating || item is not VectorText t) return;
                BeforeEdit?.Invoke("pal:text:" + t.Id);
                t.TextContent = txtContent.Text;
                Edited?.Invoke();
                RefreshValues();
            };
            cmbFont.SelectedIndexChanged += (s, e) =>
            {
                if (updating || item is not VectorText t || cmbFont.SelectedItem == null) return;
                BeforeEdit?.Invoke("pal:font:" + t.Id);
                t.FontName = cmbFont.SelectedItem.ToString()!;
                Edited?.Invoke();
                RefreshValues();
            };

            chkBold.CheckedChanged += (s, e) =>
            {
                if (updating || item is not VectorText t) return;
                BeforeEdit?.Invoke("pal:bold:" + t.Id);
                t.FontBold = chkBold.Checked;
                Edited?.Invoke();
                RefreshValues();
            };
            chkItalic.CheckedChanged += (s, e) =>
            {
                if (updating || item is not VectorText t) return;
                BeforeEdit?.Invoke("pal:italic:" + t.Id);
                t.FontItalic = chkItalic.Checked;
                Edited?.Invoke();
                RefreshValues();
            };
            chkSingleLine.CheckedChanged += (s, e) =>
            {
                if (updating || item is not VectorText t) return;
                BeforeEdit?.Invoke("pal:sline:" + t.Id);
                t.SingleLineFont = chkSingleLine.Checked;
                Edited?.Invoke();
                Relayout();
                RefreshValues();
            };
            chkWeldText.CheckedChanged += (s, e) =>
            {
                if (updating || item is not VectorText t) return;
                BeforeEdit?.Invoke("pal:weld:" + t.Id);
                t.WeldText = chkWeldText.Checked;
                Edited?.Invoke();
                RefreshValues();
            };
            cmbEngrave.SelectedIndexChanged += (s, e) =>
            {
                if (updating || item is not VectorImage vi || cmbEngrave.SelectedIndex < 0) return;
                BeforeEdit?.Invoke("pal:emode:" + vi.Id);
                vi.EngraveMode = cmbEngrave.SelectedIndex;
                Edited?.Invoke();
                Relayout();                     // ô Ngưỡng / Đảo màu ẩn hiện theo chế độ
            };
            cmbFill.SelectedIndexChanged += (s, e) =>
            {
                if (updating || item == null || !IsFillable(item) || cmbFill.SelectedIndex < 0) return;
                BeforeEdit?.Invoke("pal:fmode:" + item.Id);
                item.FillMode = cmbFill.SelectedIndex;
                FillApplied?.Invoke(item);
                Edited?.Invoke();
                Relayout();                     // các ô cách dòng / góc ẩn hiện theo chế độ
            };
            chkFillBi.CheckedChanged += (s, e) =>
            {
                if (updating || item == null || !IsFillable(item)) return;
                BeforeEdit?.Invoke("pal:fbi:" + item.Id);
                item.FillBidir = chkFillBi.Checked;
                FillApplied?.Invoke(item);
                Edited?.Invoke();
            };
            chkFillCross.CheckedChanged += (s, e) =>
            {
                if (updating || item == null || !IsFillable(item)) return;
                BeforeEdit?.Invoke("pal:fcr:" + item.Id);
                item.FillCross = chkFillCross.Checked;
                FillApplied?.Invoke(item);
                Edited?.Invoke();
            };
            chkInvert.CheckedChanged += (s, e) =>
            {
                if (updating || item is not VectorImage vi) return;
                BeforeEdit?.Invoke("pal:einv:" + vi.Id);
                vi.Invert = chkInvert.Checked;
                Edited?.Invoke();
            };

            built = true;
            Relayout();
        }

        // ---------- dựng giao diện ----------
        private List<Row> NewSection(string title)
        {
            var header = new Label
            {
                Text = title,
                UseMnemonic = false,                       // giữ nguyên dấu "&" trong tiêu đề
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(222, 229, 240),
                ForeColor = Color.FromArgb(40, 60, 100)
            };
            Controls.Add(header);
            var rows = new List<Row>();
            sections.Add((header, rows));
            return rows;
        }

        private void AddNum(List<Row> rows, string id, string label, Func<Vectoritem, bool> vis,
                            decimal min, decimal max, int dec, decimal inc)
        {
            var lbl = new Label { Text = label, UseMnemonic = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            var n = new NumericUpDown { DecimalPlaces = dec, Minimum = min, Maximum = max, Increment = inc };
            n.ValueChanged += (s, e) => { if (!updating) ApplyNumber(id, (double)n.Value); };
            Controls.Add(lbl); Controls.Add(n);
            rows.Add(new Row { Lbl = lbl, Ctl = n, Vis = vis });
            nums[id] = n; labels[id] = lbl;
        }

        // ---------- gắn đối tượng ----------
        public void Bind(Vectoritem? it)
        {
            item = it;
            updating = true;
            try
            {
                if (it != null)
                {
                    SyncLock();
                    UpdateEnabled();
                }
            }
            finally { updating = false; }
            RefreshValues();
            Relayout();
        }

        // Cập nhật số hiển thị theo đối tượng (bỏ qua ô đang gõ dở)
        public void RefreshValues()
        {
            if (item == null) return;
            bool old = updating;
            updating = true;
            try
            {
                float k = MmPerPx;
                var it = item;
                lblName.Text = ExtraSelected > 0 ? $"{it.Name}  (+{ExtraSelected} đối tượng khác)" : it.Name;

                if (ExtraSelected > 0 && GroupBoundsPx != null)
                {   // chọn nhiều: X / Y / Rộng / Cao là của khung bao CẢ NHÓM
                    var gb = GroupBoundsPx();
                    Set("X", gb.X * k); Set("Y", gb.Y * k); Set("W", gb.Width * k); Set("H", gb.Height * k);
                }
                else if (it.Rotation != 0f && it is not VectorLine && ItemBoundsPx != null)
                {   // hình đã xoay: X / Y theo khung bao (thấy được trên canvas), Rộng / Cao là kích thước riêng của hình
                    var bb = ItemBoundsPx(it);
                    Set("X", bb.X * k); Set("Y", bb.Y * k);
                    Set("W", it.Width * k); Set("H", it.Height * k);
                }
                else
                {
                    Set("X", it.X * k); Set("Y", it.Y * k);
                    Set("W", it.Width * k); Set("H", it.Height * k);
                }
                Set("Rot", it.Rotation);
                SetLabelTexts(it);

                if (it is VectorLine ln)
                {
                    float dx = ln.X2 - ln.X, dy = ln.Y2 - ln.Y;
                    double ang = Math.Atan2(-dy, dx) * 180.0 / Math.PI;      // Y hướng lên cho dễ hiểu
                    if (ang < 0) ang += 360;
                    Set("X1", ln.X * k); Set("Y1", ln.Y * k);
                    Set("X2", ln.X2 * k); Set("Y2", ln.Y2 * k);
                    Set("Len", Math.Sqrt(dx * dx + dy * dy) * k);
                    Set("Ang", ang);
                }
                if (it is VectorText t)
                {
                    Set("FS", t.FontSize);
                    Set("ArcR", t.ArcRadiusMm);
                    Set("CSp", t.CharSpacingMm);
                    if (!txtContent.ContainsFocus && txtContent.Text != t.TextContent) txtContent.Text = t.TextContent;
                    if (!cmbFont.ContainsFocus) SelectFont(t.FontName);
                    chkBold.Checked = t.FontBold;
                    chkItalic.Checked = t.FontItalic;
                    chkSingleLine.Checked = t.SingleLineFont;
                    chkWeldText.Checked = t.WeldText;
                }
                if (it is VectorImage vi)
                {
                    cmbEngrave.SelectedIndex = Math.Clamp(vi.EngraveMode, 0, 2);
                    Set("Th", vi.Threshold);
                    chkInvert.Checked = vi.Invert;
                }
                RebuildLayerCombo();
                if (cmbLayer.Items.Count > 0) cmbLayer.SelectedIndex = Math.Clamp(it.LayerId, 0, cmbLayer.Items.Count - 1);
                if (IsFillable(it))
                {
                    cmbFill.SelectedIndex = Math.Clamp(it.FillMode, 0, 2);
                    Set("FI", it.FillIntervalMm); Set("FA", it.FillAngle);
                    chkFillBi.Checked = it.FillBidir;
                    chkFillCross.Checked = it.FillCross;
                }
                Set("Pw", it.LaserPower); Set("Sp", it.LaserSpeed); Set("Ps", it.Passes);
            }
            finally { updating = old; }
            bool fv = FillOn(item);
            if (fv != fillVisCache) { fillVisCache = fv; if (built) Relayout(); }
        }

        // Dựng lại danh sách lớp trong ô chọn khi tên / màu / số lớp đổi
        private void RebuildLayerCombo()
        {
            var ls = LayersGet?.Invoke();
            if (ls == null) return;
            string sig = string.Join("|", ls.Select(x => x.Name + ":" + x.Col.ToArgb()));
            if (sig == layerSig && cmbLayer.Items.Count == ls.Count) return;
            layerSig = sig;
            layerCols = ls.Select(x => x.Col).ToList();
            cmbLayer.Items.Clear();
            foreach (var x in ls) cmbLayer.Items.Add(x.Name);
        }

        // Form1 gọi khi lớp đổi tên / màu / số lượng
        public void RefreshLayers()
        {
            if (!built) return;
            bool old = updating;
            updating = true;
            try
            {
                RebuildLayerCombo();
                if (item != null && cmbLayer.Items.Count > 0)
                    cmbLayer.SelectedIndex = Math.Clamp(item.LayerId, 0, cmbLayer.Items.Count - 1);
                cmbLayer.Invalidate();
            }
            finally { updating = old; }
        }

        private void Set(string id, double v)
        {
            var n = nums[id];
            if (n.ContainsFocus || double.IsNaN(v) || double.IsInfinity(v)) return;
            decimal d = Math.Min(n.Maximum, Math.Max(n.Minimum, (decimal)Math.Round(v, 4)));
            if (n.Value != d) n.Value = d;
        }

        private void SelectFont(string name)
        {
            int i = cmbFont.FindStringExact(name);
            if (i < 0) { cmbFont.Items.Add(name); i = cmbFont.Items.Count - 1; }
            if (cmbFont.SelectedIndex != i) cmbFont.SelectedIndex = i;
        }

        // ---------- áp giá trị gõ vào đối tượng ----------
        private void ApplyNumber(string id, double v)
        {
            if (item == null) return;
            float k = MmPerPx;
            float px = (float)(v / k);

            if (ExtraSelected > 0 && (id == "X" || id == "Y" || id == "W" || id == "H"))
            {   // chọn nhiều: sửa cả nhóm
                BeforeEdit?.Invoke("pal:group:" + id);
                GroupNumber?.Invoke(id, px);
                Edited?.Invoke();
                RefreshValues();
                return;
            }

            BeforeEdit?.Invoke("pal:" + item.Id + ":" + id);

            switch (id)
            {
                case "X": MoveBoxTo(px, null); break;
                case "Y": MoveBoxTo(null, px); break;
                case "W": SetSize(px, null); break;
                case "H": SetSize(null, px); break;

                case "X1": if (item is VectorLine a1) a1.X = px; break;
                case "Y1": if (item is VectorLine a2) a2.Y = px; break;
                case "X2": if (item is VectorLine a3) a3.X2 = px; break;
                case "Y2": if (item is VectorLine a4) a4.Y2 = px; break;
                case "Len":
                    if (item is VectorLine l)
                    {
                        float dx = l.X2 - l.X, dy = l.Y2 - l.Y;
                        double cur = Math.Sqrt(dx * dx + dy * dy);
                        if (cur < 1e-6) l.X2 = l.X + px;
                        else { l.X2 = l.X + (float)(dx / cur * px); l.Y2 = l.Y + (float)(dy / cur * px); }
                    }
                    break;
                case "Ang":
                    if (item is VectorLine m)
                    {
                        float dx = m.X2 - m.X, dy = m.Y2 - m.Y;
                        double len = Math.Sqrt(dx * dx + dy * dy), a = v * Math.PI / 180.0;
                        m.X2 = m.X + (float)(len * Math.Cos(a));
                        m.Y2 = m.Y - (float)(len * Math.Sin(a));
                    }
                    break;

                case "FS": if (item is VectorText t) t.FontSize = (float)v; break;
                case "ArcR": if (item is VectorText ta) ta.ArcRadiusMm = (float)v; break;
                case "CSp": if (item is VectorText tc) tc.CharSpacingMm = (float)v; break;
                case "Rot": item.Rotation = (float)v; break;
                case "FI": item.FillIntervalMm = v; FillApplied?.Invoke(item); break;
                case "FA": item.FillAngle = (float)v; FillApplied?.Invoke(item); break;
                case "Th": if (item is VectorImage vimg) vimg.Threshold = (int)v; break;
                case "Pw": item.LaserPower = (int)v; LaserApplied?.Invoke(item); break;
                case "Sp": item.LaserSpeed = (int)v; LaserApplied?.Invoke(item); break;
                case "Ps": item.Passes = (int)v; LaserApplied?.Invoke(item); break;
            }
            Edited?.Invoke();
            RefreshValues();      // cập nhật các ô phụ thuộc (ví dụ khóa tỉ lệ đổi Cao khi gõ Rộng)
        }

        private void SetSize(float? w, float? h)
        {
            if (item == null || !IsResizable(item)) return;
            float ow = item.Width, oh = item.Height;
            float nw = w ?? ow, nh = h ?? oh;
            if (chain.Checked && ow > 0.0001f && oh > 0.0001f)        // khóa tỉ lệ: đổi một cạnh thì cạnh kia theo
            {
                if (w != null) nh = nw * oh / ow;
                else nw = nh * ow / oh;
            }
            nw = Math.Max(1f, nw); nh = Math.Max(1f, nh);
            float nx = item.X, ny = item.Y;
            if (item.Rotation != 0f)                                  // hình đã xoay: giữ nguyên TÂM, không bị nhảy vị trí
            {
                float cx = item.X + ow / 2f, cy = item.Y + oh / 2f;
                nx = cx - nw / 2f; ny = cy - nh / 2f;
            }
            if (item is VectorPathItem pi)
            {
                pi.Scale(nw, nh, nx, ny);
            }
            else
            {
                item.X = nx; item.Y = ny;
                item.Width = nw; item.Height = nh;
            }
        }

        // ---------- bố cục ----------
        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (built) Relayout();
        }

        private void Relayout()
        {
            // Luôn chừa chỗ cho thanh cuộn dọc (có / không có thì bề rộng dùng được vẫn như nhau) -> không bao giờ sinh thanh cuộn ngang
            int w = Math.Max(150, ClientSize.Width - (VScroll ? 0 : SystemInformation.VerticalScrollBarWidth));
            int y = 0;

            bool has = item != null;
            lblName.Visible = has;
            lblEmpty.Visible = !has;
            btnDelete.Visible = has;
            bool hasChain = ChainVisible();
            int yW = -1, yH = -1, xChain = 0;

            if (has) { lblName.SetBounds(0, y, w, 28); y += 34; }
            else { lblEmpty.SetBounds(10, 10, w - 20, 90); y += 110; }

            foreach (var (header, rows) in sections)
            {
                bool any = has && rows.Any(r => r.Vis(item!));
                header.Visible = any;
                if (!any)
                {
                    foreach (var r in rows) { r.Lbl.Visible = false; r.Ctl.Visible = false; }
                    continue;
                }
                header.SetBounds(0, y, w, 22); y += 28;

                foreach (var r in rows)
                {
                    bool v = r.Vis(item!);
                    r.Lbl.Visible = r.Ctl.Visible = v;
                    if (!v) continue;
                    if (ReferenceEquals(r.Ctl, btnBarcode) || ReferenceEquals(r.Ctl, btnNodeEdit) ||
                        ReferenceEquals(r.Ctl, btnSmoothNode) || ReferenceEquals(r.Ctl, btnWeldItems) ||
                        ReferenceEquals(r.Ctl, btnTraceImage))      // nút dài, nằm riêng một dòng cho đủ chữ
                    {
                        r.Lbl.Visible = false;
                        r.Ctl.SetBounds(10, y, w - 20, 30);
                        y += 36;
                        continue;
                    }
                    if (ReferenceEquals(r.Ctl, chkSingleLine) || ReferenceEquals(r.Ctl, chkWeldText))
                    {
                        r.Lbl.Visible = false;
                        r.Ctl.SetBounds(12, y, w - 24, 24);
                        y += 28;
                        continue;
                    }
                    bool isW = ReferenceEquals(r.Ctl, nums["W"]), isH = ReferenceEquals(r.Ctl, nums["H"]);
                    int cw = Math.Max(60, w - 142 - 12 - (hasChain && (isW || isH) ? 34 : 0));
                    bool tall = ReferenceEquals(r.Ctl, pnlStyle);          // hàng nút B / I cao hơn các hàng khác
                    int rh = tall ? 28 : 24;
                    r.Lbl.SetBounds(10, y, 130, rh);
                    r.Ctl.SetBounds(142, y, cw, rh);
                    if (isW) { yW = y; xChain = 142 + cw; }
                    if (isH) yH = y;
                    y += tall ? 34 : 30;
                }
                y += 6;
            }

            if (hasChain && yW >= 0 && yH > yW)                 // nút xích nằm giữa 2 ô Rộng / Cao
            {
                chain.SetBounds(xChain + 2, yW + 1, 30, yH - yW + 24);
                chain.Visible = true;
            }
            else chain.Visible = false;

            y += 4;
            btnExport.SetBounds(10, y, w - 20, 32); y += 38;
            if (has) { btnDelete.SetBounds(10, y, w - 20, 32); y += 38; }
            AutoScrollMinSize = new Size(0, y + 20);
        }
    }

    // ------------------------------------------------------------------
    //  Gắn bảng vào Form1
    // ------------------------------------------------------------------
    public partial class Form1
    {
        private PropertyPalette propPalette = null!;
        private System.Windows.Forms.Timer palTimer = null!;
        private Vectoritem? palItem;
        private int palSig;

        private void InitPalette()
        {
            if (propPalette != null) return;      // chỉ chạy 1 lần

            propPalette = new PropertyPalette { Dock = DockStyle.Fill, MmPerPx = MmPerPx };
            propPalette.BeforeEdit += key => PushUndoCoalesced(key);
            propPalette.Edited += () => pictureBox1.Invalidate();
            propPalette.ExportClicked += () => exportGCodeToolStripMenuItem.PerformClick();
            propPalette.DeleteClicked += () => DeleteSelected();
            propPalette.NodeEditClicked += () => ToggleNodeEditMode();
            propPalette.SmoothNodeClicked += () => SmoothSelectedNode();
            propPalette.WeldClicked += () => WeldSelectedItems();
            propPalette.TraceImageClicked += () => TraceSelectedImage();
            propPalette.LockGet = it => SelCount > 1 ? groupLock : IsRatioLocked(it);
            propPalette.LockSet = (it, on) => { if (SelCount > 1) groupLock = on; else ratioLock[it.Id] = on; };
            propPalette.ItemBoundsPx = it => ItemBounds(it);
            propPalette.GroupBoundsPx = () => UnionBounds(SelectedList());
            propPalette.GroupNumber = ApplyGroupNumber;
            propPalette.FillApplied += src =>          // tô kín: chép sang các hình kín / chữ đang được chọn cùng
            {
                foreach (var o in projectItems)
                    if (o.IsSelected && o != src && (o is VectorRectangle || o is VectorEllipse || o is VectorPolygon || o is VectorText))
                    {
                        o.FillMode = src.FillMode; o.FillIntervalMm = src.FillIntervalMm; o.FillAngle = src.FillAngle;
                        o.FillCross = src.FillCross; o.FillBidir = src.FillBidir;
                    }
            };
            propPalette.LaserApplied += src => ApplyLaserFromItem(src);     // sửa mục LASER = sửa thông số của LỚP (Form1_Layers.cs)

            // Phủ lên cột trái cũ (panelProperties / textpanel / thanh Blur-Canny) -> khỏi phải xóa chúng
            splitContainer1.Panel1.Controls.Add(propPalette);
            propPalette.BringToFront();
            if (splitContainer1.SplitterDistance < 290) splitContainer1.SplitterDistance = 290;

            // Theo dõi đối tượng đang chọn: đổi lựa chọn -> gắn lại, kéo thả -> cập nhật số
            palTimer = new System.Windows.Forms.Timer { Interval = 120 };
            palTimer.Tick += (s, e) =>
            {
                Vectoritem? sel = ReadOnlyView ? null : selectedItem;
                int extra = ReadOnlyView ? 0 : Math.Max(0, SelCount - 1);
                if (extra != propPalette.ExtraSelected) { propPalette.ExtraSelected = extra; propPalette.RefreshValues(); }
                if (!ReferenceEquals(sel, palItem))
                {
                    palItem = sel;
                    propPalette.Bind(sel);
                    palSig = ItemSig(sel);
                    return;
                }
                int sg = ItemSig(sel);
                if (sg != palSig) { palSig = sg; propPalette.RefreshValues(); }
            };
            palTimer.Start();
        }

        // ------------------------------------------------------------------
        //  Khóa tỉ lệ (nút xích) + nhập số X / Y / Rộng / Cao cho CẢ NHÓM đang chọn
        // ------------------------------------------------------------------
        private bool groupLock;                                            // khóa tỉ lệ khi sửa Rộng / Cao của nhóm
        private readonly Dictionary<string, bool> ratioLock = new();       // khóa tỉ lệ của từng đối tượng (theo Id; ảnh mặc định = khóa)
        private bool IsRatioLocked(Vectoritem it) => ratioLock.TryGetValue(it.Id, out bool v) ? v : it is VectorImage;

        private void ApplyGroupNumber(string id, float px)
        {
            var sel = SelectedList();
            if (sel.Count < 2) return;
            RectangleF u = UnionBounds(sel);
            switch (id)
            {
                case "X": foreach (var it in sel) ShiftItem(it, px - u.Left, 0f); break;
                case "Y": foreach (var it in sel) ShiftItem(it, 0f, px - u.Top); break;
                case "W":
                    if (u.Width < 1e-3f) return;
                    { float sx = px / u.Width; ScaleGroup(sel, u, sx, groupLock ? sx : 1f); }
                    break;
                case "H":
                    if (u.Height < 1e-3f) return;
                    { float sy = px / u.Height; ScaleGroup(sel, u, groupLock ? sy : 1f, sy); }
                    break;
            }
            pictureBox1.Invalidate();
        }

        // Co giãn cả nhóm quanh góc trên-trái của khung bao. Hình xoay 90° đổi chỗ 2 hệ số; xoay góc lẻ thì co đều (không méo được)
        private void ScaleGroup(List<Vectoritem> sel, RectangleF u, float sx, float sy)
        {
            foreach (var it in sel)
            {
                if (it is VectorLine l)
                {
                    l.X = u.Left + (l.X - u.Left) * sx; l.Y = u.Top + (l.Y - u.Top) * sy;
                    l.X2 = u.Left + (l.X2 - u.Left) * sx; l.Y2 = u.Top + (l.Y2 - u.Top) * sy;
                    continue;
                }
                float cx = u.Left + (it.X + it.Width / 2f - u.Left) * sx;
                float cy = u.Top + (it.Y + it.Height / 2f - u.Top) * sy;

                float rot = ((it.Rotation % 180f) + 180f) % 180f;
                float fw, fh;
                if (rot < 0.5f || rot > 179.5f) { fw = sx; fh = sy; }
                else if (Math.Abs(rot - 90f) < 0.5f) { fw = sy; fh = sx; }
                else fw = fh = MathF.Sqrt(sx * sy);

                if (it is VectorText t)
                {
                    t.FontSize = Math.Clamp(t.FontSize * fh, 1f, 1000f);
                    SizeF sz = MeasureText(t);
                    t.Width = sz.Width; t.Height = sz.Height;
                }
                else
                {
                    it.Width = Math.Max(1f, it.Width * fw);
                    it.Height = Math.Max(1f, it.Height * fh);
                }
                it.X = cx - it.Width / 2f; it.Y = cy - it.Height / 2f;
            }
        }

        private static int ItemSig(Vectoritem? it)
        {
            if (it == null) return 0;
            var h = new HashCode();
            h.Add(it.X); h.Add(it.Y); h.Add(it.Width); h.Add(it.Height);
            h.Add(it.LaserPower); h.Add(it.LaserSpeed); h.Add(it.Passes);
            h.Add(ItemExtrasHash(it));
            if (it is VectorLine l) { h.Add(l.X2); h.Add(l.Y2); }
            else if (it is VectorText t) { h.Add(t.TextContent); h.Add(t.FontName); h.Add(t.FontSize); }
            return h.ToHashCode();
        }
    }

    // ##############################################################################
    //  Nút "xích" khóa tỉ lệ: ngoặc nối ô Rộng và ô Cao, giữa là mắt xích
    //  (mắt xích đóng + xanh = đang khóa; hở + xám = mở khóa)
    // ##############################################################################
    public class ChainToggle : Control
    {
        private bool isChecked, hover;
        private readonly ToolTip tip = new();
        public event EventHandler? CheckedChanged;

        public bool Checked
        {
            get => isChecked;
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                tip.SetToolTip(this, isChecked ? "Đang khóa tỉ lệ Rộng / Cao (bấm để mở khóa)" : "Bấm để khóa tỉ lệ Rộng / Cao");
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public ChainToggle()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Cursor = Cursors.Hand;
            Size = new Size(28, 60);
            tip.SetToolTip(this, "Bấm để khóa tỉ lệ Rộng / Cao");
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); Checked = !Checked; }

        private static GraphicsPath Round(RectangleF r, float rad)
        {
            float d = rad * 2;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Color.White);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color col = isChecked ? Color.FromArgb(0, 102, 204) : Color.FromArgb(140, 140, 140);
            float top = 11f, bot = Height - 13f, vx = Width - 8f, mid = (top + bot) / 2f;

            if (hover)
                using (var hb = new SolidBrush(Color.FromArgb(40, 0, 120, 215)))
                    g.FillRectangle(hb, vx - 11, mid - 16, 22, 32);

            // ngoặc: nối từ ô Rộng (trên) xuống ô Cao (dưới), chừa chỗ cho mắt xích ở giữa
            using (var pen = new Pen(col, 1.6f))
            {
                g.DrawLine(pen, 3, top, vx, top);
                g.DrawLine(pen, 3, bot, vx, bot);
                g.DrawLine(pen, vx, top, vx, mid - 12);
                g.DrawLine(pen, vx, mid + 12, vx, bot);

                if (isChecked)       // 2 mắt xích lồng vào nhau
                {
                    using var a = Round(new RectangleF(vx - 4f, mid - 11f, 8f, 13f), 3.5f);
                    using var b = Round(new RectangleF(vx - 4f, mid - 2f, 8f, 13f), 3.5f);
                    g.DrawPath(pen, a);
                    g.DrawPath(pen, b);
                }
                else                 // 2 mắt xích tách rời
                {
                    using var a = Round(new RectangleF(vx - 4f, mid - 14f, 8f, 11f), 3.5f);
                    using var b = Round(new RectangleF(vx - 4f, mid + 3f, 8f, 11f), 3.5f);
                    g.DrawPath(pen, a);
                    g.DrawPath(pen, b);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }
    }
}

