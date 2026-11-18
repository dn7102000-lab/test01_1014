// =====================================================================
//  GCode.cs  -  G-CODE: biên dịch hình thành G-code và G-code khung đèn đỏ
//
//  Gồm các phần (tìm nhanh bằng Ctrl+F "PHẦN"):
//    1. Biên dịch G-code   (trước đây: GCodeCompiler.cs)
//    2. Khung đèn đỏ / khung bao   (trước đây: GCodeFrame.cs)
// =====================================================================

// =====================================================================
// PHẦN 1: Biên dịch G-code   (trước đây là file GCodeCompiler.cs)
// =====================================================================
// =====================================================================
//  GCodeCompiler.cs  -  Biên dịch danh sách Vectoritem -> chuỗi G-code
//  Project: test01_1014 (.NET 8, WinForms)
//
//  Hỗ trợ:
//    VectorRectangle / VectorEllipse / VectorLine  -> khắc/cắt viền
//    VectorText                                    -> khắc viền chữ (outline)
//    VectorImage                                   -> khắc ảnh raster (zig-zag)
//
//  Quy ước đơn vị:
//    Canvas: pixel, gốc góc TRÊN-TRÁI, Y hướng xuống
//    G-code: mm,    gốc góc DƯỚI-TRÁI, Y hướng lên   (chuẩn máy GRBL)
//    => nhân MmPerPixel và lật trục Y.
//
//  Công suất: Vectoritem.LaserPower (0-100 %) -> S = MaxS * % / 100
//  Tốc độ   : Vectoritem.LaserSpeed (mm/phút) -> F
//  Số lần   : Vectoritem.Passes
// =====================================================================
namespace test01_1014
{
    using System.Drawing;
    using System.Drawing.Drawing2D;
    using System.Drawing.Imaging;
    using System.Globalization;
    using System.Text;

    public partial class GCodeSettings
    {
        /// <summary>1 pixel trên canvas = bao nhiêu mm. 0.25 => ô lưới 20 px = 5 mm.</summary>
        public float MmPerPixel { get; set; } = 0.25f;

        /// <summary>true: góc dưới-trái của cả bản vẽ là (0,0). false: dùng toạ độ tuyệt đối của canvas.</summary>
        public bool OriginAtJobCorner { get; set; } = true;

        /// <summary>Chiều cao canvas (px). Chỉ dùng khi OriginAtJobCorner = false.</summary>
        public float CanvasHeightPx { get; set; } = 600f;

        /// <summary>Giá trị S tối đa của firmware (GRBL mặc định $30=1000).</summary>
        public int MaxS { get; set; } = 1000;

        /// <summary>M4 = laser động (khuyên dùng cho GRBL), M3 = laser hằng.</summary>
        public string LaserOnCommand { get; set; } = "M4";

        /// <summary>Sai số khi xấp xỉ đường cong bằng đoạn thẳng (mm).</summary>
        public double CurveToleranceMm { get; set; } = 0.05;

        /// <summary>Khoảng cách giữa 2 dòng quét khi khắc ảnh (mm).</summary>
        public double RasterStepMm { get; set; } = 0.1;

        public bool ImageBinary { get; set; } = false;   // true: đen/trắng 1-bit
        public int ImageThreshold { get; set; } = 128;   // ngưỡng khi ImageBinary
        public bool ImageInvert { get; set; } = false;   // true: khắc phần sáng

        /// <summary>Khắc ảnh trước, cắt viền sau (tránh vật liệu xê dịch khi cắt xong).</summary>
        public bool EngraveImagesFirst { get; set; } = true;

        /// <summary>Tối ưu hóa thứ tự đường cắt (TSP Nearest-Neighbor + 2-Opt + chọn đỉnh bắt đầu gần nhất) để giảm quãng đường G0.</summary>
        public bool OptimizeToolpath { get; set; } = true;

        /// <summary>Cắt lỗ bên trong trước, đường bao bên ngoài sau (Inside-Out) để vật cắt không bị rơi/lệch sớm.</summary>
        public bool CutInsideOut { get; set; } = true;

        public bool ReturnToOrigin { get; set; } = true;

        /// <summary>Tốc độ di chuyển nhanh giả định, chỉ dùng để ước tính thời gian.</summary>
        public int RapidFeedForEstimate { get; set; } = 3000;
    }

    public class GCodeResult
    {
        public string Code { get; set; } = "";
        public int LineCount { get; set; }
        public double WidthMm { get; set; }
        public double HeightMm { get; set; }
        public double CutLengthMm { get; set; }     // tổng quãng đường G1
        public double RapidLengthMm { get; set; }   // tổng quãng đường G0
        public double EstimatedMinutes { get; set; }
    }

    public partial class GCodeCompiler
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly GCodeSettings _cfg;

        // ---- Job trung gian (vẫn ở toạ độ pixel) ----
        abstract class Job
        {
            public string Name = "";
            public int Power;     // giá trị S đã quy đổi
            public int Feed;
            public int Passes = 1;
        }
        sealed class PathJob : Job
        {
            public List<List<PointF>> Paths = new();
            public int FillCount;            // > 0: CountFill đường đầu là đường TÔ, phần còn lại là đường VIỀN
        }
        sealed class RasterJob : Job
        {
            public RectangleF Rect;          // vị trí + kích thước (px)
            public int Cols, Rows;
            public int[] Pw = Array.Empty<int>();   // S cho từng pixel lưới
        }

        public GCodeCompiler(GCodeSettings? settings = null)
        {
            _cfg = settings ?? new GCodeSettings();
        }

        public GCodeSettings Settings => _cfg;

        // =================================================================
        //  HÀM CHÍNH
        // =================================================================
        public GCodeResult Compile(IEnumerable<Vectoritem> items)
        {
            // 1) Đổi từng Vectoritem thành Job
            var jobs = new List<Job>();
            foreach (var it in items)
            {
                Job? j = BuildJob(it);
                if (j != null) jobs.Add(j);
            }
            if (jobs.Count == 0)
                throw new InvalidOperationException("Không có đối tượng nào để xuất G-code.");

            if (_cfg.EngraveImagesFirst)
                jobs = jobs.OrderBy(j => j is RasterJob ? 0 : 1).ToList();   // OrderBy ổn định

            // 2) Khung bao của toàn bộ bản vẽ (px)
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var job in jobs)
            {
                if (job is PathJob pj)
                {
                    foreach (var path in pj.Paths)
                        foreach (var p in path)
                        {
                            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                        }
                }
                else if (job is RasterJob rj)
                {
                    minX = Math.Min(minX, rj.Rect.Left); maxX = Math.Max(maxX, rj.Rect.Right);
                    minY = Math.Min(minY, rj.Rect.Top); maxY = Math.Max(maxY, rj.Rect.Bottom);
                }
            }

