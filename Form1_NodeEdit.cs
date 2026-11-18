using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace test01_1014
{
    using Point = System.Drawing.Point;
    using Size = System.Drawing.Size;
    using Font = System.Drawing.Font;

    public partial class Form1
    {
        // =====================================================================
        //  CHẾ ĐỘ SỬA ĐIỂM NÚT (NODE / VERTEX-LEVEL EDITING)
        //  - Kéo đỉnh (nút vuông): di chuyển tọa độ đỉnh trực tiếp (có bắt điểm SNAP)
        //  - Kéo điểm giữa cạnh (nút tròn nhỏ): chèn thêm đỉnh mới; giữ Shift khi kéo = uốn cong Bezier mượt
        //  - Chuột phải vào đỉnh (hoặc phím Delete / Backspace): xóa đỉnh đang chọn
        //  - Phím S (hoặc nút Bo tròn đỉnh): làm mượt góc nhọn tại đỉnh đang chọn thành đường cong Bezier
        //  - Phím N: bật / tắt chế độ sửa điểm nút
        // =====================================================================
        private bool nodeEditMode;
        private ToolStripButton? nodeEditBtn;

        // Đỉnh đang được chọn hoặc kéo: (chỉ số SubPath, chỉ số Point trong SubPath)
        private int neSubIdx = -1;
        private int nePtIdx = -1;
        private int neHoverSub = -1;
        private int neHoverPt = -1;
        private int neHoverMidSub = -1;
        private int neHoverMidPt = -1;
        private bool neDragging;

        // Phục vụ uốn cong Bezier khi giữ Shift kéo điểm giữa cạnh
        private bool neBezierDrag;
        private PointF neBezA, neBezB;
        private int neBezInsertIdx = -1;
        private int neBezCount = 0;

        public bool IsNodeEditMode => nodeEditMode;

        public void ToggleNodeEditMode()
        {
            SetNodeEditMode(!nodeEditMode);
        }

        public void SetNodeEditMode(bool on)
        {
            if (ReadOnlyView && on) SetActiveTab(null);
            if (on && curTool != DrawTool.None) CancelTool();

            if (on && selectedItem != null && selectedItem is not VectorPathItem && selectedItem is not VectorImage)
            {
                // Tự động chuyển hình cơ bản / chữ đang chọn sang VectorPathItem để sửa nút
                ConvertSelectedToPath(enterNodeEdit: false);
            }

            nodeEditMode = on;
            neDragging = false;
            neBezierDrag = false;
            neSubIdx = nePtIdx = -1;
            neHoverSub = neHoverPt = -1;
            neHoverMidSub = neHoverMidPt = -1;

            if (nodeEditBtn != null)
            {
                nodeEditBtn.Tag = nodeEditMode ? "active" : null;
                toolBox?.Invalidate();
            }
            pictureBox1.Cursor = nodeEditMode ? Cursors.Cross : Cursors.Default;
            propPalette?.RefreshValues();
            pictureBox1.Invalidate();
        }

        /// <summary>
        /// Chuyển đối tượng đang chọn (Hình chữ nhật, Elip, Đa giác, Đường thẳng, Văn bản) thành VectorPathItem.
        /// </summary>
        public void ConvertSelectedToPath(bool enterNodeEdit = true)
        {
            if (ReadOnlyView || selectedItem == null) return;
            if (selectedItem is VectorPathItem)
            {
                if (enterNodeEdit) SetNodeEditMode(true);
                return;
            }

            var pathItem = HersheyFont.ConvertToPathItem(selectedItem, MmPerPx);
            if (pathItem == null) return;

            PushUndo();
            int idx = projectItems.IndexOf(selectedItem);
            if (idx >= 0)
            {
                projectItems[idx] = pathItem;
            }
            else
            {
                projectItems.Add(pathItem);
            }

            foreach (var o in projectItems) o.IsSelected = false;
            pathItem.IsSelected = true;
            selectedItem = pathItem;

            if (enterNodeEdit)
                SetNodeEditMode(true);

            UpdatePropertiesToUI();
            propPalette?.Bind(selectedItem);
            pictureBox1.Invalidate();
        }

        /// <summary>
        /// Mở hộp thoại Tách nét Vector (Potrace / Contour-to-Vector) cho đối tượng ảnh đang chọn.
        /// </summary>
        public void TraceSelectedImage()
        {
            if (ReadOnlyView || selectedItem is not VectorImage vi || vi.LoadedImage == null) return;

            using var dlg = new ImageTraceDialog(vi);
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var tracedItem = ImageTracer.TraceImageItem(vi, dlg.Options);
            if (tracedItem == null || tracedItem.SubPaths.Count == 0)
            {
                MessageBox.Show(this, "Không tìm thấy đường biên vector nào với ngưỡng hiện tại.", "Tách nét Vector",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PushUndo();
            int idx = projectItems.IndexOf(vi);
            if (dlg.Options.KeepOriginalImage)
            {
                if (idx >= 0 && idx + 1 <= projectItems.Count)
                    projectItems.Insert(idx + 1, tracedItem);
                else
                    projectItems.Add(tracedItem);
            }
            else
            {
                if (idx >= 0) projectItems[idx] = tracedItem;
                else projectItems.Add(tracedItem);
            }

            foreach (var o in projectItems) o.IsSelected = false;
            tracedItem.IsSelected = true;
            selectedItem = tracedItem;

            UpdatePropertiesToUI();
            propPalette?.Bind(selectedItem);
            pictureBox1.Invalidate();
        }

        /// <summary>
        /// Hàn (Boolean Union / Weld) các đối tượng đang chọn (hoặc chữ đang chọn) thành 1 đường cắt VectorPathItem thống nhất.
        /// </summary>
        public void WeldSelectedItems()
        {
            if (ReadOnlyView) return;
            var sel = SelectedList();
            if (sel.Count == 0 && selectedItem != null) sel.Add(selectedItem);
            if (sel.Count == 0) return;

            // Nếu chỉ chọn 1 VectorText, bật WeldText và chuyển thành VectorPathItem đã hàn
            if (sel.Count == 1 && sel[0] is VectorText vt)
            {
                PushUndo();
                vt.WeldText = true;
                var pi = HersheyFont.ConvertToPathItem(vt, MmPerPx);
                if (pi != null)
                {
                    pi.Name = "Chữ hàn (" + vt.TextContent + ")";
                    int idx = projectItems.IndexOf(vt);
                    if (idx >= 0) projectItems[idx] = pi;
                    else projectItems.Add(pi);
                    foreach (var o in projectItems) o.IsSelected = false;
                    pi.IsSelected = true;
                    selectedItem = pi;
                }
                UpdatePropertiesToUI();
                propPalette?.Bind(selectedItem);
                pictureBox1.Invalidate();
                return;
            }

            var welded = HersheyFont.WeldItemsToPath(sel, MmPerPx);
            if (welded == null) return;

            PushUndo();
            int insertIdx = projectItems.IndexOf(sel[0]);
            foreach (var s in sel) projectItems.Remove(s);
            if (insertIdx >= 0 && insertIdx <= projectItems.Count)
                projectItems.Insert(insertIdx, welded);
            else
                projectItems.Add(welded);

            foreach (var o in projectItems) o.IsSelected = false;
            welded.IsSelected = true;
            selectedItem = welded;

            UpdatePropertiesToUI();
            propPalette?.Bind(selectedItem);
            pictureBox1.Invalidate();
        }

        /// <summary>
        /// Bo tròn / làm mượt đỉnh đang chọn trong chế độ sửa nút (tạo đường cong Bezier tại đỉnh).
        /// </summary>
        public void SmoothSelectedNode()
        {
            if (selectedItem is not VectorPathItem pi) return;
            if (neSubIdx < 0 || neSubIdx >= pi.SubPaths.Count) return;
            var sp = pi.SubPaths[neSubIdx];
            int n = sp.Points.Count;
            if (n < 3 || nePtIdx < 0 || nePtIdx >= n) return;
            if (!sp.IsClosed && (nePtIdx == 0 || nePtIdx == n - 1)) return;

            PushUndo();
            PointF prev = sp.Points[(nePtIdx - 1 + n) % n];
            PointF cur = sp.Points[nePtIdx];
            PointF next = sp.Points[(nePtIdx + 1) % n];

            // Điểm bắt đầu và kết thúc đoạn bo nằm ở 35% chiều dài hai cạnh kề
            PointF a = new PointF(cur.X + (prev.X - cur.X) * 0.35f, cur.Y + (prev.Y - cur.Y) * 0.35f);
            PointF b = new PointF(cur.X + (next.X - cur.X) * 0.35f, cur.Y + (next.Y - cur.Y) * 0.35f);

            var arcPts = new List<PointF>();
            const int steps = 6;
            for (int s = 0; s <= steps; s++)
            {
                float t = (float)s / steps;
                float u = 1f - t;
                float bx = u * u * a.X + 2f * u * t * cur.X + t * t * b.X;
                float by = u * u * a.Y + 2f * u * t * cur.Y + t * t * b.Y;
                arcPts.Add(new PointF(bx, by));
            }

            sp.Points.RemoveAt(nePtIdx);
            sp.Points.InsertRange(nePtIdx, arcPts);
            nePtIdx += steps / 2;
            pi.RecalculateBounds();
            propPalette?.RefreshValues();
            pictureBox1.Invalidate();
        }

        /// <summary>
        /// Xóa đỉnh đang chọn trong chế độ sửa nút.
        /// </summary>
        public bool DeleteSelectedNode()
        {
            if (!nodeEditMode || selectedItem is not VectorPathItem pi) return false;
            if (neSubIdx < 0 || neSubIdx >= pi.SubPaths.Count) return false;
            var sp = pi.SubPaths[neSubIdx];
            if (nePtIdx < 0 || nePtIdx >= sp.Points.Count) return false;

            int minPts = sp.IsClosed ? 3 : 2;
            PushUndo();
            if (sp.Points.Count <= minPts)
            {
                if (pi.SubPaths.Count > 1)
                {
                    pi.SubPaths.RemoveAt(neSubIdx);
                    neSubIdx = -1; nePtIdx = -1;
                }
            }
            else
            {
                sp.Points.RemoveAt(nePtIdx);
                if (nePtIdx >= sp.Points.Count) nePtIdx = sp.Points.Count - 1;
            }
            pi.RecalculateBounds();
            propPalette?.RefreshValues();
            pictureBox1.Invalidate();
            return true;
        }

        // Biến đổi điểm từ toạ độ cục bộ của đối tượng sang thế giới (nếu đối tượng có xoay/lật)
        private static PointF ItemLocalToWorld(Vectoritem it, PointF p)
        {
            if (it.Rotation == 0f && !it.FlipX && !it.FlipY) return p;
            float cx = it.X + it.Width / 2f, cy = it.Y + it.Height / 2f;
            float dx = p.X - cx, dy = p.Y - cy;
            if (it.FlipX) dx = -dx;
            if (it.FlipY) dy = -dy;
            if (it.Rotation != 0f)
            {
                double a = it.Rotation * Math.PI / 180.0;
                float cos = (float)Math.Cos(a), sin = (float)Math.Sin(a);
                return new PointF(cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
            }
            return new PointF(cx + dx, cy + dy);
        }

        private static PointF WorldToItemLocal(Vectoritem it, PointF w)
        {
            if (it.Rotation == 0f && !it.FlipX && !it.FlipY) return w;
            float cx = it.X + it.Width / 2f, cy = it.Y + it.Height / 2f;
            if (it.Rotation != 0f) w = RotateAround(w, cx, cy, -it.Rotation);
            if (it.FlipX) w = new PointF(2 * cx - w.X, w.Y);
            if (it.FlipY) w = new PointF(w.X, 2 * cy - w.Y);
            return w;
        }

        // Tìm đỉnh hoặc trung điểm cạnh gần con trỏ chuột
        private bool HitTestNode(VectorPathItem pi, PointF worldPt, out int subIdx, out int ptIdx, out bool isMidpoint)
        {
            subIdx = -1; ptIdx = -1; isMidpoint = false;
            PointF localPt = WorldToItemLocal(pi, worldPt);
            float tol = 8f / zoom;

            // 1) Ưu tiên bắt trúng các đỉnh hiện có
            float bestDist = tol;
            for (int s = 0; s < pi.SubPaths.Count; s++)
            {
                var pts = pi.SubPaths[s].Points;
                for (int i = 0; i < pts.Count; i++)
                {
                    float dx = pts[i].X - localPt.X, dy = pts[i].Y - localPt.Y;
                    float d = MathF.Sqrt(dx * dx + dy * dy);
                    if (d <= bestDist)
                    {
                        bestDist = d;
                        subIdx = s;
                        ptIdx = i;
                        isMidpoint = false;
                    }
                }
            }
            if (subIdx >= 0) return true;

            // 2) Kiểm tra điểm giữa của các cạnh (để thêm nút / kéo cong cạnh)
            bestDist = tol * 0.9f;
            for (int s = 0; s < pi.SubPaths.Count; s++)
            {
                var sp = pi.SubPaths[s];
                var pts = sp.Points;
                int segs = sp.IsClosed ? pts.Count : pts.Count - 1;
                for (int i = 0; i < segs; i++)
                {
                    PointF a = pts[i];
                    PointF b = pts[(i + 1) % pts.Count];
                    PointF mid = new PointF((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);
                    float dx = mid.X - localPt.X, dy = mid.Y - localPt.Y;
                    float d = MathF.Sqrt(dx * dx + dy * dy);
                    if (d <= bestDist)
                    {
                        bestDist = d;
                        subIdx = s;
                        ptIdx = i;
                        isMidpoint = true;
                    }
                }
            }
            return subIdx >= 0;
        }

        // =====================================================================
        //  XỬ LÝ CHUỘT TRONG CHẾ ĐỘ NODE EDIT
        // =====================================================================
        private bool NodeEditMouseDown(MouseEventArgs e)
        {
            if (!nodeEditMode || ReadOnlyView || curTool != DrawTool.None) return false;
            if (showRuler && (e.X < RulerSize || e.Y < RulerSize)) return false;
            if (zoomBadge.Contains(e.Location)) return false;

            PointF w = ScreenToWorld(e.Location);

            if (selectedItem is VectorPathItem pi)
            {
                // Chuột phải vào đỉnh = xóa đỉnh đó ngay
                if (e.Button == MouseButtons.Right)
                {
                    if (HitTestNode(pi, w, out int rs, out int rp, out bool rMid) && !rMid)
                    {
                        neSubIdx = rs; nePtIdx = rp;
                        DeleteSelectedNode();
                        return true;
                    }
                    return false;
                }

                if (e.Button == MouseButtons.Left)
                {
                    if (HitTestNode(pi, w, out int sIdx, out int pIdx, out bool isMid))
                    {
                        pendingUndo = TakeSnapshot();
                        neSubIdx = sIdx;

                        if (isMid)
                        {
                            // Bấm vào điểm giữa cạnh -> chèn đỉnh mới (hoặc kéo cong Bezier nếu giữ Shift)
                            var sp = pi.SubPaths[sIdx];
                            PointF a = sp.Points[pIdx];
                            PointF b = sp.Points[(pIdx + 1) % sp.Points.Count];
                            PointF mid = new PointF((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);

                            CommitPendingUndo();
                            if ((ModifierKeys & Keys.Shift) != 0)
                            {
                                // Chèn 7 điểm nội suy Bezier bậc 2 giữa a và b
                                neBezierDrag = true;
                                neBezA = a;
                                neBezB = b;
                                neBezInsertIdx = pIdx + 1;
                                neBezCount = 7;
                                var curvePts = new List<PointF>(neBezCount);
                                for (int k = 1; k <= neBezCount; k++)
                                {
                                    float t = (float)k / (neBezCount + 1);
                                    curvePts.Add(new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t));
                                }
                                sp.Points.InsertRange(neBezInsertIdx, curvePts);
                                nePtIdx = neBezInsertIdx + neBezCount / 2;
                            }
                            else
                            {
                                neBezierDrag = false;
                                sp.Points.Insert(pIdx + 1, mid);
                                nePtIdx = pIdx + 1;
                            }
                            neDragging = true;
                            pi.RecalculateBounds();
                            pictureBox1.Invalidate();
                            return true;
                        }
                        else
                        {
                            // Bắt đầu kéo đỉnh hiện có
                            neBezierDrag = false;
                            nePtIdx = pIdx;
                            neDragging = true;
                            pictureBox1.Invalidate();
                            return true;
                        }
                    }

                    // Nếu bấm trúng cạnh bất kỳ của đường dẫn (cách cạnh <= 5px) và giữ Ctrl -> chèn đỉnh tại điểm bấm
                    if ((ModifierKeys & Keys.Control) != 0 && TryInsertNodeOnSegment(pi, w, out int insSub, out int insPt))
                    {
                        neSubIdx = insSub;
                        nePtIdx = insPt;
                        neDragging = true;
                        neBezierDrag = false;
                        pictureBox1.Invalidate();
                        return true;
                    }
                }
            }

            // Nếu bấm vào hình khác khi đang ở Node Edit: chọn hình đó (và chuyển sang Path nếu cần)
            if (e.Button == MouseButtons.Left)
            {
                Vectoritem? hit = HitItemAt(w);
                if (hit != null && hit != selectedItem)
                {
                    SelectExclusive(hit);
                    if (hit is not VectorPathItem && hit is not VectorImage)
                        ConvertSelectedToPath(enterNodeEdit: true);
                    UpdatePropertiesToUI();
                    pictureBox1.Invalidate();
                    return true;
                }
            }

            return false;
        }

        private bool TryInsertNodeOnSegment(VectorPathItem pi, PointF worldPt, out int subIdx, out int ptIdx)
        {
            subIdx = -1; ptIdx = -1;
            PointF localPt = WorldToItemLocal(pi, worldPt);
            float tol = 6f / zoom;

            for (int s = 0; s < pi.SubPaths.Count; s++)
            {
                var sp = pi.SubPaths[s];
                var pts = sp.Points;
                int segs = sp.IsClosed ? pts.Count : pts.Count - 1;
                for (int i = 0; i < segs; i++)
                {
                    PointF a = pts[i];
                    PointF b = pts[(i + 1) % pts.Count];
                    if (HersheyFont.PointToSegmentDistance(localPt, a, b) <= tol)
                    {
                        PushUndo();
                        pts.Insert(i + 1, localPt);
                        pi.RecalculateBounds();
                        subIdx = s;
                        ptIdx = i + 1;
                        return true;
                    }
                }
            }
            return false;
        }

        private bool NodeEditMouseMove(MouseEventArgs e)
        {
            if (!nodeEditMode || ReadOnlyView || isPanning) return false;
            if (selectedItem is not VectorPathItem pi) return false;

            PointF w = ScreenToWorld(e.Location);

            if (neDragging && neSubIdx >= 0 && neSubIdx < pi.SubPaths.Count)
            {
                var sp = pi.SubPaths[neSubIdx];
                if (neBezierDrag && neBezInsertIdx >= 0 && neBezInsertIdx + neBezCount <= sp.Points.Count)
                {
                    PointF targetLocal = WorldToItemLocal(pi, w);
                    // Tính điểm điều khiển Bezier bậc 2 sao cho đường cong tại t=0.5 đi qua đúng targetLocal
                    PointF midAB = new PointF((neBezA.X + neBezB.X) * 0.5f, (neBezA.Y + neBezB.Y) * 0.5f);
                    PointF ctrl = new PointF(2f * targetLocal.X - midAB.X, 2f * targetLocal.Y - midAB.Y);

                    for (int k = 1; k <= neBezCount; k++)
                    {
                        float t = (float)k / (neBezCount + 1);
                        float u = 1f - t;
                        float bx = u * u * neBezA.X + 2f * u * t * ctrl.X + t * t * neBezB.X;
                        float by = u * u * neBezA.Y + 2f * u * t * ctrl.Y + t * t * neBezB.Y;
                        sp.Points[neBezInsertIdx + k - 1] = new PointF(bx, by);
                    }
                    pi.RecalculateBounds();
                    snCursor = e.Location;
                    snInfo = $"Uốn cong Bezier ({w.X * MmPerPx:0.##}, {w.Y * MmPerPx:0.##} mm)";
                    pictureBox1.Invalidate();
                    return true;
                }

                if (nePtIdx >= 0 && nePtIdx < sp.Points.Count)
                {
                    CommitPendingUndo();
                    SnHit hit = SnapPoint(w, pi);
                    snMarker = hit.Kind == SnKind.None ? SnNone : hit;
                    PointF snappedWorld = hit.P;
                    PointF localNew = WorldToItemLocal(pi, snappedWorld);

                    sp.Points[nePtIdx] = localNew;
                    pi.RecalculateBounds();

                    snCursor = e.Location;
                    snInfo = $"Nút #{nePtIdx + 1}: X {snappedWorld.X * MmPerPx:0.##}  Y {snappedWorld.Y * MmPerPx:0.##} mm";
                    pictureBox1.Invalidate();
                    return true;
                }
            }

            // Hover: kiểm tra con trỏ đang trỏ vào đỉnh hay điểm giữa cạnh
            int oldHS = neHoverSub, oldHP = neHoverPt, oldMS = neHoverMidSub, oldMP = neHoverMidPt;
            neHoverSub = neHoverPt = neHoverMidSub = neHoverMidPt = -1;

            if (HitTestNode(pi, w, out int hs, out int hp, out bool isMid))
            {
                if (isMid) { neHoverMidSub = hs; neHoverMidPt = hp; }
                else { neHoverSub = hs; neHoverPt = hp; }
                pictureBox1.Cursor = Cursors.Hand;
            }
            else
            {
                pictureBox1.Cursor = Cursors.Cross;
            }

            if (oldHS != neHoverSub || oldHP != neHoverPt || oldMS != neHoverMidSub || oldMP != neHoverMidPt)
                pictureBox1.Invalidate();

            return false;
        }

        private bool NodeEditMouseUp(MouseEventArgs e)
        {
            if (!nodeEditMode || !neDragging) return false;
            neDragging = false;
            neBezierDrag = false;
            pendingUndo = null;
            snInfo = null;
            snMarker = SnNone;
            propPalette?.RefreshValues();
            pictureBox1.Invalidate();
            return true;
        }

        // =====================================================================
        //  VẼ CÁC ĐIỂM NÚT & ĐIỂM GIỮA CẠNH TRÊN LỚP PHỦ (OVERLAY)
        // =====================================================================
        private void PaintNodeEditOverlay(Graphics g)
        {
            if (!nodeEditMode || ReadOnlyView) return;

            // Vẽ nhãn thông báo chế độ Sửa điểm nút ở góc trên
            using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
            {
                string msg = "CHẾ ĐỘ SỬA ĐIỂM NÚT (N) — Kéo nút vuông: di chuyển đỉnh | Kéo chấm tròn giữa cạnh: thêm đỉnh (giữ Shift: uốn cong Bezier) | Chuột phải / Delete: xóa đỉnh | Phím S: bo tròn đỉnh";
                var sz = g.MeasureString(msg, f);
                float bx = Inset + 10, by = Inset + 8;
                using var bg = new SolidBrush(Color.FromArgb(230, 20, 80, 160));
                g.FillRectangle(bg, bx, by, sz.Width + 12, sz.Height + 6);
                g.DrawString(msg, f, Brushes.White, bx + 6, by + 3);
            }

            if (selectedItem is not VectorPathItem pi) return;

            var state = g.Save();
            try
            {
                g.SetClip(new Rectangle(Inset, Inset,
                                        Math.Max(0, pictureBox1.Width - Inset),
                                        Math.Max(0, pictureBox1.Height - Inset)), CombineMode.Intersect);
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using var segPen = new Pen(Color.FromArgb(180, 0, 150, 255), 1.5f);
                using var nodeBorder = new Pen(Color.FromArgb(0, 90, 200), 1.4f);
                using var midBorder = new Pen(Color.FromArgb(40, 150, 80), 1.2f);
                using var activeBrush = new SolidBrush(Color.FromArgb(235, 50, 40));
                using var hoverBrush = new SolidBrush(Color.FromArgb(255, 165, 0));
                using var midBrush = new SolidBrush(Color.FromArgb(210, 230, 255, 230));

                for (int s = 0; s < pi.SubPaths.Count; s++)
                {
                    var sp = pi.SubPaths[s];
                    if (sp.Points.Count == 0) continue;

                    // Chuyển các đỉnh sang toạ độ màn hình để kích thước nút không đổi khi zoom
                    var scrPts = new PointF[sp.Points.Count];
                    for (int i = 0; i < sp.Points.Count; i++)
                    {
                        PointF wp = ItemLocalToWorld(pi, sp.Points[i]);
                        scrPts[i] = new PointF(pan.X + wp.X * zoom, pan.Y + wp.Y * zoom);
                    }

                    // Vẽ điểm giữa cạnh (nếu cạnh trên màn hình dài >= 22 px để không rối mắt)
                    int segs = sp.IsClosed ? scrPts.Length : scrPts.Length - 1;
                    for (int i = 0; i < segs; i++)
                    {
                        PointF a = scrPts[i];
                        PointF b = scrPts[(i + 1) % scrPts.Length];
                        float dx = b.X - a.X, dy = b.Y - a.Y;
                        if (dx * dx + dy * dy < 22f * 22f) continue;

                        PointF mid = new PointF((a.X + b.X) * 0.5f, (a.Y + b.Y) * 0.5f);
                        bool isHovMid = (s == neHoverMidSub && i == neHoverMidPt);
                        float mr = isHovMid ? 5f : 3.2f;
                        var mrc = new RectangleF(mid.X - mr, mid.Y - mr, mr * 2, mr * 2);
                        g.FillEllipse(isHovMid ? hoverBrush : midBrush, mrc);
                        g.DrawEllipse(midBorder, mrc);
                    }

                    // Vẽ các đỉnh (nút vuông)
                    for (int i = 0; i < scrPts.Length; i++)
                    {
                        PointF p = scrPts[i];
                        bool isSel = (s == neSubIdx && i == nePtIdx);
                        bool isHov = (s == neHoverSub && i == neHoverPt);
                        float r = (isSel || isHov) ? 5.2f : 3.8f;
                        var rc = new RectangleF(p.X - r, p.Y - r, r * 2, r * 2);

                        Brush fill = isSel ? activeBrush : isHov ? hoverBrush : Brushes.White;
                        g.FillRectangle(fill, rc);
                        g.DrawRectangle(nodeBorder, rc.X, rc.Y, rc.Width, rc.Height);
                    }
                }
            }
            finally
            {
                g.Restore(state);
            }
        }
    }
}

