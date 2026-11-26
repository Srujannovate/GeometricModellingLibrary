using System.IO;

namespace GML_WPF.Bolt
{
    /// <summary>
    /// Builds the 3D bolt described by a <see cref="BoltSpec"/> as an OCCT solid and
    /// writes it to a STEP file (AP214, millimetres).
    ///
    /// Model frame: the bolt axis is Z. The bearing face lies on z = 0, the head
    /// occupies z = 0..k and the shank runs down to z = -(length below the head).
    ///
    /// The thread is modelled as a plain cylinder at the nominal diameter with a
    /// 45-degree lead-in chamfer down to the minor diameter. No helical thread is
    /// generated, which is the usual "cosmetic thread" convention; the thread
    /// designation lives on the drawings.
    /// </summary>
    public static class BoltSolidBuilder
    {
        private static readonly double Tan30 = Math.Tan(Math.PI / 6);
        private static readonly double Tan59 = Math.Tan(59 * Math.PI / 180);

        // ------------------------------------------------------------------ public API
        public static Occt.TopoDS_Shape Build(BoltSpec spec)
        {
            var errors = spec.Validate();
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(" ", errors));

            GMLModel.OcctConfiguration.Configure();

            double k = spec.HeadHeight;
            double r = spec.D / 2;
            double r3 = spec.MinorDiameter / 2;
            double shankLength = spec.TipX - k;                 // below the bearing face
            double chamfer = Math.Min(r - r3, shankLength / 2); // lead-in at the tip
            double R = spec.HeadWidth / 2;

            Occt.TopoDS_Shape body;
            switch (spec.HeadType)
            {
                case BoltHeadType.SocketCap:
                {
                    double ch = 0.06 * spec.HeadWidth;
                    body = Revolve(
                        (0, k), (R - ch, k), (R, k - ch), (R, 0),
                        (r, 0), (r, -(shankLength - chamfer)), (r - chamfer, -shankLength), (0, -shankLength));
                    break;
                }
                case BoltHeadType.Countersunk:
                    body = Revolve(
                        (0, k), (R, k), (r, 0),
                        (r, -(shankLength - chamfer)), (r - chamfer, -shankLength), (0, -shankLength));
                    break;
                default:
                {
                    var head = BuildHead(spec);
                    var shank = Revolve(
                        (0, 0), (r, 0), (r, -(shankLength - chamfer)), (r - chamfer, -shankLength), (0, -shankLength));
                    body = Fuse(head, shank);

                    if (spec.HeadType == BoltHeadType.Carriage)
                    {
                        // Square neck under the dome, side length = thread diameter.
                        var neck = new Occt.BRepPrimAPI_MakeBox(
                            Pnt(-r, -r, -spec.NeckLength), spec.D, spec.D, spec.NeckLength).Shape;
                        body = Fuse(body, neck);
                    }
                    break;
                }
            }

            if (spec.HasSocket)
                body = Cut(body, BuildSocketTool(spec));

            if (body == null || body.IsNull)
                throw new InvalidOperationException("The bolt solid could not be generated.");
            if (!new Occt.BRepCheck_Analyzer(body).IsValid())
                throw new InvalidOperationException("The generated bolt solid failed OCCT validation.");
            return body;
        }

