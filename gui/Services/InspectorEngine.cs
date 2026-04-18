using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MedicAIGUI.Services
{
    // ── Status for each row ────────────────────────────────────────────────────
    public enum TestStatus { Pending, Running, Pass, Fail }

    // ── Per-test result — MUST implement INotifyPropertyChanged so the
    //    DataTemplate re-renders when Status / Details / ElapsedMs change ───────
    public class TestResultItem : INotifyPropertyChanged
    {
        private string     _name      = "";
        private TestStatus _status    = TestStatus.Pending;
        private string     _details   = "";
        private long       _elapsedMs;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public TestStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }

        public string Details
        {
            get => _details;
            set { _details = value; OnPropertyChanged(); }
        }

        public long ElapsedMs
        {
            get => _elapsedMs;
            set { _elapsedMs = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── Lightweight run state ──────────────────────────────────────────────────
    public class InspectorState
    {
        public string CurrentViewType { get; set; } = "";
        public string LastClickedBtn  { get; set; } = "";
        public string LastPassedTest  { get; set; } = "";
        public string LastFailedTest  { get; set; } = "";
        public int    Total           { get; set; }
        public int    Passed          { get; set; }
        public int    Failed          { get; set; }
    }

    // ── Engine ─────────────────────────────────────────────────────────────────
    public class InspectorEngine
    {
        public static InspectorEngine Instance { get; } = new();

        private InspectorEngine() { }

        // ── Rule definitions ──────────────────────────────────────────────────
        public static readonly List<TestRule> DefaultRules = new()
        {
            new TestRule
            {
                Name                 = "Navigate → Dashboard",
                ClickTargetName      = "DashboardBtn",
                ExpectedViewTypeName = "DashboardView",
                RequiredControls     = new() { "StartBotBtn", "StopBotBtn", "LogOutput" },
                DelayMs              = 600
            },
            new TestRule
            {
                Name                 = "Navigate → Priority & Players",
                ClickTargetName      = "PriorityBtn",
                ExpectedViewTypeName = "PriorityPlayersView",
                RequiredControls     = new() { "PriorityList", "NewPlayerNameBox", "TierSelector" },
                DelayMs              = 600
            },
            new TestRule
            {
                Name                 = "Navigate → Settings",
                ClickTargetName      = "SettingsBtn",
                ExpectedViewTypeName = "SettingsView",
                RequiredControls     = new() { "BotIpInput", "PortInput" },
                DelayMs              = 600
            },
            new TestRule
            {
                Name                 = "Navigate → Neural Tuning",
                ClickTargetName      = "TuningBtn",
                ExpectedViewTypeName = "TuningView",
                RequiredControls     = new() { "SldRetreatHP", "SldUberPop", "ProfileSelector" },
                DelayMs              = 600
            },
            new TestRule
            {
                Name                 = "Navigate → Triage Matrix",
                ClickTargetName      = "MatrixBtn",
                ExpectedViewTypeName = "MatrixView",
                RequiredControls     = new() { "PlayerList", "SldPowerWeight" },
                DelayMs              = 600
            },
            new TestRule
            {
                Name                 = "Navigate → Loadout",
                ClickTargetName      = "LoadoutBtn",
                ExpectedViewTypeName = "LoadoutView",
                RequiredControls     = new() { "PrimaryWeaponList", "SecondaryWeaponList", "MeleeWeaponList" },
                DelayMs              = 600
            },
            new TestRule
            {
                Name                 = "Return → Dashboard (cleanup)",
                ClickTargetName      = "DashboardBtn",
                ExpectedViewTypeName = "DashboardView",
                RequiredControls     = new() { "StartBotBtn" },
                DelayMs              = 500
            },
            new TestRule
            {
                Name                 = "Integration → Service Handshake",
                ClickTargetName      = "DashboardBtn",
                DelayMs              = 500
            },
            new TestRule
            {
                Name                 = "Integration → Input Driver Test",
                ClickTargetName      = "DashboardBtn",
                DelayMs              = 3000
            },
            new TestRule
            {
                Name                 = "Integration → Command Cycle (Start/Stop)",
                ClickTargetName      = "DashboardBtn",
                DelayMs              = 2000
            }
        };

        // ── Collections bound to the Inspector UI ─────────────────────────────
        public ObservableCollection<TestResultItem> Results { get; } = new();
        public ObservableCollection<string>         Logs    { get; } = new();
        public InspectorState State { get; } = new();

        private Window?         _mainWindow;
        private ContentControl? _viewHost;
        private bool            _testInputConfirmed;

        // ── Must be called once after InspectorView is loaded ─────────────────
        public void Attach(Window mainWindow)
        {
            _mainWindow = mainWindow;
            _viewHost   = FindByName<ContentControl>(mainWindow, "ViewHost")
                          ?? throw new InvalidOperationException("ViewHost not found in MainWindow.");

            // Listen for bot confirmation
            MedicBotService.Instance.OnActivity += msg =>
            {
                if (msg == "TEST_INPUT_SUCCESS")
                    _testInputConfirmed = true;
            };
        }

        // ── Run every rule ────────────────────────────────────────────────────
        public async Task RunAllAsync(CancellationToken ct = default)
        {
            if (_mainWindow == null)
                throw new InvalidOperationException("Call Attach() first.");

            // Reset state
            Results.Clear();
            State.Total = State.Passed = State.Failed = 0;
            State.Total = DefaultRules.Count;

            // Pre-seed all rows as Pending so the list appears immediately
            foreach (var rule in DefaultRules)
                Results.Add(new TestResultItem { Name = rule.Name, Status = TestStatus.Pending });

            Log("══════════════════════════════════════");
            Log("  INSPECTOR PRO v5  ·  RUN START");
            Log("══════════════════════════════════════");

            for (int i = 0; i < DefaultRules.Count; i++)
            {
                if (ct.IsCancellationRequested) { Log("⏹  Cancelled."); break; }
                await RunRuleAsync(DefaultRules[i], Results[i], ct);
            }

            Log("──────────────────────────────────────");
            Log($"  DONE  Total:{State.Total}  Pass:{State.Passed}  Fail:{State.Failed}");
            Log("──────────────────────────────────────");
        }

        // ── Core rule executor ────────────────────────────────────────────────
        private async Task RunRuleAsync(TestRule rule, TestResultItem row, CancellationToken ct)
        {
            // Mark as running immediately so the yellow dot appears
            await _mainWindow!.Dispatcher.InvokeAsync(() =>
                row.Status = TestStatus.Running);

            var sw    = Stopwatch.StartNew();
            bool pass = true;
            var  errs = new StringBuilder();

            Log($"▶  [{rule.Name}]");

            // Integration tests handler
            if (rule.Name.StartsWith("Integration →"))
            {
                await RunIntegrationStepAsync(rule, row, ct);
                return;
            }

            // ── Step 1: click the nav button ──────────────────────────────────
            bool clicked = false;
            await _mainWindow.Dispatcher.InvokeAsync(() =>
            {
                State.LastClickedBtn = rule.ClickTargetName;
                clicked = ClickByName(rule.ClickTargetName);
                Log(clicked
                    ? $"   ↳ CLICK  {rule.ClickTargetName}  ✔"
                    : $"   ↳ CLICK  {rule.ClickTargetName}  ✘  (not found)");
            });

            if (!clicked)
            {
                pass = false;
                errs.Append($"Button '{rule.ClickTargetName}' not found. ");
            }

            // ── Step 2: wait for view to load ─────────────────────────────────
            await Task.Delay(rule.DelayMs, CancellationToken.None);
            if (ct.IsCancellationRequested) return;

            // ── Step 3: assertions on UI thread ──────────────────────────────
            await _mainWindow.Dispatcher.InvokeAsync(() =>
            {
                // View type assertion
                if (!string.IsNullOrEmpty(rule.ExpectedViewTypeName))
                {
                    bool ok = AssertView(rule.ExpectedViewTypeName);
                    Log(ok
                        ? $"   ↳ VIEW   {rule.ExpectedViewTypeName}  ✔"
                        : $"   ↳ VIEW   expected '{rule.ExpectedViewTypeName}', got '{State.CurrentViewType}'  ✘");
                    if (!ok) { pass = false; errs.Append($"Wrong view (got {State.CurrentViewType}). "); }
                }

                // Required controls
                foreach (var ctrl in rule.RequiredControls)
                {
                    bool ok = AssertControlExists(ctrl);
                    Log(ok
                        ? $"   ↳ CTRL   {ctrl}  ✔"
                        : $"   ↳ CTRL   {ctrl}  ✘  (missing)");
                    if (!ok) { pass = false; errs.Append($"'{ctrl}' missing. "); }
                }
            });

            sw.Stop();

            // ── Update the result row — INotifyPropertyChanged fires the UI ───
            await _mainWindow.Dispatcher.InvokeAsync(() =>
            {
                row.ElapsedMs = sw.ElapsedMilliseconds;
                row.Details   = pass ? "All assertions passed." : errs.ToString().TrimEnd();
                row.Status    = pass ? TestStatus.Pass : TestStatus.Fail;
            });

            if (pass) { State.Passed++; State.LastPassedTest = rule.Name; }
            else      { State.Failed++; State.LastFailedTest  = rule.Name; }

            Log(pass
                ? $"   → PASS  ({sw.ElapsedMilliseconds} ms)"
                : $"   → FAIL  {row.Details}");
        }

        private async Task RunIntegrationStepAsync(TestRule rule, TestResultItem row, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            bool pass = false;
            string details = "";

            if (rule.Name.Contains("Handshake"))
            {
                pass = MedicBotService.Instance.IsConnected || MedicBotService.Instance.IsSimulationMode;
                details = MedicBotService.Instance.IsSimulationMode ? "Simulation Mode active (Virtual Bot)." : 
                         (pass ? "Socket connection alive." : "Socket disconnected. Ensure bot is running.");
                Log(pass ? "   ↳ SOCKET  OPEN  ✔" : "   ↳ SOCKET  error  ✘");
            }
            else if (rule.Name.Contains("Input Driver"))
            {
                if (!MedicBotService.Instance.IsConnected && !MedicBotService.Instance.IsSimulationMode)
                {
                    pass = false;
                    details = "Skipped — Socket disconnected.";
                }
                else
                {
                    _testInputConfirmed = false;
                    Log(MedicBotService.Instance.IsSimulationMode ? "   ↳ ACTION  sending 'test_input' (simulated)..." : "   ↳ ACTION  sending 'test_input'...");
                    await MedicBotService.Instance.SendTestInputAsync();

                    // Wait for confirmation or timeout
                    var timeout = DateTime.Now.AddSeconds(4);
                    while (DateTime.Now < timeout && !_testInputConfirmed && !ct.IsCancellationRequested)
                    {
                        await Task.Delay(100);
                    }

                    pass = _testInputConfirmed;
                    details = pass ? "Bot confirmed mouse/key execution." : "No confirmation from bot (timeout).";
                    Log(pass ? "   ↳ DRIVER  CONFIRMED  ✔" : "   ↳ DRIVER  TIMEOUT  ✘");
                }
            }
            else if (rule.Name.Contains("Command Cycle"))
            {
                if (!MedicBotService.Instance.IsConnected && !MedicBotService.Instance.IsSimulationMode)
                {
                    pass = false;
                    details = "Skipped — Socket disconnected.";
                }
                else
                {
                    // Test Start
                    Log(MedicBotService.Instance.IsSimulationMode ? "   ↳ ACTION  sending 'start' (simulated)..." : "   ↳ ACTION  sending 'start'...");
                    await MedicBotService.Instance.StartBotAsync();
                    await Task.Delay(1200); 
                    bool started = (bool?)(MedicBotService.Instance.LastTelemetry?["running"]) ?? false;

                    // Test Stop
                    Log(MedicBotService.Instance.IsSimulationMode ? "   ↳ ACTION  sending 'stop' (simulated)..." : "   ↳ ACTION  sending 'stop'...");
                    await MedicBotService.Instance.StopBotAsync();
                    await Task.Delay(1200);
                    bool stopped = !((bool?)(MedicBotService.Instance.LastTelemetry?["running"]) ?? true);

                    pass = started && stopped;
                    details = pass ? "Start/Stop commands verified via telemetry." : $"Cycle failed (Started:{started}, Stopped:{stopped}).";
                    Log(pass ? "   ↳ CYCLE   VERIFIED  ✔" : "   ↳ CYCLE   FAILED  ✘");
                }
            }

            sw.Stop();
            await _mainWindow!.Dispatcher.InvokeAsync(() =>
            {
                row.ElapsedMs = sw.ElapsedMilliseconds;
                row.Details = details;
                row.Status = pass ? TestStatus.Pass : TestStatus.Fail;
            });

            if (pass) { State.Passed++; State.LastPassedTest = rule.Name; }
            else { State.Failed++; State.LastFailedTest = rule.Name; }

            Log(pass ? $"   → PASS  ({sw.ElapsedMilliseconds} ms)" : $"   → FAIL  {details}");
        }

        // ── Assertions (called on UI thread) ──────────────────────────────────
        private bool AssertView(string expectedTypeName)
        {
            State.CurrentViewType = _viewHost?.Content?.GetType().Name ?? "(null)";
            return string.Equals(State.CurrentViewType, expectedTypeName,
                                 StringComparison.OrdinalIgnoreCase);
        }

        public bool AssertControlExists(string controlName)
        {
            if (_viewHost?.Content is DependencyObject viewRoot &&
                FindByName<FrameworkElement>(viewRoot, controlName) != null)
                return true;

            return _mainWindow != null &&
                   FindByName<FrameworkElement>(_mainWindow, controlName) != null;
        }

        public bool AssertEnabled(string controlName)
        {
            FrameworkElement? el = null;
            if (_viewHost?.Content is DependencyObject viewRoot)
                el = FindByName<FrameworkElement>(viewRoot, controlName);
            if (el == null && _mainWindow != null)
                el = FindByName<FrameworkElement>(_mainWindow, controlName);
            return el is UIElement uiel && uiel.IsEnabled;
        }

        public bool AssertText(string controlName, string expected)
        {
            FrameworkElement? el = null;
            if (_viewHost?.Content is DependencyObject viewRoot)
                el = FindByName<FrameworkElement>(viewRoot, controlName);
            if (el == null && _mainWindow != null)
                el = FindByName<FrameworkElement>(_mainWindow, controlName);

            if (el is TextBlock tb) return tb.Text == expected;
            if (el is TextBox tbx) return tbx.Text == expected;
            if (el is ContentControl cc) return cc.Content?.ToString() == expected;

            return false;
        }

        // ── WPF-native click — no UIAutomation dependency ──────────────────────
        private bool ClickByName(string name)
        {
            if (_mainWindow == null) return false;

            var rb = FindByName<RadioButton>(_mainWindow, name);
            if (rb != null) { rb.IsChecked = true; return true; }

            var btn = FindByName<Button>(_mainWindow, name);
            if (btn != null) { btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return true; }

            return false;
        }

        // ── Visual-tree search (FrameworkElement constraint everywhere) ─────────
        public static T? FindByName<T>(DependencyObject? root, string name)
            where T : FrameworkElement
        {
            if (root == null) return null;
            if (root is T fe && fe.Name == name) return fe;

            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var hit = FindByName<T>(VisualTreeHelper.GetChild(root, i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        // ── Extras ────────────────────────────────────────────────────────────
        public List<string> DetectDuplicateNames()
        {
            if (_mainWindow == null) return new();
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            WalkTree(_mainWindow, obj =>
            {
                if (obj is FrameworkElement fe && !string.IsNullOrEmpty(fe.Name))
                    seen[fe.Name] = seen.TryGetValue(fe.Name, out int c) ? c + 1 : 1;
            });
            var dupes = new List<string>();
            foreach (var kv in seen)
                if (kv.Value > 1) dupes.Add($"{kv.Key} (×{kv.Value})");
            return dupes;
        }

        private static void WalkTree(DependencyObject node, Action<DependencyObject> visitor)
        {
            visitor(node);
            int n = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++)
                WalkTree(VisualTreeHelper.GetChild(node, i), visitor);
        }

        public bool CheckBotServiceReachable()
        {
            try   { return MedicBotService.Instance.IsConnected || MedicBotService.Instance.IsSimulationMode; }
            catch { return false; }
        }

        // ── Log ───────────────────────────────────────────────────────────────
        private void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            Application.Current?.Dispatcher.Invoke(() => Logs.Add(line));
            DebugHub.Log(msg);
        }
    }
}