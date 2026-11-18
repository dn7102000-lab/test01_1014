using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;

namespace test01_1014
{
    /// <summary>
    /// Bộ phân tích cú pháp và nhập file AutoCAD DXF (R12 / 2000 / ASCII) chuyên dụng cho máy cắt laser.
    /// Hỗ trợ các thực thể trong phần ENTITIES:
    /// - LINE: Đoạn thẳng
    /// - LWPOLYLINE: Polyline nhẹ 2D (bao gồm toạ độ và hệ số độ cong cung tròn Bulge)
    /// - POLYLINE / VERTEX / SEQEND: Polyline cổ điển 2D
    /// - CIRCLE: Đường tròn
    /// - ARC: Cung tròn (tâm, bán kính, góc bắt đầu, góc kết thúc)
    /// - SPLINE: Đường cong Spline nội suy
    /// </summary>
    public static class DxfImporter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public class DxfImportResult
        {
            public List<VectorPathItem> Items { get; } = new List<VectorPathItem>();
        }

        public static DxfImportResult Import(string filePath, float mmPerPx = 0.25f)
        {
            using var reader = new StreamReader(filePath);
            return Import(reader, mmPerPx);
        }

        public static DxfImportResult Import(TextReader reader, float mmPerPx = 0.25f)
        {
            var res = new DxfImportResult();
            var pairs = ReadGroupPairs(reader);

            int idx = 0;
            int count = pairs.Count;

            // Tìm phần ENTITIES
            while (idx < count)
            {
                var p = pairs[idx];
                if (p.Code == 0 && p.Value.Equals("SECTION", StringComparison.OrdinalIgnoreCase))
                {
                    if (idx + 1 < count && pairs[idx + 1].Code == 2 && pairs[idx + 1].Value.Equals("ENTITIES", StringComparison.OrdinalIgnoreCase))
                    {
                        idx += 2;
                        break;
                    }
                }
                idx++;
            }

            var subPaths = new List<VectorPathItem.SubPath>();

            while (idx < count)
            {
                var p = pairs[idx];
                if (p.Code == 0)
                {
                    string entityType = p.Value.ToUpperInvariant();
                    if (entityType == "ENDSEC" || entityType == "EOF") break;

                    idx++;
                    switch (entityType)
                    {
                        case "LINE":
                            ParseLine(pairs, ref idx, subPaths, mmPerPx);
                            break;
                        case "LWPOLYLINE":
                            ParseLwPolyline(pairs, ref idx, subPaths, mmPerPx);
                            break;
                        case "POLYLINE":
                            ParsePolyline(pairs, ref idx, subPaths, mmPerPx);
                            break;
                        case "CIRCLE":
                            ParseCircle(pairs, ref idx, subPaths, mmPerPx);
                            break;
                        case "ARC":
                            ParseArc(pairs, ref idx, subPaths, mmPerPx);
                            break;
                        case "SPLINE":
                            ParseSpline(pairs, ref idx, subPaths, mmPerPx);
                            break;
                        default:
                            // Bỏ qua các thực thể không hỗ trợ (TEXT, MTEXT, POINT, INSERT...)
                            break;
                    }
                }
                else
                {
                    idx++;
                }
            }

            if (subPaths.Count > 0)
            {
                // Quy đổi trục Y của DXF (DXF Y hướng lên, còn Canvas Y hướng xuống)
                // Ta tìm bounding box và lật trục Y cho đúng chiều vẽ CAD
                FlipYCoordinate(subPaths);

                var pathItem = new VectorPathItem(subPaths, "Bản vẽ DXF");
                res.Items.Add(pathItem);
            }

            return res;
        }

        private static void FlipYCoordinate(List<VectorPathItem.SubPath> subPaths)
        {
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var sp in subPaths)
            {
                foreach (var pt in sp.Points)
                {
                    if (pt.Y < minY) minY = pt.Y;
                    if (pt.Y > maxY) maxY = pt.Y;
                }
            }

