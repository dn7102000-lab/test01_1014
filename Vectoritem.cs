using OpenCvSharp.Dnn;
using test01_1014;

using System;
using System.Drawing;
using System.Xml.Linq;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using System.Collections.Generic;
namespace test01_1014
{

    public class ProjectDataDTO
    {
        public string Name { get; set; } = "Laser Project";
        public string Version { get; set; } = "1.0";
        // Danh sách lưu chi tiết các item
        public List<VectorItemDTO> Items { get; set; } = new List<VectorItemDTO>();
        // Các lớp màu (công suất / tốc độ / số lần / bật-tắt xuất). File cũ không có thì rỗng -> dùng lớp mặc định
        public List<LayerDTO> Layers { get; set; } = new List<LayerDTO>();
    }

    public class VectorItemDTO
    {
        public string Shape { get; set; } = "";
        public string Type { get; set; } = ""; // "Rectangle", "Ellipse", "Line", "Text", "Image"
        public float X { get; set; }
        public float Y { get; set; }
        public float X2 { get; set; }           // đầu mút thứ 2 (dùng cho Line)
        public float Y2 { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public string TextContent { get; set; } = "";
        public string FontName { get; set; } = "";
        public float FontSize { get; set; }
        public string ImageBase64 { get; set; } = ""; // Ảnh chuyển thành chuỗi Base64
        // Mặc định khớp với Vectoritem để file dự án cũ (không có các trường này) vẫn mở đúng
        public int LaserPower { get; set; } = 80;
        public int LaserSpeed { get; set; } = 1000;
        public int Passes { get; set; } = 1;
        // Thuộc tính mới (file cũ không có thì lấy mặc định)
        public float Rotation { get; set; }
        public bool FontBold { get; set; }
        public bool FontItalic { get; set; }
        public int EngraveMode { get; set; }          // 0 = theo cài đặt G-code, 1 = ảnh xám, 2 = đen trắng
        public int ImageThreshold { get; set; } = 128;
        public bool ImageInvert { get; set; }
        public bool FlipX { get; set; }               // lật ngang
        public bool FlipY { get; set; }               // lật dọc
        public string GroupId { get; set; } = "";    // rỗng = không thuộc nhóm nào
        public string BarcodeText { get; set; } = "";   // rỗng = không phải mã vạch (mã vạch được lưu kèm ảnh PNG)
        public string BarcodeType { get; set; } = "";   // tên BarcodeKind: QR, Code128...
        public int BarcodeQuiet { get; set; } = -1;
        public bool BarcodeShowText { get; set; }
        public int LayerId { get; set; }                // số thứ tự lớp (0 = Lớp 01)
        public int FillMode { get; set; }               // 0 = đường viền, 1 = tô kín, 2 = tô kín + viền
        public double FillIntervalMm { get; set; } = 0.1;
        public float FillAngle { get; set; }
        public bool FillCross { get; set; }
        public bool FillBidir { get; set; } = true;
        public string Name { get; set; } = "";          // tên do người dùng đặt (rỗng = tên mặc định)
        public bool Hidden { get; set; }                // ẩn: không vẽ, không chọn, không xuất G-code
        public bool Locked { get; set; }                // khóa: vẫn vẽ và xuất, nhưng không chọn / sửa được trên canvas
        public string PathDataJson { get; set; } = "";  // dữ liệu đường dẫn phức hợp (SubPaths của VectorPathItem)
        public bool SingleLineFont { get; set; }        // Font 1 nét (Hershey stick font)
        public bool WeldText { get; set; }              // Hàn liền nét chữ giao nhau
        public float ArcRadiusMm { get; set; }          // Bán kính uốn cong chữ (mm, 0 = thẳng)
        public float CharSpacingMm { get; set; }        // Giãn cách ký tự (mm)
    }
    // Các loại hình vẽ hỗ trợ
    public enum VectorType { Image, Rectangle, Ellipse, Line, Text, Polygon, Path }

    // Lớp cơ sở (Base Class)
    public abstract class Vectoritem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Vector Object";
        public VectorType Type { get; set; }

        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public bool IsSelected { get; set; } = false;

