using System.Globalization;

namespace GML_WPF.Bolt
{
    public enum BoltHeadType { Hex, Square, SocketCap, Button, Countersunk, Carriage }
    public enum PitchSeries { Coarse, Fine }
    public enum ThreadLengthMode { Standard, Full, Custom }
    public enum ThreadHand { Right, Left }

    /// <summary>ISO metric thread size with coarse and (first-choice) fine pitch.</summary>
    public sealed record MetricSize(string Name, double D, double CoarsePitch, double FinePitch)
    {
        public override string ToString() => Name;
    }

    /// <summary>Default head proportions for a given head type and size.</summary>
    public sealed record HeadDimensions(double Width, double Height, double SocketSize, double SocketDepth, double NeckLength);

    /// <summary>
    /// Nominal dimensions taken from the common ISO/DIN product standards.
    /// Values marked as derived (NaN in the tables) are computed from typical
    /// proportions; always verify against the current edition of the standard.
    /// </summary>
    public static class BoltStandards
    {
        public static readonly IReadOnlyList<MetricSize> Sizes = new[]
        {
            new MetricSize("M3",  3,  0.5,  0.35),
            new MetricSize("M4",  4,  0.7,  0.5),
            new MetricSize("M5",  5,  0.8,  0.5),
            new MetricSize("M6",  6,  1.0,  0.75),
            new MetricSize("M8",  8,  1.25, 1.0),
            new MetricSize("M10", 10, 1.5,  1.25),
            new MetricSize("M12", 12, 1.75, 1.25),
            new MetricSize("M16", 16, 2.0,  1.5),
            new MetricSize("M20", 20, 2.5,  1.5),
            new MetricSize("M24", 24, 3.0,  2.0),
        };

        public static readonly string[] ToleranceClasses = { "6g", "4g6g", "6e", "8g" };
        public static readonly string[] PropertyClasses = { "4.6", "5.8", "8.8", "10.9", "12.9", "A2-70", "A4-80" };
        public static readonly string[] Finishes = { "Plain", "Zinc plated", "Zinc flake", "Hot-dip galvanized", "Black oxide" };

        // Index order matches Sizes (M3 .. M24). NaN = not in the standard -> derived.
        private static readonly double[] HexS = { 5.5, 7, 8, 10, 13, 16, 18, 24, 30, 36 };
        private static readonly double[] HexK = { 2, 2.8, 3.5, 4, 5.3, 6.4, 7.5, 10, 12.5, 15 };

        private static readonly double[] CapDk = { 5.5, 7, 8.5, 10, 13, 16, 18, 24, 30, 36 };
        private static readonly double[] CapS = { 2.5, 3, 4, 5, 6, 8, 10, 14, 17, 19 };
        private static readonly double[] CapT = { 1.3, 2, 2.5, 3, 4, 5, 6, 8, 10, 12 };

        private static readonly double[] BtnDk = { 5.7, 7.6, 9.5, 10.5, 14, 17.5, 21, 28, double.NaN, double.NaN };
        private static readonly double[] BtnK = { 1.65, 2.2, 2.75, 3.3, 4.4, 5.5, 6.6, 8.8, double.NaN, double.NaN };
        private static readonly double[] BtnS = { 2, 2.5, 3, 4, 5, 6, 8, 10, double.NaN, double.NaN };

        private static readonly double[] CskDk = { 6.72, 8.96, 11.2, 13.44, 17.92, 22.4, 26.88, 33.6, 40.32, double.NaN };
        private static readonly double[] CskK = { 1.86, 2.48, 3.1, 3.72, 4.96, 6.2, 7.44, 8.8, 10.16, double.NaN };
        private static readonly double[] CskS = { 2, 2.5, 3, 4, 5, 6, 8, 10, 12, double.NaN };
        private static readonly double[] CskT = { 1.1, 1.5, 1.9, 2.2, 3, 3.6, 4.3, 4.5, 4.8, double.NaN };

        private static readonly double[] CarDk = { double.NaN, double.NaN, 13.55, 16.55, 20.65, 24.65, 30.65, 38.8, 46.8, double.NaN };
        private static readonly double[] CarK = { double.NaN, double.NaN, 3.3, 3.88, 4.88, 5.38, 6.95, 8.95, 11.05, double.NaN };
        private static readonly double[] CarF = { double.NaN, double.NaN, 4.1, 4.6, 5.6, 6.6, 8.75, 12.9, 15.9, double.NaN };

        private static double Pick(double[] table, int i, double derived)
            => i >= 0 && i < table.Length && !double.IsNaN(table[i]) ? table[i] : Math.Round(derived, 2);

