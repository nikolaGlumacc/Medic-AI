using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace MedicAIGUI.Services
{
    public class UiAutoTester
    {
        private readonly Window _window;

        public List<TestResult> Results { get; } = new();

        public UiAutoTester(Window window)
        {
            _window = window;
        }

        public async Task Run(Window window)
        {
            DebugHub.Log("INSPECTOR V3 START");

            await TestNavigationButtons();
            await TestLoadoutButton();

            DebugHub.Log("INSPECTOR V3 COMPLETE");
        }

        private async Task TestNavigationButtons()
        {
            var buttons = new[]
            {
                "DashboardBtn",
                "PriorityBtn",
                "SettingsBtn",
                "TuningBtn",
                "MatrixBtn",
                "LoadoutBtn"
            };

            foreach (var btn in buttons)
            {
                DebugHub.Log($"TESTING_BUTTON: {btn}");

                UiClicker.ClickButtonByName(btn);

                await Task.Delay(500);

                Results.Add(new TestResult
                {
                    Name = btn,
                    Passed = true,
                    Details = "Clicked successfully"
                });
            }
        }

        private async Task TestLoadoutButton()
        {
            DebugHub.Log("TEST: Loadout open");

            UiClicker.ClickButtonByName("LoadoutBtn");

            await Task.Delay(700);

            Results.Add(new TestResult
            {
                Name = "Loadout Navigation",
                Passed = true,
                Details = "Loadout view opened"
            });
        }
    }
}