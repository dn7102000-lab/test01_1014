// =====================================================================
//  Form1_Canvas.cs  -  CANVAS: chọn nhiều, lưới/snap, công cụ vẽ, xoay/lật, tiện ích đối tượng
//
//  Gồm các phần (tìm nhanh bằng Ctrl+F "PHẦN"):
//    1. Chọn nhiều đối tượng (kéo khung, Ctrl+click, nhóm)   (trước đây: Form1_Select.cs)
//    2. Lưới và bắt điểm (snap / OSNAP)   (trước đây: Form1.Snap.cs)
//    3. Công cụ vẽ (đường, chữ nhật, tròn, đa giác, chữ...)   (trước đây: Form1.drawtool.cs)
//    4. Xoay / lật / vẽ hình đã biến đổi / thuộc tính mở rộng   (trước đây: Form1_Extras.cs)
//    5. Menu Biến đổi (xoay, lật, nhập góc...)   (trước đây: Form1_Transform.cs)
// =====================================================================

// =====================================================================
// PHẦN 1: Chọn nhiều đối tượng (kéo khung, Ctrl+click, nhóm)   (trước đây là file Form1_Select.cs)
// =====================================================================
// =====================================================================
//  Form1_Select.cs  -  CHỌN NHIỀU ĐỐI TƯỢNG kiểu AutoCAD
//
//  - Ctrl + click (hoặc Shift + click) : thêm / bỏ một đối tượng khỏi vùng chọn
//  - Kéo chuột trên chỗ TRỐNG          : quét chọn bằng khung
//        kéo TRÁI -> PHẢI (khung xanh nét liền) : chọn hình nằm TRỌN trong khung
//        kéo PHẢI -> TRÁI (khung xanh lá nét đứt): chọn hình CHẠM khung
//        giữ Ctrl khi quét = cộng thêm vào vùng chọn cũ
//  - Ctrl + A                           : chọn tất cả
//  - Kéo một hình đang được chọn        : cả nhóm di chuyển cùng nhau (vẫn snap lưới / OSNAP)
//  - Click (không kéo) vào 1 hình trong nhóm : chỉ còn hình đó được chọn
//  - Delete / Ctrl+C / Ctrl+X / Ctrl+V  : làm việc với cả nhóm
//  - Bảng thông số bên phải hiện đối tượng CHÍNH (cái chọn sau cùng); riêng mục LASER áp dụng cho cả nhóm
//
//  Nguồn sự thật của vùng chọn là cờ IsSelected của từng đối tượng (Form1 đã dùng sẵn để vẽ màu xanh).
//  selectedItem = đối tượng chính. Chọn nhiều thì 8 điểm kéo / đầu mút đường thẳng được ẩn.
//
//  Các hàm được gắn vào CanvasBox qua Form1_Cad.cs:
//     PreMouseDown = DrawToolMouseDown || SelectMouseDown
//     PreMouseUp   = DrawToolMouseUp   || SelectMouseUp
//     PostMouseMove -> SelectMouseMove / SyncGroupDrag
// =====================================================================
namespace test01_1014
{
    using System.Drawing.Drawing2D;

    public partial class Form1
    {
        // ----- trạng thái quét chọn -----
        private bool selMarquee;
        private bool selMarqueeAdd;
        private PointF selStart, selEnd;          // toạ độ thế giới
        private Point selStartScreen;
        private bool selMarqueeMoved;

        // ----- trạng thái kéo nhóm -----
        private PointF selGroupLast;              // vị trí đối tượng chính ở lần đồng bộ trước
        private Vectoritem? selClickCollapse;     // click không kéo vào hình trong nhóm -> thu về 1 hình

        private int SelCount => projectItems.Count(i => i.IsSelected);
        private bool MultiSelected => SelCount > 1;
        private List<Vectoritem> SelectedList() => projectItems.Where(i => i.IsSelected).ToList();

        private static bool ToggleKey => (Control.ModifierKeys & (Keys.Control | Keys.Shift)) != 0;

        private Vectoritem? HitItemAt(PointF w)
        {
            VectorLine.HitTolerance = 6f / zoom;
            for (int i = projectItems.Count - 1; i >= 0; i--)
                if (HitTestItem(projectItems[i], w)) return projectItems[i];
            return null;
        }

        private void SetPrimaryFromSelection()
        {
            if (selectedItem != null && selectedItem.IsSelected) return;
            selectedItem = projectItems.LastOrDefault(i => i.IsSelected)!;      // hình nằm trên cùng
        }

        // ----- nhóm (Group): các đối tượng cùng GroupId luôn được chọn chung -----
        // Đối tượng bị ẩn / khóa không bao giờ nằm trong vùng chọn
        private static bool ObjSelectable(Vectoritem o) => !o.Hidden && !o.Locked;

        private void ExpandGroup(Vectoritem it)
        {
            if (string.IsNullOrEmpty(it.GroupId)) return;
            foreach (var o in projectItems)
                if (o.GroupId == it.GroupId && ObjSelectable(o)) o.IsSelected = true;
        }

        private void ExpandAllGroups()
        {
            var ids = projectItems.Where(i => i.IsSelected && !string.IsNullOrEmpty(i.GroupId))
                                  .Select(i => i.GroupId!).ToHashSet();
            if (ids.Count == 0) return;
            foreach (var o in projectItems)
                if (o.GroupId != null && ids.Contains(o.GroupId) && ObjSelectable(o)) o.IsSelected = true;
        }

        private void SelectExclusive(Vectoritem it)
        {
            foreach (var o in projectItems) o.IsSelected = false;
            it.IsSelected = true;
            selectedItem = it;
            ExpandGroup(it);
        }

        private void DeselectAll()
        {
            if (ReadOnlyView) return;
            foreach (var it in projectItems) it.IsSelected = false;
            selectedItem = null!;
            UpdatePropertiesToUI();
            pictureBox1.Invalidate();
        }

        private void SelectAll()
        {
            if (ReadOnlyView) return;
            foreach (var it in projectItems) it.IsSelected = ObjSelectable(it);
            SetPrimaryFromSelection();
            UpdatePropertiesToUI();
            pictureBox1.Invalidate();
        }

        // ------------------------------------------------------------------
        //  Chuột xuống. true = đã xử lý (Form1 không nhận nữa)
        // ------------------------------------------------------------------
        private bool SelectMouseDown(MouseEventArgs e)
        {
            if (ReadOnlyView || curTool != DrawTool.None) return false;
            if (showRuler && (e.X < RulerSize || e.Y < RulerSize)) return false;
            if (zoomBadge.Contains(e.Location)) return false;

            PointF w = ScreenToWorld(e.Location);

            // --- chuột phải: giữ nguyên nhóm nếu bấm trúng hình đang được chọn ---
            if (e.Button == MouseButtons.Right)
            {
                if (SelCount > 1 && HitItemAt(w) is { IsSelected: true })
                {
                    contextWorldPos = w;
                    return true;
                }
                return false;
            }
            if (e.Button != MouseButtons.Left) return false;

            bool toggle = ToggleKey;

            // --- đang chọn 1 hình và bấm vào điểm kéo: để Form1 lo ---
            if (!toggle && (HitLineHandle(w) >= 0 || HitHandle(w) >= 0)) return false;

            Vectoritem? hit = HitItemAt(w);

            // --- Ctrl/Shift + click ---
            if (toggle)
            {
                if (hit != null)
                {
                    bool now = !hit.IsSelected;                      // cả nhóm cùng bật / tắt
                    foreach (var o in projectItems)
                        if (o == hit || (!string.IsNullOrEmpty(hit.GroupId) && o.GroupId == hit.GroupId)) o.IsSelected = now;
                    if (now) selectedItem = hit;
                    else if (selectedItem == null || !selectedItem.IsSelected)
                        selectedItem = projectItems.LastOrDefault(i => i.IsSelected)!;
                    UpdatePropertiesToUI();
                    pictureBox1.Invalidate();
                }
                else BeginMarquee(e, w, add: true);
                return true;
            }

            // --- bấm vào hình ---
            if (hit != null)
            {
                if (!hit.IsSelected)
                {
                    if (string.IsNullOrEmpty(hit.GroupId)) return false;   // hình đơn lẻ: Form1 chọn + kéo như cũ
                    SelectExclusive(hit);                                  // hình thuộc nhóm: chọn cả nhóm
                    UpdatePropertiesToUI();
                }
                if (SelCount > 1)                            // bắt đầu kéo cả nhóm
                {
                    if (selectedItem != hit) { selectedItem = hit; UpdatePropertiesToUI(); }
                    isDraggingItem = true;
                    dragOffset = new PointF(w.X - hit.X, w.Y - hit.Y);
                    selGroupLast = new PointF(hit.X, hit.Y);
                    pendingUndo = TakeSnapshot();
                    selClickCollapse = hit;
                    pictureBox1.Invalidate();
                    return true;
                }
                return false;                                // chọn 1 hình + kéo: Form1 lo như cũ
            }

            // --- bấm chỗ trống: xóa vùng chọn và bắt đầu quét ---
            BeginMarquee(e, w, add: false);
            return true;
        }

        private void BeginMarquee(MouseEventArgs e, PointF w, bool add)
        {
            selMarquee = true;
            selMarqueeAdd = add;
            selMarqueeMoved = false;
            selStart = selEnd = w;
            selStartScreen = e.Location;

            if (!add)
            {
                foreach (var it in projectItems) it.IsSelected = false;
                if (selectedItem != null) { selectedItem = null!; UpdatePropertiesToUI(); }
            }
            pictureBox1.Invalidate();
        }

        // ------------------------------------------------------------------
        //  Chuột di chuyển (gọi sau Form1). true = đang quét chọn
        // ------------------------------------------------------------------
        private bool SelectMouseMove(MouseEventArgs e)
        {
            if (!selMarquee) return false;
            selEnd = ScreenToWorld(e.Location);
            if (!selMarqueeMoved &&
                (Math.Abs(e.X - selStartScreen.X) > 3 || Math.Abs(e.Y - selStartScreen.Y) > 3))
                selMarqueeMoved = true;
            pictureBox1.Invalidate();
            return true;
        }

        // Kéo nhóm: Form1 + Snap đã đặt đối tượng chính, ở đây dời các đối tượng còn lại theo cùng độ lệch
        private void SyncGroupDrag()
        {
            if (!isDraggingItem || selectedItem == null || SelCount < 2) return;
            float dx = selectedItem.X - selGroupLast.X, dy = selectedItem.Y - selGroupLast.Y;
            if (Math.Abs(dx) < 1e-4f && Math.Abs(dy) < 1e-4f) return;

            selClickCollapse = null;                       // đã kéo thật sự
            foreach (var it in projectItems)
                if (it.IsSelected && it != selectedItem) ShiftItem(it, dx, dy);
            selGroupLast = new PointF(selectedItem.X, selectedItem.Y);
            pictureBox1.Invalidate();
        }

