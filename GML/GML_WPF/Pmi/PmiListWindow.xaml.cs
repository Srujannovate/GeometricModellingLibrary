using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using GMLModel.Pmi;

namespace GML_WPF.Pmi
{
    /// <summary>
    /// Non-modal PMI browser. Owner receives notifications via
    /// <see cref="AnnotationSelected"/> (on single click / selection change) and
    /// <see cref="AnnotationActivated"/> (on double-click / Enter).
    /// </summary>
    public partial class PmiListWindow : Window
    {
        private bool _suppressSelectionEvent;

        public event Action<PmiAnnotation?>? AnnotationSelected;
        public event Action<PmiAnnotation?>? AnnotationActivated;

        public PmiListWindow(IEnumerable<PmiAnnotation> annotations)
        {
            InitializeComponent();
            Grid.ItemsSource = annotations;
            Grid.SelectionChanged += Grid_SelectionChanged;
            Grid.MouseDoubleClick += Grid_MouseDoubleClick;
        }

        /// <summary>Programmatically select an annotation without re-raising AnnotationSelected.</summary>
        public void SyncSelectionFromOutside(PmiAnnotation? ann)
        {
            if (ann == null) return;
            _suppressSelectionEvent = true;
            try
            {
                Grid.SelectedItem = ann;
                Grid.ScrollIntoView(ann);
            }
            finally { _suppressSelectionEvent = false; }
        }

        private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressSelectionEvent) return;
            AnnotationSelected?.Invoke(Grid.SelectedItem as PmiAnnotation);
        }

        private void Grid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            AnnotationActivated?.Invoke(Grid.SelectedItem as PmiAnnotation);
        }
    }
}
