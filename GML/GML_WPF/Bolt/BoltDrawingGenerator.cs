using System.Windows;
using static GML_WPF.Bolt.BoltSpec;

namespace GML_WPF.Bolt
{
    public enum BoltDrawingKind { Detail, ThreeViews, HeadDetail, ThreadDetail }

    /// <summary>
    /// Generates A4 landscape manufacturing drawings of a bolt following
    /// ISO 128 line conventions, ISO 6410 thread representation and
    /// first-angle (ISO E) projection.
    /// </summary>
    public static class BoltDrawingGenerator
    {
        private static readonly double[] MainScales = { 10, 5, 2, 1, 0.5, 0.2, 0.1 };
        private static readonly double[] DetailScales = { 50, 20, 10, 5, 2, 1 };

        // Usable drawing area inside the frame and above the title block.
        private const double AreaLeft = 15, AreaRight = 285, AreaTop = 15;

        public static string DisplayName(BoltDrawingKind kind) => kind switch
        {
            BoltDrawingKind.Detail => "Detail Drawing",
            BoltDrawingKind.ThreeViews => "Three Views",
            BoltDrawingKind.HeadDetail => "Head Detail",
            BoltDrawingKind.ThreadDetail => "Thread Detail",
            _ => kind.ToString()
        };

        /// <summary>The drawing set, in sheet order.</summary>
        public static IReadOnlyList<BoltDrawingKind> SheetOrder { get; } = Enum.GetValues<BoltDrawingKind>();

        /// <summary>Builds every sheet of the drawing set (numbered 1..N).</summary>
        public static List<DrawingSheet> BuildSet(BoltSpec spec)
            => SheetOrder.Select(k => Build(spec, k)).ToList();

        public static DrawingSheet Build(BoltSpec spec, BoltDrawingKind kind)
        {
            var sheet = new DrawingSheet
            {
                SheetNumber = SheetOrder.ToList().IndexOf(kind) + 1,
                SheetCount = SheetOrder.Count,
                SheetName = DisplayName(kind)
            };
            switch (kind)
            {
                case BoltDrawingKind.Detail: BuildDetail(sheet, spec); break;
                case BoltDrawingKind.ThreeViews: BuildThreeViews(sheet, spec); break;
                case BoltDrawingKind.HeadDetail: BuildHeadDetail(sheet, spec); break;
                case BoltDrawingKind.ThreadDetail: BuildThreadDetail(sheet, spec); break;
            }
            return sheet;
        }

        public static string ScaleText(double sc)
            => sc >= 1 ? $"{Fmt(sc)}:1" : $"1:{Fmt(1 / sc)}";

        private static double ChooseScale(double? fixedScale, double[] scales, Func<double, bool> fits)
        {
            if (fixedScale is double f && f > 0) return f;
            foreach (var sc in scales)
                if (fits(sc)) return sc;
            return scales[^1];
        }

        // ================================================================ sheets
        private static void BuildDetail(DrawingSheet s, BoltSpec b)
        {
            double hm = HeadMax(b);
            double sc = ChooseScale(b.Scale, MainScales, sc =>
                14 + b.TipX * sc + 24 + hm * sc + 28 <= AreaRight - AreaLeft &&
                hm * sc + 32 <= 113);

            double width = 14 + b.TipX * sc + 24 + hm * sc + 28;
            double x0 = AreaLeft + Math.Max(0, (AreaRight - AreaLeft - width) / 2);
            var front = new ViewMap(x0 + 14, 70, sc);
            var end = new Point(front.OriginX + b.TipX * sc + 24 + hm * sc / 2, front.OriginY);

            DrawSide(s, front, b, topView: false);
            DimSide(s, front, b);
            DrawHeadEnd(s, end, sc, b);
            DimHeadEnd(s, end, sc, b);

            Notes(s, AreaLeft, 136, DetailNotes(b));
            TitleBlock(s, b, ScaleText(sc), "DETAIL DRAWING");
        }