            // 3) Hàm đổi toạ độ canvas(px) -> máy(mm), có lật trục Y
            float k = _cfg.MmPerPixel;
            float originX = _cfg.OriginAtJobCorner ? minX : _cfg.AbsOriginXpx;
            float originY = _cfg.OriginAtJobCorner ? maxY
                          : (float.IsNaN(_cfg.AbsOriginYpx) ? _cfg.CanvasHeightPx : _cfg.AbsOriginYpx);
            double MX(float px) => (px - originX) * k;
            double MY(float py) => (originY - py) * k;

            // 3b) Tối ưu hóa thứ tự đường chạy (TSP / Nearest Neighbor / 2-Opt / Inside-Out)
            if (_cfg.OptimizeToolpath || _cfg.CutInsideOut)
            {
                jobs = OptimizeJobSequence(jobs, originX, originY);
            }

            // 4) Phát sinh G-code
            var sb = new StringBuilder();
            int lines = 0;
            double cx = 0, cy = 0, cutLen = 0, rapidLen = 0, minutes = 0;

            void Line(string s) { sb.AppendLine(s); lines++; }
            void Rapid(double x, double y)
            {
                rapidLen += Dist(cx, cy, x, y);
                cx = x; cy = y;
                Line($"G0 X{F(x)} Y{F(y)}");
            }
            void CutXY(double x, double y, int feed, string extra)
            {
                double d = Dist(cx, cy, x, y);
                cutLen += d; minutes += d / Math.Max(1, feed);
                cx = x; cy = y;
                Line($"G1 X{F(x)} Y{F(y)}{extra}");
            }
            void CutX(double x, int s, int feed)
            {
                double d = Math.Abs(x - cx);
                cutLen += d; minutes += d / Math.Max(1, feed);
                cx = x;
                Line($"G1 X{F(x)} S{s}");
            }

            double widthMm = (maxX - minX) * k, heightMm = (maxY - minY) * k;
            Line($"; test01_1014 G-code | {jobs.Count} job | {F(widthMm)} x {F(heightMm)} mm");
            Line("G21 G90");
            Line($"{_cfg.LaserOnCommand} S0");

            int idx = 0;
            foreach (var job in jobs)
            {
                idx++;
                Line($"; [{idx}] {job.Name} | S{job.Power} F{job.Feed} x{job.Passes}");
                Line($"F{job.Feed}");

                for (int pass = 1; pass <= job.Passes; pass++)
                {
                    if (job.Passes > 1) Line($"; pass {pass}/{job.Passes}");

                    if (job is PathJob pj)
                    {
                        int pIdx = -1;
                        foreach (var path in pj.Paths)
                        {
                            pIdx++;
                            // nhãn để bản xem trước G-code tô màu riêng cho đường viền (Tô kín + viền)
                            if (pj.FillCount > 0 && pIdx == 0) Line("; fill");
                            else if (pj.FillCount > 0 && pIdx == pj.FillCount) Line("; outline");
                            if (path.Count < 2) continue;
                            Rapid(MX(path[0].X), MY(path[0].Y));
                            for (int i = 1; i < path.Count; i++)
                                CutXY(MX(path[i].X), MY(path[i].Y), job.Feed,
                                      i == 1 ? $" S{job.Power}" : "");
                            Line("S0");
                        }
                    }
                    else if (job is RasterJob rj)
                    {
                        double x0 = MX(rj.Rect.Left);
                        double yTop = MY(rj.Rect.Top);
                        double stepX = rj.Rect.Width * k / rj.Cols;
                        double stepY = rj.Rect.Height * k / rj.Rows;
                        bool ltr = true;

                        for (int r = 0; r < rj.Rows; r++)
                        {
                            int lo = -1, hi = -1, rowOff = r * rj.Cols;
                            for (int c = 0; c < rj.Cols; c++)
                                if (rj.Pw[rowOff + c] > 0) { if (lo < 0) lo = c; hi = c; }
                            if (lo < 0) continue;                       // dòng trống -> bỏ qua

                            double y = yTop - (r + 0.5) * stepY;
                            if (ltr)
                            {
                                Rapid(x0 + lo * stepX, y);
                                int cur = rj.Pw[rowOff + lo];
                                for (int c = lo + 1; c <= hi + 1; c++)
                                {
                                    int v = c <= hi ? rj.Pw[rowOff + c] : -1;
                                    if (v != cur) { CutX(x0 + c * stepX, cur, job.Feed); cur = v; }
                                }
                            }
                            else
                            {
                                Rapid(x0 + (hi + 1) * stepX, y);
                                int cur = rj.Pw[rowOff + hi];
                                for (int c = hi - 1; c >= lo - 1; c--)
                                {
                                    int v = c >= lo ? rj.Pw[rowOff + c] : -1;
                                    if (v != cur) { CutX(x0 + (c + 1) * stepX, cur, job.Feed); cur = v; }
                                }
                            }
                            Line("S0");
                            ltr = !ltr;
                        }
                    }
                }
            }

            Line("M5");
            if (_cfg.ReturnToOrigin) Rapid(0, 0);

            minutes += rapidLen / Math.Max(1, _cfg.RapidFeedForEstimate);

