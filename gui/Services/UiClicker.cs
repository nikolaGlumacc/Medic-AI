using System;
using System.Windows.Automation;

namespace MedicAIGUI.Services
{
    public static class UiClicker
    {
        public static void ClickButtonByName(string name)
        {
            var root = AutomationElement.RootElement;

            var condition = new PropertyCondition(AutomationElement.NameProperty, name);
            var element = root.FindFirst(TreeScope.Descendants, condition);

            if (element == null)
            {
                DebugHub.Log($"CLICK_FAIL: {name} not found");
                return;
            }

            var invoke = element.GetCurrentPattern(InvokePattern.Pattern) as InvokePattern;

            if (invoke == null)
            {
                DebugHub.Log($"CLICK_FAIL: {name} not clickable");
                return;
            }

            invoke.Invoke();
            DebugHub.Log($"AUTO_CLICK: {name}");
        }
    }
}