            if (minY <= maxY)
            {
                float sumY = minY + maxY;
                foreach (var sp in subPaths)
                {
                    for (int i = 0; i < sp.Points.Count; i++)
                    {
                        var pt = sp.Points[i];
                        sp.Points[i] = new PointF(pt.X, sumY - pt.Y);
                    }
                }
            }
        }

        #region Entity Parsers
        private static void ParseLine(List<DxfPair> pairs, ref int idx, List<VectorPathItem.SubPath> subPaths, float mmPerPx)
        {
            float x1 = 0, y1 = 0, x2 = 0, y2 = 0;
            while (idx < pairs.Count && pairs[idx].Code != 0)
            {
                var p = pairs[idx++];
                switch (p.Code)
                {
                    case 10: x1 = ParseFloat(p.Value) / mmPerPx; break;
                    case 20: y1 = ParseFloat(p.Value) / mmPerPx; break;
                    case 11: x2 = ParseFloat(p.Value) / mmPerPx; break;
                    case 21: y2 = ParseFloat(p.Value) / mmPerPx; break;
                }
            }

            subPaths.Add(new VectorPathItem.SubPath
            {
                Points = new List<PointF> { new PointF(x1, y1), new PointF(x2, y2) },
                IsClosed = false
            });
        }

        private class LwVertex
        {
            public float X, Y;
            public double Bulge;
        }

        private static void ParseLwPolyline(List<DxfPair> pairs, ref int idx, List<VectorPathItem.SubPath> subPaths, float mmPerPx)
        {
            bool isClosed = false;
            var vertices = new List<LwVertex>();
            LwVertex? curVert = null;

            while (idx < pairs.Count && pairs[idx].Code != 0)
            {
                var p = pairs[idx++];
                switch (p.Code)
                {
                    case 70: // Flags (bit 1 = closed)
                        int flags = ParseInt(p.Value);
                        isClosed = (flags & 1) == 1;
                        break;
                    case 10: // X coordinate starts a new vertex
                        curVert = new LwVertex { X = ParseFloat(p.Value) / mmPerPx };
                        vertices.Add(curVert);
                        break;
                    case 20: // Y coordinate
                        if (curVert != null) curVert.Y = ParseFloat(p.Value) / mmPerPx;
                        break;
                    case 42: // Bulge factor for arc
                        if (curVert != null) curVert.Bulge = ParseDouble(p.Value);
                        break;
                }
            }

            if (vertices.Count >= 2)
            {
                var pts = ExpandBulgeVertices(vertices, isClosed);
                if (pts.Count >= 2)
                {
                    subPaths.Add(new VectorPathItem.SubPath { Points = pts, IsClosed = isClosed });
                }
            }
        }

        private static void ParsePolyline(List<DxfPair> pairs, ref int idx, List<VectorPathItem.SubPath> subPaths, float mmPerPx)
        {
            bool isClosed = false;
            while (idx < pairs.Count && pairs[idx].Code != 0)
            {
                var p = pairs[idx++];
                if (p.Code == 70)
                {
                    isClosed = (ParseInt(p.Value) & 1) == 1;
                }
            }

            var vertices = new List<LwVertex>();

            while (idx < pairs.Count)
            {
                if (pairs[idx].Code == 0)
                {
                    string ent = pairs[idx].Value.ToUpperInvariant();
                    if (ent == "SEQEND" || ent == "ENDSEC" || ent == "EOF")
                    {
                        idx++;
                        break;
                    }
                    if (ent == "VERTEX")
                    {
                        idx++;
                        var v = new LwVertex();
                        while (idx < pairs.Count && pairs[idx].Code != 0)
                        {
                            var vp = pairs[idx++];
                            switch (vp.Code)
                            {
                                case 10: v.X = ParseFloat(vp.Value) / mmPerPx; break;
                                case 20: v.Y = ParseFloat(vp.Value) / mmPerPx; break;
                                case 42: v.Bulge = ParseDouble(vp.Value); break;
                            }
                        }
                        vertices.Add(v);
                        continue;
                    }
                    break;
                }
                idx++;
            }

            if (vertices.Count >= 2)
            {
                var pts = ExpandBulgeVertices(vertices, isClosed);
                if (pts.Count >= 2)
                {
                    subPaths.Add(new VectorPathItem.SubPath { Points = pts, IsClosed = isClosed });
                }
            }
        }

        private static List<PointF> ExpandBulgeVertices(List<LwVertex> vertices, bool isClosed)
        {
            var pts = new List<PointF>();
            int count = vertices.Count;
            int limit = isClosed ? count : count - 1;

            for (int i = 0; i < limit; i++)
            {
                var v1 = vertices[i];
                var v2 = vertices[(i + 1) % count];

                if (i == 0) pts.Add(new PointF(v1.X, v1.Y));

                if (Math.Abs(v1.Bulge) > 1e-6)
                {
                    // Thêm các điểm cung tròn Bulge giữa v1 và v2
                    AddBulgeArcPoints(new PointF(v1.X, v1.Y), new PointF(v2.X, v2.Y), v1.Bulge, pts);
                }
                else
                {
                    pts.Add(new PointF(v2.X, v2.Y));
                }
            }
            return pts;
        }

        private static void AddBulgeArcPoints(PointF p1, PointF p2, double bulge, List<PointF> pts)
        {
            double theta = 4.0 * Math.Atan(bulge);
            double dx = p2.X - p1.X;
            double dy = p2.Y - p1.Y;
            double chord = Math.Sqrt(dx * dx + dy * dy);
            if (chord < 1e-5) return;

            double radius = chord / (2.0 * Math.Sin(Math.Abs(theta) / 2.0));
            double sagitta = Math.Abs(bulge) * chord / 2.0;

            // Tính trung điểm của dây cung
            double mx = (p1.X + p2.X) / 2.0;
            double my = (p1.Y + p2.Y) / 2.0;

            // Vector pháp tuyến của dây cung
            double nx = -dy / chord;
            double ny = dx / chord;

            // Hướng tâm cung tròn
            double sign = bulge > 0 ? 1.0 : -1.0;
            double distToCenter = radius - sagitta;
            double cx = mx - sign * nx * distToCenter;
            double cy = my - sign * ny * distToCenter;

            double a1 = Math.Atan2(p1.Y - cy, p1.X - cx);
            double a2 = Math.Atan2(p2.Y - cy, p2.X - cx);

            double span = a2 - a1;
            if (bulge > 0 && span < 0) span += 2 * Math.PI;
            else if (bulge < 0 && span > 0) span -= 2 * Math.PI;

            int steps = Math.Max(4, (int)Math.Ceiling(Math.Abs(span) / (Math.PI / 18)));
            for (int s = 1; s <= steps; s++)
            {
                double a = a1 + span * (s / (double)steps);
                pts.Add(new PointF((float)(cx + radius * Math.Cos(a)), (float)(cy + radius * Math.Sin(a))));
            }
        }

        private static void ParseCircle(List<DxfPair> pairs, ref int idx, List<VectorPathItem.SubPath> subPaths, float mmPerPx)
        {
            float cx = 0, cy = 0, r = 0;
            while (idx < pairs.Count && pairs[idx].Code != 0)
            {
                var p = pairs[idx++];
                switch (p.Code)
                {
                    case 10: cx = ParseFloat(p.Value) / mmPerPx; break;
                    case 20: cy = ParseFloat(p.Value) / mmPerPx; break;
                    case 40: r = ParseFloat(p.Value) / mmPerPx; break;
                }
            }

            if (r > 0)
            {
                var pts = new List<PointF>(64);
                for (int i = 0; i < 64; i++)
                {
                    double a = i * Math.PI * 2 / 64;
                    pts.Add(new PointF(cx + (float)(r * Math.Cos(a)), cy + (float)(r * Math.Sin(a))));
                }
                subPaths.Add(new VectorPathItem.SubPath { Points = pts, IsClosed = true });
            }
        }

        private static void ParseArc(List<DxfPair> pairs, ref int idx, List<VectorPathItem.SubPath> subPaths, float mmPerPx)
        {
            float cx = 0, cy = 0, r = 0;
            float startAngle = 0, endAngle = 360;

            while (idx < pairs.Count && pairs[idx].Code != 0)
            {
                var p = pairs[idx++];
                switch (p.Code)
                {
                    case 10: cx = ParseFloat(p.Value) / mmPerPx; break;
                    case 20: cy = ParseFloat(p.Value) / mmPerPx; break;
                    case 40: r = ParseFloat(p.Value) / mmPerPx; break;
                    case 50: startAngle = ParseFloat(p.Value); break;
                    case 51: endAngle = ParseFloat(p.Value); break;
                }
            }

            if (r > 0)
            {
                double a1 = startAngle * Math.PI / 180.0;
                double a2 = endAngle * Math.PI / 180.0;
                double span = a2 - a1;
                while (span <= 0) span += 2 * Math.PI;

                int steps = Math.Max(4, (int)Math.Ceiling(span / (Math.PI / 18)));
                var pts = new List<PointF>(steps + 1);
                for (int i = 0; i <= steps; i++)
                {
                    double a = a1 + span * (i / (double)steps);
                    pts.Add(new PointF(cx + (float)(r * Math.Cos(a)), cy + (float)(r * Math.Sin(a))));
                }
                subPaths.Add(new VectorPathItem.SubPath { Points = pts, IsClosed = false });
            }
        }

        private static void ParseSpline(List<DxfPair> pairs, ref int idx, List<VectorPathItem.SubPath> subPaths, float mmPerPx)
        {
            var pts = new List<PointF>();
            float curX = 0;
            bool isClosed = false;

            while (idx < pairs.Count && pairs[idx].Code != 0)
            {
                var p = pairs[idx++];
                switch (p.Code)
                {
                    case 70:
                        isClosed = (ParseInt(p.Value) & 1) == 1;
                        break;
                    case 10: // Fit / Control point X
                        curX = ParseFloat(p.Value) / mmPerPx;
                        break;
                    case 20: // Y
                        pts.Add(new PointF(curX, ParseFloat(p.Value) / mmPerPx));
                        break;
                }
            }

            if (pts.Count >= 2)
            {
                subPaths.Add(new VectorPathItem.SubPath { Points = pts, IsClosed = isClosed });
            }
        }
        #endregion

        #region Group Code / Pair Reader
        public struct DxfPair
        {
            public int Code;
            public string Value;
            public DxfPair(int c, string v) { Code = c; Value = v; }
        }

        private static List<DxfPair> ReadGroupPairs(TextReader reader)
        {
            var list = new List<DxfPair>();
            string? lineCode;
            while ((lineCode = reader.ReadLine()) != null)
            {
                lineCode = lineCode.Trim();
                if (string.IsNullOrEmpty(lineCode)) continue;

                if (!int.TryParse(lineCode, NumberStyles.Integer, Inv, out int code)) continue;
                string? val = reader.ReadLine();
                if (val == null) break;
                list.Add(new DxfPair(code, val.Trim()));
            }
            return list;
        }

        private static float ParseFloat(string s) =>
            float.TryParse(s, NumberStyles.Float, Inv, out float v) ? v : 0f;

        private static double ParseDouble(string s) =>
            double.TryParse(s, NumberStyles.Float, Inv, out double v) ? v : 0.0;

        private static int ParseInt(string s) =>
            int.TryParse(s, NumberStyles.Integer, Inv, out int v) ? v : 0;
        #endregion
    }
}

