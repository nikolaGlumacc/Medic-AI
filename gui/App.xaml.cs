using System.IO;
using System.Windows;
using System;

namespace MedicAIGUI;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Set up global crash logging to find the silent killer
        AppDomain.CurrentDomain.UnhandledException += (s, ev) => {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CRASH_LOG.txt");
            File.WriteAllText(logPath, "FATAL ERROR:\n" + ev.ExceptionObject.ToString());
        };
        
        base.OnStartup(e);
    }
}