        // ------------------------------------------------------------------
        //  Chuột nhả. true = đã xử lý
        // ------------------------------------------------------------------
        private bool SelectMouseUp(MouseEventArgs e)
        {
            if (selMarquee && e.Button == MouseButtons.Left)
            {
                selMarquee = false;
                if (selMarqueeMoved) ApplyMarquee();
                pictureBox1.Invalidate();
                return true;
            }

            // click (không kéo) vào 1 hình trong nhóm -> chỉ còn hình đó
            if (e.Button == MouseButtons.Left && selClickCollapse != null)
            {
                var keep = selClickCollapse;
                selClickCollapse = null;
                if (pendingUndo != null)                   // pendingUndo còn = chưa hề di chuyển
                {
                    SelectExclusive(keep);                 // chỉ còn hình đó (kèm cả nhóm nếu nó thuộc nhóm)
                    UpdatePropertiesToUI();
                    pictureBox1.Invalidate();
                }
            }
            return false;                                  // Form1 tự dọn cờ kéo
        }

        private void ApplyMarquee()
        {
            var r = RectangleF.FromLTRB(Math.Min(selStart.X, selEnd.X), Math.Min(selStart.Y, selEnd.Y),
                                        Math.Max(selStart.X, selEnd.X), Math.Max(selStart.Y, selEnd.Y));
            bool window = selEnd.X >= selStart.X;          // trái -> phải: phải nằm trọn; phải -> trái: chỉ cần chạm

            foreach (var it in projectItems)
            {
                RectangleF b = ItemBounds(it);
                bool inside = window ? r.Contains(b) : r.IntersectsWith(b) || r.Contains(b);
                if (inside && ObjSelectable(it)) it.IsSelected = true;
            }
            ExpandAllGroups();                             // chạm 1 hình của nhóm = chọn cả nhóm
            SetPrimaryFromSelection();
            UpdatePropertiesToUI();
        }

        // ------------------------------------------------------------------
        //  Vẽ: khung quét chọn + khung bao của nhóm (toạ độ màn hình, vẽ lớp phủ)
        // ------------------------------------------------------------------
        private Rectangle WorldRectToScreen(RectangleF r) => Rectangle.FromLTRB(
            (int)Math.Round(r.Left * zoom + pan.X), (int)Math.Round(r.Top * zoom + pan.Y),
            (int)Math.Round(r.Right * zoom + pan.X), (int)Math.Round(r.Bottom * zoom + pan.Y));

        private void PaintSelectionOverlay(Graphics g)
        {
            if (ReadOnlyView) return;

            var state = g.Save();
            try
            {
                g.SetClip(new Rectangle(Inset, Inset,
                                        Math.Max(0, pictureBox1.Width - Inset),
                                        Math.Max(0, pictureBox1.Height - Inset)), CombineMode.Intersect);
                g.SmoothingMode = SmoothingMode.None;

                // khung bao cả nhóm
                var sel = SelectedList();
                if (sel.Count > 1)
                {
                    RectangleF u = ItemBounds(sel[0]);
                    foreach (var it in sel.Skip(1)) u = RectangleF.Union(u, ItemBounds(it));
                    Rectangle sr = WorldRectToScreen(u);
                    sr.Inflate(5, 5);
                    using var pen = new Pen(Color.FromArgb(0, 120, 215), 1f) { DashStyle = DashStyle.Dash };
                    g.DrawRectangle(pen, sr);

                    string txt = $"{sel.Count} đối tượng";
                    using var f = new Font("Segoe UI", 8.5f);
                    var sz = g.MeasureString(txt, f);
                    using var bg = new SolidBrush(Color.FromArgb(0, 120, 215));
                    g.FillRectangle(bg, sr.Left, sr.Top - sz.Height - 2, sz.Width + 6, sz.Height + 2);
                    g.DrawString(txt, f, Brushes.White, sr.Left + 3, sr.Top - sz.Height - 1);
                }

                // khung quét
                if (selMarquee && selMarqueeMoved)
                {
                    bool window = selEnd.X >= selStart.X;
                    var r = RectangleF.FromLTRB(Math.Min(selStart.X, selEnd.X), Math.Min(selStart.Y, selEnd.Y),
                                                Math.Max(selStart.X, selEnd.X), Math.Max(selStart.Y, selEnd.Y));
                    Rectangle sr = WorldRectToScreen(r);
                    Color c = window ? Color.FromArgb(0, 120, 215) : Color.FromArgb(40, 160, 70);
                    using var fill = new SolidBrush(Color.FromArgb(35, c));
                    using var pen = new Pen(c, 1f) { DashStyle = window ? DashStyle.Solid : DashStyle.Dash };
                    g.FillRectangle(fill, sr);
                    g.DrawRectangle(pen, sr);
                }
            }
            finally { g.Restore(state); }
        }

        // ------------------------------------------------------------------
        //  Sao chép / dán cả nhóm
        // ------------------------------------------------------------------
        private readonly List<Vectoritem> clipboardGroup = new();

        private void CopyGroup()
        {
            clipboardGroup.Clear();
            foreach (var it in SelectedList())
            {
                var c = CloneItem(it);
                if (c != null) { c.GroupId = it.GroupId; clipboardGroup.Add(c); }   // giữ nhãn nhóm cũ để dán còn biết ai chung nhóm
            }
            clipboardItem = selectedItem != null ? CloneItem(selectedItem) : null;   // giữ nguyên hành vi cũ cho menu Dán
        }

        private bool PasteGroupAt(PointF target)
        {
            if (clipboardGroup.Count < 2) return false;

            RectangleF u = ItemBounds(clipboardGroup[0]);
            foreach (var it in clipboardGroup.Skip(1)) u = RectangleF.Union(u, ItemBounds(it));
            float dx = target.X - u.Left, dy = target.Y - u.Top;

            PushUndo();
            foreach (var o in projectItems) o.IsSelected = false;
            var gmap = new Dictionary<string, string>();       // nhóm cũ -> nhóm mới (bản dán là nhóm riêng)
            Vectoritem? last = null;
            foreach (var src in clipboardGroup)
            {
                var c = CloneItem(src);
                if (c == null) continue;
                ShiftItem(c, dx, dy);
                if (!string.IsNullOrEmpty(src.GroupId))
                {
                    if (!gmap.TryGetValue(src.GroupId, out var ng)) gmap[src.GroupId] = ng = Guid.NewGuid().ToString("N");
                    c.GroupId = ng;
                }
                c.IsSelected = true;
                projectItems.Add(c);
                last = c;
            }
            if (last != null) selectedItem = last;
            UpdatePropertiesToUI();
            pictureBox1.Invalidate();
            return true;
        }
    }
}

// =====================================================================
// PHẦN 2: Lưới và bắt điểm (snap / OSNAP)   (trước đây là file Form1.Snap.cs)
// =====================================================================
// =====================================================================
//  Form1.Snap.cs  -  SNAP lưới + OSNAP (bắt điểm đối tượng) kiểu AutoCAD
//
//  - Snap lưới : bước 1 / 2 / 5 (=0.5 cm) / 10 (=1 cm) / 20 mm. Mọi thao tác kéo
//                (di chuyển, đổi kích thước, đầu mút đường thẳng) nhảy theo bước này.
//  - Chấm lưới : vẽ chấm tại các giao điểm của lưới snap (tự thưa bớt khi zoom nhỏ).
//  - OSNAP     : bắt điểm cuối, trung điểm, tâm, góc phần tư của các đối tượng khác.
//  - Ô vuông xanh = điểm cuối, tam giác = trung điểm, tròn = tâm, thoi = góc phần tư,
//    dấu + cam = giao điểm lưới. Hover gần điểm snap là hiện dấu trước khi bấm.
//  - Nhãn kích thước hiện ngay cạnh con trỏ khi kéo (Rộng x Cao / Dài + góc / Toạ độ)
//    => kéo thả là đủ, không cần gõ số.
//  - F9 = bật/tắt snap lưới, F3 = bật/tắt OSNAP. Menu "Snap" trên thanh menu.
//  - Giữ Shift khi kéo đầu đường thẳng: khóa góc 45° và dài theo bội số của bước.
//  - Cài đặt snap được nhớ lại ở %AppData%\test01_1014\snap.json
// =====================================================================
namespace test01_1014
{
    using System.Drawing.Drawing2D;
    using System.Text.Json;

    public class SnapCfg
    {
        public bool Grid { get; set; } = true;
        public bool Obj { get; set; } = true;
        public float StepMm { get; set; } = 5f;
    }

    public partial class Form1
    {
        private enum SnKind { None, Grid, End, Mid, Center, Quad }

        private readonly struct SnHit
        {
            public readonly PointF P;
            public readonly SnKind Kind;
            public SnHit(PointF p, SnKind k) { P = p; Kind = k; }
        }
        private static readonly SnHit SnNone = new SnHit(PointF.Empty, SnKind.None);

        // ----- trạng thái -----
        private bool snGridOn = true, snObjOn = true;
        private float snStepMm = 5f;                                  // 5 mm = 0.5 cm
        private static readonly float[] SnapStepsMm = { 1f, 2f, 5f, 10f, 20f };
        private float SnStepPx => snStepMm / MmPerPx;                 // bước snap theo pixel canvas

        private SnHit snMarker = SnNone;     // điểm snap đang hiển thị
        private bool snSkipNew;              // true = AddItem không snap lại (công cụ vẽ đã snap rồi)
        private string? snInfo;              // nhãn kích thước khi kéo
        private Point snCursor;              // vị trí con trỏ (toạ độ màn hình)

        private ToolStripMenuItem miSnapGrid = null!, miSnapObj = null!;
        private readonly List<ToolStripMenuItem> miSnapSteps = new();

        // =================================================================
        //  Khởi tạo (gọi 1 lần trong constructor Form1)
        // =================================================================
        private bool snInited;