            return new GCodeResult
            {
                Code = sb.ToString(),
                LineCount = lines,
                WidthMm = widthMm,
                HeightMm = heightMm,
                CutLengthMm = cutLen,
                RapidLengthMm = rapidLen,
                EstimatedMinutes = minutes
            };
        }

        // =================================================================
        //  TỐI ƯU HÓA ĐƯỜNG CHẠY DAO / LASER (TSP + INSIDE-OUT)
        // =================================================================
        private List<Job> OptimizeJobSequence(List<Job> jobs, float startX, float startY)
        {
            if (jobs.Count == 0) return jobs;

            var result = new List<Job>(jobs.Count);
            PointF head = new PointF(startX, startY);

            int i = 0;
            while (i < jobs.Count)
            {
                if (jobs[i] is RasterJob)
                {
                    // Nhóm các RasterJob liên tiếp
                    var rGroup = new List<RasterJob>();
                    while (i < jobs.Count && jobs[i] is RasterJob rj)
                    {
                        rGroup.Add(rj);
                        i++;
                    }
                    if (_cfg.OptimizeToolpath && rGroup.Count > 1)
                    {
                        while (rGroup.Count > 0)
                        {
                            int best = 0;
                            float bestD = float.MaxValue;
                            for (int k = 0; k < rGroup.Count; k++)
                            {
                                var rc = rGroup[k].Rect;
                                float d = DistSq(head, new PointF(rc.Left, rc.Top));
                                if (d < bestD) { bestD = d; best = k; }
                            }
                            var pick = rGroup[best];
                            rGroup.RemoveAt(best);
                            result.Add(pick);
                            head = new PointF(pick.Rect.Right, pick.Rect.Bottom);
                        }
                    }
                    else
                    {
                        foreach (var rj in rGroup)
                        {
                            result.Add(rj);
                            head = new PointF(rj.Rect.Right, rj.Rect.Bottom);
                        }
                    }
                    continue;
                }

                // Nhóm các PathJob liên tiếp có cùng thông số (Power, Feed, Passes) để tối ưu thứ tự cắt
                var pGroup = new List<PathJob>();
                var firstPj = (PathJob)jobs[i];
                while (i < jobs.Count && jobs[i] is PathJob pj &&
                       pj.Power == firstPj.Power && pj.Feed == firstPj.Feed && pj.Passes == firstPj.Passes)
                {
                    pGroup.Add(pj);
                    i++;
                }

                // Tách các PathJob có tô kín (FillCount > 0) chạy trước các PathJob chỉ cắt viền
                var fillJobs = pGroup.Where(p => p.FillCount > 0 && p.FillCount >= p.Paths.Count).ToList();
                var fillAndOutlineJobs = pGroup.Where(p => p.FillCount > 0 && p.FillCount < p.Paths.Count).ToList();
                var cutOnlyJobs = pGroup.Where(p => p.FillCount == 0).ToList();

                // 1) Các job chỉ tô kín: sắp xếp Nearest-Neighbor
                OrderPathJobsOnly(fillJobs, ref head, allowInsideOut: false);
                result.AddRange(fillJobs);

                // 2) Các job vừa tô kín vừa cắt viền: tối ưu phần đường viền bên trong từng job
                OrderPathJobsOnly(fillAndOutlineJobs, ref head, allowInsideOut: _cfg.CutInsideOut);
                result.AddRange(fillAndOutlineJobs);

                // 3) Các job chỉ cắt viền: sắp xếp thứ tự giữa các đối tượng (Inside-Out + Nearest-Neighbor + 2-Opt)
                //    và tối ưu các đường con bên trong từng đối tượng
                OrderPathJobsOnly(cutOnlyJobs, ref head, allowInsideOut: _cfg.CutInsideOut);
                result.AddRange(cutOnlyJobs);
            }

            return result;
        }

        private void OrderPathJobsOnly(List<PathJob> pJobs, ref PointF head, bool allowInsideOut)
        {
            if (pJobs.Count == 0) return;

            // Tính khung bao và đường bao ngoài đại diện cho từng PathJob để kiểm tra lồng nhau (Inside-Out giữa các đối tượng rời)
            int m = pJobs.Count;
            var bounds = new RectangleF[m];
            var outerPolys = new List<PointF>?[m];

            for (int i = 0; i < m; i++)
            {
                bounds[i] = GetJobBounds(pJobs[i]);
                outerPolys[i] = GetLargestClosedLoop(pJobs[i]);
            }

            var jobDepth = new int[m];
            if (allowInsideOut && m > 1)
            {
                for (int i = 0; i < m; i++)
                {
                    if (pJobs[i].Paths.Count == 0 || pJobs[i].Paths[0].Count == 0) continue;
                    PointF samplePt = pJobs[i].Paths[0][0];
                    int depth = 0;
                    for (int j = 0; j < m; j++)
                    {
                        if (i == j || outerPolys[j] == null) continue;
                        if (!bounds[j].Contains(bounds[i])) continue;
                        if (IsPointInPolygon(samplePt, outerPolys[j]!))
                            depth++;
                    }
                    jobDepth[i] = depth;
                }
            }

            // Nhóm theo từng mức độ sâu (từ sâu nhất = bên trong nhất ra ngoài cùng = 0)
            var orderedJobs = new List<PathJob>(m);
            int maxD = m > 0 ? jobDepth.Max() : 0;

            for (int d = maxD; d >= 0; d--)
            {
                var bucket = new List<PathJob>();
                for (int i = 0; i < m; i++)
                {
                    if (jobDepth[i] == d) bucket.Add(pJobs[i]);
                }
                if (bucket.Count == 0) continue;

                if (_cfg.OptimizeToolpath)
                {
                    while (bucket.Count > 0)
                    {
                        int bestIdx = 0;
                        float bestDist = float.MaxValue;
                        for (int k = 0; k < bucket.Count; k++)
                        {
                            // Tối ưu thử đường nội bộ của job hoặc lấy khoảng cách gần nhất tới điểm trong job
                            float dist = MinDistToJob(head, bucket[k]);
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                bestIdx = k;
                            }
                        }
                        var chosen = bucket[bestIdx];
                        bucket.RemoveAt(bestIdx);
                        OptimizeSubPathsInsideJob(chosen, ref head, allowInsideOut);
                        orderedJobs.Add(chosen);
                    }
                }
                else
                {
                    foreach (var pj in bucket)
                    {
                        OptimizeSubPathsInsideJob(pj, ref head, allowInsideOut);
                        orderedJobs.Add(pj);
                    }
                }
            }

            pJobs.Clear();
            pJobs.AddRange(orderedJobs);
        }

        private void OptimizeSubPathsInsideJob(PathJob pj, ref PointF head, bool allowInsideOut)
        {
            if (pj.Paths.Count == 0) return;

            // Giữ nguyên các đường tô kín (0 .. FillCount - 1) ở đầu, nhưng vẫn cập nhật vị trí head
            int startOutline = Math.Clamp(pj.FillCount, 0, pj.Paths.Count);
            for (int i = 0; i < startOutline; i++)
            {
                var fp = pj.Paths[i];
                if (fp.Count > 0) head = fp[^1];
            }

            int outlineCount = pj.Paths.Count - startOutline;
            if (outlineCount <= 0) return;

            var outlines = pj.Paths.GetRange(startOutline, outlineCount)
                                   .Where(p => p.Count >= 2)
                                   .ToList();
            if (outlines.Count == 0) return;

            // Tính độ sâu lồng nhau (Inside-Out) cho các đường cắt viền trong cùng PathJob
            int n = outlines.Count;
            var isClosed = new bool[n];
            var pBounds = new RectangleF[n];
            for (int i = 0; i < n; i++)
            {
                var p = outlines[i];
                isClosed[i] = p.Count >= 3 && DistSq(p[0], p[^1]) <= 0.25f;
                pBounds[i] = GetPathBounds(p);
            }

            var depth = new int[n];
            if (allowInsideOut && n > 1)
            {
                for (int i = 0; i < n; i++)
                {
                    PointF testPt = outlines[i][0];
                    int d = 0;
                    for (int j = 0; j < n; j++)
                    {
                        if (i == j || !isClosed[j]) continue;
                        if (!pBounds[j].Contains(pBounds[i])) continue;
                        if (IsPointInPolygon(testPt, outlines[j]))
                            d++;
                    }
                    depth[i] = d;
                }
            }

            var finalOutlines = new List<List<PointF>>(n);
            int maxDepth = depth.Max();

            for (int d = maxDepth; d >= 0; d--)
            {
                var indices = new List<int>();
                for (int i = 0; i < n; i++)
                    if (depth[i] == d) indices.Add(i);
                if (indices.Count == 0) continue;

                if (!_cfg.OptimizeToolpath)
                {
                    foreach (int idx in indices)
                    {
                        finalOutlines.Add(outlines[idx]);
                        head = outlines[idx][^1];
                    }
                    continue;
                }

                // Greedy Nearest-Neighbor kết hợp chọn đỉnh bắt đầu gần nhất (cho đường kín) hoặc đảo chiều (cho đường hở)
                var levelPaths = new List<(List<PointF> Path, bool Closed)>(indices.Count);
                var used = new bool[indices.Count];

                for (int step = 0; step < indices.Count; step++)
                {
                    int bestLocal = -1;
                    float bestDistSq = float.MaxValue;
                    bool bestReverse = false;
                    int bestStartVertex = 0;

                    for (int k = 0; k < indices.Count; k++)
                    {
                        if (used[k]) continue;
                        int origIdx = indices[k];
                        var path = outlines[origIdx];
                        bool closed = isClosed[origIdx];

                        if (closed)
                        {
                            // Có thể bắt đầu cắt tại bất kỳ đỉnh nào trên đường khép kín gần đầu laser nhất
                            int vCount = path.Count - 1;
                            for (int v = 0; v < vCount; v++)
                            {
                                float dSq = DistSq(head, path[v]);
                                if (dSq < bestDistSq)
                                {
                                    bestDistSq = dSq;
                                    bestLocal = k;
                                    bestStartVertex = v;
                                    bestReverse = false;
                                }
                            }
                        }
                        else
                        {
                            // Đường hở: kiểm tra cả điểm đầu lẫn điểm cuối (cho phép đảo chiều cắt)
                            float dStart = DistSq(head, path[0]);
                            if (dStart < bestDistSq)
                            {
                                bestDistSq = dStart;
                                bestLocal = k;
                                bestReverse = false;
                            }
                            float dEnd = DistSq(head, path[^1]);
                            if (dEnd < bestDistSq)
                            {
                                bestDistSq = dEnd;
                                bestLocal = k;
                                bestReverse = true;
                            }
                        }
                    }

                    if (bestLocal < 0) break;
                    used[bestLocal] = true;
                    int chosenOrig = indices[bestLocal];
                    var chosenPath = outlines[chosenOrig];
                    bool chosenClosed = isClosed[chosenOrig];

                    if (chosenClosed && bestStartVertex > 0)
                    {
                        chosenPath = RotateClosedLoop(chosenPath, bestStartVertex);
                    }
                    else if (!chosenClosed && bestReverse)
                    {
                        chosenPath = new List<PointF>(chosenPath);
                        chosenPath.Reverse();
                    }

                    levelPaths.Add((chosenPath, chosenClosed));
                    head = chosenPath[^1];
                }

                // Tối ưu 2-Opt cục bộ để gỡ các đoạn nhảy chéo G0
                if (levelPaths.Count >= 4 && levelPaths.Count <= 250)
                {
                    TwoOptRefine(levelPaths);
                    if (levelPaths.Count > 0)
                        head = levelPaths[^1].Path[^1];
                }

                foreach (var lp in levelPaths)
                    finalOutlines.Add(lp.Path);
            }

            // Gán lại danh sách đường đã tối ưu vào PathJob
            var newPaths = new List<List<PointF>>(startOutline + finalOutlines.Count);
            for (int i = 0; i < startOutline; i++) newPaths.Add(pj.Paths[i]);
            newPaths.AddRange(finalOutlines);
            pj.Paths = newPaths;
        }

        private static void TwoOptRefine(List<(List<PointF> Path, bool Closed)> tour)
        {
            int n = tour.Count;
            bool improved = true;
            int passes = 0;

            while (improved && passes < 4)
            {
                improved = false;
                passes++;
                for (int i = 0; i < n - 2; i++)
                {
                    PointF aEnd = tour[i].Path[^1];
                    PointF bStart = tour[i + 1].Path[0];
                    float cur1 = DistSq(aEnd, bStart);

                    for (int j = i + 2; j < n - 1; j++)
                    {
                        PointF cEnd = tour[j].Path[^1];
                        PointF dStart = tour[j + 1].Path[0];
                        float curCost = cur1 + DistSq(cEnd, dStart);

                        // Nếu đảo đoạn từ (i+1 .. j), điểm vào mới là điểm cuối của tour[j] và điểm ra là điểm đầu của tour[i+1]
                        PointF newB = tour[j].Path[^1];
                        PointF newC = tour[i + 1].Path[0];
                        float newCost = DistSq(aEnd, newB) + DistSq(newC, dStart);

                        if (newCost + 1e-3f < curCost)
                        {
                            // Đảo ngược thứ tự từ i+1 đến j và đảo chiều từng path bên trong
                            tour.Reverse(i + 1, j - i);
                            for (int k = i + 1; k <= j; k++)
                            {
                                var rev = new List<PointF>(tour[k].Path);
                                rev.Reverse();
                                tour[k] = (rev, tour[k].Closed);
                            }
                            improved = true;
                            aEnd = tour[i].Path[^1];
                            bStart = tour[i + 1].Path[0];
                            cur1 = DistSq(aEnd, bStart);
                        }
                    }
                }
            }
        }

        private static List<PointF> RotateClosedLoop(List<PointF> loop, int startIdx)
        {
            int vCount = loop.Count - 1; // phần tử cuối trùng phần tử đầu
            if (vCount < 3 || startIdx <= 0 || startIdx >= vCount) return loop;

            var rotated = new List<PointF>(loop.Count);
            for (int i = 0; i < vCount; i++)
            {
                rotated.Add(loop[(startIdx + i) % vCount]);
            }
            rotated.Add(rotated[0]);
            return rotated;
        }

        private static float MinDistToJob(PointF pt, PathJob pj)
        {
            int start = Math.Clamp(pj.FillCount, 0, pj.Paths.Count);
            float best = float.MaxValue;
            for (int i = start; i < pj.Paths.Count; i++)
            {
                var p = pj.Paths[i];
                if (p.Count == 0) continue;
                float d0 = DistSq(pt, p[0]);
                if (d0 < best) best = d0;
                float d1 = DistSq(pt, p[^1]);
                if (d1 < best) best = d1;
            }
            if (best == float.MaxValue && pj.Paths.Count > 0 && pj.Paths[0].Count > 0)
                best = DistSq(pt, pj.Paths[0][0]);
            return best;
        }

        private static RectangleF GetJobBounds(PathJob pj)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool any = false;
            foreach (var path in pj.Paths)
            {
                foreach (var p in path)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                    any = true;
                }
            }
            return any ? RectangleF.FromLTRB(minX, minY, maxX, maxY) : RectangleF.Empty;
        }

        private static RectangleF GetPathBounds(List<PointF> path)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in path)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            return RectangleF.FromLTRB(minX, minY, maxX, maxY);
        }

        private static List<PointF>? GetLargestClosedLoop(PathJob pj)
        {
            List<PointF>? best = null;
            double bestArea = 0;
            int start = Math.Clamp(pj.FillCount, 0, pj.Paths.Count);
            for (int i = start; i < pj.Paths.Count; i++)
            {
                var p = pj.Paths[i];
                if (p.Count < 4 || DistSq(p[0], p[^1]) > 0.25f) continue;
                var b = GetPathBounds(p);
                double area = (double)b.Width * b.Height;
                if (area > bestArea) { bestArea = area; best = p; }
            }
            return best;
        }

        private static bool IsPointInPolygon(PointF pt, List<PointF> poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                PointF pi = poly[i], pj = poly[j];
                if (((pi.Y > pt.Y) != (pj.Y > pt.Y)) &&
                    (pt.X < (pj.X - pi.X) * (pt.Y - pi.Y) / (pj.Y - pi.Y + 1e-12f) + pi.X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static float DistSq(PointF a, PointF b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        // =================================================================
        //  Vectoritem -> Job
        // =================================================================
        Job? BuildJob(Vectoritem it, bool withFill = true)
        {
            int power = Math.Clamp(it.LaserPower, 0, 100);
            int s = (int)Math.Round(_cfg.MaxS * power / 100.0);
            if (s <= 0) return null;                       // công suất 0% -> bỏ qua

            Job? job = it switch
            {
                VectorRectangle r => RectJob(r),
                VectorEllipse e => EllipseJob(e),
                VectorLine l => LineJob(l),
                VectorPolygon pg => PolygonJob(pg),
                VectorPathItem pi => PathItemJob(pi),
                VectorText t => TextJob(t),
                VectorImage im => RasterJobFrom(im, s),
                _ => null
            };
            if (job == null) return null;
            if ((it.Rotation != 0f || it.FlipX || it.FlipY) && job is PathJob pj && it is not VectorLine) RotatePaths(pj, it);   // hình đã xoay / lật
            // Tô kín: đường quét song song lấp đầy hình (tính SAU khi xoay nên góc quét là góc tuyệt đối trên bản vẽ; bỏ qua với font 1 nét)
            if (withFill && it.FillMode != 0 && it is not VectorLine && it is not VectorText { SingleLineFont: true } && job is PathJob fj) ApplyFill(fj, it);

            job.Power = s;
            job.Feed = Math.Max(1, it.LaserSpeed);
            job.Passes = Math.Max(1, it.Passes);
            return job;
        }

        // Xoay mọi điểm của đường cắt quanh tâm hình (cùng chiều với Form1: thuận chiều kim đồng hồ trên màn hình)
        static void RotatePaths(PathJob job, Vectoritem it)
        {
            var b = Box(it);
            float cx = b.Left + b.Width / 2f, cy = b.Top + b.Height / 2f;
            double a = it.Rotation * Math.PI / 180.0, cos = Math.Cos(a), sin = Math.Sin(a);
            foreach (var path in job.Paths)
                for (int i = 0; i < path.Count; i++)
                {
                    double dx = path[i].X - cx, dy = path[i].Y - cy;
                    if (it.FlipX) dx = -dx;                  // lật trước, xoay sau (giống cách Form1 vẽ)
                    if (it.FlipY) dy = -dy;
                    path[i] = new PointF((float)(cx + dx * cos - dy * sin), (float)(cy + dx * sin + dy * cos));
                }
        }

        // Khung bao của hình chữ nhật b sau khi xoay deg độ quanh tâm
        static RectangleF RotatedBounds(RectangleF b, float deg)
        {
            if (deg == 0f) return b;
            float cx = b.Left + b.Width / 2f, cy = b.Top + b.Height / 2f;
            double a = deg * Math.PI / 180.0, cos = Math.Cos(a), sin = Math.Sin(a);
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var (px, py) in new[] { (b.Left, b.Top), (b.Right, b.Top), (b.Right, b.Bottom), (b.Left, b.Bottom) })
            {
                double dx = px - cx, dy = py - cy;
                float x = (float)(cx + dx * cos - dy * sin), y = (float)(cy + dx * sin + dy * cos);
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            return RectangleF.FromLTRB(minX, minY, maxX, maxY);
        }

        static RectangleF Box(Vectoritem v)
        {
            return new RectangleF(Math.Min(v.X, v.X + v.Width), Math.Min(v.Y, v.Y + v.Height),
                                  Math.Abs(v.Width), Math.Abs(v.Height));
        }

        PathJob RectJob(VectorRectangle r)
        {
            var b = Box(r);
            var job = new PathJob { Name = "Rectangle" };
            job.Paths.Add(new List<PointF>
            {
                new(b.Left, b.Top), new(b.Right, b.Top), new(b.Right, b.Bottom),
                new(b.Left, b.Bottom), new(b.Left, b.Top)
            });
            return job;
        }

        PathJob EllipseJob(VectorEllipse e)
        {
            var b = Box(e);
            float rx = b.Width / 2f, ry = b.Height / 2f;
            float cx = b.Left + rx, cy = b.Top + ry;

            // Số đoạn thẳng đủ để sai số hình học <= CurveToleranceMm
            double rMm = Math.Max(rx, ry) * _cfg.MmPerPixel;
            double tol = Math.Max(0.001, _cfg.CurveToleranceMm);
            int n = rMm <= tol ? 16 : (int)Math.Ceiling(Math.PI / Math.Acos(1 - tol / rMm));
            n = Math.Clamp(n, 16, 1440);

            var pts = new List<PointF>(n + 1);
            for (int i = 0; i <= n; i++)
            {
                double a = 2 * Math.PI * i / n;
                pts.Add(new PointF(cx + (float)(rx * Math.Cos(a)), cy + (float)(ry * Math.Sin(a))));
            }
            var job = new PathJob { Name = "Ellipse" };
            job.Paths.Add(pts);
            return job;
        }
        PathJob PolygonJob(VectorPolygon p)
        {
            var pts = new List<PointF>(p.GetPoints());
            pts.Add(pts[0]);                          // khép kín đường viền
            var job = new PathJob { Name = p.Name };
            job.Paths.Add(pts);
            return job;
        }

        PathJob PathItemJob(VectorPathItem pi)
        {
            var job = new PathJob { Name = pi.Name };
            foreach (var sp in pi.SubPaths)
            {
                if (sp.Points.Count < 2) continue;
                var list = new List<PointF>(sp.Points);
                if (sp.IsClosed && list.Count >= 3)
                {
                    list.Add(list[0]); // khép kín đường cắt
                }
                job.Paths.Add(list);
            }
            return job;
        }

        PathJob LineJob(VectorLine l)
        {
            var job = new PathJob { Name = "Line" };
            job.Paths.Add(new List<PointF> { new(l.X, l.Y), new(l.X2, l.Y2) });
            return job;
        }

        PathJob? TextJob(VectorText t)
        {
            if (string.IsNullOrEmpty(t.TextContent)) return null;

            float flat = (float)(Math.Max(0.001, _cfg.CurveToleranceMm) / _cfg.MmPerPixel);

            if (t.HasCustomPath)
            {
                var subPaths = HersheyFont.BuildTextSubPaths(t, _cfg.MmPerPixel, flat);
                if (subPaths.Count == 0) return null;
                var customJob = new PathJob { Name = "Text '" + Ascii(t.TextContent) + "'" };
                foreach (var sp in subPaths)
                {
                    if (sp.Points.Count < 2) continue;
                    var list = new List<PointF>(sp.Points);
                    if (sp.IsClosed && list.Count >= 3 && list[0] != list[^1])
                        list.Add(list[0]);
                    customJob.Paths.Add(list);
                }
                return customJob.Paths.Count == 0 ? null : customJob;
            }

            FontFamily fam;
            try { fam = new FontFamily(t.FontName); }
            catch { fam = new FontFamily("Arial"); }

            using (fam)
            using (var gp = new GraphicsPath())
            {
                FontStyle want = (t.FontBold ? FontStyle.Bold : 0) | (t.FontItalic ? FontStyle.Italic : 0);
                FontStyle style = fam.IsStyleAvailable(want) ? want
                                : fam.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular
                                : fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold
                                : FontStyle.Italic;

                // Vectoritem.Draw dùng new Font(name, size) -> size tính bằng POINT ở 96 DPI
                float emPx = t.FontSize * 96f / 72f;
                gp.AddString(t.TextContent, fam, (int)style, emPx,
                             new PointF(t.X, t.Y), new StringFormat(StringFormat.GenericDefault));

                using (var m = new Matrix()) gp.Flatten(m, flat);

                var pts = gp.PathPoints;
                var job = new PathJob { Name = "Text '" + Ascii(t.TextContent) + "'" };

                using (var iter = new GraphicsPathIterator(gp))
                {
                    while (iter.NextSubpath(out int start, out int end, out bool closed) > 0)
                    {
                        var list = new List<PointF>();
                        for (int i = start; i <= end; i++) list.Add(pts[i]);
                        if (closed && list.Count > 0) list.Add(list[0]);
                        job.Paths.Add(list);
                    }
                }
                return job.Paths.Count == 0 ? null : job;
            }
        }

        // =================================================================
        //  Tô kín (Fill / Hatch)
        // =================================================================
        // Thay đường viền bằng các đường quét song song (quy tắc chẵn-lẻ nên lỗ trong chữ O, B... vẫn để trống)
        void ApplyFill(PathJob job, Vectoritem it)
        {
            var closed = job.Paths.Where(p => p.Count >= 3).ToList();
            if (closed.Count == 0) return;

            float stepPx = (float)(Math.Max(0.02, it.FillIntervalMm) / _cfg.MmPerPixel);
            var fill = new List<List<PointF>>();
            HatchPass(closed, it.FillAngle, stepPx, it.FillBidir, fill);
            if (it.FillCross) HatchPass(closed, it.FillAngle + 90f, stepPx, it.FillBidir, fill);
            if (fill.Count == 0) return;                          // hình nhỏ hơn khoảng cách tô: giữ nguyên đường viền
            job.FillCount = fill.Count;
            if (it.FillMode == 2) fill.AddRange(job.Paths);       // tô trước, viền sau
            job.Paths = fill;
        }

        static void HatchPass(List<List<PointF>> polys, float angleDeg, float stepPx, bool bidir, List<List<PointF>> output)
        {
            double a = angleDeg * Math.PI / 180.0, ca = Math.Cos(a), sa = Math.Sin(a);

            // xoay hình một góc -a để các đường quét trở thành đường NGANG
            var rot = new List<(double X, double Y)[]>();
            double yMin = double.MaxValue, yMax = double.MinValue;
            foreach (var p in polys)
            {
                var arr = new (double X, double Y)[p.Count];
                for (int i = 0; i < p.Count; i++)
                {
                    double x = p[i].X * ca + p[i].Y * sa;
                    double y = -p[i].X * sa + p[i].Y * ca;
                    arr[i] = (x, y);
                    if (y < yMin) yMin = y;
                    if (y > yMax) yMax = y;
                }
                rot.Add(arr);
            }
            if (yMax <= yMin) return;

            double step = Math.Max(1e-4, stepPx);
            long rows = (long)Math.Ceiling((yMax - yMin) / step);
            if (rows > 400000)
                throw new InvalidOperationException("Khoảng cách đường tô quá nhỏ so với kích thước hình (hơn 400.000 đường). Hãy tăng khoảng cách tô.");

            var xs = new List<double>();
            var segs = new List<(double A, double B)>();
            int rowNo = 0;
            for (long r = 0; r < rows; r++)
            {
                double y = yMin + (r + 0.5) * step;
                xs.Clear();
                foreach (var arr in rot)
                {
                    int n = arr.Length;
                    for (int i = 0; i < n; i++)
                    {
                        var p0 = arr[i];
                        var p1 = arr[(i + 1) % n];
                        if ((p0.Y <= y && p1.Y > y) || (p1.Y <= y && p0.Y > y))
                            xs.Add(p0.X + (y - p0.Y) * (p1.X - p0.X) / (p1.Y - p0.Y));
                    }
                }
                if (xs.Count < 2) continue;
                xs.Sort();

                segs.Clear();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                    if (xs[k + 1] - xs[k] > 1e-6) segs.Add((xs[k], xs[k + 1]));
                if (segs.Count == 0) continue;

                bool rev = bidir && (rowNo & 1) == 1;               // hàng lẻ chạy ngược lại (zigzag)
                if (rev) for (int k = segs.Count - 1; k >= 0; k--) output.Add(HSeg(segs[k].B, segs[k].A, y, ca, sa));
                else foreach (var s in segs) output.Add(HSeg(s.A, s.B, y, ca, sa));
                rowNo++;
            }
        }

        // đoạn quét (xa -> xb tại tung độ y trong hệ đã xoay) đổi ngược về hệ bản vẽ
        static List<PointF> HSeg(double xa, double xb, double y, double ca, double sa) => new List<PointF>
        {
            new PointF((float)(xa * ca - y * sa), (float)(xa * sa + y * ca)),
            new PointF((float)(xb * ca - y * sa), (float)(xb * sa + y * ca))
        };

        RasterJob? RasterJobFrom(VectorImage vi, int maxPower)
        {
            if (vi.LoadedImage == null) return null;
            var b = Box(vi);
            if (b.Width <= 0 || b.Height <= 0) return null;

            // Ảnh đã xoay / lật: khắc theo khung bao SAU khi xoay (ảnh được vẽ xoay vào lưới điểm ảnh)
            bool xf = vi.Rotation != 0f || vi.FlipX || vi.FlipY;
            RectangleF area = xf ? RotatedBounds(b, vi.Rotation) : b;

            double step = Math.Max(0.02, _cfg.RasterStepMm);
            int cols = Math.Max(1, (int)Math.Round(area.Width * _cfg.MmPerPixel / step));
            int rows = Math.Max(1, (int)Math.Round(area.Height * _cfg.MmPerPixel / step));
            if ((long)cols * rows > 20_000_000)
                throw new InvalidOperationException("Ảnh quá lớn so với bước quét. Hãy tăng RasterStepMm.");

            var pw = new int[cols * rows];
            using (var bmp = new Bitmap(cols, rows, PixelFormat.Format24bppRgb))
            {
                // Chế độ khắc riêng của từng ảnh (0 = theo cài đặt chung)
                bool engBinary = vi.EngraveMode == 0 ? _cfg.ImageBinary : vi.EngraveMode == 2;
                int engThr = vi.EngraveMode == 0 ? _cfg.ImageThreshold : vi.Threshold;
                bool engInv = vi.EngraveMode == 0 ? _cfg.ImageInvert : vi.Invert;

                using (var g = Graphics.FromImage(bmp))
                {
                    // vùng trong suốt = trắng = không khắc. Ảnh xoay + đảo màu: nền ngoài ảnh phải là đen (sau đảo = không khắc)
                    g.Clear(xf && engInv ? Color.Black : Color.White);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    if (!xf) g.DrawImage(vi.LoadedImage, 0, 0, cols, rows);
                    else
                    {
                        float cx = b.Left + b.Width / 2f, cy = b.Top + b.Height / 2f;
                        g.ScaleTransform(cols / area.Width, rows / area.Height);
                        g.TranslateTransform(-area.Left, -area.Top);
                        g.TranslateTransform(cx, cy);
                        if (vi.Rotation != 0f) g.RotateTransform(vi.Rotation);
                        g.ScaleTransform(vi.FlipX ? -1f : 1f, vi.FlipY ? -1f : 1f);
                        g.TranslateTransform(-cx, -cy);
                        g.DrawImage(vi.LoadedImage, b.Left, b.Top, b.Width, b.Height);
                    }
                }

                var data = bmp.LockBits(new Rectangle(0, 0, cols, rows),
                                        ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    byte[] buf = new byte[Math.Abs(data.Stride) * rows];
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, buf.Length);
                    for (int y = 0; y < rows; y++)
                        for (int x = 0; x < cols; x++)
                        {
                            int i = y * data.Stride + x * 3;
                            int gray = (buf[i + 2] * 299 + buf[i + 1] * 587 + buf[i] * 114) / 1000;
                            if (engInv) gray = 255 - gray;
                            int level = engBinary ? (gray < engThr ? 255 : 0)
                                                         : 255 - gray;      // tối = mạnh
                            pw[y * cols + x] = level * maxPower / 255;
                        }
                }
                finally { bmp.UnlockBits(data); }
            }

            return new RasterJob { Name = "Image", Rect = area, Cols = cols, Rows = rows, Pw = pw };
        }

        // =================================================================
        //  Tiện ích
        // =================================================================
        static string F(double v)
        {
            if (Math.Abs(v) < 0.0005) v = 0;               // tránh "-0"
            return v.ToString("0.###", Inv);
        }

        static double Dist(double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1, dy = y2 - y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // Comment G-code nên là ASCII (nhiều bộ gửi/firmware lỗi với tiếng Việt có dấu)
        static string Ascii(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string norm = s.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in norm)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(c < 32 || c > 126 ? ' ' : c);
            }
            return sb.ToString();
        }
    }
}

