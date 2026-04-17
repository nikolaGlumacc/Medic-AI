using System;

namespace MedicAIGUI.Services
{
    public class TestResult
    {
        public string Name { get; set; }
        public bool Passed { get; set; }
        public string Details { get; set; }
        public DateTime Time { get; set; } = DateTime.Now;
    }
}