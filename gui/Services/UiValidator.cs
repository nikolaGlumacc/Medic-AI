using System;

namespace MedicAIGUI.Services
{
    public static class UiValidator
    {
        private static string _lastView = "";

        public static void SetView(string view)
        {
            _lastView = view;
        }

        public static bool ValidateNavigation(string expectedView)
        {
            bool ok = _lastView.Contains(expectedView);

            DebugHub.Log(ok
                ? $"VALIDATION_PASS: {expectedView}"
                : $"VALIDATION_FAIL: expected {expectedView}, got {_lastView}");

            return ok;
        }
    }
}