// =====================================================================
// PHẦN 2: Khung đèn đỏ / khung bao   (trước đây là file GCodeFrame.cs)
// =====================================================================
// =====================================================================
//  GCodeFrame.cs  -  Phần mở rộng của GCodeCompiler cho KHỔ IN / ĐÈN ĐỎ
//
//  - GetBoundsPx   : khung bao (px canvas) của phần SẼ ĐƯỢC KHẮC, cùng quy tắc với Compile
//                    (bỏ qua hình công suất 0%, chữ lấy theo đường viền, hình xoay/lật tính đúng)
//  - CompileFrame  : G-code chạy viền khung bao (đèn đỏ định vị): chạy bằng laser công suất rất thấp,
//                    hoặc bằng lệnh bật/tắt đèn đỏ riêng của bo mạch. Dùng đúng gốc toạ độ như Compile.
//  Cần GCodeCompiler.cs và GCodeSettings đã đổi thành "partial class".
// =====================================================================
namespace test01_1014
{
    using System.Drawing;
    using System.Text;

    public partial class GCodeSettings
    {
        /// <summary>Gốc toạ độ X (px canvas) khi xuất theo vị trí thật trên vùng khắc (OriginAtJobCorner = false).</summary>
        public float AbsOriginXpx { get; set; } = 0f;

