using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace test01_1014
{
    using Point = System.Drawing.Point;
    using Size = System.Drawing.Size;
    using Font = System.Drawing.Font;
    using SubPath = VectorPathItem.SubPath;

    /// <summary>
    /// Tham số tách nét ảnh Raster sang Vector (Potrace / Contour-to-Vector).
    /// </summary>
    public class TraceOptions
    {
        public int Threshold { get; set; } = 128;          // Ngưỡng đen/trắng (0..255)
        public bool AutoOtsu { get; set; } = false;        // Tự động tính ngưỡng tối ưu theo Otsu
        public bool Invert { get; set; } = false;          // Đảo ngược vùng sáng / tối
        public int BlurRadius { get; set; } = 1;           // Làm mượt ảnh trước khi tách biên (0..5)
        public double TurdSizePx { get; set; } = 10.0;     // Loại bỏ đốm nhiễu nhỏ có diện tích < TurdSizePx (px²)
        public double AlphaMax { get; set; } = 1.0;        // Ngưỡng uốn cong Potrace (0 = đa giác sắc cạnh, 1.0..1.33 = làm mượt Bezier)
        public double CurveTolerance { get; set; } = 1.1;  // Độ thu gọn điểm nút (Douglas-Peucker epsilon, px)
        public bool KeepOriginalImage { get; set; } = false;
    }

    /// <summary>
    /// Bộ chuyển đổi ảnh Bitmap/Raster (logo, bản vẽ tay, hình chụp) thành đường cắt VectorPathItem mượt
    /// theo thuật toán Potrace (phát hiện biên lồng nhau + lọc nhiễu TurdSize + xấp xỉ đa giác tối ưu + làm mượt góc Bezier).
    /// </summary>
    public static class ImageTracer
    {
        /// <summary>
        /// Tách nét từ đối tượng VectorImage trên canvas thành VectorPathItem khớp đúng vị trí, kích thước, góc xoay.
        /// </summary>
        public static VectorPathItem? TraceImageItem(VectorImage imgItem, TraceOptions opt)
        {
            if (imgItem.LoadedImage == null) return null;

            var rawPaths = TraceBitmap(imgItem.LoadedImage, opt, out int bmpW, out int bmpH);
            if (rawPaths.Count == 0 || bmpW <= 0 || bmpH <= 0) return null;

            float sx = imgItem.Width / bmpW;
            float sy = imgItem.Height / bmpH;
            float ox = imgItem.X;
            float oy = imgItem.Y;

            var worldPaths = new List<SubPath>(rawPaths.Count);
            foreach (var sp in rawPaths)
            {
                var pts = new List<PointF>(sp.Points.Count);
                foreach (var p in sp.Points)
                {
                    pts.Add(new PointF(ox + p.X * sx, oy + p.Y * sy));
                }
                if (pts.Count >= 3)
                {
                    worldPaths.Add(new SubPath { Points = pts, IsClosed = true });
                }
            }

            if (worldPaths.Count == 0) return null;

            var pathItem = new VectorPathItem
            {
                Name = imgItem.Name + " (Vector Trace)",
                LayerId = imgItem.LayerId,
                LaserPower = imgItem.LaserPower,
                LaserSpeed = imgItem.LaserSpeed,
                Passes = imgItem.Passes,
                Rotation = imgItem.Rotation,
                FlipX = imgItem.FlipX,
                FlipY = imgItem.FlipY,
                FillMode = imgItem.FillMode,
                FillIntervalMm = imgItem.FillIntervalMm,
                FillAngle = imgItem.FillAngle,
                FillCross = imgItem.FillCross,
                FillBidir = imgItem.FillBidir,
                SubPaths = worldPaths
            };
            pathItem.RecalculateBounds();
            return pathItem;
        }

        /// <summary>
        /// Tách nét một ảnh System.Drawing.Image thành danh sách SubPath trong hệ tọa độ ảnh.
        /// </summary>
        public static List<SubPath> TraceBitmap(Image srcImage, TraceOptions opt, out int workW, out int workH)
        {
            workW = srcImage.Width;
            workH = srcImage.Height;
            var result = new List<SubPath>();
            if (workW <= 1 || workH <= 1) return result;

            // Giới hạn kích thước xử lý tối đa 1800px để đảm bảo tốc độ phản hồi thời gian thực
            float scale = Math.Min(1f, 1800f / Math.Max(workW, workH));
            int w = Math.Max(2, (int)Math.Round(workW * scale));
            int h = Math.Max(2, (int)Math.Round(workH * scale));
            float invScale = 1f / scale;

            using var flatBmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(flatBmp))
            {
                g.Clear(opt.Invert ? Color.Black : Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(srcImage, 0, 0, w, h);
            }

            using var bgr = BitmapConverter.ToMat(flatBmp);
            using var gray = new Mat();
            Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

            using var blurred = new Mat();
            if (opt.BlurRadius > 0)
            {
                int k = 2 * Math.Clamp(opt.BlurRadius, 1, 8) + 1;
                Cv2.GaussianBlur(gray, blurred, new OpenCvSharp.Size(k, k), 0);
            }
            else
            {
                gray.CopyTo(blurred);
            }

            using var bin = new Mat();
            ThresholdTypes thType = opt.Invert ? ThresholdTypes.Binary : ThresholdTypes.BinaryInv;
            if (opt.AutoOtsu) thType |= ThresholdTypes.Otsu;
            Cv2.Threshold(blurred, bin, Math.Clamp(opt.Threshold, 0, 255), 255, thType);

            // Tìm toàn bộ biên ngoài + lỗ bên trong (RetrievalModes.List lấy đủ cả đường bao ngoài lẫn lỗ)
            Cv2.FindContours(bin, out OpenCvSharp.Point[][] contours, out HierarchyIndex[] _,
                             RetrievalModes.List, ContourApproximationModes.ApproxNone);

            double minArea = Math.Max(1.0, opt.TurdSizePx * scale * scale);
            double eps = Math.Max(0.35, opt.CurveTolerance * scale);
            float alphaMax = (float)Math.Clamp(opt.AlphaMax, 0.0, 1.34);

            foreach (var rawContour in contours)
            {
                if (rawContour.Length < 4) continue;
                double area = Math.Abs(Cv2.ContourArea(rawContour));
                if (area < minArea) continue;

                // Bước 1 (Potrace Polygonization): rút gọn bậc thang pixel thành đa giác tối ưu
                OpenCvSharp.Point[] poly = Cv2.ApproxPolyDP(rawContour, eps, closed: true);
                if (poly.Length < 3) continue;

                var polyPts = new List<PointF>(poly.Length);
                foreach (var pt in poly)
                {
                    polyPts.Add(new PointF(pt.X * invScale, pt.Y * invScale));
                }

                // Bước 2 (Potrace Curve Smoothing): phát hiện góc nhọn vs uốn cong Bezier mượt
                List<PointF> smoothed = alphaMax > 0.02f
                    ? SmoothPolygonPotrace(polyPts, alphaMax)
                    : polyPts;

                if (smoothed.Count >= 3)
                {
                    result.Add(new SubPath { Points = smoothed, IsClosed = true });
                }
            }

            return result;
        }

        /// <summary>
        /// Làm mượt đa giác theo nguyên lý Potrace:
        /// Tại mỗi đỉnh P_i, nối trung điểm M_{i-1} của cạnh trước với trung điểm M_i của cạnh sau.
        /// Dựa vào góc gập tại P_i và hệ số alphaMax:
        /// - Nếu là góc nhọn thực sự (alpha > alphaMax): giữ nguyên đỉnh sắc nét P_i.
        /// - Nếu là đoạn cong mượt: nội suy đường cong Bezier bậc 3 đi qua kiểm soát Potrace.
        /// </summary>
        private static List<PointF> SmoothPolygonPotrace(List<PointF> poly, float alphaMax)
        {
            int n = poly.Count;
            if (n < 3) return poly;

            var outPts = new List<PointF>(n * 4);

            for (int i = 0; i < n; i++)
            {
                PointF prev = poly[(i - 1 + n) % n];
                PointF cur = poly[i];
                PointF next = poly[(i + 1) % n];

                PointF m0 = new PointF((prev.X + cur.X) * 0.5f, (prev.Y + cur.Y) * 0.5f);
                PointF m1 = new PointF((cur.X + next.X) * 0.5f, (cur.Y + next.Y) * 0.5f);

                // Tính góc lệch giữa 2 vector (prev -> cur) và (cur -> next)
                float v1x = cur.X - prev.X, v1y = cur.Y - prev.Y;
                float v2x = next.X - cur.X, v2y = next.Y - cur.Y;
                float len1 = MathF.Sqrt(v1x * v1x + v1y * v1y);
                float len2 = MathF.Sqrt(v2x * v2x + v2y * v2y);

                if (len1 < 0.01f || len2 < 0.01f)
                {
                    outPts.Add(cur);
                    continue;
                }

                float cosAngle = Math.Clamp((v1x * v2x + v1y * v2y) / (len1 * len2), -1f, 1f);
                // cosAngle = 1: thẳng hàng; cosAngle = 0: vuông góc 90 độ; cosAngle < 0: góc nhọn gập ngược
                // Quy đổi sang tham số alpha của Potrace (góc càng nhọn thì alpha càng lớn)
                float turnFactor = (1f - cosAngle) * 0.85f;

                if (turnFactor > alphaMax)
                {
                    // Giữ đỉnh góc sắc nét
                    outPts.Add(m0);
                    outPts.Add(cur);
                }
                else
                {
                    // Tạo đường cong Bezier bậc 3 từ m0 tới m1 ôm theo đỉnh cur
                    float alpha = Math.Clamp(0.55f + 0.25f * alphaMax, 0.4f, 0.88f);
                    PointF c1 = new PointF(m0.X + (cur.X - m0.X) * alpha, m0.Y + (cur.Y - m0.Y) * alpha);
                    PointF c2 = new PointF(m1.X + (cur.X - m1.X) * alpha, m1.Y + (cur.Y - m1.Y) * alpha);

                    float segLen = MathF.Sqrt((m1.X - m0.X) * (m1.X - m0.X) + (m1.Y - m0.Y) * (m1.Y - m0.Y));
                    int steps = Math.Clamp((int)MathF.Ceiling(segLen / 3.5f), 3, 10);

                    for (int s = 0; s < steps; s++)
                    {
                        float t = (float)s / steps;
                        float u = 1f - t;
                        float bx = u * u * u * m0.X + 3f * u * u * t * c1.X + 3f * u * t * t * c2.X + t * t * t * m1.X;
                        float by = u * u * u * m0.Y + 3f * u * u * t * c1.Y + 3f * u * t * t * c2.Y + t * t * t * m1.Y;
                        outPts.Add(new PointF(bx, by));
                    }
                }
            }

            return outPts;
        }
    }

    /// <summary>
    /// Hộp thoại tách nét Vector (Potrace) có xem trước trực quan theo thời gian thực.
    /// </summary>
    public sealed class ImageTraceDialog : Form
    {
        private readonly VectorImage srcItem;
        private readonly PictureBox previewBox;
        private readonly NumericUpDown nudThreshold, nudBlur, nudTurd, nudSmooth, nudTolerance;
        private readonly CheckBox chkOtsu, chkInvert, chkKeepImg, chkShowBg;
        private readonly Label lblStats;
        private readonly System.Windows.Forms.Timer debounceTimer;

        public TraceOptions Options { get; } = new TraceOptions();
        public List<SubPath> PreviewPaths { get; private set; } = new();
        private int previewW = 1, previewH = 1;

        public ImageTraceDialog(VectorImage item)
        {
            srcItem = item;
            Options.Threshold = item.EngraveMode == 2 ? item.Threshold : 128;
            Options.Invert = item.Invert;

            Text = "Tách nét Vector từ ảnh (Potrace / Contour-to-Vector)";
            Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(900, 600);
            MinimumSize = new Size(740, 480);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                FixedPanel = FixedPanel.Panel2,
                SplitterDistance = 570
            };
            Controls.Add(split);

            // Bên trái: khung xem trước đường vector trên nền ảnh
            previewBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(245, 247, 250),
                BorderStyle = BorderStyle.FixedSingle
            };
            previewBox.Paint += PreviewBox_Paint;
            previewBox.Resize += (s, e) => previewBox.Invalidate();
            split.Panel1.Controls.Add(previewBox);

            // Bên phải: bảng thông số Potrace
            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), AutoScroll = true, BackColor = Color.White };
            split.Panel2.Controls.Add(right);

            int y = 10;
            int w = 285;

            Label AddHeader(string title)
            {
                var l = new Label
                {
                    Text = title,
                    Bounds = new Rectangle(10, y, w, 24),
                    BackColor = Color.FromArgb(222, 229, 240),
                    ForeColor = Color.FromArgb(40, 60, 100),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(6, 0, 0, 0)
                };
                right.Controls.Add(l);
                y += 30;
                return l;
            }

            NumericUpDown AddNumRow(string label, decimal min, decimal max, decimal val, int dec, decimal inc)
            {
                var lbl = new Label { Text = label, Bounds = new Rectangle(10, y + 3, 180, 22), Font = new Font("Segoe UI", 9f, FontStyle.Regular) };
                var nud = new NumericUpDown
                {
                    Bounds = new Rectangle(195, y, 100, 26),
                    Minimum = min,
                    Maximum = max,
                    DecimalPlaces = dec,
                    Increment = inc,
                    Value = Math.Clamp(val, min, max)
                };
                nud.ValueChanged += (s, e) => ScheduleTrace();
                right.Controls.Add(lbl);
                right.Controls.Add(nud);
                y += 32;
                return nud;
            }

            CheckBox AddCheckRow(string text, bool isChecked)
            {
                var chk = new CheckBox
                {
                    Text = text,
                    Bounds = new Rectangle(12, y, w, 24),
                    Checked = isChecked,
                    Font = new Font("Segoe UI", 9f, FontStyle.Regular)
                };
                chk.CheckedChanged += (s, e) => ScheduleTrace();
                right.Controls.Add(chk);
                y += 28;
                return chk;
            }

            AddHeader("1. NGƯỠNG TÁCH ĐEN / TRẮNG");
            nudThreshold = AddNumRow("Ngưỡng sáng tối (0-255):", 1, 254, Options.Threshold, 0, 5);
            chkOtsu = AddCheckRow("Tự động lấy ngưỡng tối ưu (Otsu)", false);
            chkOtsu.CheckedChanged += (s, e) => nudThreshold.Enabled = !chkOtsu.Checked;
            chkInvert = AddCheckRow("Đảo ngược vùng sáng / tối", Options.Invert);
            nudBlur = AddNumRow("Khử răng cưa ảnh (Blur 0-5):", 0, 5, 1, 0, 1);

            y += 6;
            AddHeader("2. LÀM MƯỢT POTRACE & LỌC NHIỄU");
            nudTurd = AddNumRow("Lọc bỏ đốm nhiễu < (px²):", 0, 1000, 10, 0, 5);
            nudSmooth = AddNumRow("Độ mượt cong Bezier (0-1.3):", 0m, 1.33m, 1.0m, 2, 0.1m);
            nudTolerance = AddNumRow("Độ thu gọn điểm nút (px):", 0.3m, 8.0m, 1.1m, 1, 0.2m);

            y += 6;
            AddHeader("3. TÙY CHỌN ĐẦU RA");
            chkShowBg = AddCheckRow("Hiện ảnh gốc mờ phía dưới", true);
            chkShowBg.CheckedChanged += (s, e) => previewBox.Invalidate();
            chkKeepImg = AddCheckRow("Giữ lại cả đối tượng ảnh gốc", false);

            y += 6;
            lblStats = new Label
            {
                Bounds = new Rectangle(10, y, w, 46),
                ForeColor = Color.FromArgb(30, 80, 150),
                Text = "Đang tính toán đường vector..."
            };
            right.Controls.Add(lblStats);
            y += 52;

            var btnOk = new Button
            {
                Text = "Tạo đường cắt Vector",
                Bounds = new Rectangle(10, y, 185, 36),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                DialogResult = DialogResult.OK
            };
            var btnCancel = new Button
            {
                Text = "Hủy",
                Bounds = new Rectangle(202, y, 93, 36),
                DialogResult = DialogResult.Cancel
            };
            right.Controls.Add(btnOk);
            right.Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            debounceTimer = new System.Windows.Forms.Timer { Interval = 80 };
            debounceTimer.Tick += (s, e) =>
            {
                debounceTimer.Stop();
                RunTracePreview();
            };

            Shown += (s, e) => RunTracePreview();
        }

        private void ScheduleTrace()
        {
            if (debounceTimer == null) return;
            debounceTimer.Stop();
            debounceTimer.Start();
        }

        private void RunTracePreview()
        {
            if (srcItem.LoadedImage == null) return;
            Options.Threshold = (int)nudThreshold.Value;
            Options.AutoOtsu = chkOtsu.Checked;
            Options.Invert = chkInvert.Checked;
            Options.BlurRadius = (int)nudBlur.Value;
            Options.TurdSizePx = (double)nudTurd.Value;
            Options.AlphaMax = (double)nudSmooth.Value;
            Options.CurveTolerance = (double)nudTolerance.Value;
            Options.KeepOriginalImage = chkKeepImg.Checked;

            PreviewPaths = ImageTracer.TraceBitmap(srcItem.LoadedImage, Options, out previewW, out previewH);
            int totalNodes = PreviewPaths.Sum(sp => sp.Points.Count);
            lblStats.Text = $"Số đường cắt (SubPaths): {PreviewPaths.Count}\nTổng số điểm nút (Nodes): {totalNodes}";
            previewBox.Invalidate();
        }

        private void PreviewBox_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (srcItem.LoadedImage == null || previewW <= 0 || previewH <= 0) return;

            float pad = 20f;
            float availW = Math.Max(50f, previewBox.Width - pad * 2);
            float availH = Math.Max(50f, previewBox.Height - pad * 2);
            float sc = Math.Min(availW / previewW, availH / previewH);
            float ox = (previewBox.Width - previewW * sc) * 0.5f;
            float oy = (previewBox.Height - previewH * sc) * 0.5f;

            // Khung nền trắng của ảnh
            g.FillRectangle(Brushes.White, ox, oy, previewW * sc, previewH * sc);

            if (chkShowBg.Checked)
            {
                var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.28f }; // độ mờ 28%
                using var ia = new System.Drawing.Imaging.ImageAttributes();
                ia.SetColorMatrix(cm);
                g.DrawImage(srcItem.LoadedImage,
                    new Rectangle((int)ox, (int)oy, (int)(previewW * sc), (int)(previewH * sc)),
                    0, 0, srcItem.LoadedImage.Width, srcItem.LoadedImage.Height, GraphicsUnit.Pixel, ia);
            }

            g.DrawRectangle(Pens.Silver, ox, oy, previewW * sc, previewH * sc);

            // Vẽ các đường biên vector vừa tách được bằng nét đỏ Crimson
            using var pen = new Pen(Color.Crimson, 1.6f) { LineJoin = LineJoin.Round };
            foreach (var sp in PreviewPaths)
            {
                if (sp.Points.Count < 3) continue;
                var pts = new PointF[sp.Points.Count];
                for (int i = 0; i < sp.Points.Count; i++)
                {
                    pts[i] = new PointF(ox + sp.Points[i].X * sc, oy + sp.Points[i].Y * sc);
                }
                g.DrawPolygon(pen, pts);
            }
        }
    }
}
