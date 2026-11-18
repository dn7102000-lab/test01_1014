using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

namespace test01_1014
{
    /// <summary>
    /// Đối tượng vector đường dẫn phức hợp (gồm nhiều đoạn thẳng, đường cong, cung tròn, polyline)
    /// Dùng cho việc nhập các file vector tiêu chuẩn như SVG và DXF.
    /// </summary>
    public class VectorPathItem : Vectoritem
    {
        // Danh sách các đường con (mỗi đường con là danh sách điểm nối tiếp nhau)
        // và cờ IsClosed xác định xem đường con đó có khép kín không.
        public class SubPath
        {
            public List<PointF> Points { get; set; } = new List<PointF>();
            public bool IsClosed { get; set; }
        }

        public List<SubPath> SubPaths { get; set; } = new List<SubPath>();

        public VectorPathItem()
        {
            Type = VectorType.Path;
            Name = "Đường dẫn Vector";
        }

        public VectorPathItem(List<SubPath> subPaths, string name = "Đường dẫn Vector")
        {
            Type = VectorType.Path;
            Name = name;
            SetPaths(subPaths);
        }

        /// <summary>
        /// Cập nhật danh sách sub-paths và tính toán lại hộp bao (X, Y, Width, Height)
        /// theo tọa độ gốc cục bộ.
        /// </summary>
        public void SetPaths(List<SubPath> subPaths)
        {
            SubPaths = subPaths ?? new List<SubPath>();
            RecalculateBounds();
        }

        public void RecalculateBounds()
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            bool hasPoints = false;

            foreach (var sp in SubPaths)
            {
                foreach (var p in sp.Points)
                {
                    hasPoints = true;
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }
            }

            if (hasPoints)
            {
                X = minX;
                Y = minY;
                Width = Math.Max(1f, maxX - minX);
                Height = Math.Max(1f, maxY - minY);
            }
            else
            {
                X = 0;
                Y = 0;
                Width = 10;
                Height = 10;
            }
        }

        public override void Draw(Graphics g)
        {
            if (SubPaths.Count == 0) return;

            using var pen = new Pen(StrokeColor, IsSelected ? 2f : 1f);

            foreach (var sp in SubPaths)
            {
                if (sp.Points.Count < 2) continue;
                var pts = sp.Points.ToArray();
                if (sp.IsClosed && pts.Length >= 3)
                {
                    g.DrawPolygon(pen, pts);
                }
                else
                {
                    g.DrawLines(pen, pts);
                }
            }

            if (IsSelected)
            {
                using var dash = new Pen(Color.Blue, 1f) { DashStyle = DashStyle.Dash };
                g.DrawRectangle(dash, X, Y, Width, Height);
            }
        }

        public override bool HitTest(PointF point)
        {
            // Kiểm tra sơ bộ bằng hộp bao
            RectangleF box = new RectangleF(X - 3, Y - 3, Width + 6, Height + 6);
            if (!box.Contains(point)) return false;

            const float toleranceSq = 25f; // Bán kính bấm trúng 5px

            foreach (var sp in SubPaths)
            {
                var pts = sp.Points;
                int count = pts.Count;
                if (count < 2) continue;

                // Nếu là hình khép kín và điểm rơi vào bên trong polygon
                if (sp.IsClosed && count >= 3 && PointInPolygon(point, pts))
                {
                    return true;
                }

                // Kiểm tra khoảng cách tới từng đoạn thẳng
                int limit = sp.IsClosed ? count : count - 1;
                for (int i = 0; i < limit; i++)
                {
                    var p1 = pts[i];
                    var p2 = pts[(i + 1) % count];
                    if (DistToSegmentSq(point, p1, p2) <= toleranceSq)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public override void MoveTo(float x, float y)
        {
            float dx = x - X;
            float dy = y - Y;
            X = x;
            Y = y;

            foreach (var sp in SubPaths)
            {
                for (int i = 0; i < sp.Points.Count; i++)
                {
                    var p = sp.Points[i];
                    sp.Points[i] = new PointF(p.X + dx, p.Y + dy);
                }
            }
        }

        /// <summary>
        /// Tỷ lệ lại kích thước hình khi người dùng kéo 8 điểm điều khiển
        /// </summary>
        public void Scale(float newWidth, float newHeight, float newX, float newY)
        {
            float sx = Width > 0.001f ? newWidth / Width : 1f;
            float sy = Height > 0.001f ? newHeight / Height : 1f;

            foreach (var sp in SubPaths)
            {
                for (int i = 0; i < sp.Points.Count; i++)
                {
                    float px = newX + (sp.Points[i].X - X) * sx;
                    float py = newY + (sp.Points[i].Y - Y) * sy;
                    sp.Points[i] = new PointF(px, py);
                }
            }

            X = newX;
            Y = newY;
            Width = Math.Max(1f, newWidth);
            Height = Math.Max(1f, newHeight);
        }

        private static bool PointInPolygon(PointF pt, List<PointF> v)
        {
            bool inside = false;
            for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
            {
                if ((v[i].Y > pt.Y) != (v[j].Y > pt.Y) &&
                    pt.X < (v[j].X - v[i].X) * (pt.Y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static float DistToSegmentSq(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float lenSq = dx * dx + dy * dy;
            if (lenSq < 1e-6f)
            {
                float dxx = p.X - a.X, dyy = p.Y - a.Y;
                return dxx * dxx + dyy * dyy;
            }
            float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
            t = Math.Clamp(t, 0f, 1f);
            float ex = p.X - (a.X + t * dx), ey = p.Y - (a.Y + t * dy);
            return ex * ex + ey * ey;
        }
    }
}

