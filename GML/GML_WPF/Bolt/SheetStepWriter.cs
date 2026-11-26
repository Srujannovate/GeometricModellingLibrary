using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace GML_WPF.Bolt
{
    /// <summary>
    /// Writes 2D drawing sheets to a STEP AP214 (ISO 10303-21) file as wireframe
    /// geometry in millimetres. OCCT is not used, so this works independently of
    /// the OCCT file I/O.
    ///
    /// Layout:
    /// <list type="bullet">
    /// <item>Each sheet is its own product (part) named "&lt;drawing no.&gt; Sheet n - &lt;name&gt;".</item>
    /// <item>Sheets lie in the XY plane (z = 0), side by side along +X, so they don't overlap when imported together.</item>
    /// <item>Curves are grouped by line type (visible, thin, hidden, centre, border, text, hatch)
    ///       in separate geometric curve sets, each styled with its ISO line font and width.</item>
    /// <item>Text is converted to glyph outline polylines so every CAD system shows it.</item>
    /// </list>
    /// </summary>
    public static class SheetStepWriter
    {
        /// <summary>Gap between sheets along X, in mm.</summary>
        public const double SheetGap = 20;

        private const double FlattenTolerance = 0.01; // mm, for glyph outlines
        private const double HatchSpacing = 2;        // matches the on-screen hatch tile

        private enum Layer { Visible, Thin, Hidden, Center, Border, Text, Hatch }

        private static readonly (Layer layer, string font, double width)[] LayerStyles =
        {
            (Layer.Border,  "continuous", 0.7),
            (Layer.Visible, "continuous", 0.5),
            (Layer.Hidden,  "dashed",     0.35),
            (Layer.Center,  "chain",      0.25),
            (Layer.Thin,    "continuous", 0.25),
            (Layer.Text,    "continuous", 0.13),
            (Layer.Hatch,   "continuous", 0.15),
        };

        public static void Write(IReadOnlyList<DrawingSheet> sheets, string path, string drawingNumber, string author)
        {
            if (sheets.Count == 0) throw new ArgumentException("No sheets to export.", nameof(sheets));

            var w = new StepBuilder();

            // ---- Shared contexts and units
            int appCtx = w.Add("APPLICATION_CONTEXT('automotive_design')");
            w.Add($"APPLICATION_PROTOCOL_DEFINITION('international standard','automotive_design',2000,{R(appCtx)})");
            int prodCtx = w.Add($"PRODUCT_CONTEXT('',{R(appCtx)},'mechanical')");
            int pdCtx = w.Add($"PRODUCT_DEFINITION_CONTEXT('part definition',{R(appCtx)},'design')");
            int mm = w.Add("(LENGTH_UNIT()NAMED_UNIT(*)SI_UNIT(.MILLI.,.METRE.))");
            int rad = w.Add("(NAMED_UNIT(*)PLANE_ANGLE_UNIT()SI_UNIT($,.RADIAN.))");
            int sr = w.Add("(NAMED_UNIT(*)SI_UNIT($,.STERADIAN.)SOLID_ANGLE_UNIT())");
            int unc = w.Add($"UNCERTAINTY_MEASURE_WITH_UNIT(LENGTH_MEASURE(1.E-07),{R(mm)},'distance_accuracy_value','confusion accuracy')");
            int ctx = w.Add($"(GEOMETRIC_REPRESENTATION_CONTEXT(3)GLOBAL_UNCERTAINTY_ASSIGNED_CONTEXT(({R(unc)}))" +
                            $"GLOBAL_UNIT_ASSIGNED_CONTEXT(({R(mm)},{R(rad)},{R(sr)}))" +
                            "REPRESENTATION_CONTEXT('Context #1','3D Context with UNIT and UNCERTAINTY'))");
            int dirZ = w.Add("DIRECTION('',(0.,0.,1.))");
            int dirX = w.Add("DIRECTION('',(1.,0.,0.))");
            int black = w.Add("DRAUGHTING_PRE_DEFINED_COLOUR('black')");

            // One presentation style per layer, shared by all sheets.
            var styleByLayer = new Dictionary<Layer, int>();
            foreach (var (layer, font, width) in LayerStyles)
            {
                int f = w.Add($"DRAUGHTING_PRE_DEFINED_CURVE_FONT('{font}')");
                int cs = w.Add($"CURVE_STYLE('{layer.ToString().ToLowerInvariant()}',{R(f)},POSITIVE_LENGTH_MEASURE({N(width)}),{R(black)})");
                styleByLayer[layer] = w.Add($"PRESENTATION_STYLE_ASSIGNMENT(({R(cs)}))");
            }

            var styledItems = new List<int>();
            var sheetSummaries = new List<string>();

            for (int i = 0; i < sheets.Count; i++)
            {
                var sheet = sheets[i];
                double offsetX = i * (DrawingSheet.A4Width + SheetGap);
                string name = $"{drawingNumber} Sheet {sheet.SheetNumber} - {sheet.SheetName}".Trim();

                // ---- Product structure
                int product = w.Add($"PRODUCT({S($"{drawingNumber}-S{sheet.SheetNumber}")},{S(name)},{S($"Drawing sheet {sheet.SheetNumber}/{sheet.SheetCount}, A4 landscape, mm")},({R(prodCtx)}))");
                w.Add($"PRODUCT_RELATED_PRODUCT_CATEGORY('part',$,({R(product)}))");
                int pdf = w.Add($"PRODUCT_DEFINITION_FORMATION('','',{R(product)})");
                int pd = w.Add($"PRODUCT_DEFINITION('design','',{R(pdf)},{R(pdCtx)})");
                int pds = w.Add($"PRODUCT_DEFINITION_SHAPE('','',{R(pd)})");

                // ---- Geometry, grouped by layer
                var curves = Enum.GetValues<Layer>().ToDictionary(l => l, _ => new List<int>());
                var geo = new SheetGeometry(w, offsetX, sheet.Height, dirZ, dirX);
                foreach (var p in sheet.Primitives)
                    geo.Emit(p, curves);

                // The placement must be the global origin: the sheet offset is already in
                // the coordinates, and readers apply this placement on top of them.
                int origin = w.Add($"AXIS2_PLACEMENT_3D('',{R(w.Add("CARTESIAN_POINT('',(0.,0.,0.))"))},{R(dirZ)},{R(dirX)})");
                // One edge set per sheet: OCCT's XDE reader (FreeCAD, and the GML PMI
                // loader) drops the whole part when a wireframe model has several sets.
                // Line types are kept as per-edge styles instead.
                var allEdges = new List<int>();
                foreach (var (layer, _, _) in LayerStyles)
                {
                    foreach (var edge in curves[layer])
                    {
                        allEdges.Add(edge);
                        styledItems.Add(w.Add($"STYLED_ITEM({S(layer.ToString().ToLowerInvariant())},({R(styleByLayer[layer])}),{R(edge)})"));
                    }
                }
                int curveCount = allEdges.Count;
                if (curveCount == 0) continue;

                int edgeSet = w.Add($"CONNECTED_EDGE_SET({S(name)},({string.Join(",", allEdges.Select(R))}))");
                int model = w.Add($"EDGE_BASED_WIREFRAME_MODEL({S(name)},({R(edgeSet)}))");
                int rep = w.Add($"EDGE_BASED_WIREFRAME_SHAPE_REPRESENTATION({S(name)},({R(origin)},{R(model)}),{R(ctx)})");
                w.Add($"SHAPE_DEFINITION_REPRESENTATION({R(pds)},{R(rep)})");
                sheetSummaries.Add($"{name}: {curveCount} curves");
            }

            w.Add($"MECHANICAL_DESIGN_GEOMETRIC_PRESENTATION_REPRESENTATION('',({string.Join(",", styledItems.Select(R))}),{R(ctx)})");

            // ---- File
            var sb = new StringBuilder();
            sb.Append("ISO-10303-21;\n");
            sb.Append("HEADER;\n");
            sb.Append($"FILE_DESCRIPTION(({S("Bolt manufacturing drawing set - 2D sheets (wireframe)")}),'2;1');\n");
            sb.Append($"FILE_NAME({S(Path.GetFileName(path))},{S(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture))}," +
                      $"({S(author)}),(''),'GML bolt drawing generator','GML','');\n");
            sb.Append("FILE_SCHEMA(('AUTOMOTIVE_DESIGN { 1 0 10303 214 1 1 1 1 }'));\n");
            sb.Append("ENDSEC;\n");
            sb.Append("DATA;\n");
            w.WriteTo(sb);
            sb.Append("ENDSEC;\n");
            sb.Append("END-ISO-10303-21;\n");

            // Write next to the target first so a failure never leaves a partial file.
            var full = Path.GetFullPath(path);
            var temp = Path.Combine(Path.GetDirectoryName(full)!, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
                File.Move(temp, full, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        // ------------------------------------------------------------------ geometry
        private sealed class SheetGeometry
        {
            private readonly StepBuilder _w;
            private readonly double _ox, _height;
            private readonly int _dirZ, _dirX;

            public SheetGeometry(StepBuilder w, double offsetX, double height, int dirZ, int dirX)
            {
                _w = w; _ox = offsetX; _height = height; _dirZ = dirZ; _dirX = dirX;
            }

            /// <summary>Sheet point (mm, y down) to STEP point (mm, y up, z = 0).</summary>
            public int Point(Point p)
                => _w.Add($"CARTESIAN_POINT('',({N(p.X + _ox)},{N(_height - p.Y)},0.))");

            /// <summary>Points closer than this are merged (readers reject zero-length segments).</summary>
            private const double MinSegment = 1e-3;

            private int Polyline(IReadOnlyList<Point> pts, bool closed)
            {
                // Drop consecutive duplicates, judged on the coordinates as written to the file.
                var kept = new List<Point>(pts.Count + 1);
                foreach (var p in pts)
                {
                    var q = new Point(Math.Round(p.X, 6), Math.Round(p.Y, 6));
                    if (kept.Count > 0 && (q - kept[^1]).Length < MinSegment) continue;
                    kept.Add(q);
                }

                if (closed)
                {
                    // A closing point equal to the start would add a zero-length segment.
                    while (kept.Count > 1 && (kept[^1] - kept[0]).Length < MinSegment)
                        kept.RemoveAt(kept.Count - 1);
                    if (kept.Count < 3) closed = false;
                }
                if (kept.Count < 2) return -1;

                var ids = kept.Select(Point).ToList();
                if (closed) ids.Add(ids[0]);
                int polyline = _w.Add($"POLYLINE('',({string.Join(",", ids.Select(R))}))");
                int v1 = Vertex(ids[0]);
                int v2 = closed ? v1 : Vertex(ids[^1]);
                return Edge(v1, v2, polyline);
            }

            private int Vertex(int pointId) => _w.Add($"VERTEX_POINT('',{R(pointId)})");

            /// <summary>
            /// Topological edge. XDE-based importers (FreeCAD, many CAD systems) ignore
            /// loose curves, but accept edges in an edge-based wireframe model.
            /// </summary>
            private int Edge(int v1, int v2, int curve)
                => _w.Add($"EDGE_CURVE('',{R(v1)},{R(v2)},{R(curve)},.T.)");

            private int Circle(Point c, double r)
            {
                int axis = _w.Add($"AXIS2_PLACEMENT_3D('',{R(Point(c))},{R(_dirZ)},{R(_dirX)})");
                return _w.Add($"CIRCLE('',{R(axis)},{N(r)})");
            }

            private int FullCircleEdge(Point c, double r)
            {
                int circle = Circle(c, r);
                int v = Vertex(Point(new Point(c.X + r, c.Y)));
                return Edge(v, v, circle);
            }

            public void Emit(SheetPrimitive p, Dictionary<Layer, List<int>> curves)
            {
                switch (p)
                {
                    case PolyPrimitive poly:
                    {
                        if (poly.Hatch)
                            foreach (var seg in HatchSegments(poly.Points))
                                Add(curves, Layer.Hatch, Polyline(seg, false));
                        Add(curves, ToLayer(poly.Kind), Polyline(poly.Points, poly.Closed));
                        break;
                    }
                    case ArrowPrimitive arrow:
                        Add(curves, Layer.Thin, Polyline(arrow.Points, true));
                        break;
                    case CirclePrimitive c when c.Radius > 1e-6:
                        Add(curves, ToLayer(c.Kind), FullCircleEdge(c.Center, c.Radius));
                        break;
                    case ArcPrimitive a when a.Radius > 1e-6 && Math.Abs(a.SweepDeg) > 1e-9:
                    {
                        // Sheet angles are CCW with y flipped, i.e. standard CCW in STEP (y up).
                        double s = a.StartDeg, e = a.StartDeg + a.SweepDeg;
                        if (a.SweepDeg < 0) (s, e) = (e, s);
                        int circle = Circle(a.Center, a.Radius);
                        int p1 = Point(ArcPoint(a.Center, a.Radius, s));
                        int p2 = Point(ArcPoint(a.Center, a.Radius, e));
                        // The circle runs CCW about +Z, so the edge from p1 to p2 follows it (.T.).
                        Add(curves, ToLayer(a.Kind), Edge(Vertex(p1), Vertex(p2), circle));
                        break;
                    }
                    case TextPrimitive t when !string.IsNullOrWhiteSpace(t.Text):
                        foreach (var (pts, closed) in TextOutlines(t))
                            Add(curves, Layer.Text, Polyline(pts, closed));
                        break;
                }
            }

            private static void Add(Dictionary<Layer, List<int>> curves, Layer layer, int id)
            {
                if (id > 0) curves[layer].Add(id);
            }

            private static Point ArcPoint(Point c, double r, double deg)
            {
                double a = deg * Math.PI / 180;
                return new Point(c.X + r * Math.Cos(a), c.Y - r * Math.Sin(a));
            }

            private static double Normalize(double a)
            {
                a %= 2 * Math.PI;
                return a < 0 ? a + 2 * Math.PI : a;
            }

            private static Layer ToLayer(LineKind k) => k switch
            {
                LineKind.Visible => Layer.Visible,
                LineKind.Hidden => Layer.Hidden,
                LineKind.Center => Layer.Center,
                LineKind.Border => Layer.Border,
                _ => Layer.Thin
            };
        }

        /// <summary>
        /// 45° hatch lines (x + y = const in sheet coordinates, same as the on-screen
        /// hatch brush) clipped to a polygon with the even-odd rule.
        /// </summary>
        private static IEnumerable<Point[]> HatchSegments(Point[] poly)
        {
            if (poly.Length < 3) yield break;
            double min = poly.Min(p => p.X + p.Y), max = poly.Max(p => p.X + p.Y);
            for (double c = Math.Ceiling(min / HatchSpacing) * HatchSpacing; c < max; c += HatchSpacing)
            {
                var hits = new List<Point>();
                for (int i = 0; i < poly.Length; i++)
                {
                    var a = poly[i];
                    var b = poly[(i + 1) % poly.Length];
                    double fa = a.X + a.Y - c, fb = b.X + b.Y - c;
                    if ((fa > 0) == (fb > 0)) continue;
                    double t = fa / (fa - fb);
                    hits.Add(a + (b - a) * t);
                }
                hits.Sort((p, q) => p.X.CompareTo(q.X));
                for (int i = 0; i + 1 < hits.Count; i += 2)
                    if ((hits[i + 1] - hits[i]).Length > 1e-6)
                        yield return new[] { hits[i], hits[i + 1] };
            }
        }

        /// <summary>Glyph outlines laid out exactly like the WPF TextBlock on the sheet.</summary>
        private static IEnumerable<(List<Point> pts, bool closed)> TextOutlines(TextPrimitive t)
        {
            var typeface = new Typeface(DrawingSheet.FontFamily, FontStyles.Normal,
                t.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            var ft = new FormattedText(t.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, t.FontSize, Brushes.Black, 1.0);
            var geometry = ft.BuildGeometry(new Point(t.Left, t.Top));
            if (Math.Abs(t.AngleDeg) > 1e-9)
                geometry.Transform = new RotateTransform(t.AngleDeg, t.Anchor.X, t.Anchor.Y);

            var flat = geometry.GetFlattenedPathGeometry(FlattenTolerance, ToleranceType.Absolute);
            foreach (var fig in flat.Figures)
            {
                var pts = new List<Point> { fig.StartPoint };
                foreach (var seg in fig.Segments)
                {
                    switch (seg)
                    {
                        case LineSegment ls: pts.Add(ls.Point); break;
                        case PolyLineSegment pls: pts.AddRange(pls.Points); break;
                    }
                }
                if (pts.Count >= 2) yield return (pts, fig.IsClosed || pts.Count > 2);
            }
        }

        // ------------------------------------------------------------------- output
        private sealed class StepBuilder
        {
            private readonly List<string> _entities = new();
            public int Add(string body)
            {
                _entities.Add(body);
                return _entities.Count;
            }

            public void WriteTo(StringBuilder sb)
            {
                for (int i = 0; i < _entities.Count; i++)
                    sb.Append('#').Append(i + 1).Append('=').Append(_entities[i]).Append(";\n");
            }
        }

        private static string R(int id) => "#" + id.ToString(CultureInfo.InvariantCulture);

        /// <summary>STEP real: always contains a decimal point.</summary>
        private static string N(double v)
        {
            if (Math.Abs(v) < 1e-12) v = 0;
            var s = v.ToString("0.0#####", CultureInfo.InvariantCulture);
            return s;
        }

        /// <summary>STEP string literal (ISO 10303-21 encoding for non-ASCII).</summary>
        private static string S(string? value)
        {
            var sb = new StringBuilder("'");
            foreach (char ch in value ?? "")
            {
                if (ch == '\'') sb.Append("''");
                else if (ch == '\\') sb.Append("\\\\");
                else if (ch >= 32 && ch < 127) sb.Append(ch);
                else if (ch >= 32) sb.Append("\\X2\\").Append(((int)ch).ToString("X4")).Append("\\X0\\");
            }
            return sb.Append('\'').ToString();
        }
    }
}
