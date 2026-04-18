using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MedicAIGUI.Services
{
    // ── Status enum ──────────────────────────────────────────────────────────
    public enum TestStatus { Pending, Running, Pass, Fail }

    // ── Per-test result row (bound to the ListBox) ────────────────────────────
    public class TestResultItem
    {
        public string     Name      { get; set; } = "";
        public TestStatus Status    { get; set; } = TestStatus.Pending;
        public string     Details   { get; set; } = "";
        public long       ElapsedMs { get; set; }
    }

    // ── Lightweight state snapshot ────────────────────────────────────────────
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

    // ── Main engine ───────────────────────────────────────────────────────────
    public class InspectorEngine
    {
        // ── Built-in rule set ────────────────────────────────────────────────
        public static readonly List<TestRule> DefaultRules = new()
        {
            new TestRule
            {
                Name                 = "Navigate → Dashboard",
                ClickTargetName      = "DashboardBtn",
                ExpectedViewTypeName = "DashboardView",
                RequiredControls     = new() { "StartBotBtn", "StopBotBtn", "LogOutput" },
                DelayMs              = 500
            },
            new TestRule
            {
                Name                 = "Navigate → Priority & Players",
                ClickTargetName      = "PriorityBtn",
                ExpectedViewTypeName = "PriorityPlayersView",
                RequiredControls     = new() { "PriorityList", "NewPlayerNameBox", "TierSelector" },
                DelayMs              = 500
            },
            new TestRule
            {
                Name                 = "Navigate → Settings",
                ClickTargetName      = "SettingsBtn",
                ExpectedViewTypeName = "SettingsView",
                RequiredControls     = new() { "BotIpInput", "PortInput" },
                DelayMs              = 500
            },
            new TestRule
            {
                Name                 = "Navigate → Neural Tuning",
                ClickTargetName      = "TuningBtn",
                ExpectedViewTypeName = "TuningView",
                RequiredControls     = new() { "SldRetreatHP", "SldUberPop", "ProfileSelector" },
                DelayMs              = 500
            },
            new TestRule
            {
                Name                 = "Navigate → Triage Matrix",
                ClickTargetName      = "MatrixBtn",
                ExpectedViewTypeName = "MatrixView",
                RequiredControls     = new() { "PlayerList", "SldPowerWeight" },
                DelayMs              = 500
            },
            new TestRule
            {
                Name                 = "Navigate → Loadout",
                ClickTargetName      = "LoadoutBtn",
                ExpectedViewTypeName = "LoadoutView",
                RequiredControls     = new() { "PrimaryWeaponList", "SecondaryWeaponList", "MeleeWeaponList" },
                DelayMs              = 500
            },
            new TestRule
            {
                Name                 = "Return → Dashboard (cleanup)",
                ClickTargetName      = "DashboardBtn",
                ExpectedViewTypeName = "DashboardView",
                RequiredControls     = new() { "StartBotBtn" },
                DelayMs              = 400
            }
        };

        // ── Observable collections for UI binding ────────────────────────────
        public ObservableCollection<TestResultItem> Results { get; } = new();
        public ObservableCollection<string>         Logs    { get; } = new();
        public InspectorState State { get; } = new();

        private Window?         _mainWindow;
        private ContentControl? _viewHost;

        // ── Must be called once after the InspectorView is loaded ────────────
        public void Attach(Window mainWindow)
        {
            _mainWindow = mainWindow;
            _viewHost   = FindByName<ContentControl>(mainWindow, "ViewHost")
                          ?? throw new InvalidOperationException("ViewHost not found.");
        }

        // ── Run all rules sequentially ───────────────────────────────────────
        public async Task RunAllAsync(CancellationToken ct = default)
        {
            if (_mainWindow == null) throw new InvalidOperationException("Call Attach() first.");

            Results.Clear();
            State.Total = State.Passed = State.Failed = 0;
            State.Total = DefaultRules.Count;

            foreach (var rule in DefaultRules)
                Results.Add(new TestResultItem { Name = rule.Name, Status = TestStatus.Pending });

            Log("═══════════════════════════════════");
            Log("  INSPECTOR PRO v5  —  RUN START");
            Log("═══════════════════════════════════");

            for (int i = 0; i < DefaultRules.Count; i++)
            {
                if (ct.IsCancellationRequested)
                {
                    Log("⏹  Cancelled.");
                    break;
                }
                await RunRuleAsync(DefaultRules[i], Results[i], ct);
            }

            Log("───────────────────────────────────");
            Log($"  DONE  Total:{State.Total}  Pass:{State.Passed}  Fail:{State.Failed}");
            Log("───────────────────────────────────");
        }

        // ── Run a single rule by index ───────────────────────────────────────
        public async Task RunSingleAsync(int index, CancellationToken ct = default)
        {
            if (_mainWindow == null) throw new InvalidOperationException("Call Attach() first.");
            if (index < 0 || index >= DefaultRules.Count) return;

            while (Results.Count <= index)
                Results.Add(new TestResultItem { Name = DefaultRules[Results.Count].Name });

            await RunRuleAsync(DefaultRules[index], Results[index], ct);
        }

        // ── Core rule executor ───────────────────────────────────────────────
        private async Task RunRuleAsync(TestRule rule, TestResultItem row, CancellationToken ct)
        {
            row.Status = TestStatus.Running;
            var sw     = Stopwatch.StartNew();
            bool pass  = true;
            var  errs  = new System.Text.StringBuilder();

            Log($"▶  [{rule.Name}]");

            // Step 1 — click
            await _mainWindow!.Dispatcher.InvokeAsync(() =>
            {
                State.LastClickedBtn = rule.ClickTargetName;
                bool clicked = ClickByName(rule.ClickTargetName);
                Log(clicked
                    ? $"   ↳ CLICK  {rule.ClickTargetName}  ✔"
                    : $"   ↳ CLICK  {rule.ClickTargetName}  ✘ (not found)");
                if (!clicked) { pass = false; errs.Append($"Button '{rule.ClickTargetName}' not found. "); }
            });

            // Step 2 — wait
            await Task.Delay(rule.DelayMs, CancellationToken.None);
            if (ct.IsCancellationRequested) return;

            // Step 3 — assertions
            await _mainWindow.Dispatcher.InvokeAsync(() =>
            {
                // View check
                if (!string.IsNullOrEmpty(rule.ExpectedViewTypeName))
                {
                    bool ok = AssertView(rule.ExpectedViewTypeName);
                    Log(ok
                        ? $"   ↳ VIEW   {rule.ExpectedViewTypeName}  ✔"
                        : $"   ↳ VIEW   expected '{rule.ExpectedViewTypeName}', got '{State.CurrentViewType}'  ✘");
                    if (!ok) { pass = false; errs.Append("Wrong view. "); }
                }

                // Control presence checks
                foreach (var ctrl in rule.RequiredControls)
                {
                    bool ok = AssertControlExists(ctrl);
                    Log(ok
                        ? $"   ↳ CTRL   {ctrl}  ✔"
                        : $"   ↳ CTRL   {ctrl}  ✘ (missing)");
                    if (!ok) { pass = false; errs.Append($"'{ctrl}' missing. "); }
                }
            });

            sw.Stop();
            row.ElapsedMs = sw.ElapsedMilliseconds;
            row.Details   = pass ? "All assertions passed." : errs.ToString().TrimEnd();
            row.Status    = pass ? TestStatus.Pass : TestStatus.Fail;

            if (pass) { State.Passed++; State.LastPassedTest = rule.Name; }
            else      { State.Failed++; State.LastFailedTest  = rule.Name; }

            Log(pass
                ? $"   → PASS  ({sw.ElapsedMilliseconds} ms)"
                : $"   → FAIL  {row.Details}");
        }

        // ── Assertions (must be called on UI thread) ──────────────────────────
        private bool AssertView(string expectedTypeName)
        {
            var content = _viewHost?.Content;
            State.CurrentViewType = content?.GetType().Name ?? "(null)";
            return string.Equals(State.CurrentViewType, expectedTypeName, StringComparison.OrdinalIgnoreCase);
        }

        public bool AssertControlExists(string controlName)
        {
            if (_viewHost?.Content is DependencyObject viewRoot)
                if (FindByName<FrameworkElement>(viewRoot, controlName) != null) return true;
            return _mainWindow != null && FindByName<FrameworkElement>(_mainWindow, controlName) != null;
        }

        public bool AssertEnabled(string controlName)
        {
            // Use FrameworkElement (not UIElement) to satisfy the generic constraint
            FrameworkElement? el = null;
            if (_viewHost?.Content is DependencyObject viewRoot)
                el = FindByName<FrameworkElement>(viewRoot, controlName);
            if (el == null && _mainWindow != null)
                el = FindByName<FrameworkElement>(_mainWindow, controlName);
            return el is UIElement uiel && uiel.IsEnabled;
        }

        // ── WPF-native click (no external automation library needed) ──────────
        private bool ClickByName(string name)
        {
            if (_mainWindow == null) return false;

            var rb = FindByName<RadioButton>(_mainWindow, name);
            if (rb != null) { rb.IsChecked = true; return true; }

            var btn = FindByName<Button>(_mainWindow, name);
            if (btn != null) { btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); return true; }

            return false;
        }

        // ── Visual-tree walker ────────────────────────────────────────────────
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

        // ── Extras: duplicate name detection ─────────────────────────────────
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
            for (int i = 0; i < n; i++) WalkTree(VisualTreeHelper.GetChild(node, i), visitor);
        }

        // ── Bot service health check ──────────────────────────────────────────
        public bool CheckBotServiceReachable()
        {
            try   { return MedicBotService.Instance.IsConnected; }
            catch { return false; }
        }

        // ── Internal log helper ───────────────────────────────────────────────
        private void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
            Application.Current?.Dispatcher.Invoke(() => Logs.Add(line));
            DebugHub.Log(msg);
        }
    }
}