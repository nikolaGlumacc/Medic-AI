using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MedicAIGUI.Services;

namespace MedicAIGUI.Views
{
    public partial class MatrixView : UserControl
    {
        private readonly MedicBotService _service = MedicBotService.Instance;
        private readonly DispatcherTimer _simulationTimer;
        private readonly Random _rng = new Random();

        public MatrixView()
        {
            InitializeComponent();
            
            // Start a simulation timer to update the "Triage Matrix" with live-looking data
            _simulationTimer = new DispatcherTimer();
            _simulationTimer.Interval = TimeSpan.FromSeconds(1.5);
            _simulationTimer.Tick += SimulationTimer_Tick;
            _simulationTimer.Start();
        }

        private void SimulationTimer_Tick(object sender, EventArgs e)
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
        public string Name { get; set; }
        public int Tier { get; set; }
    }
}