        /// <summary>Gốc toạ độ Y (px canvas). NaN = dùng CanvasHeightPx (gốc ở góc DƯỚI-TRÁI như cũ).</summary>
        public float AbsOriginYpx { get; set; } = float.NaN;
    }

    public class FrameOptions
    {
        public float MarginMm { get; set; } = 0f;          // chừa lề quanh sản phẩm
        public float PowerPercent { get; set; } = 1f;      // % công suất đèn đỏ (0 = chỉ di chuyển, không bắn)
        public int Feed { get; set; } = 3000;              // mm/phút
        public int Loops { get; set; } = 1;                // số vòng chạy khung
        public bool Cross { get; set; } = false;           // thêm chữ thập ở tâm khung
        public string OnCmd { get; set; } = "";            // lệnh bật đèn đỏ riêng (rỗng = dùng laser công suất thấp)
        public string OffCmd { get; set; } = "";           // lệnh tắt đèn đỏ riêng
    }

    public partial class GCodeCompiler
    {
        // Khung bao (px) của phần sẽ khắc; null nếu không có gì để khắc
        public RectangleF? GetBoundsPx(IEnumerable<Vectoritem> items)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            bool any = false;
            void Extend(float x, float y)
            {
                any = true;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }

            foreach (var it in items)
            {
                int power = Math.Clamp(it.LaserPower, 0, 100);
                if ((int)Math.Round(_cfg.MaxS * power / 100.0) <= 0) continue;     // 0% = không khắc

                if (it is VectorImage vi)           // ảnh / mã vạch: không dựng ảnh raster chỉ để lấy khung
                {
                    var b = Box(vi);
                    if (b.Width <= 0 || b.Height <= 0) continue;
                    var r = RotatedBounds(b, vi.Rotation);
                    Extend(r.Left, r.Top); Extend(r.Right, r.Bottom);
                    continue;
                }

                if (BuildJob(it, false) is PathJob pj)      // false = không dựng đường tô (chỉ cần khung bao)
                    foreach (var path in pj.Paths)
                        foreach (var p in path) Extend(p.X, p.Y);
            }
            return any ? RectangleF.FromLTRB(minX, minY, maxX, maxY) : null;
        }

