using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace IRSpeedyVPN.Services.SplitTunneling
{
    internal enum AppMatchKind { ExactPath, Versioned, Folder }

    internal sealed class SplitTunnelApp
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Source { get; set; }
        public string ShortcutPath { get; set; }
        public AppMatchKind Kind { get; set; }
        public string Root { get; set; }
        public string ExeName { get; set; }
        [JsonIgnore] public string Identity => Kind == AppMatchKind.ExactPath
            ? Path : Kind + "|" + Root + "|" + ExeName;
    }

    internal sealed class SplitTunnelSettings
    {
        public int Version { get; set; } = 2;
        public bool Enabled { get; set; }
        public List<SplitTunnelApp> Apps { get; set; } = new List<SplitTunnelApp>();
    }
}
