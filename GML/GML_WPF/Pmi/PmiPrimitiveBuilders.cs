using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
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
    ///
    /// Notes on text rendering:
    ///   HelixToolkit's <c>TextInfo.Foreground</c> / <c>.Background</c> are
    ///   <c>SharpDX.Color4</c> values. SharpDX is not referenced directly from
    ///   this project, so we set them through reflection with a WPF
    ///   <see cref="Color"/> converted to (r,g,b,a) float arguments.
    ///   Without an explicit foreground the text renders with a transparent /
    ///   black color and appears invisible.
    /// </summary>
    internal static class PmiPrimitiveBuilders
    {
        // Cached reflection info for SharpDX.Color4(r,g,b,a) — resolved once on first use.
        private static readonly Type? s_color4Type = ResolveColor4Type();
        private static readonly ConstructorInfo? s_color4Ctor =
            s_color4Type?.GetConstructor(new[] { typeof(float), typeof(float), typeof(float), typeof(float) });

        /// <param name="fallbackAnchor">Point to use when the annotation has no
        /// geometric anchor (AttachPoint and TextPosition both zero). Callers
        /// should stagger this between annotations so they don't overlap.</param>
        public static IList<Hx.Element3D> Build(PmiAnnotation ann, double sceneScale, Vector3 fallbackAnchor)
        {
            var list = new List<Hx.Element3D>();

            // Resolve attach + text positions. If the loader couldn't supply
            // either, scatter annotations near the scene anchor so they're
            // still visible instead of all collapsing to (0,0,0).
            Vector3 attach = ann.AttachPoint;
            Vector3 textPos = ann.TextPosition;
            bool hasAttach = attach != Vector3.Zero;
            bool hasText = textPos != Vector3.Zero;
            if (!hasAttach && !hasText)
            {
                attach = fallbackAnchor;
                textPos = fallbackAnchor;
            }
            else if (!hasText) textPos = attach + new Vector3(0, 0, 5f);
            else if (!hasAttach) attach = textPos;

            // --- Leader polyline (only when attach and text actually differ) ---
            if (ann.LeaderPolyline != null && ann.LeaderPolyline.Count >= 2)
            {
                list.Add(BuildLeader(ann.LeaderPolyline, sceneScale, ann.Kind));
            }
            else if (attach != textPos)
            {
                list.Add(BuildLeader(new List<Vector3> { attach, textPos }, sceneScale, ann.Kind));
            }

            // --- Billboard text (DisplayText / FormattedText) ---
            string billboardText = string.IsNullOrWhiteSpace(ann.DisplayText)
                ? ann.Kind.ToString()
                : ann.DisplayText;

            Vector3 scaled = textPos * (float)sceneScale;

            var textInfo = new HxSharpDX.TextInfo(billboardText, scaled)
            {
                Scale = 1.0f, // readable on a typical STEP model (was 0.4f)
            };
            ApplyTextColors(textInfo, ColorFor(ann.Kind), backgroundAlpha: 0.7f);

            var billboard = new Hx.BillboardTextModel3D
            {
                Geometry = new HxSharpDX.BillboardSingleText3D { TextInfo = textInfo },
                FixedSize = true,
            };
            list.Add(billboard);

            // --- Feature Control Frame outline under the text ---
            if (ann is GeometricTolerance)
            {
                list.Add(BuildFcfBox(scaled, sceneScale));
            }

            // --- Datum flag (triangle + leader) ---
            if (ann is Datum || ann is DatumTarget)
            {
                list.Add(BuildDatumFlag(attach * (float)sceneScale, scaled, sceneScale));
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

        private static Hx.LineGeometryModel3D BuildFcfBox(Vector3 center, double sceneScale)
        {
            float w = (float)(6.0 * sceneScale);
            float h = (float)(2.0 * sceneScale);
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
                Thickness = 1.5,
                DepthBias = -60,
            };
        }

        private static Hx.LineGeometryModel3D BuildDatumFlag(Vector3 attach, Vector3 text, double sceneScale)
        {
            float s = (float)(1.5 * sceneScale);
            var positions = new HT.Vector3Collection
            {
                attach,
                text,
                new Vector3(text.X - s, text.Y - s, text.Z),
                new Vector3(text.X + s, text.Y - s, text.Z),
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
            PmiKind.Note               => Colors.LightYellow,
            _                          => Colors.Yellow,
        };

        // ---- Reflection-based foreground/background setters ------------------
        private static Type? ResolveColor4Type()
        {
            // SharpDX.Color4 lives in SharpDX.Mathematics.dll (new) or SharpDX.dll (old).
            foreach (var name in new[]
            {
                "SharpDX.Color4, SharpDX.Mathematics",
                "SharpDX.Color4, SharpDX",
                "SharpDX.Color4, SharpDX.Core",
            })
            {
                var t = Type.GetType(name, throwOnError: false);
                if (t != null) return t;
            }
            // Fallback: scan loaded assemblies.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType("SharpDX.Color4", throwOnError: false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        private static object? MakeColor4(Color c, float alpha = 1f)
        {
            if (s_color4Ctor == null) return null;
            return s_color4Ctor.Invoke(new object[] { c.R / 255f, c.G / 255f, c.B / 255f, alpha });
        }

        private static void ApplyTextColors(HxSharpDX.TextInfo info, Color foreground, float backgroundAlpha)
        {
            if (s_color4Type == null) return;
            var type = info.GetType();
            var fg = type.GetProperty("Foreground");
            var bg = type.GetProperty("Background");
            try
            {
                fg?.SetValue(info, MakeColor4(foreground, 1f));
                bg?.SetValue(info, MakeColor4(Colors.Black, backgroundAlpha));
            }
            catch { /* older HelixToolkit lacks these; text will render with defaults */ }
        }
    }
}