        private static void BuildThreeViews(DrawingSheet s, BoltSpec b)
        {
            double hm = HeadMax(b);
            double sc = ChooseScale(b.Scale, MainScales, sc =>
                hm * sc + 20 + 14 + b.TipX * sc + 24 + hm * sc + 28 <= AreaRight - AreaLeft &&
                2 * hm * sc + 28 + 26 <= 150);

            double width = hm * sc + 20 + 14 + b.TipX * sc + 24 + hm * sc + 28;
            double x0 = AreaLeft + Math.Max(0, (AreaRight - AreaLeft - width) / 2);
            double y1 = AreaTop + 14 + hm * sc / 2;
            double y2 = y1 + hm * sc + 28;

            var tipEnd = new Point(x0 + hm * sc / 2, y1);              // view from the right (placed left)
            var front = new ViewMap(x0 + hm * sc + 20 + 14, y1, sc);
            var headEnd = new Point(front.OriginX + b.TipX * sc + 24 + hm * sc / 2, y1); // view from the left
            var top = new ViewMap(front.OriginX, y2, sc);              // view from above (placed below)

            DrawSide(s, front, b, topView: false);
            DimSide(s, front, b);
            DrawHeadEnd(s, headEnd, sc, b);
            DimHeadEnd(s, headEnd, sc, b);
            DrawTipEnd(s, tipEnd, sc, b);
            DrawSide(s, top, b, topView: true);

            Notes(s, AreaLeft, 175, new[]
            {
                $"Designation: {b.Designation}",
                $"Thread {b.ThreadDesignation}, b = {Fmt(Math.Round(b.ThreadLength, 2))}",
                "First angle projection (ISO E). Dimensions in mm.",
                "General tolerances ISO 2768-m."
            }, heading: false);
            TitleBlock(s, b, ScaleText(sc), "THREE VIEWS (FRONT / TOP / ENDS)");
        }

        private static void BuildHeadDetail(DrawingSheet s, BoltSpec b)
        {
            double hm = HeadMax(b);
            double stub = Math.Min(b.TipX, b.ShankStartX + Math.Max(b.D, 4));
            bool broken = stub < b.TipX - 1e-6;
            double sc = ChooseScale(null, DetailScales, sc =>
                14 + stub * sc + 30 + hm * sc + 30 <= AreaRight - AreaLeft &&
                hm * sc + 32 <= 113);

            double width = 14 + stub * sc + 30 + hm * sc + 30;
            double x0 = AreaLeft + Math.Max(0, (AreaRight - AreaLeft - width) / 2);
            var front = new ViewMap(x0 + 14, 70, sc);
            var end = new Point(front.OriginX + stub * sc + 30 + hm * sc / 2, front.OriginY);

            DrawSide(s, front, b, topView: false, breakAt: broken ? stub : null);
            DimHead(s, front, b);
            DrawHeadEnd(s, end, sc, b);
            DimHeadEnd(s, end, sc, b);

            s.Text($"HEAD DETAIL — SCALE {ScaleText(sc)}", AreaLeft, 20, 3.5, bold: true);
            Notes(s, AreaLeft, 136, HeadNotes(b));
            TitleBlock(s, b, ScaleText(sc), "HEAD DETAIL");
        }

