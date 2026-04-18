using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MedicAIGUI.Services;

namespace MedicAIGUI.Views
{
    public partial class InspectorView : UserControl
    {
        private readonly InspectorEngine            _engine = new();
        private System.Threading.CancellationTokenSource? _cts;

        public InspectorView()
        {
            InitializeComponent();

            ResultsList.ItemsSource = _engine.Results;
            LogItems.ItemsSource    = _engine.Logs;

            // Auto-scroll live log
            _engine.Logs.CollectionChanged += (_, _) =>
                Dispatcher.InvokeAsync(() =>
                {
                    LogScroller.ScrollToBottom();
                    UpdateSummary();
                });

            _engine.Results.CollectionChanged += (_, _) =>
                Dispatcher.InvokeAsync(UpdateSummary);
        }

        // ── Attach the engine once the view is visible ────────────────────
        private void InspectorView_Loaded(object sender, RoutedEventArgs e)
        {
            var mw = Window.GetWindow(this);
            if (mw == null) return;
            try   { _engine.Attach(mw); }
            catch (Exception ex) { DebugHub.Log($"Attach failed: {ex.Message}"); }
            UpdateBotHealth();
        }

        // ── Run All ───────────────────────────────────────────────────────
        private async void RunAllBtn_Click(object sender, RoutedEventArgs e)
        {
            SetRunning(true);
            _cts = new System.Threading.CancellationTokenSource();
            RunStatusLabel.Text = "Running…";

            try
            {
                await _engine.RunAllAsync(_cts.Token);
                RunStatusLabel.Text = _cts.IsCancellationRequested
                    ? "Cancelled."
                    : $"Done — {_engine.State.Passed} passed, {_engine.State.Failed} failed.";
            }
            catch (OperationCanceledException)
            {
                RunStatusLabel.Text = "Stopped.";
            }
            finally
            {
                SetRunning(false);
                UpdateSummary();
                UpdateBotHealth();
            }
        }

        // ── Stop ─────────────────────────────────────────────────────────
        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            RunStatusLabel.Text = "Stopping…";
        }

        // ── Clear ────────────────────────────────────────────────────────
        private void ClearBtn_Click(object sender, RoutedEventArgs e)
        {
            _engine.Results.Clear();
            _engine.Logs.Clear();
            TotalLabel.Text     = "0";
            PassedLabel.Text    = "0";
            FailedLabel.Text    = "0";
            RunStatusLabel.Text = "Logs cleared.";
        }

        // ── Extras ───────────────────────────────────────────────────────
        private void ExtrasBtn_Click(object sender, RoutedEventArgs e)
        {
            var mw = Window.GetWindow(this);
            if (mw == null) { _engine.Logs.Add("[EXTRAS] MainWindow not found."); return; }

            _engine.Logs.Add("── EXTRAS ──────────────────────────────");

            // Duplicate names
            var dupes = _engine.DetectDuplicateNames();
            _engine.Logs.Add(dupes.Count == 0
                ? "  Duplicate names: none ✔"
                : $"  Duplicate names ({dupes.Count}):");
            foreach (var d in dupes) _engine.Logs.Add($"    ⚠  {d}");

            // Nav button presence
            string[] navBtns = { "DashboardBtn","PriorityBtn","SettingsBtn",
                                  "TuningBtn","MatrixBtn","LoadoutBtn","InspectorBtn" };
            _engine.Logs.Add("  Nav buttons:");
            foreach (var b in navBtns)
                _engine.Logs.Add(_engine.AssertControlExists(b) ? $"    {b}  ✔" : $"    {b}  ✘ MISSING");

            // Bot health
            bool bot = _engine.CheckBotServiceReachable();
            _engine.Logs.Add($"  Bot service: {(bot ? "CONNECTED ✔" : "OFFLINE ✘")}");
            _engine.Logs.Add("── END EXTRAS ──────────────────────────");

            UpdateBotHealth();
        }

        // ── Copy log ─────────────────────────────────────────────────────
        private void CopyLogsBtn_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(string.Join(Environment.NewLine, _engine.Logs));
            RunStatusLabel.Text = "Logs copied to clipboard.";
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private void SetRunning(bool running)
        {
            RunAllBtn.IsEnabled = !running;
            StopBtn.IsEnabled   =  running;
            ClearBtn.IsEnabled  = !running;
            ExtrasBtn.IsEnabled = !running;
        }

        private void UpdateSummary()
        {
            TotalLabel.Text  = _engine.State.Total.ToString();
            PassedLabel.Text = _engine.State.Passed.ToString();
            FailedLabel.Text = _engine.State.Failed.ToString();
        }

        private void UpdateBotHealth()
        {
            bool ok = _engine.CheckBotServiceReachable();
            var green = Color.FromRgb(57, 217, 138);
            var grey  = Color.FromRgb(85, 94,  112);
            BotHealthDot.Fill         = new SolidColorBrush(ok ? green : grey);
            BotHealthLabel.Text       = ok ? "Connected" : "Offline";
            BotHealthLabel.Foreground = new SolidColorBrush(ok ? green : grey);
        }
    }
}