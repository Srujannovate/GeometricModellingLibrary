using System.Collections.Generic;
using System.Windows;
using GMLModel.Pmi;

namespace GML_WPF.Pmi
{
    public partial class PmiListWindow : Window
    {
        public PmiListWindow(IEnumerable<PmiAnnotation> annotations)
        {
            InitializeComponent();
            Grid.ItemsSource = annotations;
        }
    }
}