        private static void BuildThreadDetail(DrawingSheet s, BoltSpec b)
        {
            const int pitches = 5;
            double P = b.P, h3 = b.ThreadDepth;
            double sc = ChooseScale(null, DetailScales, sc => pitches * P * sc <= 175 && h3 * sc <= 60);

            // Basic ISO 68-1 external profile with flat crest P/8 and flat root.
            double cf = P / 8;
            double fr = h3 * Math.Tan(Math.PI / 6);
            double rf = P - cf - 2 * fr;
            double CrestCenter(int i) => i * P + rf / 2 + fr + cf / 2;

            var m = new ViewMap(45, 100, sc);
            var profile = new List<Point>();
            for (int i = 0; i < pitches; i++)
            {
                double x = i * P;
                profile.Add(m.P(x, 0));
                profile.Add(m.P(x + rf / 2, 0));
                profile.Add(m.P(x + rf / 2 + fr, h3));
                profile.Add(m.P(x + rf / 2 + fr + cf, h3));
                profile.Add(m.P(x + rf / 2 + 2 * fr + cf, 0));
            }
            profile.Add(m.P(pitches * P, 0));

            // Hatched material below the profile, bounded by thin break lines.
            double depth = 12 / sc;
            var material = new List<Point>(profile)
            {
                m.P(pitches * P, -depth),
                m.P(0, -depth)
            };
            s.Poly(material, LineKind.Thin, closed: true, hatch: true);
            s.Poly(profile, LineKind.Visible);

            // Pitch-diameter line.
            double yPitch = (b.PitchDiameter - b.MinorDiameter) / 2;
            double xe = m.P(pitches * P, 0).X;
            s.Line(m.P(0, yPitch) + new Vector(-4, 0), new Point(xe + 14, m.P(0, yPitch).Y), LineKind.Center);

            // Diameter labels.
            s.Line(new Point(xe, m.P(0, h3).Y), new Point(xe + 14, m.P(0, h3).Y), LineKind.Thin);
            s.Line(new Point(xe, m.P(0, 0).Y), new Point(xe + 14, m.P(0, 0).Y), LineKind.Thin);
            s.Text($"d = {Fmt(Math.Round(b.D, 3))}  (major Ø)", xe + 15, m.P(0, h3).Y);
            s.Text($"d2 = {Fmt(Math.Round(b.PitchDiameter, 3))}  (pitch Ø)", xe + 15, m.P(0, yPitch).Y);
            s.Text($"d3 = {Fmt(Math.Round(b.MinorDiameter, 3))}  (minor Ø)", xe + 15, m.P(0, 0).Y);

            // Pitch.
            s.Dim(m.P(CrestCenter(2), h3), m.P(CrestCenter(3), h3), true, m.P(0, h3).Y - 10, $"P = {Fmt(P)}");

            // Thread depth.
            s.Dim(m.P(0, 0), m.P(rf / 2 + fr, h3), false, m.P(0, 0).X - 8, $"h3 = {Fmt(Math.Round(h3, 3))}");

            // 60° flank angle in the first groove.
            double gx = CrestCenter(0) + P / 2;
            double apexY = -(rf / 2) / Math.Tan(Math.PI / 6);
            var apex = m.P(gx, apexY);
            double rho = (h3 - apexY) * sc + 6;
            s.Arc(apex, rho, 60, 60, LineKind.Thin);
            foreach (var deg in new[] { 60.0, 120.0 })
            {
                double a = deg * Math.PI / 180;
                var dir = new Vector(Math.Cos(a), -Math.Sin(a));
                double crestDist = (h3 - apexY) * sc / Math.Sin(a);
                s.Line(apex + dir * crestDist, apex + dir * (rho + 1.5), LineKind.Thin);
            }
            s.Text("60°", apex.X, apex.Y - rho - 2.5, DrawingSheet.TextHeight, TextAlign.Center);

            s.Text($"THREAD PROFILE {b.ThreadDesignation} (ISO 68-1)  —  SCALE {ScaleText(sc)}", AreaLeft, 20, 3.5, bold: true);

            var left = new[]
            {
                $"Designation: {b.ThreadDesignation}",
                $"Nominal Ø d: {Fmt(b.D)}",
                $"Pitch P: {Fmt(P)} ({b.Pitch.ToString().ToLowerInvariant()})",
                $"Pitch Ø d2: {Fmt(Math.Round(b.PitchDiameter, 3))}",
                $"Minor Ø d3: {Fmt(Math.Round(b.MinorDiameter, 3))}",
                $"Thread depth h3: {Fmt(Math.Round(h3, 3))}",
            };
            var right = new[]
            {
                $"Fundamental H = 0.866P = {Fmt(Math.Round(0.86603 * P, 3))}",
                $"Root radius R ≈ 0.144P = {Fmt(Math.Round(0.14434 * P, 3))}",
                $"Tolerance class: {b.ToleranceClass} (ISO 965-1)",
                $"Hand: {(b.Hand == ThreadHand.Left ? "left (LH)" : "right")}, single start",
                $"Thread length b: {Fmt(Math.Round(b.ThreadLength, 2))}{(b.IsFullyThreaded ? " (full)" : "")}",
                "Rolled thread; no undercut.",
            };
            Notes(s, AreaLeft, 128, left);
            Notes(s, 120, 128, right, heading: false, firstNumber: left.Length + 1, startOffset: 1);
            TitleBlock(s, b, ScaleText(sc), "THREAD DETAIL");
        }