        public int LaserPower { get; set; } = 80;
        public int LaserSpeed { get; set; } = 1000;
        public int Passes { get; set; } = 1;

        // Góc xoay (độ, thuận chiều kim đồng hồ) quanh tâm hình. Form1 lo việc vẽ / bấm trúng, G-code xoay đường cắt
        public float Rotation { get; set; }

        // Lật quanh tâm hình (áp dụng sau khi xoay) và nhãn nhóm: các đối tượng cùng GroupId được chọn / kéo chung
        public bool FlipX { get; set; }
        public bool FlipY { get; set; }
        public string? GroupId { get; set; }

        // Lớp màu (số thứ tự, -1 = chưa gán: Form1 sẽ gán vào lớp đang chọn khi thêm vào canvas)
        public int LayerId { get; set; } = -1;
        // Danh sách đối tượng: ẩn (không vẽ / không chọn / không xuất G-code), khóa (vẫn vẽ + xuất nhưng không chọn được trên canvas)
        public bool Hidden { get; set; }
        public bool Locked { get; set; }
        // Màu nét vẽ: đang chọn = xanh dương, không thì màu của lớp
        // Chế độ khắc hình kín (lấy từ lớp): 0 = đường viền, 1 = tô kín (đường quét song song), 2 = tô kín + viền
        public int FillMode { get; set; }
        public double FillIntervalMm { get; set; } = 0.1;    // khoảng cách giữa 2 đường quét (mm)
        public float FillAngle { get; set; }                  // góc đường quét (độ, 0 = nằm ngang)
        public bool FillCross { get; set; }                   // quét thêm lượt vuông góc
        public bool FillBidir { get; set; } = true;           // quét 2 chiều (zigzag)
        protected Color StrokeColor => IsSelected ? Color.Blue : LayerColors.Of(LayerId);

        public abstract void Draw(Graphics g);
        public abstract bool HitTest(PointF point);

        // Di chuyển đối tượng tới vị trí mới (Line ghi đè để dịch cả 2 đầu mút)
        public virtual void MoveTo(float x, float y) { X = x; Y = y; }
    }

    public class VectorImage : Vectoritem
    {
        public Image LoadedImage { get; set; }

        // Chế độ khắc riêng của ảnh này: 0 = theo cài đặt xuất G-code, 1 = ảnh xám, 2 = đen trắng (1-bit)
        public int EngraveMode { get; set; }
        public int Threshold { get; set; } = 128;   // ngưỡng khi đen trắng
        public bool Invert { get; set; }            // khắc vùng sáng thay vì vùng tối

        public VectorImage(Image img)
        {
            Type = VectorType.Image;
            Name = "Hình ảnh";
            LoadedImage = img;
            Width = img.Width > 200 ? 200 : img.Width;   // Kích thước mặc định vừa phải
            Height = img.Height > 200 ? 200 : img.Height;
        }

