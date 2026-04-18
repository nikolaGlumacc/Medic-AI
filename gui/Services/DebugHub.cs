using System;
using System.Collections.ObjectModel;

namespace MedicAIGUI.Services
{
    public static class DebugHub
    {
        public static ObservableCollection<string> Events { get; } = new();

        public static void Log(string msg)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
            {
                Events.Add(line);
                if (Events.Count > 800)
                    Events.RemoveAt(0);
            });
        }

        public static void Clear() => Events.Clear();
    }
}