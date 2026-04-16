using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MedicAIGUI
{
    public class AudioEvent
    {
        public string Label { get; set; } = "";
        public string Key { get; set; } = "";
        public bool Enabled { get; set; }
        public double Volume { get; set; }
        public string CustomFilePath { get; set; } = "Default.mp3";
    }

    public class VoiceLine
    {
        public string Label { get; set; } = "";
        public string Key { get; set; } = "";
        public bool Enabled { get; set; }
        public double Volume { get; set; }
        public bool TtsFallbackEnabled { get; set; }
        public double TtsVolume { get; set; } = 80;
        public string CustomFilePath { get; set; } = "Bot_Fallback.mp3";
    }

    public class PriorityPlayer
    {
        public string Name { get; set; } = "";
        public int Tier { get; set; } = 1;
        public int Deaths { get; set; }
        public string StatusIcon { get; set; } = "✚";
    }

    public class BehaviorProfile
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public double AggressionFactor { get; set; }
        public bool MirroringEnabled { get; set; }
        public bool StealthMode { get; set; }
    }

    public class TelemetrySnapshot
    {
        public double UberPercent { get; set; }
        public int HealingOutput { get; set; }
        public int MeleeKills { get; set; }
        public string State { get; set; } = "IDLE";
        public int Latency { get; set; }
        public bool IsRunning { get; set; }
        public List<double> TargetHPGraph { get; set; } = new List<double>();
    }
}
