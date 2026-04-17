using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MedicAIGUI.Services
{
    public static class PerformanceMonitor
    {
        private static bool _running;

        public static double LastFps { get; private set; }

        public static void Start()
        {
            if (_running) return;
            _running = true;

            Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                int frames = 0;

                while (_running)
                {
                    frames++;
                    await Task.Delay(16);

                    if (sw.ElapsedMilliseconds >= 1000)
                    {
                        LastFps = frames;
                        frames = 0;
                        sw.Restart();

                        DebugHub.Log($"FPS: {LastFps}");
                    }
                }
            });
        }

        public static void Stop() => _running = false;
    }
}