        // ============================================================== geometry
        private static double HexCorner(double s) => s / Math.Cos(Math.PI / 6);

        /// <summary>Largest extent of the head as seen in the end view (diameter).</summary>
        private static double HeadMax(BoltSpec b) => b.HeadType switch
        {
            BoltHeadType.Hex => HexCorner(b.HeadWidth),
            BoltHeadType.Square => b.HeadWidth * Math.Sqrt(2),
            _ => b.HeadWidth
        };

        /// <summary>Half the head height as projected in the front (or top) view.</summary>
        private static double HeadHalf(BoltSpec b, bool topView) => b.HeadType switch
        {
            BoltHeadType.Hex => topView ? b.HeadWidth / 2 : HexCorner(b.HeadWidth) / 2,
            BoltHeadType.Square => b.HeadWidth * Math.Sqrt(2) / 2,
            _ => b.HeadWidth / 2
        };

        private static bool IsDome(BoltSpec b) => b.HeadType is BoltHeadType.Button or BoltHeadType.Carriage;

        private static double CapChamfer(BoltSpec b) => 0.06 * b.HeadWidth;

        private static IEnumerable<Point> RectPts(ViewMap m, double x1, double y1, double x2, double y2)
            => new[] { m.P(x1, y1), m.P(x2, y1), m.P(x2, y2), m.P(x1, y2) };

        /// <summary>Side (front or top) view with head on the left, axis horizontal.</summary>
        private static void DrawSide(DrawingSheet s, ViewMap m, BoltSpec b, bool topView, double? breakAt = null)
        {
            double r = b.D / 2, r3 = b.MinorDiameter / 2, k = b.HeadHeight, R = b.HeadWidth / 2;
            double tip = breakAt ?? b.TipX;
            bool showTip = breakAt == null;
            double c = r - r3;

            // ---- head
            switch (b.HeadType)
            {
                case BoltHeadType.Hex:
                {
                    double h = HeadHalf(b, topView);
                    double e = HexCorner(b.HeadWidth);
                    s.Poly(RectPts(m, 0, -h, k, h), closed: true);
                    if (topView) s.Line(m.P(0, 0), m.P(k, 0));
                    else
                    {
                        s.Line(m.P(0, e / 4), m.P(k, e / 4));
                        s.Line(m.P(0, -e / 4), m.P(k, -e / 4));
                    }
                    break;
                }
                case BoltHeadType.Square:
                {
                    double h = HeadHalf(b, topView);
                    s.Poly(RectPts(m, 0, -h, k, h), closed: true);
                    s.Line(m.P(0, 0), m.P(k, 0));
                    break;
                }
                case BoltHeadType.SocketCap:
                {
                    double ch = CapChamfer(b);
                    s.Poly(new[]
                    {
                        m.P(0, R - ch), m.P(ch, R), m.P(k, R), m.P(k, -R), m.P(ch, -R), m.P(0, -(R - ch))
                    }, closed: true);
                    s.Line(m.P(ch, R), m.P(ch, -R), LineKind.Visible);
                    break;
                }
                case BoltHeadType.Countersunk:
                    s.Poly(new[] { m.P(0, R), m.P(k, r), m.P(k, -r), m.P(0, -R) }, closed: true);
                    break;
                case BoltHeadType.Button:
                case BoltHeadType.Carriage:
                    s.Poly(DomePoints(m, k, R), closed: true);
                    break;
            }

            // ---- hex socket (hidden)
            if (b.HasSocket)
            {
                double half = topView ? b.SocketSize / 2 : HexCorner(b.SocketSize) / 2;
                double t = b.SocketDepth;
                double cone = half * Math.Tan((90 - 59) * Math.PI / 180); // 118° drill point
                s.Poly(new[] { m.P(0, half), m.P(t, half), m.P(t + cone, 0), m.P(t, -half), m.P(0, -half) }, LineKind.Hidden);
                s.Line(m.P(t, half), m.P(t, -half), LineKind.Hidden);
            }

            // ---- carriage square neck transition
            if (b.HeadType == BoltHeadType.Carriage && k + b.NeckLength < tip)
                s.Line(m.P(k + b.NeckLength, r), m.P(k + b.NeckLength, -r));

            // ---- shank
            if (showTip)
            {
                s.Poly(new[]
                {
                    m.P(k, r), m.P(tip - c, r), m.P(tip, r3), m.P(tip, -r3), m.P(tip - c, -r), m.P(k, -r)
                });
                s.Line(m.P(tip - c, r), m.P(tip - c, -r), LineKind.Thin);
            }
            else
            {
                s.Line(m.P(k, r), m.P(tip, r));
                s.Line(m.P(k, -r), m.P(tip, -r));
                // Freehand-style short break line.
                var top = m.P(tip, r);
                double hgt = m.L(2 * r);
                var pts = Enumerable.Range(0, 21).Select(i =>
                    new Point(top.X + 1.2 * Math.Sin(2 * Math.PI * i / 20), top.Y + hgt * i / 20));
                s.Poly(pts, LineKind.Thin);
            }

            // ---- thread (ISO 6410: minor Ø thin, thread end thick)
            double threadStart = b.TipX - b.ThreadLength;
            if (b.ThreadLength > 0 && threadStart < tip)
            {
                double xs = Math.Max(threadStart, b.ShankStartX);
                s.Line(m.P(xs, r3), m.P(tip, r3), LineKind.Thin);
                s.Line(m.P(xs, -r3), m.P(tip, -r3), LineKind.Thin);
                if (!b.IsFullyThreaded)
                    s.Line(m.P(threadStart, r), m.P(threadStart, -r));
            }

            // ---- axis
            s.Line(m.P(0, 0) + new Vector(-3, 0), m.P(tip, 0) + new Vector(3, 0), LineKind.Center);
        }

