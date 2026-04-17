using System.Collections.Generic;
using System.Threading.Tasks;

namespace MedicAIGUI.Debug
{
    public class BotDebugger
    {
        public List<DebugTest> Tests = new();

        public void Add(DebugTest test)
        {
            Tests.Add(test);
        }

        public async Task RunAll()
        {
            foreach (var test in Tests)
            {
                DebugLogger.Log("RUN: " + test.Name);

                await test.Action();

                await Task.Delay(300);

                bool ok = await test.Validate();

                DebugLogger.Log(ok
                    ? "PASS: " + test.Name
                    : "FAIL: " + test.Name);
            }
        }
    }
}