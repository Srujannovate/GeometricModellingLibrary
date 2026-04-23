using System.Collections.Generic;
using System.Numerics;
using System.Windows.Media;
using GMLModel.Pmi;
using HT = HelixToolkit;
using Hx = HelixToolkit.Wpf.SharpDX;
using HxSharpDX = HelixToolkit.SharpDX;

namespace GML_WPF.Pmi
{
    /// <summary>
    /// Converts PMI annotations into HelixToolkit SharpDX scene primitives
    /// (billboard text + leader lines + feature control frame composites).
    /// Each call returns the created elements as a flat list plus the logical
    /// attachment point used for centering / saved-view fade-in effects.
    /// </summary>
    internal static class PmiPrimitiveBuilders
    {
        public static IList<Hx.Element3D> Build(PmiAnnotation ann, double sceneScale)
        {
            var list = new List<Hx.Element3D>();

            // --- Leader polyline (if any) ---
            if (ann.LeaderPolyline != null && ann.LeaderPolyline.Count >= 2)
            {
                list.Add(BuildLeader(ann.LeaderPolyline, sceneScale, ann.Kind));
            }
            else if (ann.AttachPoint != ann.TextPosition && ann.TextPosition != Vector3.Zero)
            {
                list.Add(BuildLeader(new List<Vector3> { ann.AttachPoint, ann.TextPosition }, sceneScale, ann.Kind));
            }

            // --- Billboard text (Display/FormattedText) ---
            var billboardText = ann.DisplayText;
            if (string.IsNullOrWhiteSpace(billboardText))
                billboardText = ann.Kind.ToString();

            var textPos = ann.TextPosition != Vector3.Zero ? ann.TextPosition : ann.AttachPoint;
            textPos *= (float)sceneScale;

            var billboard = new Hx.BillboardTextModel3D
            {
                Geometry = new HxSharpDX.BillboardSingleText3D
                {
                    TextInfo = new HxSharpDX.TextInfo(billboardText, textPos)
                    {
                        Scale = 0.4f,
                    },
                },
                FixedSize = true,
            };
            list.Add(billboard);

            // --- Composite: Feature Control Frame (draw a small outlined rectangle under the text) ---
            if (ann is GeometricTolerance)
            {
                list.Add(BuildFcfBox(textPos));
            }

            // --- Datum letter is already rendered via the billboard, but add a triangular flag ---
            if (ann is Datum || ann is DatumTarget)
            {
                list.Add(BuildDatumFlag(ann.AttachPoint * (float)sceneScale, textPos));
            }

            return list;
        }

        private static Hx.LineGeometryModel3D BuildLeader(IList<Vector3> polyline, double sceneScale, PmiKind kind)
        {
            var positions = new HT.Vector3Collection(polyline.Count);
            foreach (var p in polyline) positions.Add(p * (float)sceneScale);

            var indices = new HT.IntCollection((polyline.Count - 1) * 2);
            for (int i = 0; i < polyline.Count - 1; i++) { indices.Add(i); indices.Add(i + 1); }

            return new Hx.LineGeometryModel3D
            {
                Geometry = new HxSharpDX.LineGeometry3D { Positions = positions, Indices = indices },
                Color = ColorFor(kind),
                Thickness = 1.5,
                DepthBias = -50,
            };
        }

        private static Hx.LineGeometryModel3D BuildFcfBox(Vector3 center)
        {
            // Simple small rectangle in screen-parallel XY (billboard-ish approximation).
            // Exact FCF cell layout can be added later; this primitive makes the frame visible.
            float w = 4f, h = 1.4f;
            var positions = new HT.Vector3Collection
            {
                new Vector3(center.X - w, center.Y - h, center.Z),
                new Vector3(center.X + w, center.Y - h, center.Z),
                new Vector3(center.X + w, center.Y + h, center.Z),
                new Vector3(center.X - w, center.Y + h, center.Z),
            };
            var indices = new HT.IntCollection { 0,1, 1,2, 2,3, 3,0 };
            return new Hx.LineGeometryModel3D
            {
                Geometry = new HxSharpDX.LineGeometry3D { Positions = positions, Indices = indices },
                Color = Colors.Cyan,
                Thickness = 1.0,
                DepthBias = -60,
            };
        }

        private static Hx.LineGeometryModel3D BuildDatumFlag(Vector3 attach, Vector3 text)
        {
            var positions = new HT.Vector3Collection
            {
                attach,
                text,
                new Vector3(text.X - 1f, text.Y - 1f, text.Z),
                new Vector3(text.X + 1f, text.Y - 1f, text.Z),
                text,
            };
            var indices = new HT.IntCollection { 0,1, 2,3, 2,4, 3,4 };
            return new Hx.LineGeometryModel3D
            {
                Geometry = new HxSharpDX.LineGeometry3D { Positions = positions, Indices = indices },
                Color = Colors.LightGreen,
                Thickness = 1.5,
                DepthBias = -60,
            };
        }

        private static Color ColorFor(PmiKind kind) => kind switch
        {
            PmiKind.LinearDimension    => Colors.White,
            PmiKind.AngularDimension   => Colors.White,
            PmiKind.RadialDimension    => Colors.White,
            PmiKind.GeometricTolerance => Colors.Cyan,
            PmiKind.SurfaceFinish      => Colors.Orange,
            PmiKind.Datum              => Colors.LightGreen,
            PmiKind.DatumTarget        => Colors.LightGreen,
            PmiKind.WeldSymbol         => Colors.Red,
            _                          => Colors.Yellow,
        };
    }
}