        /// <summary>Circular-arc dome from the apex (0,0) to the bearing face at x = k.</summary>
        private static IEnumerable<Point> DomePoints(ViewMap m, double k, double R)
        {
            double cx = (k * k + R * R) / (2 * k);
            double phiMax = Math.Atan2(R, cx - k);
            const int n = 36;
            for (int i = 0; i <= n; i++)
            {
                double phi = -phiMax + 2 * phiMax * i / n;
                yield return m.P(cx - cx * Math.Cos(phi), cx * Math.Sin(phi));
            }
        }

        /// <summary>End view looking at the top of the head.</summary>
        private static void DrawHeadEnd(DrawingSheet s, Point c, double sc, BoltSpec b)
        {
            double R = b.HeadWidth / 2 * sc;
            switch (b.HeadType)
            {
                case BoltHeadType.Hex:
                    s.Poly(DrawingSheet.RegularPolygon(c, HexCorner(b.HeadWidth) / 2 * sc, 6, 90), closed: true);
                    s.Circle(c, 0.95 * R, LineKind.Thin);
                    break;
                case BoltHeadType.Square:
                    s.Poly(DrawingSheet.RegularPolygon(c, HeadMax(b) / 2 * sc, 4, 90), closed: true);
                    break;
                case BoltHeadType.SocketCap:
                    s.Circle(c, R);
                    s.Circle(c, R - CapChamfer(b) * sc);
                    break;
                default:
                    s.Circle(c, R);
                    break;
            }
            if (b.HasSocket)
                s.Poly(DrawingSheet.RegularPolygon(c, HexCorner(b.SocketSize) / 2 * sc, 6, 90), closed: true);
            s.CenterCross(c, HeadMax(b) / 2 * sc);
        }

