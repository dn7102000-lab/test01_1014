namespace test01_1014
{
    public partial class Form1
    {
        private PointF cursorWorld;          // tọa độ chuột (đơn vị canvas px)
        private bool cursorInside;
        private RectangleF coordBadge;       // vùng nhãn tọa độ (tọa độ màn hình)

        // Gọi 1 lần trong constructor Form1
        private void InitCursorCoords()
        {
            pictureBox1.MouseMove += (s, e) =>
            {
                cursorWorld = ScreenToWorld(e.Location);
                cursorInside = true;
                // chỉ vẽ lại vùng nhãn cho nhẹ (cộng thêm vùng cũ để xóa vết)
                var r = Rectangle.Inflate(Rectangle.Ceiling(coordBadge), 80, 4);
                pictureBox1.Invalidate(r);
            };
            pictureBox1.MouseLeave += (s, e) =>
            {
                cursorInside = false;
                pictureBox1.Invalidate();
            };
        }

        // Gọi cuối pictureBox1_Paint (sau khi vẽ zoomBadge)
        private void DrawCursorCoords(Graphics g)
        {
            if (!cursorInside) return;

            float xMm = cursorWorld.X * MmPerPx;
            float yMm = cursorWorld.Y * MmPerPx;
            string txt = $"X: {xMm:0.00} mm   Y: {yMm:0.00} mm";

            using var f = new Font("Segoe UI", 9f);
            var sz = g.MeasureString(txt, f);

            // nằm bên trái nhãn zoom, cùng hàng, sát góc phải dưới
            float right = zoomBadge.Left - 6;
            coordBadge = new RectangleF(right - sz.Width - 8,
                                        pictureBox1.Height - sz.Height - 8,
                                        sz.Width + 8, sz.Height + 4);

            using (var bg = new SolidBrush(Color.FromArgb(210, 255, 255, 255)))
                g.FillRectangle(bg, coordBadge);
            g.DrawString(txt, f, Brushes.DimGray, coordBadge.X + 4, coordBadge.Y + 2);
        }
    }
}