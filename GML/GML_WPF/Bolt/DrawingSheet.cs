using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GML_WPF.Bolt
{
    /// <summary>ISO 128 line types used on the drawing.</summary>
    public enum LineKind { Visible, Thin, Hidden, Center, Border }

    public enum TextAlign { Left, Center, Right }

    /// <summary>
    /// A drawing sheet whose coordinates are in millimetres (origin top-left,
    /// y pointing down). The canvas is scaled to screen/print by the caller.
    /// </summary>
    public sealed class DrawingSheet
    {
        public const double A4Width = 297, A4Height = 210;
        public const double TextHeight = 2.5;

        private static readonly Brush Ink = Brushes.Black;
        private static readonly FontFamily Font = new("Arial");
        private static readonly Brush HatchBrush = CreateHatch();

        public Canvas Canvas { get; }
        public double Width { get; }
        public double Height { get; }

        /// <summary>Position of this sheet within its drawing set (shown in the title block).</summary>
        public int SheetNumber { get; set; } = 1;
        public int SheetCount { get; set; } = 1;
        /// <summary>Sheet name used for PDF bookmarks / page labels.</summary>
        public string SheetName { get; set; } = "";

        private readonly List<SheetPrimitive> _primitives = new();

        /// <summary>Vector record of everything drawn, in sheet millimetres (y down).</summary>
        public IReadOnlyList<SheetPrimitive> Primitives => _primitives;

        public static double FontSizeFor(double capHeight) => capHeight / 0.72;
        public static FontFamily FontFamily => Font;

        public DrawingSheet(double width = A4Width, double height = A4Height)
        {
            Width = width;
            Height = height;
            Canvas = new Canvas
            {
                Width = width,
                Height = height,
                Background = Brushes.White,
                ClipToBounds = true
            };
        }

        // ---------------------------------------------------------------- styles
        private static void ApplyStyle(Shape s, LineKind kind)
        {
            s.Stroke = Ink;
            s.StrokeLineJoin = PenLineJoin.Miter;
            double t;
            switch (kind)
            {
                case LineKind.Border: t = 0.7; break;
                case LineKind.Visible: t = 0.5; break;
                case LineKind.Hidden:
                    t = 0.35;
                    s.StrokeDashArray = Dashes(t, 2.5, 1.0);
                    break;
                case LineKind.Center:
                    t = 0.25;
                    s.StrokeDashArray = Dashes(t, 8, 1.2, 1.0, 1.2);
                    break;
                default: t = 0.25; break;
            }
            s.StrokeThickness = t;
        }

        /// <summary>WPF dash arrays are in multiples of stroke thickness; convert from mm.</summary>
        private static DoubleCollection Dashes(double thickness, params double[] mm)
            => new(mm.Select(v => v / thickness));

        private static Brush CreateHatch()
        {
            var drawing = new GeometryDrawing(null, new Pen(Brushes.Black, 0.15),
                new LineGeometry(new Point(0, 2), new Point(2, 0)));
            var brush = new DrawingBrush(drawing)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 2, 2),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, 2, 2),
                ViewboxUnits = BrushMappingMode.Absolute
            };
            brush.Freeze();
            return brush;
        }

        // ------------------------------------------------------------ primitives
        public void Line(Point a, Point b, LineKind kind = LineKind.Visible)
        {
            var l = new Line { X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y };
            ApplyStyle(l, kind);
            Canvas.Children.Add(l);
            _primitives.Add(new PolyPrimitive(new[] { a, b }, false, kind, false));
        }

        public void Line(double x1, double y1, double x2, double y2, LineKind kind = LineKind.Visible)
            => Line(new Point(x1, y1), new Point(x2, y2), kind);

        public void Poly(IEnumerable<Point> points, LineKind kind = LineKind.Visible, bool closed = false, bool hatch = false)
        {
            var array = points.ToArray();
            var pts = new PointCollection(array);
            Shape s = closed ? new Polygon { Points = pts } : new Polyline { Points = pts };
            ApplyStyle(s, kind);
            if (hatch) s.Fill = HatchBrush;
            Canvas.Children.Add(s);
            _primitives.Add(new PolyPrimitive(array, closed, kind, hatch));
        }

        public void Rect(double x, double y, double w, double h, LineKind kind = LineKind.Visible)
            => Poly(new[] { new Point(x, y), new Point(x + w, y), new Point(x + w, y + h), new Point(x, y + h) }, kind, true);

        public void Circle(Point c, double r, LineKind kind = LineKind.Visible)
        {
            var e = new Ellipse { Width = 2 * r, Height = 2 * r };
            ApplyStyle(e, kind);
            Canvas.SetLeft(e, c.X - r);
            Canvas.SetTop(e, c.Y - r);
            Canvas.Children.Add(e);
            // WPF strokes an Ellipse inside its bounds; record the stroke centre-line radius.
            _primitives.Add(new CirclePrimitive(c, Math.Max(0, r - e.StrokeThickness / 2), kind));
        }

        /// <summary>Arc with angles in degrees, counter-clockwise as seen on the sheet (0° = +x).</summary>
        public void Arc(Point c, double r, double startDeg, double sweepDeg, LineKind kind = LineKind.Visible)
        {
            Point At(double deg)
            {
                double a = deg * Math.PI / 180;
                return new Point(c.X + r * Math.Cos(a), c.Y - r * Math.Sin(a));
            }

            var fig = new PathFigure { StartPoint = At(startDeg), IsClosed = false };
            fig.Segments.Add(new ArcSegment(At(startDeg + sweepDeg), new Size(r, r), 0,
                Math.Abs(sweepDeg) > 180,
                sweepDeg > 0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true));
            var path = new Path { Data = new PathGeometry(new[] { fig }) };
            ApplyStyle(path, kind);
            Canvas.Children.Add(path);
            _primitives.Add(new ArcPrimitive(c, r, startDeg, sweepDeg, kind));
        }

        public static IEnumerable<Point> RegularPolygon(Point c, double circumRadius, int sides, double startDeg)
        {
            for (int i = 0; i < sides; i++)
            {
                double a = (startDeg + i * 360.0 / sides) * Math.PI / 180;
                yield return new Point(c.X + circumRadius * Math.Cos(a), c.Y - circumRadius * Math.Sin(a));
            }
        }

        /// <summary>Center-line cross for circular views.</summary>
        public void CenterCross(Point c, double r)
        {
            Line(c.X - r - 3, c.Y, c.X + r + 3, c.Y, LineKind.Center);
            Line(c.X, c.Y - r - 3, c.X, c.Y + r + 3, LineKind.Center);
        }

        /// <summary>
        /// Text; <paramref name="y"/> is the vertical centre of the text line.
        /// Height is the ISO letter (cap) height in mm.
        /// </summary>
        public Size Text(string text, double x, double y, double height = TextHeight,
                         TextAlign align = TextAlign.Left, double angleDeg = 0, bool bold = false)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontFamily = Font,
                FontSize = height / 0.72,
                Foreground = Ink,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal
            };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var size = tb.DesiredSize;

            double left = align switch
            {
                TextAlign.Center => x - size.Width / 2,
                TextAlign.Right => x - size.Width,
                _ => x
            };
            Canvas.SetLeft(tb, left);
            Canvas.SetTop(tb, y - size.Height / 2);
            if (Math.Abs(angleDeg) > 1e-9)
            {
                // Rotate about the anchor point.
                tb.RenderTransform = new RotateTransform(angleDeg, x - left, size.Height / 2);
            }
            Canvas.Children.Add(tb);
            _primitives.Add(new TextPrimitive(text, left, y - size.Height / 2, new Point(x, y),
                tb.FontSize, bold, angleDeg));
            return size;
        }

        // ------------------------------------------------------------ dimensions
        /// <summary>Filled arrowhead with its tip at <paramref name="tip"/>, pointing along <paramref name="dir"/>.</summary>
        public void Arrow(Point tip, Vector dir)
        {
            dir.Normalize();
            var n = new Vector(-dir.Y, dir.X);
            var bas = tip - dir * 2.5;
            var points = new[] { tip, bas + n * 0.45, bas - n * 0.45 };
            var poly = new Polygon
            {
                Points = new PointCollection(points),
                Fill = Ink,
                Stroke = Ink,
                StrokeThickness = 0.1
            };
            Canvas.Children.Add(poly);
            _primitives.Add(new ArrowPrimitive(points));
        }

        /// <summary>
        /// Horizontal or vertical linear dimension between feature points
        /// <paramref name="a"/> and <paramref name="b"/>. <paramref name="linePos"/> is the
        /// y (horizontal) or x (vertical) of the dimension line.
        /// </summary>
        public void Dim(Point a, Point b, bool horizontal, double linePos, string text)
        {
            Point da = horizontal ? new Point(a.X, linePos) : new Point(linePos, a.Y);
            Point db = horizontal ? new Point(b.X, linePos) : new Point(linePos, b.Y);

            ExtensionLine(a, da, horizontal);
            ExtensionLine(b, db, horizontal);

            // Order so text reads left-to-right / bottom-to-top.
            if (horizontal ? da.X > db.X : da.Y < db.Y) (da, db) = (db, da);
            Vector? outward = null;
            if (!horizontal)
                outward = new Vector(linePos >= a.X ? 1 : -1, 0);
            DimLine(da, db, text, outward);
        }

        private void ExtensionLine(Point feature, Point onDimLine, bool horizontal)
        {
            double delta = horizontal ? onDimLine.Y - feature.Y : onDimLine.X - feature.X;
            if (Math.Abs(delta) < 0.5) return;
            double s = Math.Sign(delta);
            var dir = horizontal ? new Vector(0, s) : new Vector(s, 0);
            Line(feature + dir * 0.8, onDimLine + dir * 1.5, LineKind.Thin);
        }

        /// <summary>Dimension line with arrows and text placed above it.</summary>
        /// <param name="outward">For vertical dimensions: side away from the feature, used to
        /// place horizontal text when it does not fit along the line.</param>
        public void DimLine(Point da, Point db, string text, Vector? outward = null)
        {
            var u = db - da;
            double len = u.Length;
            if (len < 1e-6) return;
            u /= len;

            bool inside = len >= 8;
            if (inside)
            {
                Line(da, db, LineKind.Thin);
                Arrow(da, -u);
                Arrow(db, u);
            }
            else
            {
                Line(da - u * 5, db + u * 5, LineKind.Thin);
                Arrow(da, u);
                Arrow(db, -u);
            }

            double ang = Math.Atan2(u.Y, u.X) * 180 / Math.PI;
            if (ang >= 90) ang -= 180;
            if (ang < -90) ang += 180;
            double rad = ang * Math.PI / 180;
            var up = new Vector(Math.Sin(rad), -Math.Cos(rad));
            var along = new Vector(Math.Cos(rad), Math.Sin(rad));

            Point mid = da + (db - da) / 2;
            double estWidth = text.Length * TextHeight * 0.7;
            bool fits = inside && estWidth <= len - 3;
            if (!fits && outward is Vector o)
            {
                // Short vertical dimension: horizontal text beside the line, away from the part.
                Text(text, mid.X + o.X * 1.5, mid.Y, TextHeight, o.X > 0 ? TextAlign.Left : TextAlign.Right);
                return;
            }
            if (!fits)
            {
                // Not enough room: place text beyond the end of the dimension line.
                var end = (db - da) * along >= 0 ? db : da;
                mid = end + along * (6 + estWidth / 2);
            }
            var pos = mid + up * (0.6 + TextHeight * 0.8);
            Text(text, pos.X, pos.Y, TextHeight, TextAlign.Center, ang);
        }

        /// <summary>Leader line with arrow at the target and a text label at the elbow.</summary>
        public void Leader(Point target, Point elbow, string text, bool textRight = true)
        {
            Line(target, elbow, LineKind.Thin);
            Arrow(target, target - elbow);
            double shoulder = Math.Max(6, text.Length * TextHeight * 0.68);
            var end = new Point(elbow.X + (textRight ? shoulder : -shoulder), elbow.Y);
            Line(elbow, end, LineKind.Thin);
            Text(text, textRight ? elbow.X + 0.5 : elbow.X - 0.5, elbow.Y - TextHeight * 0.8, TextHeight,
                 textRight ? TextAlign.Left : TextAlign.Right);
        }
    }

    // ---- Vector record of a sheet (sheet mm, origin top-left, y down) ----
    public abstract record SheetPrimitive;
    public sealed record PolyPrimitive(Point[] Points, bool Closed, LineKind Kind, bool Hatch) : SheetPrimitive;
    public sealed record CirclePrimitive(Point Center, double Radius, LineKind Kind) : SheetPrimitive;
    /// <summary>Angles in degrees, counter-clockwise as seen on the sheet.</summary>
    public sealed record ArcPrimitive(Point Center, double Radius, double StartDeg, double SweepDeg, LineKind Kind) : SheetPrimitive;
    public sealed record ArrowPrimitive(Point[] Points) : SheetPrimitive;
    /// <summary>Text laid out like the WPF TextBlock: box top-left, rotated about <paramref name="Anchor"/>.</summary>
    public sealed record TextPrimitive(string Text, double Left, double Top, Point Anchor,
                                       double FontSize, bool Bold, double AngleDeg) : SheetPrimitive;

    /// <summary>Maps model coordinates (x along the bolt axis, y up, mm) onto the sheet.</summary>
    public readonly record struct ViewMap(double OriginX, double OriginY, double Scale)
    {
        public Point P(double x, double y) => new(OriginX + x * Scale, OriginY - y * Scale);
        public double L(double modelLength) => modelLength * Scale;
    }
}