        /// <summary>Builds the bolt and writes it to <paramref name="path"/> as STEP.</summary>
        public static void ExportStep(BoltSpec spec, string path)
        {
            var shape = Build(spec);

            // The writer's constructor registers the STEP parameters; set them afterwards.
            var writer = new Occt.STEPControl_Writer();
            Occt.Interface_Static.SetCVal("write.step.unit", "MM");
            var status = writer.Transfer(shape, Occt.STEPControl_StepModelType.STEPControl_AsIs);
            if (status != Occt.IFSelect_ReturnStatus.IFSelect_RetDone)
                throw new InvalidOperationException($"STEP transfer failed (status={status}).");

            // Write beside the target first so a failed export never leaves a broken file behind.
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var temp = Path.Combine(dir, $".{Path.GetFileNameWithoutExtension(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                status = writer.Write(temp);
                if (status != Occt.IFSelect_ReturnStatus.IFSelect_RetDone)
                    throw new InvalidOperationException($"STEP write failed (status={status}).");
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        // ---------------------------------------------------------------------- heads
        private static Occt.TopoDS_Shape BuildHead(BoltSpec spec)
        {
            double k = spec.HeadHeight;
            double s = spec.HeadWidth;

            switch (spec.HeadType)
            {
                case BoltHeadType.Hex:
                {
                    double corner = s / Math.Sqrt(3);          // circumradius
                    var prism = Prism(Polygon(Ngon(corner, 6, 90, 0)), k);

                    // 30-degree chamfer on the top corners, bearing diameter ~0.95 s.
                    double top = 0.95 * s / 2;
                    double outer = corner + 0.5;
                    double zOuter = k - (outer - top) * Tan30;
                    double zCorner = k - (corner - top) * Tan30;
                    if (zCorner > 0.1 * k && zOuter > 0)
                    {
                        var cone = Revolve((0, 0), (outer, 0), (outer, zOuter), (top, k), (0, k));
                        return Common(prism, cone);
                    }
                    return prism;
                }
                case BoltHeadType.Square:
                    return Prism(Polygon(Ngon(s / Math.Sqrt(2), 4, 45, 0)), k);

                case BoltHeadType.Button:
                case BoltHeadType.Carriage:
                {
                    // Spherical cap of height k and base radius R, resting on z = 0.
                    double R = s / 2;
                    double sphereR = (k * k + R * R) / (2 * k);
                    var sphere = new Occt.BRepPrimAPI_MakeSphere(Pnt(0, 0, k - sphereR), sphereR).Shape;
                    var slab = new Occt.BRepPrimAPI_MakeBox(
                        Pnt(-2 * R, -2 * R, 0), 4 * R, 4 * R, k + 1).Shape;
                    return Common(sphere, slab);
                }
                default:
                    throw new NotSupportedException($"Head type {spec.HeadType} is not supported.");
            }
        }

        /// <summary>Hex socket with a 118-degree drill point, opening through the head top.</summary>
        private static Occt.TopoDS_Shape BuildSocketTool(BoltSpec spec)
        {
            double k = spec.HeadHeight, s = spec.SocketSize, t = spec.SocketDepth;
            double floor = k - t;

            var prism = Prism(Polygon(Ngon(s / Math.Sqrt(3), 6, 90, floor)), t + 1);
            double coneR = s / 2;
            var cone = new Occt.BRepPrimAPI_MakeCone(
                new Occt.gp_Ax2(Pnt(0, 0, floor), new Occt.gp_Dir(0, 0, -1)),
                coneR, 0, coneR / Tan59).Shape;
            return Fuse(prism, cone);
        }

        // ------------------------------------------------------------------- helpers
        private static Occt.gp_Pnt Pnt(double x, double y, double z) => new(x, y, z);

        private static IEnumerable<(double x, double y, double z)> Ngon(
            double circumRadius, int sides, double startDeg, double z)
        {
            for (int i = 0; i < sides; i++)
            {
                double a = (startDeg + i * 360.0 / sides) * Math.PI / 180;
                yield return (circumRadius * Math.Cos(a), circumRadius * Math.Sin(a), z);
            }
        }

        private static Occt.TopoDS_Face Polygon(IEnumerable<(double x, double y, double z)> points)
        {
            var poly = new Occt.BRepBuilderAPI_MakePolygon();
            foreach (var (x, y, z) in points)
                poly.Add(Pnt(x, y, z));
            poly.Close();
            return new Occt.BRepBuilderAPI_MakeFace(poly.Wire).Face;
        }

        private static Occt.TopoDS_Shape Prism(Occt.TopoDS_Face face, double height)
            => new Occt.BRepPrimAPI_MakePrism(face, new Occt.gp_Vec(0, 0, height)).Shape;

        /// <summary>Revolves a closed (radius, z) profile 360 degrees about the Z axis.</summary>
        private static Occt.TopoDS_Shape Revolve(params (double r, double z)[] profile)
        {
            var face = Polygon(profile.Select(p => (p.r, 0.0, p.z)));
            var axis = new Occt.gp_Ax1(Pnt(0, 0, 0), new Occt.gp_Dir(0, 0, 1));
            return new Occt.BRepPrimAPI_MakeRevol(face, axis).Shape;
        }

        private static Occt.TopoDS_Shape Fuse(Occt.TopoDS_Shape a, Occt.TopoDS_Shape b)
            => new Occt.BRepAlgoAPI_Fuse(a, b).Shape;

        private static Occt.TopoDS_Shape Cut(Occt.TopoDS_Shape a, Occt.TopoDS_Shape b)
            => new Occt.BRepAlgoAPI_Cut(a, b).Shape;

        private static Occt.TopoDS_Shape Common(Occt.TopoDS_Shape a, Occt.TopoDS_Shape b)
            => new Occt.BRepAlgoAPI_Common(a, b).Shape;
    }
}