        private void InitSnap()
        {
            if (snInited) return;            // chỉ chạy 1 lần (constructor hoặc OnHandleCreated gọi trước đều được)
            snInited = true;
            LoadSnapCfg();

            // Ô nhập kích thước: hiện mm, 2 số lẻ; mũi tên lên/xuống = 1 bước snap
            numWidth.DecimalPlaces = 2; numHeight.DecimalPlaces = 2;
            numWidth.Increment = (decimal)snStepMm; numHeight.Increment = (decimal)snStepMm;

            var root = new ToolStripMenuItem("Snap");
            miSnapGrid = new ToolStripMenuItem("Snap lưới (F9)") { CheckOnClick = true, Checked = snGridOn };
            miSnapGrid.CheckedChanged += (s, e) => { snGridOn = miSnapGrid.Checked; SnapSettingsChanged(); };
            miSnapObj = new ToolStripMenuItem("Bắt điểm đối tượng - OSNAP (F3)") { CheckOnClick = true, Checked = snObjOn };
            miSnapObj.CheckedChanged += (s, e) => { snObjOn = miSnapObj.Checked; SnapSettingsChanged(); };
            root.DropDownItems.Add(miSnapGrid);
            root.DropDownItems.Add(miSnapObj);
            root.DropDownItems.Add(new ToolStripSeparator());

            foreach (float mm in SnapStepsMm)
            {
                float v = mm;
                var mi = new ToolStripMenuItem(StepText(v)) { Checked = Math.Abs(v - snStepMm) < 0.001f };
                mi.Click += (s, e) => { snStepMm = v; SnapSettingsChanged(); };
                miSnapSteps.Add(mi);
                root.DropDownItems.Add(mi);
            }

            int idx = menuStrip1.Items.IndexOf(viewToolStripMenuItem);
            if (idx >= 0) menuStrip1.Items.Insert(idx + 1, root); else menuStrip1.Items.Add(root);

            // Phím tắt
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F9) { miSnapGrid.Checked = !miSnapGrid.Checked; e.Handled = true; }
                else if (e.KeyCode == Keys.F3) { miSnapObj.Checked = !miSnapObj.Checked; e.Handled = true; }
            };

