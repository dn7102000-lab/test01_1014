// =====================================================================
//  Form1_Status.cs  -  THANH TRẠNG THÁI dưới cùng
//
//    X: 12.50  Y: 30.00 mm | Zoom: 100% | Đối tượng: 5 (chọn 2) | Vùng chọn: 40.00 × 25.00 mm | ... | ● Máy: chưa kết nối
//
//  - Tọa độ chuột cập nhật theo MouseMove (mm, gốc ở góc thước, giống thước trên canvas)
//  - Các ô còn lại làm mới mỗi 200 ms (chỉ gán lại chữ khi có đổi, không nháy)
//  - Ô "Máy" nằm sát phải. Khi làm phần kết nối cổng COM / GRBL, gọi:
//        SetMachineStatus("Máy: COM3 - GRBL sẵn sàng", true);
//        SetMachineStatus("Máy: chưa kết nối", false);
//  Gọi InitStatusBar() 1 lần ở cuối InitWorkspaceLayout() (Form1_Tools.cs).
// =====================================================================
namespace test01_1014
{
    public partial class Form1
    {
        private StatusStrip statusBar = null!;
        private ToolStripStatusLabel stCoord = null!, stZoom = null!, stCount = null!, stSel = null!, stMachine = null!;
        private System.Windows.Forms.Timer? statusTimer;

        private void InitStatusBar()
        {
            if (statusBar != null) return;

            statusBar = new StatusStrip
            {
                Dock = DockStyle.Bottom,
                SizingGrip = false,
                BackColor = Color.FromArgb(243, 243, 243),
                Font = new Font("Segoe UI", 9f)
            };

            ToolStripStatusLabel Cell(string text, int width) => new ToolStripStatusLabel(text)
            {
                AutoSize = false,
                Width = Scale96(width),
                TextAlign = ContentAlignment.MiddleLeft,
                BorderSides = ToolStripStatusLabelBorderSides.Right,
                BorderStyle = Border3DStyle.Etched
            };

            stCoord = Cell("X: —    Y: —", 200);
            stZoom = Cell("Zoom: 100%", 100);
            stCount = Cell("Đối tượng: 0", 150);
            stSel = Cell("Vùng chọn: —", 230);
            var spring = new ToolStripStatusLabel { Spring = true };
            stMachine = new ToolStripStatusLabel
            {
                AutoSize = true,
                BorderSides = ToolStripStatusLabelBorderSides.Left,
                BorderStyle = Border3DStyle.Etched,
                Padding = new Padding(6, 0, 6, 0)
            };

            // Ô "Khổ in": AutoSize = luôn vừa chữ, không bị cắt (chữ do Form1_PrintArea.cs cập nhật)
            stArea = new ToolStripStatusLabel("Khổ in: —")
            {
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                BorderSides = ToolStripStatusLabelBorderSides.Right,
                BorderStyle = Border3DStyle.Etched,
                Padding = new Padding(4, 0, 6, 0)
            };

            // Zoom: bấm vào = thu phóng vừa vùng khắc (thay cho nhãn % trên canvas trước đây)
            stZoom.ToolTipText = "Bấm để thu phóng vừa vùng khắc  (Ctrl+0)";
            stZoom.Click += (s, e) => ZoomFitArea();
            statusBar.ShowItemToolTips = true;

            statusBar.Items.AddRange(new ToolStripItem[] { stCoord, stZoom, stCount, stSel, stArea, spring, stMachine });
            SetMachineStatus("Máy: chưa kết nối", false);

            Controls.Add(statusBar);
            statusBar.SendToBack();           // giành mép dưới trước các thanh bên
            menuStrip1.SendToBack();          // menu vẫn giữ mép trên

            pictureBox1.MouseMove += (s, e) =>
            {
                PointF w = ScreenToWorld(e.Location);
                stCoord.Text = $"X: {w.X * MmPerPx:0.00}    Y: {w.Y * MmPerPx:0.00} mm";
            };
            pictureBox1.MouseLeave += (s, e) => stCoord.Text = "X: —    Y: —";

            statusTimer = new System.Windows.Forms.Timer { Interval = 200 };
            statusTimer.Tick += (s, e) => RefreshStatus();
            statusTimer.Start();
            RefreshStatus();
        }

        // Dùng cho phần kết nối máy sau này
        public void SetMachineStatus(string text, bool connected)
        {
            if (stMachine == null) return;
            stMachine.Text = "●  " + text;
            stMachine.ForeColor = connected ? Color.FromArgb(30, 140, 60) : Color.Firebrick;
        }

        private void SetStatusText(ToolStripStatusLabel l, string t)
        {
            if (l.Text != t) l.Text = t;
        }

        private void RefreshStatus()
        {
            if (statusBar == null) return;

            SetStatusText(stZoom, $"Zoom: {zoom * 100:0}%");

            int total = projectItems.Count;
            var sel = ReadOnlyView ? new List<Vectoritem>() : ArrangeTargets();
            SetStatusText(stCount, sel.Count > 0 ? $"Đối tượng: {total}  (chọn {sel.Count})" : $"Đối tượng: {total}");

            if (sel.Count == 0) SetStatusText(stSel, "Vùng chọn: —");
            else
            {
                RectangleF u = UnionBounds(sel);
                SetStatusText(stSel, $"Vùng chọn: {u.Width * MmPerPx:0.00} × {u.Height * MmPerPx:0.00} mm");
            }
        }
    }
}