        /// <summary>End view looking at the thread tip.</summary>
        private static void DrawTipEnd(DrawingSheet s, Point c, double sc, BoltSpec b)
        {
            switch (b.HeadType)
            {
                case BoltHeadType.Hex:
                    s.Poly(DrawingSheet.RegularPolygon(c, HexCorner(b.HeadWidth) / 2 * sc, 6, 90), closed: true);
                    break;
                case BoltHeadType.Square:
                    s.Poly(DrawingSheet.RegularPolygon(c, HeadMax(b) / 2 * sc, 4, 90), closed: true);
                    break;
                default:
                    s.Circle(c, b.HeadWidth / 2 * sc);
                    break;
            }
            if (b.HeadType == BoltHeadType.Carriage)
                s.Poly(DrawingSheet.RegularPolygon(c, b.D / 2 * Math.Sqrt(2) * sc, 4, 45), closed: true);

            s.Circle(c, b.D / 2 * sc);                                   // major Ø: thick full circle
            s.Arc(c, b.MinorDiameter / 2 * sc, 90, 270, LineKind.Thin);  // minor Ø: thin 3/4 circle
            s.CenterCross(c, HeadMax(b) / 2 * sc);
        }

        // ============================================================ dimensions
        private static void DimSide(DrawingSheet s, ViewMap m, BoltSpec b)
        {
            double r = b.D / 2, r3 = b.MinorDiameter / 2, k = b.HeadHeight;
            double h = HeadHalf(b, false);
            double yTop = m.P(0, h).Y, yBot = m.P(0, -h).Y;
            bool carriage = b.HeadType == BoltHeadType.Carriage;

            // Head height k.
            s.Dim(m.P(0, IsDome(b) ? 0 : h), m.P(k, b.IsCountersunk ? r : h), true, yTop - 7, Fmt(k));

            // Thread length b.
            double ts = b.TipX - b.ThreadLength;
            if (b.ThreadLength > 0 && !b.IsFullyThreaded)
                s.Dim(m.P(ts, r), m.P(b.TipX, r3), true, yTop - 7, Fmt(Math.Round(b.ThreadLength, 2)));

            // Carriage neck length.
            if (carriage)
                s.Dim(m.P(k, -r), m.P(k + b.NeckLength, -r), true, yBot + 6, Fmt(b.NeckLength));

            // Nominal length L.
            double lStart = b.IsCountersunk ? 0 : k;
            s.Dim(m.P(lStart, -h), m.P(b.TipX, -r3), true, yBot + (carriage ? 13 : 9), Fmt(b.Length));

            // Thread callout.
            double c = r - r3;
            s.Dim(m.P(b.TipX - c, -r), m.P(b.TipX - c, r), false, m.P(b.TipX, 0).X + 7, b.ThreadDesignation);

            // Round head diameter on the front view.
            if (b.HeadType is not (BoltHeadType.Hex or BoltHeadType.Square))
            {
                double x = b.HeadType switch
                {
                    BoltHeadType.SocketCap => CapChamfer(b),
                    BoltHeadType.Countersunk => 0,
                    _ => k
                };
                s.Dim(m.P(x, -h), m.P(x, h), false, m.P(0, 0).X - 8, "Ø" + Fmt(b.HeadWidth));
            }
        }

        /// <summary>Head-only dimensions for the enlarged head detail.</summary>
        private static void DimHead(DrawingSheet s, ViewMap m, BoltSpec b)
        {
            double r = b.D / 2, k = b.HeadHeight;
            double h = HeadHalf(b, false);
            double yTop = m.P(0, h).Y, yBot = m.P(0, -h).Y;

            s.Dim(m.P(0, IsDome(b) ? 0 : h), m.P(k, b.IsCountersunk ? r : h), true, yTop - 7, Fmt(k));

            if (b.HeadType == BoltHeadType.Hex)
            {
                // Across-corners reference dimension.
                s.Dim(m.P(0, -h), m.P(0, h), false, m.P(0, 0).X - 8, $"({Fmt(Math.Round(HexCorner(b.HeadWidth), 2))})");
            }

            if (b.HeadType == BoltHeadType.SocketCap)
            {
                double ch = CapChamfer(b);
                s.Leader(m.P(ch / 2, h - ch / 2), m.P(0, h) + new Vector(-8, -6), $"{Fmt(Math.Round(ch, 2))}x45°", textRight: false);
            }

            if (b.IsCountersunk)
            {
                double R = b.HeadWidth / 2;
                double halfAngle = Math.Atan2(R - r, k);
                double apexX = k + r / Math.Tan(halfAngle);
                var apex = m.P(apexX, 0);
                double rho = (m.P(0, R) - apex).Length + 5;
                double deg = halfAngle * 180 / Math.PI;
                s.Arc(apex, rho, 180 - deg, 2 * deg, LineKind.Thin);
                foreach (var sign in new[] { 1, -1 })
                {
                    var corner = m.P(0, sign * R);
                    var dir = corner - apex; dir.Normalize();
                    s.Line(corner + dir * 0.8, apex + dir * (rho + 1.5), LineKind.Thin);
                }
                s.Text($"{Fmt(Math.Round(2 * deg))}°", apex.X - rho - 2, apex.Y - 3, DrawingSheet.TextHeight, TextAlign.Right);
            }

            if (b.HeadType == BoltHeadType.Carriage)
            {
                s.Dim(m.P(k, -r), m.P(k + b.NeckLength, -r), true, yBot + 6, Fmt(b.NeckLength));
                s.Leader(m.P(k + b.NeckLength / 2, r), m.P(k + b.NeckLength / 2, h) + new Vector(4, -8), "□" + Fmt(b.D));
            }
            else if (b.HasSocket)
            {
                // Socket depth measured from the head top, below the view.
                double half = HexCorner(b.SocketSize) / 2;
                s.Dim(m.P(0, -half), m.P(b.SocketDepth, -half), true, yBot + 7, Fmt(b.SocketDepth));
            }
        }