            // Hover: hiện dấu snap khi con trỏ gần điểm bắt được
            pictureBox1.MouseMove += SnapHover;
            pictureBox1.MouseUp += (s, e) => { snMarker = SnNone; snInfo = null; pictureBox1.Invalidate(); };
            pictureBox1.MouseLeave += (s, e) =>
            {
                if (snMarker.Kind != SnKind.None) pictureBox1.Invalidate(SnRect(snMarker));
                snMarker = SnNone;
            };
        }

        private static string StepText(float mm) =>
            mm < 5f ? $"{mm:0.#} mm" : $"{mm / 10f:0.#} cm  ({mm:0.#} mm)";

        private void SnapSettingsChanged()
        {
            for (int i = 0; i < miSnapSteps.Count; i++)
                miSnapSteps[i].Checked = Math.Abs(SnapStepsMm[i] - snStepMm) < 0.001f;
            numWidth.Increment = (decimal)snStepMm;
            numHeight.Increment = (decimal)snStepMm;
            snMarker = SnNone;
            SaveSnapCfg();
            pictureBox1.Invalidate();
        }

        // =================================================================
        //  Tìm điểm snap
        // =================================================================
        private static float SnDist(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }

        private PointF GridSnap(PointF p)
        {
            float s = SnStepPx;
            return new PointF(MathF.Round(p.X / s) * s, MathF.Round(p.Y / s) * s);
        }

        // Các điểm đặc biệt của 1 đối tượng
        private static IEnumerable<(PointF P, SnKind K)> SnapKeyPoints(Vectoritem it)
        {
            if (it is VectorLine l)
            {
                yield return (new PointF(l.X, l.Y), SnKind.End);
                yield return (new PointF(l.X2, l.Y2), SnKind.End);
                yield return (new PointF((l.X + l.X2) / 2, (l.Y + l.Y2) / 2), SnKind.Mid);
                yield break;
            }

            float L = it.X, T = it.Y, R = it.X + it.Width, B = it.Y + it.Height;
            float cx = (L + R) / 2, cy = (T + B) / 2;

            if (it is VectorPolygon pg)
            {
                foreach (var p in pg.GetPoints()) yield return (p, SnKind.End);
            }
            else if (it is VectorEllipse)
            {
                yield return (new PointF(cx, T), SnKind.Quad);
                yield return (new PointF(R, cy), SnKind.Quad);
                yield return (new PointF(cx, B), SnKind.Quad);
                yield return (new PointF(L, cy), SnKind.Quad);
            }
            else   // chữ nhật, ảnh, chữ: 4 góc + 4 trung điểm cạnh
            {
                yield return (new PointF(L, T), SnKind.End);
                yield return (new PointF(R, T), SnKind.End);
                yield return (new PointF(R, B), SnKind.End);
                yield return (new PointF(L, B), SnKind.End);
                yield return (new PointF(cx, T), SnKind.Mid);
                yield return (new PointF(R, cy), SnKind.Mid);
                yield return (new PointF(cx, B), SnKind.Mid);
                yield return (new PointF(L, cy), SnKind.Mid);
            }
            yield return (new PointF(cx, cy), SnKind.Center);
        }

        // Điểm của đối tượng KHÁC gần p nhất (trong vòng 10 px màn hình)
        private SnHit? FindObjectSnap(PointF p, Vectoritem? exclude)
        {
            float best = 10f / zoom;
            SnHit? res = null;
            foreach (var it in projectItems)
            {
                if (it == exclude || it.Hidden || (isDraggingItem && it.IsSelected)) continue;   // không snap vào chính nhóm đang kéo
                foreach (var (pt, k) in SnapKeyPoints(it))
                {
                    float d = SnDist(pt, p);
                    if (d < best) { best = d; res = new SnHit(pt, k); }
                }
            }
            return res;
        }

        // Dùng cho hover: chỉ trả về khi con trỏ nằm gần điểm snap
        private SnHit? FindSnapNear(PointF w, Vectoritem? exclude)
        {
            if (snObjOn)
            {
                var o = FindObjectSnap(w, exclude);
                if (o != null) return o;
            }
            if (snGridOn)
            {
                var g = GridSnap(w);
                if (SnDist(g, w) <= 10f / zoom) return new SnHit(g, SnKind.Grid);
            }
            return null;
        }

        // Dùng khi kéo: OSNAP ưu tiên, không có thì nhảy theo lưới (luôn luôn)
        private SnHit SnapPoint(PointF p, Vectoritem? exclude)
        {
            if (snObjOn)
            {
                var o = FindObjectSnap(p, exclude);
                if (o != null) return o.Value;
            }
            if (snGridOn) return new SnHit(GridSnap(p), SnKind.Grid);
            return new SnHit(p, SnKind.None);
        }

        // =================================================================
        //  Áp dụng snap vào từng thao tác (được gọi từ Form1.cs)
        // =================================================================

        // Thêm đối tượng mới: góc trên-trái nhảy vào điểm lưới gần nhất
        private void SnapNewItem(Vectoritem it)
        {
            if (!snGridOn || snSkipNew) return;
            var p = GridSnap(new PointF(it.X, it.Y));
            it.MoveTo(p.X, p.Y);
        }

        // Kéo di chuyển: trả về vị trí góc trên-trái sau khi snap
        private PointF SnapMoveTopLeft(Vectoritem it, float nx, float ny)
        {
            snMarker = SnNone;

            if (snObjOn)
            {
                float best = 10f / zoom, bdx = 0, bdy = 0;
                bool found = false;
                SnHit target = SnNone;
                float ox = nx - it.X, oy = ny - it.Y;

                foreach (var (mp, _) in SnapKeyPoints(it))
                {
                    var m = new PointF(mp.X + ox, mp.Y + oy);
                    foreach (var o in projectItems)
                    {
                        if (o == it || o.Hidden) continue;
                        foreach (var (tp, tk) in SnapKeyPoints(o))
                        {
                            float d = SnDist(m, tp);
                            if (d < best)
                            {
                                best = d; found = true;
                                bdx = tp.X - m.X; bdy = tp.Y - m.Y;
                                target = new SnHit(tp, tk);
                            }
                        }
                    }
                }
                if (found) { snMarker = target; return new PointF(nx + bdx, ny + bdy); }
            }

            if (snGridOn)
            {
                var g = GridSnap(new PointF(nx, ny));
                snMarker = new SnHit(g, SnKind.Grid);
                return g;
            }
            return new PointF(nx, ny);
        }

        // Kéo 8 điểm: snap vị trí của ĐIỂM KÉO (không phải con trỏ), trả về "con trỏ ảo"
        // để ApplyResize tính tiếp như bình thường.
        private PointF SnapResizeMouse(PointF w)
        {
            snMarker = SnNone;
            if (selectedItem == null || (!snGridOn && !snObjOn)) return w;

            int h = resizeHandle;
            if (resizeRot != 0f && h % 2 == 1) return w;     // cạnh giữa của hình đã xoay: trục lệch lưới, không snap

            PointF start = HandlePointsOf(resizeStart, resizeRot)[h];
            var target = new PointF(start.X + (w.X - resizeMouseStart.X),
                                    start.Y + (w.Y - resizeMouseStart.Y));
            SnHit hit = SnapPoint(target, selectedItem);
            if (hit.Kind == SnKind.None) return w;

            bool axisX = h != 1 && h != 5;     // điểm giữa cạnh trên/dưới chỉ đổi theo trục Y
            bool axisY = h != 3 && h != 7;     // điểm giữa cạnh trái/phải chỉ đổi theo trục X
            float sx = axisX ? hit.P.X : target.X;
            float sy = axisY ? hit.P.Y : target.Y;

            snMarker = new SnHit(new PointF(sx, sy), hit.Kind);
            return new PointF(resizeMouseStart.X + (sx - start.X), resizeMouseStart.Y + (sy - start.Y));
        }

        // Kéo đầu mút đường thẳng
        private PointF SnapLinePoint(VectorLine ln, PointF w)
        {
            SnHit hit = SnapPoint(w, ln);
            snMarker = hit.Kind == SnKind.None ? SnNone : hit;
            return hit.P;
        }

        // Chạy sau khi Form1 xử lý MouseMove: ghi đè vị trí/kích thước bằng giá trị đã snap CHÍNH XÁC
        // (con trỏ chỉ có độ chính xác 1 pixel màn hình nên Form1 tự tính sẽ lệch vài phần mm).
        private void SnapPostMove(MouseEventArgs e)
        {
            if ((!snGridOn && !snObjOn) || ReadOnlyView || selectedItem == null) return;
            if ((e.Button & MouseButtons.Left) == 0) return;
            PointF w = ScreenToWorld(e.Location);

            if (lineHandle >= 0 && selectedItem is VectorLine ln)
            {
                float ox = lineHandle == 0 ? ln.X2 : ln.X, oy = lineHandle == 0 ? ln.Y2 : ln.Y;
                PointF p;
                if ((ModifierKeys & Keys.Shift) != 0)          // khóa góc 45°, dài theo bội số bước snap
                {
                    float dx = w.X - ox, dy = w.Y - oy;
                    double ang = Math.Atan2(dy, dx), len = Math.Sqrt(dx * dx + dy * dy);
                    double sn = Math.Round(ang / (Math.PI / 4)) * (Math.PI / 4);
                    if (snGridOn) len = Math.Round(len / SnStepPx) * SnStepPx;
                    p = new PointF(ox + (float)(len * Math.Cos(sn)), oy + (float)(len * Math.Sin(sn)));
                    snMarker = SnNone;
                }
                else p = SnapLinePoint(ln, w);

                CommitPendingUndo();
                if (lineHandle == 0) { ln.X = p.X; ln.Y = p.Y; } else { ln.X2 = p.X; ln.Y2 = p.Y; }
                SnLineInfo(ln, e.Location);
                pictureBox1.Invalidate();
            }
            else if (resizeHandle >= 0)
            {
                CommitPendingUndo();
                ApplyResize(SnapResizeMouse(w));
                SnSizeInfo(selectedItem, e.Location);
                pictureBox1.Invalidate();
            }
            else if (isDraggingItem)
            {
                var sp = SnapMoveTopLeft(selectedItem, w.X - dragOffset.X, w.Y - dragOffset.Y);
                if (sp.X != selectedItem.X || sp.Y != selectedItem.Y)
                {
                    CommitPendingUndo();
                    selectedItem.MoveTo(sp.X, sp.Y);
                }
                SnMoveInfo(selectedItem, e.Location);
                pictureBox1.Invalidate();
            }
        }

        // ----- nhãn kích thước cạnh con trỏ -----
        private void SnSizeInfo(Vectoritem it, Point cur)
        {
            snCursor = cur;
            snInfo = $"{it.Width * MmPerPx:0.##} × {it.Height * MmPerPx:0.##} mm";
        }

        private void SnMoveInfo(Vectoritem it, Point cur)
        {
            snCursor = cur;
            snInfo = $"X {it.X * MmPerPx:0.##}   Y {it.Y * MmPerPx:0.##} mm";
        }

        private void SnLineInfo(VectorLine ln, Point cur)
        {
            snCursor = cur;
            float dx = ln.X2 - ln.X, dy = ln.Y2 - ln.Y;
            double len = Math.Sqrt(dx * dx + dy * dy) * MmPerPx;
            double ang = Math.Atan2(-dy, dx) * 180.0 / Math.PI;      // trục Y hướng lên cho dễ hiểu
            if (ang < 0) ang += 360;
            snInfo = $"L = {len:0.##} mm   ∠ {ang:0.#}°";
        }

        // =================================================================
        //  Hover: hiện dấu snap khi con trỏ lại gần
        // =================================================================
        private Rectangle SnRect(SnHit h)
        {
            int x = (int)(pan.X + h.P.X * zoom), y = (int)(pan.Y + h.P.Y * zoom);
            return new Rectangle(x - 16, y - 16, 200, 46);
        }

        private void SnapHover(object? s, MouseEventArgs e)
        {
            snCursor = e.Location;
            if (isPanning || isDraggingItem || selMarquee || resizeHandle >= 0 || lineHandle >= 0 || ReadOnlyView
                || curTool != DrawTool.None) return;      // đang dùng công cụ vẽ: Form1.DrawTool.cs tự lo dấu snap

            SnHit old = snMarker;
            SnHit? hit = (snGridOn || snObjOn) ? FindSnapNear(ScreenToWorld(e.Location), null) : null;
            snMarker = hit ?? SnNone;

            if (old.Kind != snMarker.Kind || old.P != snMarker.P)
            {
                if (old.Kind != SnKind.None) pictureBox1.Invalidate(SnRect(old));
                if (snMarker.Kind != SnKind.None) pictureBox1.Invalidate(SnRect(snMarker));
            }
        }

        // =================================================================
        //  Vẽ
        // =================================================================
        private static readonly float[] DotMultipliers = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000 };

        // Chấm tại giao điểm lưới snap (đang ở hệ toạ độ canvas, vẽ TRƯỚC các đối tượng)
        // Các hàm Draw* dưới đây giữ lại cho tương thích: lớp phủ mới (Form1.Cad.cs) đã vẽ thay.
        private void DrawSnapDots(Graphics g) { }
        private void DrawSnapMarker(Graphics g) { }
        private void DrawSnapOverlay(Graphics g) { }

        private void PaintSnapDots(Graphics g)
        {
            if (!snGridOn || ReadOnlyView) return;

            float step = SnStepPx, mul = DotMultipliers[DotMultipliers.Length - 1];
            foreach (float m in DotMultipliers)
                if (step * m * zoom >= 9f) { mul = m; break; }     // thưa bớt để chấm cách nhau >= 9 px
            float s = step * mul;

            var tl = ScreenToWorld(new Point(0, 0));
            var bot = ScreenToWorld(new Point(pictureBox1.Width, pictureBox1.Height));
            int i0 = (int)Math.Floor(tl.X / s), i1 = (int)Math.Ceiling(bot.X / s);
            int j0 = (int)Math.Floor(tl.Y / s), j1 = (int)Math.Ceiling(bot.Y / s);
            if ((long)(i1 - i0 + 1) * (j1 - j0 + 1) > 60000) return;

            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.None;
            float d = 2f / zoom;
            using var brush = new SolidBrush(Color.FromArgb(150, 80, 80, 80));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    g.FillRectangle(brush, i * s - d / 2, j * s - d / 2, d, d);
            g.SmoothingMode = old;
        }

        // Dấu snap (hệ toạ độ canvas, vẽ SAU các đối tượng)
        private void PaintSnapMarker(Graphics g)
        {
            if (ReadOnlyView || snMarker.Kind == SnKind.None) return;

            float h = 6f / zoom;
            PointF p = snMarker.P;
            Color c = snMarker.Kind == SnKind.Grid ? Color.OrangeRed : Color.FromArgb(0, 170, 0);
            using var pen = new Pen(c, 2f / zoom);

            switch (snMarker.Kind)
            {
                case SnKind.Grid:
                    g.DrawLine(pen, p.X - h, p.Y, p.X + h, p.Y);
                    g.DrawLine(pen, p.X, p.Y - h, p.X, p.Y + h);
                    break;
                case SnKind.End:
                    g.DrawRectangle(pen, p.X - h, p.Y - h, 2 * h, 2 * h);
                    break;
                case SnKind.Mid:
                    g.DrawPolygon(pen, new[] {
                        new PointF(p.X, p.Y - h), new PointF(p.X + h, p.Y + h), new PointF(p.X - h, p.Y + h) });
                    break;
                case SnKind.Center:
                    g.DrawEllipse(pen, p.X - h, p.Y - h, 2 * h, 2 * h);
                    break;
                case SnKind.Quad:
                    g.DrawPolygon(pen, new[] {
                        new PointF(p.X, p.Y - h), new PointF(p.X + h, p.Y),
                        new PointF(p.X, p.Y + h), new PointF(p.X - h, p.Y) });
                    break;
            }
        }

        // Nhãn trạng thái + tên điểm snap + kích thước khi kéo (toạ độ màn hình, vẽ cuối cùng)
        private void PaintSnapOverlay(Graphics g)
        {
            if (ReadOnlyView) return;

            // 1) Nhãn trạng thái góc dưới trái
            string stepLabel = snStepMm < 5f ? $"{snStepMm:0.#} mm" : $"{snStepMm / 10f:0.#} cm";
            string st = (snGridOn ? "SNAP " + stepLabel : "SNAP tắt")
                        + "   " + (snObjOn ? "OSNAP" : "OSNAP tắt");     // ví dụ: "SNAP 0.5 cm   OSNAP"
            using (var f = new Font("Segoe UI", 9f))
            {
                var sz = g.MeasureString(st, f);
                float bx = Inset + 8, by = pictureBox1.Height - sz.Height - 12;
                using (var bg = new SolidBrush(Color.FromArgb(210, 255, 255, 255)))
                    g.FillRectangle(bg, bx, by, sz.Width + 8, sz.Height + 4);
                g.DrawString(st, f, snGridOn || snObjOn ? Brushes.DarkGreen : Brushes.Gray, bx + 4, by + 2);
            }

            // 2) Tên điểm snap cạnh dấu
            if (snMarker.Kind != SnKind.None && snMarker.Kind != SnKind.Grid)
            {
                string name = snMarker.Kind switch
                {
                    SnKind.End => "Điểm cuối",
                    SnKind.Mid => "Trung điểm",
                    SnKind.Center => "Tâm",
                    _ => "Góc phần tư"
                };
                float sx = pan.X + snMarker.P.X * zoom, sy = pan.Y + snMarker.P.Y * zoom;
                using var f = new Font("Segoe UI", 8.5f);
                var sz = g.MeasureString(name, f);
                using var bg = new SolidBrush(Color.FromArgb(235, 255, 255, 200));
                g.FillRectangle(bg, sx + 12, sy + 10, sz.Width + 6, sz.Height + 2);
                g.DrawString(name, f, Brushes.Black, sx + 14, sy + 11);
            }

            // 3) Kích thước / dài / toạ độ khi đang kéo
            if (snInfo != null)
            {
                using var f = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                var sz = g.MeasureString(snInfo, f);
                float x = Math.Min(snCursor.X + 18, pictureBox1.Width - sz.Width - 10);
                float y = Math.Min(snCursor.Y + 24, pictureBox1.Height - sz.Height - 10);
                using var bg = new SolidBrush(Color.FromArgb(225, 35, 35, 35));
                g.FillRectangle(bg, x - 4, y - 2, sz.Width + 8, sz.Height + 4);
                g.DrawString(snInfo, f, Brushes.White, x, y);
            }
        }

        // =================================================================
        //  Lưu / nạp cài đặt snap
        // =================================================================
        private static string SnapCfgPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "test01_1014", "snap.json");

        private void LoadSnapCfg()
        {
            try
            {
                if (!File.Exists(SnapCfgPath)) return;
                var c = JsonSerializer.Deserialize<SnapCfg>(File.ReadAllText(SnapCfgPath));
                if (c == null) return;
                snGridOn = c.Grid;
                snObjOn = c.Obj;
                if (SnapStepsMm.Contains(c.StepMm)) snStepMm = c.StepMm;
            }
            catch { /* file hỏng thì dùng mặc định */ }
        }

        private void SaveSnapCfg()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SnapCfgPath)!);
                File.WriteAllText(SnapCfgPath,
                    JsonSerializer.Serialize(new SnapCfg { Grid = snGridOn, Obj = snObjOn, StepMm = snStepMm }));
            }
            catch { }
        }
    }
}