        public static HeadDimensions DefaultHead(BoltHeadType type, MetricSize size)
        {
            int i = Sizes.ToList().FindIndex(s => s.Name == size.Name);
            double d = size.D;
            return type switch
            {
                BoltHeadType.Hex => new(Pick(HexS, i, 1.5 * d), Pick(HexK, i, 0.63 * d), 0, 0, 0),
                BoltHeadType.Square => new(Pick(HexS, i, 1.5 * d), Pick(HexK, i, 0.63 * d) * 1.1, 0, 0, 0),
                BoltHeadType.SocketCap => new(Pick(CapDk, i, 1.5 * d), d, Pick(CapS, i, 0.8 * d), Pick(CapT, i, 0.5 * d), 0),
                BoltHeadType.Button => new(Pick(BtnDk, i, 1.75 * d), Pick(BtnK, i, 0.55 * d), Pick(BtnS, i, 0.6 * d),
                                           Math.Round(0.6 * Pick(BtnK, i, 0.55 * d), 2), 0),
                BoltHeadType.Countersunk => new(Pick(CskDk, i, 2.0 * d), Pick(CskK, i, 0.5 * d), Pick(CskS, i, 0.6 * d),
                                                Pick(CskT, i, 0.23 * d), 0),
                BoltHeadType.Carriage => new(Pick(CarDk, i, 2.5 * d), Pick(CarK, i, 0.6 * d), 0, 0, Pick(CarF, i, 0.7 * d)),
                _ => new(1.5 * d, 0.63 * d, 0, 0, 0)
            };
        }

        public static string StandardFor(BoltHeadType type, bool fullyThreaded) => type switch
        {
            BoltHeadType.Hex => fullyThreaded ? "ISO 4017" : "ISO 4014",
            BoltHeadType.Square => "DIN 478",
            BoltHeadType.SocketCap => "ISO 4762",
            BoltHeadType.Button => "ISO 7380-1",
            BoltHeadType.Countersunk => "ISO 10642",
            BoltHeadType.Carriage => "DIN 603",
            _ => ""
        };

        public static string DisplayName(BoltHeadType type) => type switch
        {
            BoltHeadType.Hex => "Hexagon head",
            BoltHeadType.Square => "Square head",
            BoltHeadType.SocketCap => "Socket head cap",
            BoltHeadType.Button => "Button head (hex socket)",
            BoltHeadType.Countersunk => "Countersunk (hex socket)",
            BoltHeadType.Carriage => "Carriage (round head, square neck)",
            _ => type.ToString()
        };

        public static bool HasSocket(BoltHeadType t)
            => t is BoltHeadType.SocketCap or BoltHeadType.Button or BoltHeadType.Countersunk;
    }

    /// <summary>All user inputs for a bolt plus derived thread/geometry values.</summary>
    public sealed class BoltSpec
    {
        public BoltHeadType HeadType { get; set; } = BoltHeadType.Hex;
        public MetricSize Size { get; set; } = BoltStandards.Sizes[5];
        public PitchSeries Pitch { get; set; } = PitchSeries.Coarse;
        public string ToleranceClass { get; set; } = "6g";
        public ThreadHand Hand { get; set; } = ThreadHand.Right;
        /// <summary>Nominal length L. Measured from the bearing face, or overall for countersunk heads.</summary>
        public double Length { get; set; } = 50;
        public ThreadLengthMode ThreadMode { get; set; } = ThreadLengthMode.Standard;
        public double CustomThreadLength { get; set; } = 26;

        /// <summary>Across flats (hex/square) or head diameter dk (round heads).</summary>
        public double HeadWidth { get; set; }
        public double HeadHeight { get; set; }
        public double SocketSize { get; set; }
        public double SocketDepth { get; set; }
        public double NeckLength { get; set; }

        public string PropertyClass { get; set; } = "8.8";
        public string Finish { get; set; } = "Zinc plated";

        public string DrawingNumber { get; set; } = "GML-BLT-001";
        public string DrawnBy { get; set; } = "";
        /// <summary>Drawing scale (model mm per sheet mm inverted, e.g. 2 = 2:1). Null = automatic.</summary>
        public double? Scale { get; set; }

        // ---- Derived values ----
        public double D => Size.D;
        public double P => Pitch == PitchSeries.Coarse ? Size.CoarsePitch : Size.FinePitch;
        /// <summary>External thread minor diameter d3 (ISO 724).</summary>
        public double MinorDiameter => D - 1.22687 * P;
        /// <summary>Pitch diameter d2 (ISO 724).</summary>
        public double PitchDiameter => D - 0.64952 * P;
        public double ThreadDepth => 0.61343 * P;

