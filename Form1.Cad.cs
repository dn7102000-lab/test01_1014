// =====================================================================
//  Form1.Cad.cs  -  "Keo" cac tinh nang kieu CAD vao Form1 ma khong can sua nhieu trong Form1.cs
//
//  - Tu khoi tao Snap, cong cu ve, bang thong so khi cua so duoc tao (OnHandleCreated)
//  - Gan cac mong chuot / ve vao CanvasBox (xem CanvasBox.cs)
//  - Ve lop phu: cham luoi, hinh dang keo, dau snap, nhan kich thuoc
// =====================================================================
using System.Drawing.Drawing2D;

namespace test01_1014
{
    public partial class Form1
    {
        private bool cadInited;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (cadInited || DesignMode) return;
            cadInited = true;

            // Cac ham Init* co co chong goi lai, nen neu constructor da goi roi thi khong sao
            InitSnap();
            InitDrawTool();
            InitPalette();
            InitWorkspaceLayout();      // thanh cong cu trai + chuyen bang thong so sang phai (Form1_Tools.cs)

            pictureBox1.PreMouseDown = e => DrawToolMouseDown(e) || NodeEditMouseDown(e) || SelectMouseDown(e);   // công cụ vẽ -> sửa điểm nút -> chọn nhiều
            pictureBox1.PreMouseUp = e => DrawToolMouseUp(e) || NodeEditMouseUp(e) || SelectMouseUp(e);
            pictureBox1.PostMouseMove = OnCanvasPostMove;
            pictureBox1.OverlayPaint += OnOverlayPaint;
            pictureBox1.MouseDoubleClick += (s, ev) =>
            {
                if (ev.Button == MouseButtons.Left && !ReadOnlyView && curTool == DrawTool.None)
                {
                    PointF w = ScreenToWorld(ev.Location);
                    var hit = HitItemAt(w);
                    if (hit != null && hit is not VectorImage)
                    {
                        SelectExclusive(hit);
                        SetNodeEditMode(!nodeEditMode);
                    }
                    else if (nodeEditMode)
                    {
                        SetNodeEditMode(false);
                    }
                }
            };

            // Cho phép kéo thả file SVG / DXF trực tiếp vào canvas
            pictureBox1.AllowDrop = true;
            pictureBox1.DragEnter += (s, ev) =>
            {
                if (ev.Data != null && ev.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[]? files = (string[]?)ev.Data.GetData(DataFormats.FileDrop);
                    if (files != null && files.Length > 0)
                    {
                        string ext = System.IO.Path.GetExtension(files[0]).ToLowerInvariant();
                        if (ext == ".svg" || ext == ".dxf" || ext == ".lzr" || ext == ".json")
                        {
                            ev.Effect = DragDropEffects.Copy;
                            return;
                        }
                    }
                }
                ev.Effect = DragDropEffects.None;
            };
            pictureBox1.DragDrop += (s, ev) =>
            {
                if (ev.Data != null && ev.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[]? files = (string[]?)ev.Data.GetData(DataFormats.FileDrop);
                    if (files != null && files.Length > 0)
                    {
                        string file = files[0];
                        string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                        if (ext == ".svg" || ext == ".dxf")
                        {
                            ImportVectorFile(file, clearCurrent: false);
                        }
                        else if (ext == ".lzr" || ext == ".json")
                        {
                            if (ConfirmSaveChanges() && LoadProject(file))
                            {
                                currentFilePath = file;
                                MarkClean();
                            }
                        }
                    }
                }
            };
        }

        // Sau khi Form1 xu ly xong MouseMove
        private void OnCanvasPostMove(MouseEventArgs e)
        {
            if (SelectMouseMove(e)) return;     // dang quet chon bang khung
            if (ToolMouseMove(e)) return;       // dang dung cong cu ve
            if (NodeEditMouseMove(e)) return;   // dang keo / sua diem nut
            SnapPostMove(e);                    // dang keo hinh: nhay theo luoi / OSNAP chinh xac
            SyncGroupDrag();                    // keo nhom: dua cac hinh con lai di theo
        }

        // Lop phu ve len tren cung
        private void OnOverlayPaint(object? sender, PaintEventArgs e)
        {
            if (ReadOnlyView) return;
            var g = e.Graphics;

            var state = g.Save();
            try
            {
                // Khong ve de len thuoc
                g.SetClip(new Rectangle(Inset, Inset,
                                        Math.Max(0, pictureBox1.Width - Inset),
                                        Math.Max(0, pictureBox1.Height - Inset)), CombineMode.Intersect);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(pan.X, pan.Y);
                g.ScaleTransform(zoom, zoom);

                PaintSnapDots(g);       // cham giao diem luoi
                PaintToolPreview(g);    // hinh dang keo
                PaintSnapMarker(g);     // dau diem snap
            }
            finally { g.Restore(state); }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            PaintSnapOverlay(g);        // nhan SNAP, ten diem, kich thuoc canh con tro
            PaintSelectionOverlay(g);   // khung quet chon + khung bao nhom (Form1_Select.cs)
            PaintNodeEditOverlay(g);    // cac diem nut khi dang o che do Node Edit (Form1_NodeEdit.cs)
        }
    }
}