// =====================================================================
// PHẦN 3: Công cụ vẽ (đường, chữ nhật, tròn, đa giác, chữ...)   (trước đây là file Form1.drawtool.cs)
// =====================================================================
// =====================================================================
//  Form1.DrawTool.cs  -  Vẽ hình / chữ kiểu CAD: KÉO THẢ hoặc CLICK-CLICK + nhập số
//
//  Chọn Line / Rectangle / Circle / đa giác / Text từ menu -> con trỏ thành dấu +,
//  bảng "Vẽ: ..." hiện góc trên-trái canvas. Có 3 cách vẽ:
//
//   1) KÉO THẢ : nhấn chuột trái tại điểm đầu, kéo, thả = tạo hình. Hình hiện theo con trỏ,
//                nhãn kích thước chạy cạnh con trỏ, các ô số trong bảng cập nhật theo.
//   2) CLICK-CLICK (như AutoCAD): click 1 lần = điểm đầu, rê chuột (hình chạy theo),
//                click lần 2 = điểm cuối.
//   3) NHẬP SỐ : click điểm đầu rồi GÕ SỐ ngay (không cần bấm vào ô; số đầu tiên tự nhảy vào ô
//                Rộng/Dài), Tab sang ô kế, Enter = tạo đúng kích thước đó tại điểm đầu.
//                Chưa click mà bấm Enter / nút "Tạo ở giữa" = tạo giữa vùng đang nhìn.
//
//   Shift : hình vuông/tròn đều, đường thẳng khóa góc 45°.   Esc : bỏ điểm đầu / thoát công cụ.
//   Chuột phải : như Esc.   Tick "Vẽ liên tục" để vẽ nhiều hình liền.
//   Kích thước lần trước được nhớ cho lần vẽ sau.
// =====================================================================
namespace test01_1014
{
    using System.Globalization;
    using test01_1014;
    using static System.Windows.Forms.LinkLabel;
    using Point = System.Drawing.Point;   // tránh nhập nhằng với OpenCvSharp.Point
    using Size = System.Drawing.Size;
    using Font = System.Drawing.Font;

    public partial class Form1
    {
        private enum DrawTool { None, Rectangle, Ellipse, Polygon, Line, Text }

        private DrawTool curTool = DrawTool.None;
        private PolygonKind curPoly = PolygonKind.Diamond;

        private bool dtDragging;      // đang nhấn giữ chuột
        private bool dtMoved;         // đã kéo đủ xa (>= 4 px)
        private bool dtAnchored;      // đã click điểm đầu, chờ điểm cuối / số nhập
        private PointF dtStart;
        private Point dtStartScreen;
        private Vectoritem? dtPreview;
        private float dtPxPerPt = 1.6f;

        // Giá trị nhớ lại cho từng công cụ: A = Rộng/Dài/Cỡ chữ, B = Cao/Góc
        private readonly Dictionary<DrawTool, (decimal A, decimal B)> dtVals = new()
        {
            [DrawTool.Rectangle] = (30m, 20m),
            [DrawTool.Ellipse] = (25m, 25m),
            [DrawTool.Polygon] = (25m, 25m),
            [DrawTool.Line] = (30m, 0m),
            [DrawTool.Text] = (18m, 0m),
        };

        private Panel dtPanel = null!;
        private Label dtTitle = null!, dtLblA = null!, dtLblB = null!, dtLblT = null!, dtHint = null!;
        private NumericUpDown dtA = null!, dtB = null!;
        private TextBox dtText = null!;
        private CheckBox dtRepeat = null!;
        private Button dtBtnCreate = null!, dtBtnCancel = null!;

        // =================================================================
        //  Khởi tạo
        // =================================================================
        private void InitDrawTool()
        {
            if (dtPanel != null) return;          // chỉ chạy 1 lần

            dtPanel = new Panel
            {
                Width = 252,
                Height = 200,
                Visible = false,
                BackColor = Color.FromArgb(250, 250, 250),
                BorderStyle = BorderStyle.FixedSingle
            };

            dtTitle = new Label { Left = 8, Top = 6, Width = 232, Height = 20, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            dtLblA = new Label { Left = 8, Width = 92, Height = 20 };
            dtA = new NumericUpDown { Left = 104, Width = 138, DecimalPlaces = 2, Minimum = 0.1m, Maximum = 10000m };
            dtLblB = new Label { Left = 8, Width = 92, Height = 20 };
            dtB = new NumericUpDown { Left = 104, Width = 138, DecimalPlaces = 2, Minimum = 0.1m, Maximum = 10000m };
            dtLblT = new Label { Left = 8, Width = 92, Height = 20, Text = "Nội dung" };
            dtText = new TextBox { Left = 104, Width = 138, Text = "Văn bản" };
            dtRepeat = new CheckBox { Left = 8, Width = 232, Text = "Vẽ liên tục (không tự thoát công cụ)" };
            dtBtnCreate = new Button { Left = 8, Width = 140, Height = 26, Text = "Tạo ở giữa (Enter)" };
            dtBtnCancel = new Button { Left = 154, Width = 88, Height = 26, Text = "Hủy (Esc)" };
            dtHint = new Label { Left = 8, Width = 232, Height = 62, ForeColor = Color.DimGray, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };

            dtPanel.Controls.AddRange(new Control[] {
                dtTitle, dtLblA, dtA, dtLblB, dtB, dtLblT, dtText, dtRepeat, dtBtnCreate, dtBtnCancel, dtHint });
            pictureBox1.Controls.Add(dtPanel);

            dtBtnCreate.Click += (s, e) => CreateFromPanel();
            dtBtnCancel.Click += (s, e) => CancelTool();

            void EnterCreates(object? s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true; e.Handled = true;
                CreateFromPanel();
            }
            dtA.KeyDown += EnterCreates;
            dtB.KeyDown += EnterCreates;
            dtText.KeyDown += EnterCreates;

            // Gõ số khi đã có điểm đầu: hình xem trước cập nhật theo số đang gõ
            void LiveFromPanel(object? s, EventArgs e)
            {
                if (!dtAnchored || !dtPanel.ContainsFocus) return;
                dtPreview = BuildFromPanel(dtStart);
                pictureBox1.Invalidate();
            }
            dtA.ValueChanged += LiveFromPanel;
            dtB.ValueChanged += LiveFromPanel;
            dtText.TextChanged += LiveFromPanel;

            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (curTool == DrawTool.None) return;

                if (e.KeyCode == Keys.Escape)
                {
                    if (dtAnchored || dtDragging) CancelDraft(); else CancelTool();
                    e.Handled = true; e.SuppressKeyPress = true;
                    return;
                }
                if (dtPanel.ContainsFocus) return;

                if (e.KeyCode == Keys.Enter)            // đang ở canvas: Enter = tạo theo số trong bảng
                {
                    CreateFromPanel();
                    e.Handled = true; e.SuppressKeyPress = true;
                }
                else if (curTool != DrawTool.Text && !e.Control && !e.Alt && IsNumberKey(e.KeyCode))
                {
                    dtA.Focus();                        // gõ số là nhảy ngay vào ô đầu, số đầu thay giá trị cũ
                    dtA.Select(0, dtA.Text.Length);
                }
            };
        }

        private static bool IsNumberKey(Keys k) =>
            (k >= Keys.D0 && k <= Keys.D9) || (k >= Keys.NumPad0 && k <= Keys.NumPad9) ||
            k == Keys.OemPeriod || k == Keys.Oemcomma || k == Keys.Decimal;

        // =================================================================
        //  Bắt đầu / kết thúc công cụ
        // =================================================================
        private void BeginDrawTool(DrawTool tool, PolygonKind kind = PolygonKind.Diamond)
        {
            if (ReadOnlyView) SetActiveTab(null);          // đang ở tab xem -> quay về Edit
            CancelTool();

            curTool = tool;
            curPoly = kind;

            foreach (var o in projectItems) o.IsSelected = false;   // bỏ chọn hình cũ
            selectedItem = null!;
            UpdatePropertiesToUI();

            dtPxPerPt = TextPxPerPt("Arial");
            ConfigurePanel();
            pictureBox1.Cursor = Cursors.Cross;
            pictureBox1.Focus();
            pictureBox1.Invalidate();
        }

        // Bỏ hình đang vẽ dở (điểm đầu), vẫn giữ công cụ
        private void CancelDraft()
        {
            dtDragging = false; dtMoved = false; dtAnchored = false; dtPreview = null;
            snInfo = null; snMarker = SnNone;
            if (dtHint != null) UpdateHint();
            pictureBox1.Invalidate();
        }

        // Thoát hẳn công cụ
        private void CancelTool()
        {
            bool was = curTool != DrawTool.None || dtPreview != null;
            curTool = DrawTool.None;
            dtDragging = false; dtMoved = false; dtAnchored = false; dtPreview = null;
            if (dtPanel != null) dtPanel.Visible = false;
            if (was)
            {
                snInfo = null; snMarker = SnNone;
                pictureBox1.Cursor = Cursors.Default;
                pictureBox1.Invalidate();
            }
        }

        private void UpdateHint()
        {
            dtHint.Text = dtAnchored
                ? "Đã có điểm đầu:\n• click điểm cuối để kết thúc\n• hoặc gõ số rồi Enter  (Esc: bỏ điểm đầu)"
                : "• Kéo thả trên canvas để vẽ\n• hoặc click điểm đầu rồi click điểm cuối\n• hoặc click điểm đầu rồi gõ số + Enter\n  Shift: vuông / khóa góc 45°";
        }

        private float TextPxPerPt(string fontName)
        {
            using var g = pictureBox1.CreateGraphics();
            using var f = new Font(fontName, 100f);
            float h = g.MeasureString("Ag", f).Height / 100f;
            return h > 0.1f ? h : 1.6f;
        }

