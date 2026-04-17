using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;

namespace MedicAIGUI.Services
{
    public static class UiInspector
    {
        public static List<string> Scan(Window window)
        {
            var results = new List<string>();

            void Walk(DependencyObject parent)
            {
                int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);

                for (int i = 0; i < count; i++)
                {
                    var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);

                    if (child is Button b)
                        results.Add($"BUTTON:{b.Name}");

                    if (child is ListBox l)
                        results.Add($"LISTBOX:{l.Name}");

                    Walk(child);
                }
            }

            Walk(window);
            return results;
        }
    }
}