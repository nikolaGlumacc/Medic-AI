using System;
using System.Threading.Tasks;

namespace MedicAIGUI.Debug
{
    public class DebugTest
    {
        public string Name;
        public Func<Task> Action;
        public Func<Task<bool>> Validate;
    }
}