using System.Globalization;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace GML_WPF.Bolt
{
    /// <summary>
    /// Parameter editor and live preview for bolt manufacturing drawings.
    /// </summary>
    public partial class BoltDrawingWindow : Window
    {
        private const double DipPerMm = 96.0 / 25.4;

        private static readonly double[] PreferredLengths =
        {
            6, 8, 10, 12, 16, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 80, 90,
            100, 110, 120, 130, 140, 150, 160, 180, 200, 220, 240, 260, 280, 300
        };

        private sealed record Choice<T>(string Text, T Value)
        {
            public override string ToString() => Text;
        }

        private readonly Dictionary<BoltDrawingKind, Border> _hosts = new();
        private readonly ScaleTransform _zoom = new();
        private BoltSpec? _spec;
        private bool _suppress = true;

        public BoltDrawingWindow()
        {
            InitializeComponent();

            HeadTypeCombo.ItemsSource = Enum.GetValues<BoltHeadType>()
                .Select(t => new Choice<BoltHeadType>(BoltStandards.DisplayName(t), t)).ToList();
            HeadTypeCombo.SelectedIndex = 0;

            SizeCombo.ItemsSource = BoltStandards.Sizes;
            SizeCombo.SelectedIndex = 5; // M10
            UpdatePitchItems();

            ToleranceCombo.ItemsSource = BoltStandards.ToleranceClasses;
            ToleranceCombo.SelectedIndex = 0;

            HandCombo.ItemsSource = new[]
            {
                new Choice<ThreadHand>("Right hand", ThreadHand.Right),
                new Choice<ThreadHand>("Left hand (LH)", ThreadHand.Left)
            };
            HandCombo.SelectedIndex = 0;

            ThreadModeCombo.ItemsSource = new[]
            {
                new Choice<ThreadLengthMode>("Standard (per product standard)", ThreadLengthMode.Standard),
                new Choice<ThreadLengthMode>("Fully threaded", ThreadLengthMode.Full),
                new Choice<ThreadLengthMode>("Custom", ThreadLengthMode.Custom)
            };
            ThreadModeCombo.SelectedIndex = 0;

            LengthBox.ItemsSource = PreferredLengths.Select(BoltSpec.Fmt).ToList();
            LengthBox.Text = "50";
            LengthBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(Input_Changed));
            LengthBox.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(Regenerate);

            PropertyClassCombo.ItemsSource = BoltStandards.PropertyClasses;
            PropertyClassCombo.SelectedItem = "8.8";
            FinishCombo.ItemsSource = BoltStandards.Finishes;
            FinishCombo.SelectedItem = "Zinc plated";

            ScaleCombo.ItemsSource = new[]
            {
                new Choice<double?>("Auto", null),
                new Choice<double?>("10:1", 10), new Choice<double?>("5:1", 5), new Choice<double?>("2:1", 2),
                new Choice<double?>("1:1", 1), new Choice<double?>("1:2", 0.5), new Choice<double?>("1:5", 0.2)
            };
            ScaleCombo.SelectedIndex = 0;

            DrawingNumberBox.Text = "GML-BLT-001";
            DrawnByBox.Text = Environment.UserName;

            foreach (var kind in Enum.GetValues<BoltDrawingKind>())
            {
                var host = new Border
                {
                    LayoutTransform = _zoom,
                    Margin = new Thickness(16),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 2, Opacity = 0.4 }
                };
                _hosts[kind] = host;
                DrawingTabs.Items.Add(new TabItem
                {
                    Header = BoltDrawingGenerator.DisplayName(kind),
                    Tag = kind,
                    Content = new ScrollViewer
                    {
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Background = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                        Content = host
                    }
                });
            }
            DrawingTabs.SelectedIndex = 0;

            SetZoom(ZoomSlider.Value);
            ApplyHeadDefaults();
            _suppress = false;
            Regenerate();

            Loaded += (_, _) => Dispatcher.BeginInvoke(FitToWindow, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ------------------------------------------------------------ inputs
        private BoltHeadType SelectedHead => ((Choice<BoltHeadType>)HeadTypeCombo.SelectedItem).Value;
        private MetricSize SelectedSize => (MetricSize)SizeCombo.SelectedItem;

        private void UpdatePitchItems()
        {
            int index = Math.Max(0, PitchCombo.SelectedIndex);
            var size = SelectedSize;
            PitchCombo.ItemsSource = new[]
            {
                new Choice<PitchSeries>($"Coarse ({BoltSpec.Fmt(size.CoarsePitch)} mm)", PitchSeries.Coarse),
                new Choice<PitchSeries>($"Fine ({BoltSpec.Fmt(size.FinePitch)} mm)", PitchSeries.Fine)
            };
            PitchCombo.SelectedIndex = index;
        }

        private void ApplyHeadDefaults()
        {
            bool old = _suppress;
            _suppress = true;
            try
            {
                var type = SelectedHead;
                var dims = BoltStandards.DefaultHead(type, SelectedSize);
                HeadWidthBox.Text = BoltSpec.Fmt(dims.Width);
                HeadHeightBox.Text = BoltSpec.Fmt(dims.Height);
                SocketSizeBox.Text = BoltSpec.Fmt(dims.SocketSize);
                SocketDepthBox.Text = BoltSpec.Fmt(dims.SocketDepth);
                NeckLengthBox.Text = BoltSpec.Fmt(dims.NeckLength);

                HeadWidthLabel.Content = type is BoltHeadType.Hex or BoltHeadType.Square
                    ? "Across flats s (mm):"
                    : "Head Ø dk (mm):";
                LengthLabel.Content = type == BoltHeadType.Countersunk
                    ? "Overall length _L (mm):"
                    : "Nominal length _L (mm):";

                var socket = BoltStandards.HasSocket(type) ? Visibility.Visible : Visibility.Collapsed;
                SocketSizeLabel.Visibility = SocketSizeBox.Visibility = socket;
                SocketDepthLabel.Visibility = SocketDepthBox.Visibility = socket;
                var neck = type == BoltHeadType.Carriage ? Visibility.Visible : Visibility.Collapsed;
                NeckLengthLabel.Visibility = NeckLengthBox.Visibility = neck;
            }
            finally
            {
                _suppress = old;
            }
        }

        private static bool TryParse(string text, out double value)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
               double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

        private BoltSpec? ReadSpec(List<string> errors)
        {
            double Num(TextBox box, string name, bool required = true)
            {
                if (!required) return 0;
                if (TryParse(box.Text, out var v) && double.IsFinite(v)) return v;
                errors.Add($"{name} is not a valid number.");
                return 0;
            }

            var type = SelectedHead;
            bool socket = BoltStandards.HasSocket(type);
            var spec = new BoltSpec
            {
                HeadType = type,
                Size = SelectedSize,
                Pitch = ((Choice<PitchSeries>)PitchCombo.SelectedItem).Value,
                ToleranceClass = (string)ToleranceCombo.SelectedItem,
                Hand = ((Choice<ThreadHand>)HandCombo.SelectedItem).Value,
                ThreadMode = ((Choice<ThreadLengthMode>)ThreadModeCombo.SelectedItem).Value,
                HeadWidth = Num(HeadWidthBox, "Head width"),
                HeadHeight = Num(HeadHeightBox, "Head height"),
                SocketSize = Num(SocketSizeBox, "Socket size", socket),
                SocketDepth = Num(SocketDepthBox, "Socket depth", socket),
                NeckLength = Num(NeckLengthBox, "Neck length", type == BoltHeadType.Carriage),
                PropertyClass = (string)PropertyClassCombo.SelectedItem,
                Finish = (string)FinishCombo.SelectedItem,
                Scale = ((Choice<double?>)ScaleCombo.SelectedItem).Value,
                DrawingNumber = DrawingNumberBox.Text.Trim(),
                DrawnBy = DrawnByBox.Text.Trim()
            };

            if (TryParse(LengthBox.Text, out var length) && double.IsFinite(length)) spec.Length = length;
            else errors.Add("Length is not a valid number.");

            if (spec.ThreadMode == ThreadLengthMode.Custom)
                spec.CustomThreadLength = Num(ThreadLengthBox, "Custom thread length");

            if (errors.Count > 0) return null;
            errors.AddRange(spec.Validate());
            return errors.Count == 0 ? spec : null;
        }

        private void Regenerate()
        {
            if (_suppress) return;

            ThreadLengthBox.IsEnabled = ThreadModeCombo.SelectedItem is Choice<ThreadLengthMode> { Value: ThreadLengthMode.Custom };

            var errors = new List<string>();
            var spec = ReadSpec(errors);
            ExportPngButton.IsEnabled = ExportAllButton.IsEnabled = PrintButton.IsEnabled = spec != null;
            ExportStepButton.IsEnabled = spec != null && !_stepExportRunning;
            if (spec == null)
            {
                _spec = null;
                ValidationText.Text = string.Join(Environment.NewLine, errors);
                DesignationText.Text = "";
                return;
            }

            try
            {
                foreach (var (kind, host) in _hosts)
                    host.Child = BoltDrawingGenerator.Build(spec, kind).Canvas;
                _spec = spec;
                ValidationText.Text = "";
                DesignationText.Text = spec.Designation;
            }
            catch (Exception ex)
            {
                _spec = null;
                ValidationText.Text = "Could not generate drawing: " + ex.Message;
                ExportPngButton.IsEnabled = ExportAllButton.IsEnabled = PrintButton.IsEnabled = false;
                ExportStepButton.IsEnabled = false;
            }
        }

        private void HeadOrSize_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_suppress) return;
            if (ReferenceEquals(sender, SizeCombo))
            {
                _suppress = true;
                UpdatePitchItems();
                _suppress = false;
            }
            ApplyHeadDefaults();
            Regenerate();
        }

        private void Input_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppress) return;
            if (ReferenceEquals(sender, ThreadModeCombo) &&
                ThreadModeCombo.SelectedItem is Choice<ThreadLengthMode> { Value: ThreadLengthMode.Custom } &&
                _spec != null)
            {
                // Seed the custom value from the current thread length.
                _suppress = true;
                ThreadLengthBox.Text = BoltSpec.Fmt(Math.Round(_spec.ThreadLength, 2));
                _suppress = false;
            }
            Regenerate();
        }

        private void ResetHeadButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyHeadDefaults();
            Regenerate();
        }

        // -------------------------------------------------------------- zoom
        private void SetZoom(double zoom)
        {
            _zoom.ScaleX = _zoom.ScaleY = zoom * DipPerMm;
            if (ZoomText != null) ZoomText.Text = $"{zoom * 100:0}%";
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => SetZoom(e.NewValue);

        private void FitButton_Click(object sender, RoutedEventArgs e) => FitToWindow();

        private void FitToWindow()
        {
            if (DrawingTabs.SelectedContent is not ScrollViewer sv || sv.ViewportWidth <= 0) return;
            double zoom = Math.Min((sv.ViewportWidth - 40) / (DrawingSheet.A4Width * DipPerMm),
                                   (sv.ViewportHeight - 40) / (DrawingSheet.A4Height * DipPerMm));
            ZoomSlider.Value = Math.Clamp(zoom, ZoomSlider.Minimum, ZoomSlider.Maximum);
        }

        // ------------------------------------------------------ export/print
        private BoltDrawingKind CurrentKind
            => DrawingTabs.SelectedItem is TabItem { Tag: BoltDrawingKind k } ? k : BoltDrawingKind.Detail;

        private static string SafeFileName(string s)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
            return cleaned.Length == 0 ? "bolt" : cleaned;
        }

        private void ExportPngButton_Click(object sender, RoutedEventArgs e)
        {
            if (_spec == null) return;
            var kind = CurrentKind;
            var dlg = new SaveFileDialog
            {
                Title = "Export drawing as PNG",
                Filter = "PNG image (*.png)|*.png",
                FileName = $"{SafeFileName(_spec.DrawingNumber)}_{kind}.png"
            };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                SavePng(BoltDrawingGenerator.Build(_spec, kind), dlg.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Max time to wait for OCCT to build and write the STEP file.</summary>
        private static readonly TimeSpan StepExportTimeout = TimeSpan.FromSeconds(60);
        private bool _stepExportRunning;

        private async void ExportStepButton_Click(object sender, RoutedEventArgs e)
        {
            if (_spec == null || _stepExportRunning) return;
            var spec = _spec;
            var dlg = new SaveFileDialog
            {
                Title = "Export bolt 3D model as STEP",
                Filter = "STEP file (*.step;*.stp)|*.step;*.stp",
                DefaultExt = ".step",
                FileName = $"{SafeFileName(spec.DrawingNumber)}.step"
            };
            if (dlg.ShowDialog(this) != true) return;
            var path = dlg.FileName;

            _stepExportRunning = true;
            ExportStepButton.IsEnabled = false;
            var oldContent = ExportStepButton.Content;
            bool stuck = false;
            ExportStepButton.Content = "Exporting STEP...";
            Cursor = System.Windows.Input.Cursors.AppStarting;
            try
            {
                // Run OCCT on a background thread so a slow or stuck native call
                // cannot freeze the window. IsBackground lets the app exit even if
                // the native call never returns.
                var done = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var worker = new Thread(() =>
                {
                    try { BoltSolidBuilder.ExportStep(spec, path); done.TrySetResult(null); }
                    catch (Exception ex) { done.TrySetResult(ex); }
                })
                { IsBackground = true, Name = "Bolt STEP export" };
                worker.Start();

                var finished = await Task.WhenAny(done.Task, Task.Delay(StepExportTimeout));
                if (finished != done.Task)
                {
                    stuck = true;
                    ExportStepButton.ToolTip = "STEP export is unavailable until the application is restarted.";
                    MessageBox.Show(this,
                        $"The STEP export did not finish within {StepExportTimeout.TotalSeconds:0} seconds. " +
                        "The OCCT file writer appears to be stuck, so no file was written.\n\n" +
                        "Restart the application before trying again.",
                        "STEP export timed out", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var error = await done.Task;
                if (error != null)
                {
                    MessageBox.Show(this, error.Message, "STEP export failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                MessageBox.Show(this, $"Saved the 3D bolt model to:\n{path}", "STEP export complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                Cursor = null;
                ExportStepButton.Content = oldContent;
                // A stuck native call keeps running; keep the button disabled so
                // further clicks don't pile up more stuck threads.
                _stepExportRunning = stuck;
                ExportStepButton.IsEnabled = _spec != null && !stuck;
            }
        }

        private void ExportAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_spec == null) return;
            var dlg = new OpenFolderDialog { Title = "Choose a folder for the drawings" };
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                var baseName = SafeFileName(_spec.DrawingNumber);
                foreach (var kind in Enum.GetValues<BoltDrawingKind>())
                    SavePng(BoltDrawingGenerator.Build(_spec, kind), Path.Combine(dlg.FolderName, $"{baseName}_{kind}.png"));
                MessageBox.Show(this, $"Saved {Enum.GetValues<BoltDrawingKind>().Length} drawings to {dlg.FolderName}.",
                    "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>Renders the sheet to a PNG at the given resolution (default 300 dpi).</summary>
        private static void SavePng(DrawingSheet sheet, string path, double dpi = 300)
        {
            var host = new Border
            {
                Child = sheet.Canvas,
                Background = Brushes.White,
                LayoutTransform = new ScaleTransform(DipPerMm, DipPerMm)
            };
            var size = new Size(sheet.Width * DipPerMm, sheet.Height * DipPerMm);
            host.Measure(size);
            host.Arrange(new Rect(size));
            host.UpdateLayout();

            int pw = (int)Math.Ceiling(sheet.Width / 25.4 * dpi);
            int ph = (int)Math.Ceiling(sheet.Height / 25.4 * dpi);
            var bmp = new RenderTargetBitmap(pw, ph, dpi, dpi, PixelFormats.Pbgra32);
            bmp.Render(host);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(path);
            encoder.Save(fs);
        }

        private void PrintButton_Click(object sender, RoutedEventArgs e)
        {
            if (_spec == null) return;
            var dlg = new PrintDialog();
            try { dlg.PrintTicket.PageOrientation = PageOrientation.Landscape; } catch { /* driver may not support */ }
            if (dlg.ShowDialog() != true) return;

            try
            {
                var sheet = BoltDrawingGenerator.Build(_spec, CurrentKind);
                var area = dlg.PrintQueue.GetPrintCapabilities(dlg.PrintTicket).PageImageableArea;
                double ox = area?.OriginWidth ?? 0, oy = area?.OriginHeight ?? 0;
                double w = area?.ExtentWidth ?? dlg.PrintableAreaWidth;
                double h = area?.ExtentHeight ?? dlg.PrintableAreaHeight;
                double k = Math.Min(w / sheet.Width, h / sheet.Height);

                var host = new Border
                {
                    Child = sheet.Canvas,
                    LayoutTransform = new ScaleTransform(k, k),
                    Margin = new Thickness(ox, oy, 0, 0)
                };
                var page = new Size(dlg.PrintableAreaWidth, dlg.PrintableAreaHeight);
                host.Measure(page);
                host.Arrange(new Rect(page));
                dlg.PrintVisual(host, $"{_spec.DrawingNumber} {BoltDrawingGenerator.DisplayName(CurrentKind)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Print failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
