using System.Collections.Generic;

namespace MedicAIGUI.Services
{
    /// <summary>
    /// Defines one automated UI test rule.
    /// </summary>
    public class TestRule
    {
        /// <summary>Human-readable test name shown in the Inspector panel.</summary>
        public string Name { get; set; } = "";

        /// <summary>x:Name of the RadioButton/Button to click (e.g. "DashboardBtn").</summary>
        public string ClickTargetName { get; set; } = "";

        /// <summary>
        /// Expected view type name after navigation (e.g. "DashboardView").
        /// Matched against ContentControl.Content.GetType().Name.
        /// </summary>
        public string ExpectedViewTypeName { get; set; } = "";

        /// <summary>
        /// x:Names of controls that MUST exist in the visual tree after navigation.
        /// </summary>
        public List<string> RequiredControls { get; set; } = new();

        /// <summary>Milliseconds to wait after click before assertions. Default 400.</summary>
        public int DelayMs { get; set; } = 400;
    }
}