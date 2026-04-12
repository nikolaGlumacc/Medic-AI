using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MedicAIGUI.Views
{
    public partial class MatrixView : UserControl
    {
        private readonly DispatcherTimer _simulationTimer;

        public MatrixView()
        {
            InitializeComponent();

            _simulationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5)
            };
            _simulationTimer.Tick += SimulationTimer_Tick;
            Loaded += MatrixView_Loaded;
            Unloaded += MatrixView_Unloaded;
        }

        private void MatrixView_Loaded(object sender, RoutedEventArgs e)
        {
            _simulationTimer.Start();
        }

        private void MatrixView_Unloaded(object sender, RoutedEventArgs e)
        {
            _simulationTimer.Stop();
        }

        private void SimulationTimer_Tick(object? sender, EventArgs e)
        {
            // In a production app, we would populate the 'UnitList' StackPanel
            // dynamically here. For now, since the XAML has static examples,
            // we'll just keep it clean and perform a build-safe operation.
        }

        // The following handlers exist to prevent build errors if they were previously referenced
        private void AddBtn_Click(object sender, RoutedEventArgs e) { }
        private void DeletePlayer_Click(object sender, RoutedEventArgs e) { }
    }

    public class PriorityPlayer
    {
        public string Name { get; set; } = string.Empty;
        public int Tier { get; set; }
    }
}
