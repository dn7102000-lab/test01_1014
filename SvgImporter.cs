using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace test01_1014
{
    /// <summary>
    /// Bộ phân tích cú pháp và nhập file vector SVG dành cho máy cắt/khắc laser.
    /// Hỗ trợ: <path> (M, L, H, V, C, S, Q, T, A, Z cả hoa lẫn thường),
    /// <polyline>, <polygon>, <line>, <rect>, <circle>, <ellipse>,
    /// nhóm <g> với thuộc tính transform (translate, scale, rotate, matrix).
    /// </summary>
    public static class SvgImporter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public class SvgImportResult
        {
            public List<VectorPathItem> Items { get; } = new List<VectorPathItem>();
            public float Width { get; set; }
            public float Height { get; set; }
        }

        public static SvgImportResult Import(string filePath, float mmPerPx = 0.25f)
        {
            using var stream = File.OpenRead(filePath);
            return Import(stream, mmPerPx);
        }

        public static SvgImportResult Import(Stream stream, float mmPerPx = 0.25f)
        {
            var res = new SvgImportResult();
            var doc = XDocument.Load(stream);
            var root = doc.Root;
            if (root == null) return res;

            // Xử lý viewBox hoặc width/height của SVG
            float svgW = ParseLength(root.Attribute("width")?.Value, mmPerPx, 400);
            float svgH = ParseLength(root.Attribute("height")?.Value, mmPerPx, 400);

            var vbAttr = root.Attribute("viewBox")?.Value;
            if (!string.IsNullOrWhiteSpace(vbAttr))
            {
                var vbParts = SplitNumbers(vbAttr);
                if (vbParts.Count >= 4)
                {
                    if (svgW <= 0) svgW = vbParts[2];
                    if (svgH <= 0) svgH = vbParts[3];
                }
            }

            res.Width = svgW;
            res.Height = svgH;

            var currentMatrix = new Matrix2D(); // Identity
            ParseContainer(root, currentMatrix, res.Items, mmPerPx);

            return res;
        }

        private static void ParseContainer(XElement container, Matrix2D parentTransform, List<VectorPathItem> output, float mmPerPx)
        {
            foreach (var elem in container.Elements())
            {
                string localName = elem.Name.LocalName.ToLowerInvariant();
                var elemTransform = Matrix2D.Multiply(parentTransform, ParseTransform(elem.Attribute("transform")?.Value));

                switch (localName)
                {
                    case "g":
                        ParseContainer(elem, elemTransform, output, mmPerPx);
                        break;
                    case "path":
                        ParsePathElement(elem, elemTransform, output);
                        break;
                    case "line":
                        ParseLineElement(elem, elemTransform, output);
                        break;
                    case "rect":
                        ParseRectElement(elem, elemTransform, output);
                        break;
                    case "circle":
                        ParseCircleElement(elem, elemTransform, output);
                        break;
                    case "ellipse":
                        ParseEllipseElement(elem, elemTransform, output);
                        break;
                    case "polyline":
                        ParsePolylineElement(elem, elemTransform, output, false);
                        break;
                    case "polygon":
                        ParsePolylineElement(elem, elemTransform, output, true);
                        break;
                }
            }
        }

        private static void ParsePathElement(XElement elem, Matrix2D xform, List<VectorPathItem> output)
        {
            string? d = elem.Attribute("d")?.Value;
            if (string.IsNullOrWhiteSpace(d)) return;

            var subPaths = ParsePathData(d);
            if (subPaths.Count > 0)
            {
                // Áp dụng transform cho từng điểm
                foreach (var sp in subPaths)
                {
                    for (int i = 0; i < sp.Points.Count; i++)
                    {
                        sp.Points[i] = xform.Transform(sp.Points[i]);
                    }
                }

                string name = elem.Attribute("id")?.Value ?? "SVG Path";
                var item = new VectorPathItem(subPaths, name);
                output.Add(item);
            }
        }

        private static void ParseLineElement(XElement elem, Matrix2D xform, List<VectorPathItem> output)
        {
            float x1 = ParseFloat(elem.Attribute("x1")?.Value);
            float y1 = ParseFloat(elem.Attribute("y1")?.Value);
            float x2 = ParseFloat(elem.Attribute("x2")?.Value);
            float y2 = ParseFloat(elem.Attribute("y2")?.Value);

            var sp = new VectorPathItem.SubPath
            {
                Points = new List<PointF> { xform.Transform(new PointF(x1, y1)), xform.Transform(new PointF(x2, y2)) },
                IsClosed = false
            };
            output.Add(new VectorPathItem(new List<VectorPathItem.SubPath> { sp }, elem.Attribute("id")?.Value ?? "SVG Line"));
        }

        private static void ParseRectElement(XElement elem, Matrix2D xform, List<VectorPathItem> output)
        {
            float x = ParseFloat(elem.Attribute("x")?.Value);
            float y = ParseFloat(elem.Attribute("y")?.Value);
            float w = ParseFloat(elem.Attribute("width")?.Value);
            float h = ParseFloat(elem.Attribute("height")?.Value);
            if (w <= 0 || h <= 0) return;

            var pts = new List<PointF>
            {
                xform.Transform(new PointF(x, y)),
                xform.Transform(new PointF(x + w, y)),
                xform.Transform(new PointF(x + w, y + h)),
                xform.Transform(new PointF(x, y + h))
            };

            var sp = new VectorPathItem.SubPath { Points = pts, IsClosed = true };
            output.Add(new VectorPathItem(new List<VectorPathItem.SubPath> { sp }, elem.Attribute("id")?.Value ?? "SVG Rect"));
        }

        private static void ParseCircleElement(XElement elem, Matrix2D xform, List<VectorPathItem> output)
        {
            float cx = ParseFloat(elem.Attribute("cx")?.Value);
            float cy = ParseFloat(elem.Attribute("cy")?.Value);
            float r = ParseFloat(elem.Attribute("r")?.Value);
            if (r <= 0) return;

            var pts = ApproximateEllipse(cx, cy, r, r, 64);
            for (int i = 0; i < pts.Count; i++) pts[i] = xform.Transform(pts[i]);

            var sp = new VectorPathItem.SubPath { Points = pts, IsClosed = true };
            output.Add(new VectorPathItem(new List<VectorPathItem.SubPath> { sp }, elem.Attribute("id")?.Value ?? "SVG Circle"));
        }

        private static void ParseEllipseElement(XElement elem, Matrix2D xform, List<VectorPathItem> output)
        {
            float cx = ParseFloat(elem.Attribute("cx")?.Value);
            float cy = ParseFloat(elem.Attribute("cy")?.Value);
            float rx = ParseFloat(elem.Attribute("rx")?.Value);
            float ry = ParseFloat(elem.Attribute("ry")?.Value);
            if (rx <= 0 || ry <= 0) return;

            var pts = ApproximateEllipse(cx, cy, rx, ry, 64);
            for (int i = 0; i < pts.Count; i++) pts[i] = xform.Transform(pts[i]);

            var sp = new VectorPathItem.SubPath { Points = pts, IsClosed = true };
            output.Add(new VectorPathItem(new List<VectorPathItem.SubPath> { sp }, elem.Attribute("id")?.Value ?? "SVG Ellipse"));
        }

        private static void ParsePolylineElement(XElement elem, Matrix2D xform, List<VectorPathItem> output, bool closed)
        {
            string? rawPts = elem.Attribute("points")?.Value;
            if (string.IsNullOrWhiteSpace(rawPts)) return;

            var nums = SplitNumbers(rawPts);
            if (nums.Count < 4) return;

            var pts = new List<PointF>();
            for (int i = 0; i + 1 < nums.Count; i += 2)
            {
                pts.Add(xform.Transform(new PointF(nums[i], nums[i + 1])));
            }

            var sp = new VectorPathItem.SubPath { Points = pts, IsClosed = closed };
            output.Add(new VectorPathItem(new List<VectorPathItem.SubPath> { sp }, elem.Attribute("id")?.Value ?? (closed ? "SVG Polygon" : "SVG Polyline")));
        }

        private static List<PointF> ApproximateEllipse(float cx, float cy, float rx, float ry, int segments)
        {
            var pts = new List<PointF>(segments);
            double step = Math.PI * 2 / segments;
            for (int i = 0; i < segments; i++)
            {
                double angle = i * step;
                pts.Add(new PointF(cx + (float)(rx * Math.Cos(angle)), cy + (float)(ry * Math.Sin(angle))));
            }
            return pts;
        }

        #region SVG Path Parser (D attribute)
        public static List<VectorPathItem.SubPath> ParsePathData(string d)
        {
            var subPaths = new List<VectorPathItem.SubPath>();
            var currentPoints = new List<PointF>();
            bool isCurrentClosed = false;

            float curX = 0, curY = 0;
            float startX = 0, startY = 0;
            float lastCtrlX = 0, lastCtrlY = 0;
            char lastCmd = '\0';

            void FinishSubPath()
            {
                if (currentPoints.Count > 1)
                {
                    subPaths.Add(new VectorPathItem.SubPath
                    {
                        Points = new List<PointF>(currentPoints),
                        IsClosed = isCurrentClosed
                    });
                }
                currentPoints.Clear();
                isCurrentClosed = false;
            }

            // Tách tokens: lệnh (chữ cái) hoặc tham số số
            var tokens = TokenizePathData(d);
            int idx = 0;

            while (idx < tokens.Count)
            {
                string tok = tokens[idx];
                char cmd = tok[0];
                if (char.IsLetter(cmd))
                {
                    idx++;
                }
                else
                {
                    // Lặp lại lệnh trước đó nếu không có chữ cái mới (ngoại trừ M -> L hoặc m -> l)
                    if (lastCmd == 'M') cmd = 'L';
                    else if (lastCmd == 'm') cmd = 'l';
                    else cmd = lastCmd;
                }

                bool rel = char.IsLower(cmd);
                char uCmd = char.ToUpperInvariant(cmd);

                switch (uCmd)
                {
                    case 'M':
                        {
                            if (currentPoints.Count > 0) FinishSubPath();
                            float x = NextFloat(tokens, ref idx);
                            float y = NextFloat(tokens, ref idx);
                            if (rel) { x += curX; y += curY; }
                            curX = startX = x;
                            curY = startY = y;
                            currentPoints.Add(new PointF(curX, curY));
                            lastCtrlX = curX; lastCtrlY = curY;
                            lastCmd = cmd;
                        }
                        break;

                    case 'L':
                        {
                            float x = NextFloat(tokens, ref idx);
                            float y = NextFloat(tokens, ref idx);
                            if (rel) { x += curX; y += curY; }
                            curX = x; curY = y;
                            currentPoints.Add(new PointF(curX, curY));
                            lastCtrlX = curX; lastCtrlY = curY;
                            lastCmd = cmd;
                        }
                        break;

                    case 'H':
                        {
                            float x = NextFloat(tokens, ref idx);
                            if (rel) x += curX;
                            curX = x;
                            currentPoints.Add(new PointF(curX, curY));
                            lastCtrlX = curX; lastCtrlY = curY;
                            lastCmd = cmd;
                        }
                        break;

                    case 'V':
                        {
                            float y = NextFloat(tokens, ref idx);
                            if (rel) y += curY;
                            curY = y;
                            currentPoints.Add(new PointF(curX, curY));
                            lastCtrlX = curX; lastCtrlY = curY;
                            lastCmd = cmd;
                        }
                        break;

                    case 'C': // Cubic Bezier: x1 y1 x2 y2 x y
                        {
                            float x1 = NextFloat(tokens, ref idx);
                            float y1 = NextFloat(tokens, ref idx);
                            float x2 = NextFloat(tokens, ref idx);
                            float y2 = NextFloat(tokens, ref idx);
                            float x = NextFloat(tokens, ref idx);
                            float y = NextFloat(tokens, ref idx);
                            if (rel)
                            {
                                x1 += curX; y1 += curY;
                                x2 += curX; y2 += curY;
                                x += curX; y += curY;
                            }
                            ApproximateCubicBezier(new PointF(curX, curY), new PointF(x1, y1), new PointF(x2, y2), new PointF(x, y), currentPoints);
                            lastCtrlX = x2; lastCtrlY = y2;
                            curX = x; curY = y;
                            lastCmd = cmd;
                        }
                        break;

                    case 'S': // Smooth Cubic Bezier: x2 y2 x y
                        {
                            float x2 = NextFloat(tokens, ref idx);
                            float y2 = NextFloat(tokens, ref idx);
                            float x = NextFloat(tokens, ref idx);
                            float y = NextFloat(tokens, ref idx);
                            if (rel)
                            {
                                x2 += curX; y2 += curY;
                                x += curX; y += curY;
                            }
                            // Điểm điều khiển 1 đối xứng với lastCtrl qua cur
                            float x1 = (lastCmd == 'C' || lastCmd == 'c' || lastCmd == 'S' || lastCmd == 's') ? 2 * curX - lastCtrlX : curX;
                            float y1 = (lastCmd == 'C' || lastCmd == 'c' || lastCmd == 'S' || lastCmd == 's') ? 2 * curY - lastCtrlY : curY;

                            ApproximateCubicBezier(new PointF(curX, curY), new PointF(x1, y1), new PointF(x2, y2), new PointF(x, y), currentPoints);
                            lastCtrlX = x2; lastCtrlY = y2;
                            curX = x; curY = y;
                            lastCmd = cmd;
                        }
                        break;

                    case 'Q': // Quadratic Bezier: x1 y1 x y
                        {
                            float x1 = NextFloat(tokens, ref idx);
                            float y1 = NextFloat(tokens, ref idx);
                            float x = NextFloat(tokens, ref idx);
                            float y = NextFloat(tokens, ref idx);
                            if (rel)
                            {
                                x1 += curX; y1 += curY;
                                x += curX; y += curY;
                            }
                            ApproximateQuadBezier(new PointF(curX, curY), new PointF(x1, y1), new PointF(x, y), currentPoints);
                            lastCtrlX = x1; lastCtrlY = y1;
                            curX = x; curY = y;
                            lastCmd = cmd;
                        }
                        break;

                    case 'T': // Smooth Quadratic Bezier: x y
                        {
                            float x = NextFloat(tokens, ref idx);
                            float y = NextFloat(tokens, ref idx);
                            if (rel) { x += curX; y += curY; }
                            float x1 = (lastCmd == 'Q' || lastCmd == 'q' || lastCmd == 'T' || lastCmd == 't') ? 2 * curX - lastCtrlX : curX;
                            float y1 = (lastCmd == 'Q' || lastCmd == 'q' || lastCmd == 'T' || lastCmd == 't') ? 2 * curY - lastCtrlY : curY;

                            ApproximateQuadBezier(new PointF(curX, curY), new PointF(x1, y1), new PointF(x, y), currentPoints);
                            lastCtrlX = x1; lastCtrlY = y1;
                            curX = x; curY = y;
                            lastCmd = cmd;
                        }
                        break;

                    case 'A': // Arc: rx ry x-axis-rotation large-arc-flag sweep-flag x y
                        {
                            float rx = Math.Abs(NextFloat(tokens, ref idx));
                            float ry = Math.Abs(NextFloat(tokens, ref idx));
                            float rot = NextFloat(tokens, ref idx);
                            bool largeArc = NextFloat(tokens, ref idx) != 0;
                            bool sweep = NextFloat(tokens, ref idx) != 0;
                            float x = NextFloat(tokens, ref idx);
                            float y = NextFloat(tokens, ref idx);
                            if (rel) { x += curX; y += curY; }

                            ApproximateArc(curX, curY, rx, ry, rot, largeArc, sweep, x, y, currentPoints);
                            curX = x; curY = y;
                            lastCtrlX = curX; lastCtrlY = curY;
                            lastCmd = cmd;
                        }
                        break;

                    case 'Z':
                        {
                            isCurrentClosed = true;
                            if (currentPoints.Count > 0 && (Math.Abs(curX - startX) > 0.001f || Math.Abs(curY - startY) > 0.001f))
                            {
                                currentPoints.Add(new PointF(startX, startY));
                            }
                            curX = startX;
                            curY = startY;
                            lastCtrlX = curX; lastCtrlY = curY;
                            FinishSubPath();
                            lastCmd = cmd;
                        }
                        break;

                    default:
                        idx++;
                        break;
                }
            }

            FinishSubPath();
            return subPaths;
        }

        private static List<string> TokenizePathData(string d)
        {
            var list = new List<string>();
            int i = 0, len = d.Length;
            while (i < len)
            {
                char c = d[i];
                if (char.IsWhiteSpace(c) || c == ',')
                {
                    i++;
                    continue;
                }
                if (char.IsLetter(c))
                {
                    list.Add(c.ToString());
                    i++;
                    continue;
                }
                // Số (bao gồm dấu +, -, số thập phân, e/E khoa học)
                int start = i;
                if (c == '+' || c == '-') i++;
                bool hasDot = false;
                while (i < len)
                {
                    char ch = d[i];
                    if (char.IsDigit(ch)) { i++; }
                    else if (ch == '.' && !hasDot) { hasDot = true; i++; }
                    else if ((ch == 'e' || ch == 'E') && i + 1 < len)
                    {
                        i++;
                        if (d[i] == '+' || d[i] == '-') i++;
                    }
                    else break;
                }
                if (i > start)
                {
                    list.Add(d.Substring(start, i - start));
                }
                else i++;
            }
            return list;
        }

        private static float NextFloat(List<string> tokens, ref int idx)
        {
            if (idx >= tokens.Count) return 0f;
            float val = ParseFloat(tokens[idx]);
            idx++;
            return val;
        }

        private static void ApproximateCubicBezier(PointF p0, PointF p1, PointF p2, PointF p3, List<PointF> pts, int steps = 20)
        {
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float u = 1f - t;
                float tt = t * t;
                float uu = u * u;
                float uuu = uu * u;
                float ttt = tt * t;

                float x = uuu * p0.X + 3 * uu * t * p1.X + 3 * u * tt * p2.X + ttt * p3.X;
                float y = uuu * p0.Y + 3 * uu * t * p1.Y + 3 * u * tt * p2.Y + ttt * p3.Y;
                pts.Add(new PointF(x, y));
            }
        }

        private static void ApproximateQuadBezier(PointF p0, PointF p1, PointF p2, List<PointF> pts, int steps = 16)
        {
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float u = 1f - t;
                float tt = t * t;
                float uu = u * u;

                float x = uu * p0.X + 2 * u * t * p1.X + tt * p2.X;
                float y = uu * p0.Y + 2 * u * t * p1.Y + tt * p2.Y;
                pts.Add(new PointF(x, y));
            }
        }

        private static void ApproximateArc(float x0, float y0, float rx, float ry, float angleDeg, bool largeArc, bool sweep, float x1, float y1, List<PointF> pts)
        {
            if (rx <= 1e-5f || ry <= 1e-5f || (Math.Abs(x0 - x1) < 1e-5f && Math.Abs(y0 - y1) < 1e-5f))
            {
                pts.Add(new PointF(x1, y1));
                return;
            }

            double phi = angleDeg * Math.PI / 180.0;
            double cosPhi = Math.Cos(phi), sinPhi = Math.Sin(phi);

            double dx = (x0 - x1) / 2.0;
            double dy = (y0 - y1) / 2.0;
            double x1p = cosPhi * dx + sinPhi * dy;
            double y1p = -sinPhi * dx + cosPhi * dy;

            double rxSq = rx * rx;
            double rySq = ry * ry;
            double x1pSq = x1p * x1p;
            double y1pSq = y1p * y1p;

            // Kiểm tra bán kính có đủ lớn không
            double lambda = (x1pSq / rxSq) + (y1pSq / rySq);
            if (lambda > 1.0)
            {
                double lSqrt = Math.Sqrt(lambda);
                rx = (float)(rx * lSqrt);
                ry = (float)(ry * lSqrt);
                rxSq = rx * rx;
                rySq = ry * ry;
            }

            double sign = (largeArc == sweep) ? -1.0 : 1.0;
            double sq = ((rxSq * rySq) - (rxSq * y1pSq) - (rySq * x1pSq)) / ((rxSq * y1pSq) + (rySq * x1pSq));
            if (sq < 0) sq = 0;
            double coef = sign * Math.Sqrt(sq);

            double cxp = coef * ((rx * y1p) / ry);
            double cyp = coef * -((ry * x1p) / rx);

            double cx = cosPhi * cxp - sinPhi * cyp + ((x0 + x1) / 2.0);
            double cy = sinPhi * cxp + cosPhi * cyp + ((y0 + y1) / 2.0);

            double ux = (x1p - cxp) / rx;
            double uy = (y1p - cyp) / ry;
            double vx = (-x1p - cxp) / rx;
            double vy = (-y1p - cyp) / ry;

            double startAngle = Math.Atan2(uy, ux);
            double deltaAngle = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);

            if (!sweep && deltaAngle > 0) deltaAngle -= 2 * Math.PI;
            else if (sweep && deltaAngle < 0) deltaAngle += 2 * Math.PI;

            int steps = Math.Max(4, (int)Math.Ceiling(Math.Abs(deltaAngle) / (Math.PI / 18))); // mỗi 10 độ 1 đoạn
            for (int i = 1; i <= steps; i++)
            {
                double theta = startAngle + deltaAngle * (i / (double)steps);
                double ex = rx * Math.Cos(theta);
                double ey = ry * Math.Sin(theta);
                double px = cosPhi * ex - sinPhi * ey + cx;
                double py = sinPhi * ex + cosPhi * ey + cy;
                pts.Add(new PointF((float)px, (float)py));
            }
        }
        #endregion

        #region Helpers & Matrix Transform
        private static float ParseFloat(string? s, float fallback = 0f)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            return float.TryParse(s, NumberStyles.Float, Inv, out float v) ? v : fallback;
        }

        private static float ParseLength(string? s, float mmPerPx, float fallback = 0f)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            s = s.Trim().ToLowerInvariant();
            if (s.EndsWith("mm")) return ParseFloat(s.Substring(0, s.Length - 2)) / mmPerPx;
            if (s.EndsWith("cm")) return (ParseFloat(s.Substring(0, s.Length - 2)) * 10f) / mmPerPx;
            if (s.EndsWith("in")) return (ParseFloat(s.Substring(0, s.Length - 2)) * 25.4f) / mmPerPx;
            if (s.EndsWith("pt")) return (ParseFloat(s.Substring(0, s.Length - 2)) * (25.4f / 72f)) / mmPerPx;
            if (s.EndsWith("px")) return ParseFloat(s.Substring(0, s.Length - 2));
            return ParseFloat(s, fallback);
        }

        private static List<float> SplitNumbers(string text)
        {
            var res = new List<float>();
            var rx = new Regex(@"[-+]?[0-9]*\.?[0-9]+([eE][-+]?[0-9]+)?");
            foreach (Match m in rx.Matches(text))
            {
                if (float.TryParse(m.Value, NumberStyles.Float, Inv, out float v)) res.Add(v);
            }
            return res;
        }

        private static Matrix2D ParseTransform(string? tStr)
        {
            var mat = new Matrix2D();
            if (string.IsNullOrWhiteSpace(tStr)) return mat;

            var rx = new Regex(@"(translate|rotate|scale|matrix)\s*\(([^)]+)\)", RegexOptions.IgnoreCase);
            foreach (Match m in rx.Matches(tStr))
            {
                string fn = m.Groups[1].Value.ToLowerInvariant();
                var args = SplitNumbers(m.Groups[2].Value);
                if (args.Count == 0) continue;

                switch (fn)
                {
                    case "translate":
                        float tx = args[0];
                        float ty = args.Count > 1 ? args[1] : 0f;
                        mat = Matrix2D.Multiply(mat, Matrix2D.CreateTranslation(tx, ty));
                        break;
                    case "scale":
                        float sx = args[0];
                        float sy = args.Count > 1 ? args[1] : sx;
                        mat = Matrix2D.Multiply(mat, Matrix2D.CreateScale(sx, sy));
                        break;
                    case "rotate":
                        float ang = args[0];
                        if (args.Count >= 3)
                        {
                            mat = Matrix2D.Multiply(mat, Matrix2D.CreateTranslation(args[1], args[2]));
                            mat = Matrix2D.Multiply(mat, Matrix2D.CreateRotation(ang));
                            mat = Matrix2D.Multiply(mat, Matrix2D.CreateTranslation(-args[1], -args[2]));
                        }
                        else
                        {
                            mat = Matrix2D.Multiply(mat, Matrix2D.CreateRotation(ang));
                        }
                        break;
                    case "matrix":
                        if (args.Count >= 6)
                        {
                            var custom = new Matrix2D(args[0], args[1], args[2], args[3], args[4], args[5]);
                            mat = Matrix2D.Multiply(mat, custom);
                        }
                        break;
                }
            }
            return mat;
        }

        public struct Matrix2D
        {
            public float M11, M12, M21, M22, DX, DY;

            public Matrix2D(float m11, float m12, float m21, float m22, float dx, float dy)
            {
                M11 = m11; M12 = m12; M21 = m21; M22 = m22; DX = dx; DY = dy;
            }

            public static Matrix2D Identity => new Matrix2D(1, 0, 0, 1, 0, 0);

            public static Matrix2D CreateTranslation(float dx, float dy) => new Matrix2D(1, 0, 0, 1, dx, dy);
            public static Matrix2D CreateScale(float sx, float sy) => new Matrix2D(sx, 0, 0, sy, 0, 0);
            public static Matrix2D CreateRotation(float deg)
            {
                double rad = deg * Math.PI / 180.0;
                float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
                return new Matrix2D(cos, sin, -sin, cos, 0, 0);
            }

            public PointF Transform(PointF p) => new PointF(M11 * p.X + M21 * p.Y + DX, M12 * p.X + M22 * p.Y + DY);

            public static Matrix2D Multiply(Matrix2D a, Matrix2D b)
            {
                return new Matrix2D(
                    a.M11 * b.M11 + a.M12 * b.M21,
                    a.M11 * b.M12 + a.M12 * b.M22,
                    a.M21 * b.M11 + a.M22 * b.M21,
                    a.M21 * b.M12 + a.M22 * b.M22,
                    a.DX * b.M11 + a.DY * b.M21 + b.DX,
                    a.DX * b.M12 + a.DY * b.M22 + b.DY
                );
            }
        }
        #endregion
    }
}