        private static void DimHeadEnd(DrawingSheet s, Point c, double sc, BoltSpec b)
        {
            double hm = HeadMax(b) / 2 * sc;
            switch (b.HeadType)
            {
                case BoltHeadType.Hex:
                {
                    double sw = b.HeadWidth / 2 * sc, e4 = HexCorner(b.HeadWidth) / 4 * sc;
                    s.Dim(new Point(c.X - sw, c.Y + e4), new Point(c.X + sw, c.Y + e4), true, c.Y + hm + 6, Fmt(b.HeadWidth));
                    break;
                }
                case BoltHeadType.Square:
                {
                    var target = new Point(c.X + hm / 2, c.Y - hm / 2);
                    s.Leader(target, target + new Vector(6, -8), "□" + Fmt(b.HeadWidth));
                    break;
                }
                default:
                {
                    double R = b.HeadWidth / 2 * sc;
                    s.Dim(new Point(c.X - R, c.Y), new Point(c.X + R, c.Y), true, c.Y + R + 6, "Ø" + Fmt(b.HeadWidth));
                    break;
                }
            }

            if (b.HasSocket)
            {
                double ri = b.SocketSize / 2 * sc;
                var target = new Point(c.X + ri * Math.Cos(Math.PI / 3), c.Y - ri * Math.Sin(Math.PI / 3));
                var elbow = new Point(c.X + hm * 0.75 + 3, c.Y - hm - 6);
                s.Leader(target, elbow, $"HEX {Fmt(b.SocketSize)} ↧{Fmt(b.SocketDepth)}");
            }
        }

        // ================================================================= notes
        private static string MaterialStandard(BoltSpec b)
            => b.PropertyClass.StartsWith('A') ? "ISO 3506-1" : "ISO 898-1";

        private static string HeadSummary(BoltSpec b)
        {
            var parts = new List<string>();
            parts.Add(b.HeadType is BoltHeadType.Hex or BoltHeadType.Square
                ? $"s = {Fmt(b.HeadWidth)}"
                : $"dk = {Fmt(b.HeadWidth)}");
            parts.Add($"k = {Fmt(b.HeadHeight)}");
            if (b.HasSocket) parts.Add($"hex socket s = {Fmt(b.SocketSize)}, t = {Fmt(b.SocketDepth)}");
            if (b.HeadType == BoltHeadType.Carriage) parts.Add($"square neck {Fmt(b.D)} x {Fmt(b.NeckLength)}");
            return string.Join(", ", parts);
        }