        private void ConfigurePanel()
        {
            bool isLine = curTool == DrawTool.Line, isText = curTool == DrawTool.Text;

            dtTitle.Text = curTool switch
            {
                DrawTool.Rectangle => "Vẽ: Hình chữ nhật",
                DrawTool.Ellipse => "Vẽ: Hình tròn / elip",
                DrawTool.Polygon => "Vẽ: " + new VectorPolygon(curPoly).Name,
                DrawTool.Line => "Vẽ: Đường thẳng",
                _ => "Vẽ: Văn bản"
            };

            if (isLine)
            {
                dtLblA.Text = "Dài (mm)"; dtLblB.Text = "Góc (°)";
                dtA.Minimum = 0.1m; dtA.Maximum = 10000m; dtA.DecimalPlaces = 2;
                dtB.Minimum = 0m; dtB.Maximum = 360m; dtB.DecimalPlaces = 1;
            }
            else if (isText)
            {
                dtLblA.Text = "Cỡ chữ (pt)";
                dtA.Minimum = 4m; dtA.Maximum = 500m; dtA.DecimalPlaces = 1;
            }
            else
            {
                dtLblA.Text = "Rộng (mm)"; dtLblB.Text = "Cao (mm)";
                dtA.Minimum = 0.5m; dtA.Maximum = 10000m; dtA.DecimalPlaces = 2;
                dtB.Minimum = 0.5m; dtB.Maximum = 10000m; dtB.DecimalPlaces = 2;
            }

            var v = dtVals[curTool];
            dtA.Value = Math.Min(dtA.Maximum, Math.Max(dtA.Minimum, v.A));
            dtB.Value = Math.Min(dtB.Maximum, Math.Max(dtB.Minimum, v.B));

            // Bố cục theo DPI màn hình (điều khiển tạo bằng code không tự co giãn nên phải tự nhân)
            int S(int n) => Scale96(n);
            int pad = S(10), colL = S(112), wL = S(100);
            int wAll = S(310), wInner = wAll - 2 * pad;
            dtPanel.Width = wAll;
            dtPanel.Font = new Font("Segoe UI", 9f, FontStyle.Bold);     // chữ đậm cho dễ nhìn (chú thích bên dưới giữ chữ thường)

            dtTitle.SetBounds(pad, S(8), wInner, S(22));
            int y = S(38);
            int rowH = S(36);

            void Row(Label l, Control c)
            {
                l.SetBounds(pad, y + S(4), wL, S(22));
                c.SetBounds(colL, y, wAll - colL - pad - S(2), S(26));
                y += rowH;
            }

            Row(dtLblA, dtA);
            dtLblB.Visible = dtB.Visible = !isText;
            if (!isText) Row(dtLblB, dtB);
            dtLblT.Visible = dtText.Visible = isText;
            if (isText) Row(dtLblT, dtText);

            y += S(2);
            dtRepeat.SetBounds(pad, y, wInner, S(26)); y += S(34);
            int wCreate = (wInner - S(6)) * 58 / 100;
            dtBtnCreate.SetBounds(pad, y, wCreate, S(32));
            dtBtnCancel.SetBounds(pad + wCreate + S(6), y, wInner - wCreate - S(6), S(32));
            y += S(42);
            UpdateHint();
            dtHint.SetBounds(pad, y, wInner, S(78)); y += dtHint.Height + S(8);
            dtPanel.Height = y;

            dtPanel.Location = new Point(Inset + 10, Inset + 10);
            dtPanel.Visible = true;
            dtPanel.BringToFront();
        }

        // =================================================================
        //  Tạo hình
        // =================================================================
        private static decimal ReadNum(NumericUpDown n)
        {
            // Số đang gõ dở có thể chưa được NumericUpDown cập nhật -> đọc từ chữ
            if (decimal.TryParse(n.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var v))
                return Math.Min(n.Maximum, Math.Max(n.Minimum, v));
            return n.Value;
        }

        private static void SetNum(NumericUpDown n, double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return;
            n.Value = Math.Min(n.Maximum, Math.Max(n.Minimum, (decimal)v));
        }

        private Vectoritem MakeBox(float x, float y, float w, float h)
        {
            switch (curTool)
            {
                case DrawTool.Ellipse:
                    return new VectorEllipse { X = x, Y = y, Width = w, Height = h, Name = "Hình tròn" };
                case DrawTool.Polygon:
                    return new VectorPolygon(curPoly) { X = x, Y = y, Width = w, Height = h };
                default:
                    return new VectorRectangle { X = x, Y = y, Width = w, Height = h, Name = "Hình chữ nhật" };
            }
        }

        private VectorText MakeText(float x, float y, float fontSize)
        {
            string content = string.IsNullOrWhiteSpace(dtText.Text) ? "Văn bản" : dtText.Text;
            return new VectorText { X = x, Y = y, TextContent = content, FontName = "Arial", FontSize = fontSize };
        }

        // Tạo theo SỐ TRONG BẢNG, góc trên-trái (hoặc điểm đầu đường thẳng) tại a
        private Vectoritem? BuildFromPanel(PointF a)
        {
            float va = (float)ReadNum(dtA);
            switch (curTool)
            {
                case DrawTool.Line:
                    {
                        float len = va / MmPerPx;
                        double ang = (double)ReadNum(dtB) * Math.PI / 180.0;       // trục Y hướng lên
                        return new VectorLine
                        {
                            X = a.X,
                            Y = a.Y,
                            X2 = a.X + (float)(len * Math.Cos(ang)),
                            Y2 = a.Y - (float)(len * Math.Sin(ang)),
                            Name = "Đường thẳng"
                        };
                    }
                case DrawTool.Text:
                    return MakeText(a.X, a.Y, va);
                default:
                    {
                        float w = va / MmPerPx, h = (float)ReadNum(dtB) / MmPerPx;
                        return MakeBox(a.X, a.Y, w, h);
                    }
            }
        }

        // Tạo theo KÉO / theo 2 điểm a -> b
        private Vectoritem? BuildFromDrag(PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            bool shift = (ModifierKeys & Keys.Shift) != 0;

            if (curTool == DrawTool.Line)
            {
                if (shift)
                {
                    double ang = Math.Atan2(dy, dx), len = Math.Sqrt(dx * dx + dy * dy);
                    double sn = Math.Round(ang / (Math.PI / 4)) * (Math.PI / 4);
                    if (snGridOn) len = Math.Round(len / SnStepPx) * SnStepPx;
                    dx = (float)(len * Math.Cos(sn)); dy = (float)(len * Math.Sin(sn));
                }
                if (Math.Sqrt(dx * dx + dy * dy) < 2f) return null;
                return new VectorLine { X = a.X, Y = a.Y, X2 = a.X + dx, Y2 = a.Y + dy, Name = "Đường thẳng" };
            }

            if (curTool == DrawTool.Text)
            {
                float hPx = Math.Abs(dy);
                if (hPx < 8f) return null;
                float fs = Math.Clamp(hPx / dtPxPerPt, 4f, 500f);       // chiều cao kéo = cỡ chữ
                return MakeText(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), fs);
            }

            if (shift)
            {
                float s = Math.Max(Math.Abs(dx), Math.Abs(dy));
                dx = (dx < 0 ? -1 : 1) * s;
                dy = (dy < 0 ? -1 : 1) * s;
            }
            float w = Math.Abs(dx), h = Math.Abs(dy);
            if (w < 1f || h < 1f) return null;
            return MakeBox(dx < 0 ? a.X + dx : a.X, dy < 0 ? a.Y + dy : a.Y, w, h);
        }

        private void CommitItem(Vectoritem it)
        {
            var old = dtVals[curTool];
            dtVals[curTool] = (ReadNum(dtA), dtB.Visible ? ReadNum(dtB) : old.B);   // nhớ cho lần sau

            snSkipNew = true;                    // vị trí đã snap rồi, đừng snap lại
            try { AddItem(it); }
            finally { snSkipNew = false; }

            dtDragging = false; dtMoved = false; dtAnchored = false;
            dtPreview = null; snInfo = null; snMarker = SnNone;

            if (!dtRepeat.Checked) CancelTool();
            else { UpdateHint(); pictureBox1.Invalidate(); }
        }

        // Enter / nút "Tạo": có điểm đầu thì tạo tại đó, chưa có thì tạo giữa vùng đang nhìn
        private void CreateFromPanel()
        {
            if (curTool == DrawTool.None) return;

            if (dtAnchored)
            {
                var a = BuildFromPanel(dtStart);
                if (a != null) CommitItem(a);
                return;
            }

            var it = BuildFromPanel(PointF.Empty);
            if (it == null) return;

            var c = ViewCenter();
            if (it is VectorLine ln)
                it.MoveTo(c.X - (ln.X2 - ln.X) / 2, c.Y - (ln.Y2 - ln.Y) / 2);
            else if (it is VectorText)
                it.MoveTo(c.X - 75, c.Y - 20);
            else
                it.MoveTo(c.X - it.Width / 2, c.Y - it.Height / 2);

            if (snGridOn)
            {
                var p = GridSnap(new PointF(it.X, it.Y));
                it.MoveTo(p.X, p.Y);
            }
            CommitItem(it);
        }

        // =================================================================
        //  Chuột. DrawToolMouseDown / Up được CanvasBox gọi TRƯỚC Form1.
        //  true = công cụ đã xử lý, Form1 không nhận sự kiện (không chọn / kéo hình khác).
        // =================================================================
        private bool DrawToolMouseDown(MouseEventArgs e)
        {
            if (curTool == DrawTool.None) return false;

            if (e.Button == MouseButtons.Right)           // chuột phải = Esc
            {
                if (dtAnchored || dtDragging) CancelDraft(); else CancelTool();
                return true;
            }
            if (e.Button != MouseButtons.Left) return false;          // chuột giữa vẫn pan được
            if (zoomBadge.Contains(e.Location)) return false;

            SnHit hit = SnapPoint(ScreenToWorld(e.Location), null);

            if (dtAnchored)                                // click lần 2 = điểm cuối
            {
                var it = BuildFromDrag(dtStart, hit.P);
                if (it != null) CommitItem(it);
                return true;
            }

            dtStart = hit.P;
            dtStartScreen = e.Location;
            dtDragging = true; dtMoved = false; dtPreview = null;
            return true;
        }

        private bool DrawToolMouseUp(MouseEventArgs e)
        {
            if (curTool == DrawTool.None || !dtDragging || e.Button != MouseButtons.Left) return false;
            dtDragging = false;

            if (dtMoved)                                   // kéo thả xong = tạo hình
            {
                var it = dtPreview;
                dtPreview = null; snInfo = null; snMarker = SnNone;
                if (it != null) CommitItem(it);
            }
            else                                           // click không kéo = điểm đầu, chờ điểm cuối / số nhập
            {
                dtAnchored = true;
                dtPreview = BuildFromPanel(dtStart);
                UpdateHint();
            }
            pictureBox1.Invalidate();
            return true;
        }

        // Hàm cũ (trước đây Form1 gọi ở đầu MouseMove). Giữ lại để không lỗi nếu còn dòng gọi; không làm gì.
        private bool DrawToolMouseMove(MouseEventArgs e) => false;

