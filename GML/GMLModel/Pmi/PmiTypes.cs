using System.Collections.Generic;
using System.Numerics;

namespace GMLModel.Pmi
{
    /// <summary>
    /// High-level category of a PMI annotation. Used by the renderer to pick
    /// primitive builders without having to do runtime type checks.
    /// </summary>
    public enum PmiKind
    {
        LinearDimension,
        AngularDimension,
        RadialDimension,
        GeometricTolerance,
        SurfaceFinish,
        Datum,
        DatumTarget,
        Note,
        WeldSymbol,
    }

    /// <summary>GD&amp;T feature control frame symbol kinds (ISO 1101 / ASME Y14.5).</summary>
    public enum FcfSymbol
    {
        Unknown,
        // Form
        Straightness, Flatness, Circularity, Cylindricity,
        // Profile
        LineProfile, SurfaceProfile,
        // Orientation
        Parallelism, Perpendicularity, Angularity,
        // Location
        Position, Concentricity, Symmetry,
        // Runout
        CircularRunout, TotalRunout,
    }

    public enum MaterialCondition
    {
        None,
        MMC, // Maximum Material Condition (Ⓜ)
        LMC, // Least Material Condition (Ⓛ)
        RFS, // Regardless of Feature Size (Ⓢ)
    }

    /// <summary>A single feature control frame, i.e. the "box" for a GD&amp;T tolerance.</summary>
    public sealed class FeatureControlFrame
    {
        public FcfSymbol Symbol { get; set; } = FcfSymbol.Unknown;
        public double ToleranceValue { get; set; }
        public MaterialCondition Modifier { get; set; } = MaterialCondition.None;
        public string[] DatumRefs { get; set; } = System.Array.Empty<string>();
        /// <summary>Unicode-pre-rendered text suitable for a billboard. Example: "⌖ ∅0.05 Ⓜ A B C".</summary>
        public string FormattedText { get; set; } = string.Empty;
    }

    /// <summary>Base class for every PMI annotation rendered in 3D.</summary>
    public abstract class PmiAnnotation
    {
        public string Id { get; set; } = string.Empty;
        public PmiKind Kind { get; set; }
        /// <summary>Human readable value, e.g. "25.00 ±0.05".</summary>
        public string DisplayText { get; set; } = string.Empty;
        /// <summary>Point on the geometry the annotation refers to (world coordinates, scene units).</summary>
        public Vector3 AttachPoint { get; set; }
        /// <summary>Preferred position of the annotation text billboard.</summary>
        public Vector3 TextPosition { get; set; }
        /// <summary>Normal of the presentation plane; (0,0,0) when billboarding.</summary>
        public Vector3 PlaneNormal { get; set; }
        /// <summary>Polyline coordinates used to draw the leader (2+ points).</summary>
        public List<Vector3> LeaderPolyline { get; set; } = new List<Vector3>();
    }

    public sealed class LinearDimension  : PmiAnnotation { public double Value; public double PlusTol; public double MinusTol; }
    public sealed class AngularDimension : PmiAnnotation { public double ValueDeg; public double PlusTol; public double MinusTol; }
    public sealed class RadialDimension  : PmiAnnotation { public double Radius; public bool IsDiameter; public double PlusTol; public double MinusTol; }

    public sealed class GeometricTolerance : PmiAnnotation
    {
        public FeatureControlFrame FCF { get; set; } = new FeatureControlFrame();
    }

    public sealed class SurfaceFinish : PmiAnnotation
    {
        public double? Ra { get; set; }
        public string Symbol { get; set; } = string.Empty;
    }

    public sealed class Datum : PmiAnnotation
    {
        public string Letter { get; set; } = string.Empty;
    }

    public sealed class DatumTarget : PmiAnnotation
    {
        public string Letter { get; set; } = string.Empty;
        public string TargetId { get; set; } = string.Empty;
    }

    public sealed class Note : PmiAnnotation
    {
        public string Text { get; set; } = string.Empty;
    }

    public sealed class WeldSymbol : PmiAnnotation
    {
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>A saved camera view stored inside AP242 files.</summary>
    public sealed class PmiSavedView
    {
        public string Name { get; set; } = string.Empty;
        public Vector3 Eye { get; set; }
        public Vector3 LookDir { get; set; } = new Vector3(0, 0, -1);
        public Vector3 UpDir  { get; set; } = new Vector3(0, 1, 0);
        public double  FovDeg { get; set; } = 45.0;
        public HashSet<string> VisibleAnnotationIds { get; } = new HashSet<string>();
    }

    /// <summary>Top-level PMI container returned by the loader.</summary>
    public sealed class PmiModel
    {
        public List<PmiAnnotation> Annotations { get; } = new List<PmiAnnotation>();
        public List<PmiSavedView> SavedViews  { get; } = new List<PmiSavedView>();
        /// <summary>Conversion factor: one STEP length unit expressed in millimetres (e.g. 1.0 for mm, 25.4 for inch).</summary>
        public double LengthUnitMm { get; set; } = 1.0;
        public bool IsEmpty => Annotations.Count == 0 && SavedViews.Count == 0;
    }
}