        // Đường laser của 1 đối tượng ở chế độ tô kín, tách riêng đường TÔ và đường VIỀN (toạ độ px canvas).
        // null nếu đối tượng không tô được / công suất 0% / lỗi. Dùng để xem trước trên canvas.
        public (List<List<PointF>> Fill, List<List<PointF>> Outline)? GetFillParts(Vectoritem it)
        {
            if (it.FillMode == 0 || it is VectorLine || it is VectorImage) return null;
            try
            {
                if (BuildJob(it, true) is not PathJob pj) return null;
                var fill = new List<List<PointF>>();
                var outline = new List<List<PointF>>();
                if (pj.FillCount > 0)
                    for (int i = 0; i < pj.Paths.Count; i++) (i < pj.FillCount ? fill : outline).Add(pj.Paths[i]);
                else
                    outline.AddRange(pj.Paths);          // hình nhỏ hơn khoảng cách tô: chỉ còn đường viền
                return (fill, outline);
            }
            catch { return null; }
        }

        // G-code chạy viền khung bao của sản phẩm (định vị bằng đèn đỏ)
        public GCodeResult CompileFrame(IEnumerable<Vectoritem> items, FrameOptions o)
        {
            RectangleF bb = GetBoundsPx(items)
                ?? throw new InvalidOperationException("Không có đối tượng nào (công suất > 0%) để lấy khung đèn đỏ.");

            float k = _cfg.MmPerPixel;
            // Gốc toạ độ: GIỐNG HỆT Compile (chế độ góc bản vẽ dùng khung bao CHƯA cộng lề)
            float originX = _cfg.OriginAtJobCorner ? bb.Left : _cfg.AbsOriginXpx;
            float originY = _cfg.OriginAtJobCorner ? bb.Bottom
                          : (float.IsNaN(_cfg.AbsOriginYpx) ? _cfg.CanvasHeightPx : _cfg.AbsOriginYpx);
            double MX(float px) => (px - originX) * k;
            double MY(float py) => (originY - py) * k;

            float m = Math.Max(0f, o.MarginMm) / k;
            double x0 = MX(bb.Left - m), x1 = MX(bb.Right + m);
            double yLow = MY(bb.Bottom + m), yHigh = MY(bb.Top - m);     // Y máy hướng lên
            double wMm = x1 - x0, hMm = yHigh - yLow;

            int feed = Math.Max(1, o.Feed);
            int loops = Math.Clamp(o.Loops, 1, 100);
            bool custom = !string.IsNullOrWhiteSpace(o.OnCmd);
            int s = (int)Math.Round(_cfg.MaxS * Math.Clamp(o.PowerPercent, 0f, 100f) / 100.0);

            var sb = new StringBuilder();
            int lines = 0;
            void Line(string t) { sb.AppendLine(t); lines++; }
            string Pt(double x, double y) => $"X{F(x)} Y{F(y)}";

            Line($"; RED LIGHT FRAME {F(wMm)} x {F(hMm)} mm, {loops} loop(s)");
            Line(custom ? "; mode: custom red-light commands"
                        : s > 0 ? $"; mode: low-power laser S{s} ({F(o.PowerPercent)}%) - CHECK it does not mark the material"
                                : "; mode: dry run, laser OFF (S0), head movement only");
            Line("G21 G90");
            if (!custom) Line($"{_cfg.LaserOnCommand} S0");
            Line($"F{feed}");

            double cutLen = 0, rapidLen = 0;
            double cx = 0, cy = 0;
            void Rapid(double x, double y) { rapidLen += Dist(cx, cy, x, y); cx = x; cy = y; Line($"G0 {Pt(x, y)}"); }
            void Draw(double x, double y, bool first)
            {
                cutLen += Dist(cx, cy, x, y); cx = x; cy = y;
                Line($"G1 {Pt(x, y)}{(first && !custom && s > 0 ? " S" + s : "")}");
            }
            void On() { if (custom) Line(o.OnCmd.Trim()); }
            void Off() { if (custom) Line(o.OffCmd.Trim()); else Line("S0"); }

            for (int i = 0; i < loops; i++)
            {
                if (loops > 1) Line($"; loop {i + 1}/{loops}");
                Rapid(x0, yLow); On();
                Draw(x1, yLow, true); Draw(x1, yHigh, false); Draw(x0, yHigh, false); Draw(x0, yLow, false);
                Off();
            }

            if (o.Cross)
            {
                double mx = (x0 + x1) / 2, my = (yLow + yHigh) / 2;
                double arm = Math.Min(5.0, Math.Min(wMm, hMm) / 4);
                Line("; center cross");
                Rapid(mx - arm, my); On(); Draw(mx + arm, my, true); Off();
                Rapid(mx, my - arm); On(); Draw(mx, my + arm, true); Off();
            }

            if (!custom) Line("M5");
            if (_cfg.ReturnToOrigin) Rapid(0, 0);

            return new GCodeResult
            {
                Code = sb.ToString(),
                LineCount = lines,
                WidthMm = wMm,
                HeightMm = hMm,
                CutLengthMm = cutLen,
                RapidLengthMm = rapidLen,
                EstimatedMinutes = cutLen / feed + rapidLen / Math.Max(1, _cfg.RapidFeedForEstimate)
            };
        }
    }
}