        private static IEnumerable<string> DetailNotes(BoltSpec b)
        {
            yield return $"Thread {b.ThreadDesignation} {(b.Hand == ThreadHand.Left ? "left hand" : "right hand")} to ISO 261 / ISO 965-1; " +
                         $"thread length b = {Fmt(Math.Round(b.ThreadLength, 2))}{(b.IsFullyThreaded ? " (threaded to head)" : "")}.";
            yield return $"Head: {BoltStandards.DisplayName(b.HeadType).ToLowerInvariant()}, {HeadSummary(b)} ({b.Standard}).";
            if (b.IsCountersunk) yield return "Countersink angle 90°; L is overall length including head.";
            yield return $"Property class {b.PropertyClass} to {MaterialStandard(b)}; finish: {b.Finish.ToLowerInvariant()}.";
            yield return "Unless otherwise specified: general tolerances ISO 2768-m; remove burrs, break sharp edges.";
            yield return $"Designation: {b.Designation}.";
        }

        private static IEnumerable<string> HeadNotes(BoltSpec b)
        {
            yield return $"Head type: {BoltStandards.DisplayName(b.HeadType)} ({b.Standard}).";
            yield return $"Head dimensions: {HeadSummary(b)}.";
            if (b.HeadType == BoltHeadType.Hex)
                yield return $"Across corners e ≈ 1.155 s = {Fmt(Math.Round(HexCorner(b.HeadWidth), 2))} (reference). Chamfer 30° on head top.";
            if (b.HasSocket)
                yield return "Socket: 118° drill point at socket bottom; socket edge may be countersunk.";
            yield return "Head bearing face square to the shank axis within 1°.";
            yield return "Dimensions are nominal; verify against the current edition of the product standard.";
        }

        private static void Notes(DrawingSheet s, double x, double y, IEnumerable<string> lines,
                                  bool heading = true, int firstNumber = 1, int startOffset = 0)
        {
            double step = 4.6;
            int row = startOffset;
            if (heading)
            {
                s.Text("NOTES", x, y, 2.5, bold: true);
                row = 1;
            }
            int n = firstNumber;
            foreach (var line in lines)
            {
                s.Text(heading || firstNumber > 1 ? $"{n++}. {line}" : line, x, y + row * step, 2.2);
                row++;
            }
        }

        private static void TitleBlock(DrawingSheet s, BoltSpec b, string scale, string viewName)
        {
            s.Rect(10, 10, 277, 190, LineKind.Border);

            const double x0 = 117, y0 = 170, w = 170, h = 30, cw = w / 4;
            s.Rect(x0, y0, w, h, LineKind.Visible);
            s.Line(x0, y0 + 9, x0 + w, y0 + 9);
            s.Line(x0, y0 + 16, x0 + w, y0 + 16, LineKind.Thin);
            s.Line(x0, y0 + 23, x0 + w, y0 + 23, LineKind.Thin);
            for (int i = 1; i < 4; i++)
                s.Line(x0 + i * cw, y0 + 9, x0 + i * cw, y0 + h, LineKind.Thin);

            s.Text(b.Title, x0 + 2, y0 + 3.3, 3.2, bold: true);
            s.Text(viewName, x0 + 2, y0 + 7.2, 1.8);

            string[,] cells =
            {
                { "STANDARD", b.Standard },
                { "THREAD x LENGTH", $"{b.ThreadDesignation} x {Fmt(b.Length)}" },
                { "PROPERTY CLASS", b.PropertyClass },
                { "FINISH", b.Finish },
                { "DRAWING NO.", b.DrawingNumber },
                { "SCALE", scale },
                { "SHEET", $"A4  {s.SheetNumber}/{s.SheetCount}" },
                { "PROJECTION", "1st angle (ISO E)" },
                { "DRAWN BY", string.IsNullOrWhiteSpace(b.DrawnBy) ? "-" : b.DrawnBy },
                { "DATE", DateTime.Today.ToString("yyyy-MM-dd") },
                { "UNITS", "mm" },
                { "GENERAL TOL.", "ISO 2768-m" },
            };
            for (int i = 0; i < cells.GetLength(0); i++)
            {
                double cx = x0 + (i % 4) * cw;
                double cy = y0 + 9 + (i / 4) * 7;
                s.Text(cells[i, 0], cx + 1, cy + 1.5, 1.3);
                s.Text(cells[i, 1], cx + 1, cy + 4.6, 2.1);
            }
        }
    }
}