        public bool IsCountersunk => HeadType == BoltHeadType.Countersunk;
        public bool HasSocket => BoltStandards.HasSocket(HeadType);

        /// <summary>Sheet x-coordinate (model) of the tip.</summary>
        public double TipX => IsCountersunk ? Length : HeadHeight + Length;
        /// <summary>Where the plain/threaded shank starts (after the head or carriage neck).</summary>
        public double ShankStartX => HeadHeight + (HeadType == BoltHeadType.Carriage ? NeckLength : 0);

        public double ThreadLength
        {
            get
            {
                double available = TipX - ShankStartX;
                double b = ThreadMode switch
                {
                    ThreadLengthMode.Full => available,
                    ThreadLengthMode.Custom => CustomThreadLength,
                    _ => StandardThreadLength()
                };
                return Math.Max(0, Math.Min(b, available));
            }
        }

        public bool IsFullyThreaded => ThreadLength >= TipX - ShankStartX - 1e-6;

        private double StandardThreadLength()
        {
            switch (HeadType)
            {
                case BoltHeadType.Button:
                case BoltHeadType.Countersunk:
                    return double.MaxValue; // usually threaded to the head
                case BoltHeadType.SocketCap:
                    return 2 * D + 12;
                default:
                    // ISO 4014 reference thread length b
                    if (Length <= 125) return 2 * D + 6;
                    if (Length <= 200) return 2 * D + 12;
                    return 2 * D + 25;
            }
        }

        public string ThreadDesignation
        {
            get
            {
                var s = Pitch == PitchSeries.Coarse ? Size.Name : $"{Size.Name}x{Fmt(P)}";
                s += $"-{ToleranceClass}";
                if (Hand == ThreadHand.Left) s += "-LH";
                return s;
            }
        }

        public string Standard => BoltStandards.StandardFor(HeadType, HeadType == BoltHeadType.Hex && IsFullyThreaded);

        /// <summary>ISO-style product designation, e.g. "ISO 4014 - M10x1.25 x 50 - 8.8".</summary>
        public string Designation
        {
            get
            {
                var thread = Pitch == PitchSeries.Coarse ? Size.Name : $"{Size.Name}x{Fmt(P)}";
                if (Hand == ThreadHand.Left) thread += "-LH";
                return $"{Standard} - {thread} x {Fmt(Length)} - {PropertyClass}";
            }
        }

        public string Title => $"{BoltStandards.DisplayName(HeadType).ToUpperInvariant()} BOLT {Size.Name} x {Fmt(Length)}";

        /// <summary>Returns a list of problems; empty when the spec can be drawn.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (Length <= 0) errors.Add("Length must be greater than 0.");
            if (HeadHeight <= 0) errors.Add("Head height k must be greater than 0.");
            if (HeadWidth <= D) errors.Add($"Head width must be larger than the thread diameter ({Fmt(D)}).");
            if (IsCountersunk && Length <= HeadHeight + 2 * P)
                errors.Add("Countersunk length (overall) must exceed head height + 2 pitches.");
            if (!IsCountersunk && Length < 2 * P) errors.Add("Length is too short for the thread pitch.");
            if (HeadType == BoltHeadType.Carriage)
            {
                if (NeckLength <= 0) errors.Add("Neck length must be greater than 0.");
                else if (NeckLength >= Length) errors.Add("Neck length must be shorter than the bolt length.");
            }
            if (HasSocket)
            {
                if (SocketSize <= 0) errors.Add("Socket size must be greater than 0.");
                else if (SocketSize * 1.1547 >= (IsCountersunk ? HeadWidth : HeadWidth * 0.95))
                    errors.Add("Socket is too large for the head.");
                if (SocketDepth <= 0) errors.Add("Socket depth must be greater than 0.");
                else if (SocketDepth >= HeadHeight + (IsCountersunk ? 0 : 0.5 * D))
                    errors.Add("Socket depth is too deep for the head.");
            }
            if (ThreadMode == ThreadLengthMode.Custom && CustomThreadLength <= 0)
                errors.Add("Custom thread length must be greater than 0.");
            if (Scale is double sc && sc <= 0) errors.Add("Scale must be positive.");
            return errors;
        }

        public static string Fmt(double v)
            => v.ToString(Math.Abs(v - Math.Round(v)) < 1e-9 ? "0" : "0.##", CultureInfo.InvariantCulture);
    }
}
