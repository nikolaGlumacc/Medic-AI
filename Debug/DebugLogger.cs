using System;
using System.Collections.ObjectModel;

namespace MedicAIGUI.Debug
{
    public static class DebugLogger
    {
        public static ObservableCollection<string> Logs { get; } = new();

        public static void Log(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            Logs.Add(line);
            Console.WriteLine(line);
        }
    }
}