        // Gọi sau khi Form1 xử lý MouseMove (xem OnCanvasPostMove)
        private bool ToolMouseMove(MouseEventArgs e)
        {
            if (curTool == DrawTool.None || isPanning) return false;

            pictureBox1.Cursor = Cursors.Cross;
            snCursor = e.Location;
            SnHit hit = SnapPoint(ScreenToWorld(e.Location), null);

            if (dtDragging)
            {
                if (!dtMoved && Math.Abs(e.X - dtStartScreen.X) < 4 && Math.Abs(e.Y - dtStartScreen.Y) < 4)
                    return true;                               // chưa kéo đủ xa: coi như click
                dtMoved = true;
                snMarker = hit.Kind == SnKind.None ? SnNone : hit;
                dtPreview = BuildFromDrag(dtStart, hit.P);
                if (dtPreview != null) DtReflect(dtPreview); else snInfo = null;
                pictureBox1.Invalidate();
                return true;
            }

            if (dtAnchored)                                    // click-click: hình chạy theo con trỏ
            {
                snMarker = hit.Kind == SnKind.None ? SnNone : hit;
                if (!dtPanel.ContainsFocus)                    // đang gõ số thì hình theo số, không theo chuột
                {
                    dtPreview = BuildFromDrag(dtStart, hit.P);
                    if (dtPreview != null) DtReflect(dtPreview); else snInfo = null;
                }
                pictureBox1.Invalidate();
                return true;
            }

            // Chưa bấm: hiện điểm sẽ bắt được
            SnHit old = snMarker;
            snMarker = hit.Kind == SnKind.None ? SnNone : hit;
            if (old.Kind != snMarker.Kind || old.P != snMarker.P)
            {
                if (old.Kind != SnKind.None) pictureBox1.Invalidate(SnRect(old));
                if (snMarker.Kind != SnKind.None) pictureBox1.Invalidate(SnRect(snMarker));
            }
            return true;
        }

        // Cập nhật ô số trong bảng + nhãn cạnh con trỏ theo hình đang kéo
        private void DtReflect(Vectoritem it)
        {
            switch (it)
            {
                case VectorLine ln:
                    {
                        float dx = ln.X2 - ln.X, dy = ln.Y2 - ln.Y;
                        double len = Math.Sqrt(dx * dx + dy * dy) * MmPerPx;
                        double ang = Math.Atan2(-dy, dx) * 180.0 / Math.PI;
                        if (ang < 0) ang += 360;
                        SetNum(dtA, len); SetNum(dtB, ang);
                        snInfo = $"L = {len:0.##} mm   ∠ {ang:0.#}°";
                        break;
                    }
                case VectorText t:
                    SetNum(dtA, t.FontSize);
                    snInfo = $"Cỡ chữ {t.FontSize:0.#} pt";
                    break;
                default:
                    {
                        double w = it.Width * MmPerPx, h = it.Height * MmPerPx;
                        SetNum(dtA, w); SetNum(dtB, h);
                        snInfo = $"{w:0.##} × {h:0.##} mm";
                        break;
                    }
            }
        }

        // Hình đang kéo (vẽ trong hệ toạ độ canvas, gọi từ lớp phủ)
        private void PaintToolPreview(Graphics g)
        {
            if (dtPreview == null) return;
            dtPreview.IsSelected = true;          // vẽ màu xanh để phân biệt hình đang kéo
            dtPreview.Draw(g);
        }
    }
}

// =====================================================================
// PHẦN 4: Xoay / lật / vẽ hình đã biến đổi / thuộc tính mở rộng   (trước đây là file Form1_Extras.cs)
// =====================================================================
// =====================================================================
//  Form1_Extras.cs  -  Các thuộc tính mới của đối tượng: XOAY, chữ ĐẬM / NGHIÊNG, CHẾ ĐỘ KHẮC ẢNH
//
//  - Xoay: vẽ / bấm trúng / G-code đều quay quanh TÂM hình. Draw() và HitTest() của từng hình
//    giữ nguyên, Form1 xoay hệ tọa độ bên ngoài (DrawItem, HitTestItem).
//  - Các thuộc tính mới đi theo Sao chép / Undo / Lưu file / Autosave / chữ ký "đã sửa"
//    qua các hàm CopyItemExtras, FillDtoExtras, ApplyDtoExtras, ItemExtrasHash.
// =====================================================================
namespace test01_1014
{
    using OpenCvSharp.Dnn;
    using test01_1014;

    public partial class Form1
    {
        // ------------------------------------------------------------------
        //  XOAY
        // ------------------------------------------------------------------
        private static PointF RotateAround(PointF p, float cx, float cy, float deg)
        {
            double a = deg * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
            double dx = p.X - cx, dy = p.Y - cy;
            return new PointF((float)(cx + dx * c - dy * s), (float)(cy + dx * s + dy * c));
        }

        // Bấm trúng hình đã xoay / lật: đưa điểm bấm về hệ tọa độ chưa biến đổi rồi dùng HitTest gốc
        // (vẽ: p' = c + R*S*(p-c)  =>  ngược lại: p = c + S*R^-1*(p'-c))
        private static bool HitTestItem(Vectoritem it, PointF w)
        {
            if (it.Hidden || it.Locked) return false;           // ẩn / khóa: không bấm trúng được trên canvas
            if (it is not VectorLine && (it.Rotation != 0f || it.FlipX || it.FlipY))
            {
                float cx = it.X + it.Width / 2f, cy = it.Y + it.Height / 2f;
                if (it.Rotation != 0f) w = RotateAround(w, cx, cy, -it.Rotation);
                if (it.FlipX) w = new PointF(2 * cx - w.X, w.Y);
                if (it.FlipY) w = new PointF(w.X, 2 * cy - w.Y);
            }
            return it.HitTest(w);
        }

        // Vẽ hình (có thể đã xoay / lật). 'draw' = cách vẽ khác (ví dụ vẽ đường laser) nếu cần
        private static void DrawItem(Graphics g, Vectoritem it, Action<Graphics, Vectoritem>? draw = null)
        {
            if (it is VectorText vt)   // Draw() chỉ cập nhật Rộng/Cao của chữ SAU khi đã xoay -> đo trước để tâm xoay luôn đúng
            {
                if (vt.HasCustomPath)
                {
                    HersheyFont.BuildTextSubPaths(vt, MmPerPx, 0.3f);
                }
                else
                {
                    using var mf = vt.MakeFont();
                    SizeF msz = g.MeasureString(vt.TextContent, mf);
                    vt.Width = msz.Width; vt.Height = msz.Height;
                }
            }
            draw ??= (gg, ii) => ii.Draw(gg);
            bool xf = it.Rotation != 0f || it.FlipX || it.FlipY;
            if (!xf || it is VectorLine) { draw(g, it); return; }

            float cx = it.X + it.Width / 2f, cy = it.Y + it.Height / 2f;
            var saved = g.Transform;
            g.TranslateTransform(cx, cy);
            if (it.Rotation != 0f) g.RotateTransform(it.Rotation);
            if (it.FlipX || it.FlipY) g.ScaleTransform(it.FlipX ? -1f : 1f, it.FlipY ? -1f : 1f);
            g.TranslateTransform(-cx, -cy);
            try { draw(g, it); }
            finally { g.Transform = saved; saved.Dispose(); }
        }

        // ------------------------------------------------------------------
        //  Thuộc tính mới đi theo sao chép / lưu file / autosave / chữ ký
        // ------------------------------------------------------------------
        private static void CopyItemExtras(Vectoritem src, Vectoritem dst)
        {
            dst.Rotation = src.Rotation;
            dst.FlipX = src.FlipX; dst.FlipY = src.FlipY;
            dst.LayerId = src.LayerId;
            dst.Hidden = src.Hidden; dst.Locked = src.Locked;
            dst.FillMode = src.FillMode; dst.FillIntervalMm = src.FillIntervalMm; dst.FillAngle = src.FillAngle;
            dst.FillCross = src.FillCross; dst.FillBidir = src.FillBidir;
            if (src is VectorText st && dst is VectorText dt)
            {
                dt.FontBold = st.FontBold; dt.FontItalic = st.FontItalic;
                dt.SingleLineFont = st.SingleLineFont; dt.WeldText = st.WeldText;
                dt.ArcRadiusMm = st.ArcRadiusMm; dt.CharSpacingMm = st.CharSpacingMm;
            }
            if (src is VectorImage si && dst is VectorImage di)
            { di.EngraveMode = si.EngraveMode; di.Threshold = si.Threshold; di.Invert = si.Invert; }
        }

        private static void FillDtoExtras(VectorItemDTO d, Vectoritem item)
        {
            d.Rotation = item.Rotation;
            d.FlipX = item.FlipX; d.FlipY = item.FlipY; d.GroupId = item.GroupId ?? ""; d.LayerId = Math.Max(0, item.LayerId);
            d.Name = item.Name; d.Hidden = item.Hidden; d.Locked = item.Locked;
            d.FillMode = item.FillMode; d.FillIntervalMm = item.FillIntervalMm; d.FillAngle = item.FillAngle;
            d.FillCross = item.FillCross; d.FillBidir = item.FillBidir;
            if (item is VectorText t)
            {
                d.FontBold = t.FontBold; d.FontItalic = t.FontItalic;
                d.SingleLineFont = t.SingleLineFont; d.WeldText = t.WeldText;
                d.ArcRadiusMm = t.ArcRadiusMm; d.CharSpacingMm = t.CharSpacingMm;
            }
            if (item is VectorImage vi) { d.EngraveMode = vi.EngraveMode; d.ImageThreshold = vi.Threshold; d.ImageInvert = vi.Invert; }
            if (item is VectorBarcode bc)
            { d.BarcodeText = bc.Content; d.BarcodeType = bc.Kind.ToString(); d.BarcodeQuiet = bc.Quiet; d.BarcodeShowText = bc.ShowText; }
        }

        private static void ApplyDtoExtras(Vectoritem it, VectorItemDTO d)
        {
            it.Rotation = d.Rotation;
            it.FlipX = d.FlipX; it.FlipY = d.FlipY;
            it.GroupId = string.IsNullOrEmpty(d.GroupId) ? null : d.GroupId;
            it.LayerId = Math.Max(0, d.LayerId);
            if (!string.IsNullOrEmpty(d.Name)) it.Name = d.Name;
            it.Hidden = d.Hidden; it.Locked = d.Locked;
            it.FillMode = d.FillMode; it.FillIntervalMm = d.FillIntervalMm > 0 ? d.FillIntervalMm : 0.1; it.FillAngle = d.FillAngle;
            it.FillCross = d.FillCross; it.FillBidir = d.FillBidir;
            if (it is VectorText t)
            {
                t.FontBold = d.FontBold; t.FontItalic = d.FontItalic;
                t.SingleLineFont = d.SingleLineFont; t.WeldText = d.WeldText;
                t.ArcRadiusMm = d.ArcRadiusMm; t.CharSpacingMm = d.CharSpacingMm;
            }
            if (it is VectorImage vi) { vi.EngraveMode = d.EngraveMode; vi.Threshold = d.ImageThreshold; vi.Invert = d.ImageInvert; }
        }