        public override void Draw(Graphics g)
        {
            if (LoadedImage != null)
            {
                // Vẽ hình ảnh theo vị trí X, Y và kích thước Width, Height
                g.DrawImage(LoadedImage, X, Y, Width, Height);
            }

            // Vẽ khung viền nét đứt khi hình ảnh được chọn
            if (IsSelected)
            {
                using (Pen pen = new Pen(Color.Blue, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                {
                    g.DrawRectangle(pen, X, Y, Width, Height);
                }
            }
        }

        public override bool HitTest(PointF point)
        {
            RectangleF rect = new RectangleF(X, Y, Width, Height);
            return rect.Contains(point);
        }
    }

    public class VectorRectangle : Vectoritem
    {
        public VectorRectangle() { Type = VectorType.Rectangle; Name = "Hình chữ nhật"; }

        public override void Draw(Graphics g)
        {
            using (Pen pen = new Pen(StrokeColor, IsSelected ? 2f : 1f))
            {
                g.DrawRectangle(pen, X, Y, Width, Height);
            }
        }

        public override bool HitTest(PointF point)
        {
            RectangleF rect = new RectangleF(X, Y, Width, Height);
            return rect.Contains(point);
        }
    }
    public enum PolygonKind { Diamond, Pentagon, Hexagon, Star }

    public class VectorPolygon : Vectoritem
    {
        public PolygonKind Kind { get; }
        private readonly PointF[] unit;      // các đỉnh chuẩn hóa trong hộp 0..1

        public VectorPolygon(PolygonKind kind)
        {
            Kind = kind;
            Type = VectorType.Polygon;
            Name = kind switch
            {
                PolygonKind.Diamond => "Hình thoi",
                PolygonKind.Pentagon => "Ngũ giác",
                PolygonKind.Hexagon => "Lục giác",
                _ => "Ngôi sao"
            };
            unit = BuildUnit(kind);
            Width = 100; Height = 100;
        }

        public static PointF[] BuildUnit(PolygonKind kind)
        {
            var raw = new List<PointF>();
            switch (kind)
            {
                case PolygonKind.Diamond:
                    return new[] { new PointF(0.5f, 0), new PointF(1, 0.5f), new PointF(0.5f, 1), new PointF(0, 0.5f) };
                case PolygonKind.Pentagon: Regular(raw, 5, -90); break;
                case PolygonKind.Hexagon: Regular(raw, 6, 0); break;
                case PolygonKind.Star:
                    for (int i = 0; i < 10; i++)            // đỉnh ngoài/trong xen kẽ
                    {
                        double r = i % 2 == 0 ? 1.0 : 0.382;
                        double a = (-90 + 36 * i) * Math.PI / 180;
                        raw.Add(new PointF((float)(r * Math.Cos(a)), (float)(r * Math.Sin(a))));
                    }
                    break;
            }
            // Co giãn cho vừa khít hộp 0..1 để kéo 8 điểm là hình biến dạng theo
            float minX = raw.Min(p => p.X), maxX = raw.Max(p => p.X);
            float minY = raw.Min(p => p.Y), maxY = raw.Max(p => p.Y);
            return raw.Select(p => new PointF((p.X - minX) / (maxX - minX), (p.Y - minY) / (maxY - minY))).ToArray();
        }

        private static void Regular(List<PointF> pts, int n, double startDeg)
        {
            for (int i = 0; i < n; i++)
            {
                double a = (startDeg + 360.0 * i / n) * Math.PI / 180;
                pts.Add(new PointF((float)Math.Cos(a), (float)Math.Sin(a)));
            }
        }

        // Đỉnh theo tọa độ canvas (dùng cho vẽ, hit-test và xuất G-code)
        public PointF[] GetPoints()
        {
            var r = new PointF[unit.Length];
            for (int i = 0; i < r.Length; i++)
                r[i] = new PointF(X + unit[i].X * Width, Y + unit[i].Y * Height);
            return r;
        }

        public override void Draw(Graphics g)
        {
            using (var pen = new Pen(StrokeColor, IsSelected ? 2f : 1f))
                g.DrawPolygon(pen, GetPoints());

            if (IsSelected)   // khung nét đứt để thấy 8 điểm kéo nằm ở đâu
                using (var dash = new Pen(Color.Blue, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    g.DrawRectangle(dash, X, Y, Width, Height);
        }

        public override bool HitTest(PointF pt)       // point-in-polygon (đúng cả với ngôi sao lõm)
        {
            var v = GetPoints();
            bool inside = false;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i++)
                if ((v[i].Y > pt.Y) != (v[j].Y > pt.Y) &&
                    pt.X < (v[j].X - v[i].X) * (pt.Y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X)
                    inside = !inside;
            return inside;
        }
    }
    // Hình tròn / Elip
    public class VectorEllipse : Vectoritem
    {
        public VectorEllipse() { Type = VectorType.Ellipse; Name = "Hình tròn/Elip"; }

        public override void Draw(Graphics g)
        {
            using (Pen pen = new Pen(StrokeColor, IsSelected ? 2f : 1f))
            {
                g.DrawEllipse(pen, X, Y, Width, Height);
            }
        }

        public override bool HitTest(PointF point)
        {
            RectangleF rect = new RectangleF(X, Y, Width, Height);
            return rect.Contains(point);
        }
    }

    // Đường thẳng
    public class VectorLine : Vectoritem
    {
        public float X2 { get; set; }
        public float Y2 { get; set; }

        // Độ "dày" vùng bấm trúng đường thẳng (Form1 chỉnh theo mức zoom)
        public static float HitTolerance = 6f;

        public VectorLine() { Type = VectorType.Line; Name = "Đường thẳng"; }

        public override void Draw(Graphics g)
        {
            using (Pen pen = new Pen(StrokeColor, IsSelected ? 2f : 1f))
            {
                g.DrawLine(pen, X, Y, X2, Y2);
            }
        }

        public override bool HitTest(PointF p)
        {
            float dx = X2 - X, dy = Y2 - Y;
            float len2 = dx * dx + dy * dy;
            float t = len2 == 0 ? 0 : ((p.X - X) * dx + (p.Y - Y) * dy) / len2;
            t = Math.Clamp(t, 0f, 1f);
            float ex = p.X - (X + t * dx), ey = p.Y - (Y + t * dy);
            return ex * ex + ey * ey <= HitTolerance * HitTolerance;
        }

        public override void MoveTo(float x, float y)
        {
            float dx = x - X, dy = y - Y;
            X = x; Y = y;
            X2 += dx; Y2 += dy;
        }
    }

    // Chữ văn bản
    public class VectorText : Vectoritem
    {
        public string TextContent { get; set; } = "Nhập văn bản";
        public string FontName { get; set; } = "Arial";
        public float FontSize { get; set; } = 16f;
        public bool FontBold { get; set; }
        public bool FontItalic { get; set; }
        public bool SingleLineFont { get; set; }      // true = Font 1 nét (Hershey) khắc nhanh
        public bool WeldText { get; set; }            // true = Hàn liền các chữ cái giao nhau
        public float ArcRadiusMm { get; set; }        // Bán kính uốn cong (mm, 0 = chữ thẳng, >0 vòm xuống, <0 võng lên)
        public float CharSpacingMm { get; set; }      // Khoảng cách tăng/giảm giữa các ký tự (mm)

        public bool HasCustomPath => SingleLineFont || WeldText || Math.Abs(ArcRadiusMm) >= 1f || Math.Abs(CharSpacingMm) > 0.01f;

        public Font MakeFont()
        {
            var st = (FontBold ? FontStyle.Bold : 0) | (FontItalic ? FontStyle.Italic : 0);
            try { return new Font(FontName, FontSize, st); }
            catch (ArgumentException) { return new Font(FontName, FontSize); }   // phông không có kiểu này
        }

        public VectorText()
        {
            Type = VectorType.Text;
            Name = "Văn bản";
            Width = 150;
            Height = 40;
        }

        public override void Draw(Graphics g)
        {
            if (HasCustomPath)
            {
                var subPaths = HersheyFont.BuildTextSubPaths(this, 0.25f, 0.3f);
                if (IsSelected)
                {
                    using (Pen selPen = new Pen(Color.Blue, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    {
                        g.DrawRectangle(selPen, X, Y, Width, Height);
                    }
                }
                using (Pen strokePen = new Pen(StrokeColor, SingleLineFont ? 1.5f : 1.3f))
                {
                    strokePen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                    foreach (var sp in subPaths)
                    {
                        if (sp.Points.Count < 2) continue;
                        var pts = sp.Points.ToArray();
                        if (sp.IsClosed && pts.Length >= 3) g.DrawPolygon(strokePen, pts);
                        else g.DrawLines(strokePen, pts);
                    }
                }
                return;
            }

            using (Font font = MakeFont())
            {
                SizeF size = g.MeasureString(TextContent, font);
                Width = size.Width;
                Height = size.Height;

                // Vẽ viền xanh nét đứt bao quanh khi được chọn
                if (IsSelected)
                {
                    using (Pen pen = new Pen(Color.Blue, 1f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                    {
                        g.DrawRectangle(pen, X, Y, Width, Height);
                    }
                }

                // Vẽ chữ lên canvas
                using (Brush brush = new SolidBrush(StrokeColor))
                {
                    g.DrawString(TextContent, font, brush, X, Y);
                }
            }
        }

        public override bool HitTest(PointF point)
        {
            RectangleF rect = new RectangleF(X, Y, Width, Height);
            return rect.Contains(point);
        }
    }
}