        private static int ItemExtrasHash(Vectoritem it)
        {
            var h = new HashCode();
            h.Add(it.Rotation); h.Add(it.FlipX); h.Add(it.FlipY); h.Add(it.GroupId); h.Add(it.LayerId); h.Add(it.Name); h.Add(it.Hidden); h.Add(it.Locked);
            h.Add(it.FillMode); h.Add(it.FillIntervalMm); h.Add(it.FillAngle); h.Add(it.FillCross); h.Add(it.FillBidir);
            if (it is VectorText t)
            {
                h.Add(t.FontBold); h.Add(t.FontItalic);
                h.Add(t.SingleLineFont); h.Add(t.WeldText);
                h.Add(t.ArcRadiusMm); h.Add(t.CharSpacingMm);
            }
            if (it is VectorImage vi) { h.Add(vi.EngraveMode); h.Add(vi.Threshold); h.Add(vi.Invert); }
            return h.ToHashCode();
        }
    }
}

// =====================================================================
// PHẦN 5: Menu Biến đổi (xoay, lật, nhập góc...)   (trước đây là file Form1_Transform.cs)
// =====================================================================
// =====================================================================
//  Form1_Transform.cs  -  XOAY, LẬT NGANG / DỌC, NHÓM (Group) / RÃ NHÓM
//
//  XOAY  : quanh TÂM của vùng chọn (1 hình = tâm hình đó; nhiều hình = tâm khung bao cả nhóm).
//          Hình có Rotation riêng (cộng thêm góc), đường thẳng xoay 2 đầu mút.
//          Quy ước góc: dương = thuận chiều kim đồng hồ trên màn hình (giống Form1 + G-code).
//  LẬT   : đối xứng qua tâm vùng chọn. Hình được vẽ bằng ScaleTransform(-1,..) nên chữ / ảnh /
//          đa giác đều lật đúng; G-code lật cùng (xem GCodeCompiler.RotatePaths / RasterJobFrom).
//  NHÓM  : gán chung một GroupId. Click / Ctrl+click / quét khung chạm 1 hình của nhóm = chọn cả nhóm,
//          kéo thì cả nhóm đi cùng. Rã nhóm = xóa GroupId. (Chưa hỗ trợ nhóm lồng nhau.)
//  Tất cả đều Hoàn tác được và bị khóa khi đang ở tab xem.
// =====================================================================
namespace test01_1014
{
    public partial class Form1
    {
        private readonly List<ToolStripItem> groupNeedsTwo = new();       // bật khi chọn >= 2 hình
        private readonly List<ToolStripItem> ungroupNeedsGroup = new();   // bật khi vùng chọn có nhóm

        // ------------------------------------------------------------------
        //  Menu
        // ------------------------------------------------------------------
        private ToolStripItem[] BuildGroupItems(bool withKeys)
        {
            var g = new ToolStripMenuItem("Nhóm (Group)");
            var u = new ToolStripMenuItem("Rã nhóm (Ungroup)");
            if (withKeys)
            {
                g.ShortcutKeys = Keys.Control | Keys.G;
                u.ShortcutKeys = Keys.Control | Keys.Shift | Keys.G;
            }
            g.Click += (s, e) => GroupSelection();
            u.Click += (s, e) => UngroupSelection();
            groupNeedsTwo.Add(g);
            ungroupNeedsGroup.Add(u);
            return new ToolStripItem[] { g, u };
        }

        private ToolStripItem[] BuildTransformItems()
        {
            var rot = new ToolStripMenuItem("Xoay");
            ToolStripMenuItem R(string text, Action act, Keys keys = Keys.None)
            {
                var mi = new ToolStripMenuItem(text) { ShortcutKeys = keys };
                mi.Click += (s, e) => act();
                arrangeNeedsSelection.Add(mi);
                return mi;
            }
            rot.DropDownItems.Add(R("Xoay 90° phải", () => RotateSelection(90), Keys.Control | Keys.R));
            rot.DropDownItems.Add(R("Xoay 90° trái", () => RotateSelection(-90), Keys.Control | Keys.Shift | Keys.R));
            rot.DropDownItems.Add(R("Xoay 180°", () => RotateSelection(180)));
            rot.DropDownItems.Add(new ToolStripSeparator());
            rot.DropDownItems.Add(R("Xoay theo góc...", () => { if (PromptAngle(out float a)) RotateSelection(a); }));

            var flip = new ToolStripMenuItem("Lật");
            flip.DropDownItems.Add(R("Lật ngang (trái ↔ phải)", () => FlipSelection(true), Keys.Control | Keys.Shift | Keys.H));
            flip.DropDownItems.Add(R("Lật dọc (trên ↔ dưới)", () => FlipSelection(false), Keys.Control | Keys.Shift | Keys.V));

            return new ToolStripItem[] { rot, flip };
        }

        private void RefreshTransformEnabled()
        {
            bool edit = !ReadOnlyView;
            bool two = edit && SelCount >= 2;
            bool grouped = edit && SelectedList().Any(i => !string.IsNullOrEmpty(i.GroupId));
            foreach (var mi in groupNeedsTwo) mi.Enabled = two;
            foreach (var mi in ungroupNeedsGroup) mi.Enabled = grouped;
        }

        // ------------------------------------------------------------------
        //  Xoay
        // ------------------------------------------------------------------
        private static float NormAngle(float a)
        {
            a %= 360f;
            if (a < 0) a += 360f;
            a = (float)Math.Round(a, 3);
            return a >= 360f ? 0f : a;
        }

        private void RotateSelection(float deg)
        {
            if (ReadOnlyView) return;
            var sel = ArrangeTargets();
            if (sel.Count == 0 || Math.Abs(deg) < 1e-4f) return;

            RectangleF u = UnionBounds(sel);
            float px = u.Left + u.Width / 2f, py = u.Top + u.Height / 2f;

            PushUndo();
            CancelInteractions();
            foreach (var it in sel)
            {
                if (it is VectorLine l)
                {
                    var a = RotateAround(new PointF(l.X, l.Y), px, py, deg);
                    var b = RotateAround(new PointF(l.X2, l.Y2), px, py, deg);
                    l.X = a.X; l.Y = a.Y; l.X2 = b.X; l.Y2 = b.Y;
                    continue;
                }
                var nc = RotateAround(new PointF(it.X + it.Width / 2f, it.Y + it.Height / 2f), px, py, deg);
                it.X = nc.X - it.Width / 2f;
                it.Y = nc.Y - it.Height / 2f;
                it.Rotation = NormAngle(it.Rotation + deg);
            }
            AfterArrange();
        }

        // ------------------------------------------------------------------
        //  Lật: horizontal = đối xứng qua trục dọc (trái <-> phải)
        //  Vẽ: M = T(c) R(rot) S(fx,fy) T(-c); lật F ngoài cùng: F R(t) S = R(-t) (F S)  =>  rot = -rot, đảo cờ lật
        // ------------------------------------------------------------------
        private void FlipSelection(bool horizontal)
        {
            if (ReadOnlyView) return;
            var sel = ArrangeTargets();
            if (sel.Count == 0) return;

            RectangleF u = UnionBounds(sel);
            float px = u.Left + u.Width / 2f, py = u.Top + u.Height / 2f;

            PushUndo();
            CancelInteractions();
            foreach (var it in sel)
            {
                if (it is VectorLine l)
                {
                    if (horizontal) { l.X = 2 * px - l.X; l.X2 = 2 * px - l.X2; }
                    else { l.Y = 2 * py - l.Y; l.Y2 = 2 * py - l.Y2; }
                    continue;
                }
                float cx = it.X + it.Width / 2f, cy = it.Y + it.Height / 2f;
                if (horizontal) { it.X = 2 * px - cx - it.Width / 2f; it.FlipX = !it.FlipX; }
                else { it.Y = 2 * py - cy - it.Height / 2f; it.FlipY = !it.FlipY; }
                it.Rotation = NormAngle(-it.Rotation);
            }
            AfterArrange();
        }

        // ------------------------------------------------------------------
        //  Nhóm / Rã nhóm
        // ------------------------------------------------------------------
        private void GroupSelection()
        {
            if (ReadOnlyView) return;
            var sel = SelectedList();
            if (sel.Count < 2) return;

            PushUndo();
            string gid = Guid.NewGuid().ToString("N");
            foreach (var it in sel) it.GroupId = gid;       // nhóm cũ (nếu có) được gộp vào nhóm mới
            AfterArrange();
        }

        private void UngroupSelection()
        {
            if (ReadOnlyView) return;
            var ids = SelectedList().Where(i => !string.IsNullOrEmpty(i.GroupId))
                                    .Select(i => i.GroupId!).ToHashSet();
            if (ids.Count == 0) return;

            PushUndo();
            foreach (var it in projectItems)
                if (it.GroupId != null && ids.Contains(it.GroupId)) it.GroupId = null;
            AfterArrange();
        }

        // ------------------------------------------------------------------
        //  Hộp nhập góc xoay
        // ------------------------------------------------------------------
        private bool PromptAngle(out float deg)
        {
            deg = 0;
            using var f = new Form
            {
                Text = "Xoay theo góc",
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                AutoScaleMode = AutoScaleMode.None,
                ClientSize = new Size(Scale96(320), Scale96(120)),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            var lbl = new Label
            {
                Text = "Góc xoay (độ). Dương = theo chiều kim đồng hồ:",
                Bounds = new Rectangle(Scale96(14), Scale96(12), Scale96(292), Scale96(36))
            };
            var num = new NumericUpDown
            {
                DecimalPlaces = 1,
                Minimum = -360,
                Maximum = 360,
                Value = 90,
                Increment = 5,
                Bounds = new Rectangle(Scale96(14), Scale96(52), Scale96(120), Scale96(26))
            };
            var ok = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Bounds = new Rectangle(Scale96(150), Scale96(80), Scale96(75), Scale96(28))
            };
            var cancel = new Button
            {
                Text = "Hủy",
                DialogResult = DialogResult.Cancel,
                Bounds = new Rectangle(Scale96(231), Scale96(80), Scale96(75), Scale96(28))
            };
            f.Controls.AddRange(new Control[] { lbl, num, ok, cancel });
            f.AcceptButton = ok; f.CancelButton = cancel;

            if (f.ShowDialog(this) != DialogResult.OK) return false;
            deg = (float)num.Value;
            return true;
        }
    }
}
