using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace IRSpeedyVPN.UserControls
{
    public partial class ConnectionTrafficPanel
    {
        // Retain the supplied screenshot fixture for design previews. The connected
        // screen displays live traffic only; fixtures never enter its ledger or totals.
        private static List<TrafficPreviewRow> CreatePreviewRows()
        {
            return new List<TrafficPreviewRow>
            {
                new TrafficPreviewRow("firefox.exe", 18.41m, "MiB", 2.05m, "MiB", 20.47m, "MiB"),
                new TrafficPreviewRow("IDMan.exe", 5.58m, "MiB", 288.25m, "KiB", 5.86m, "MiB"),
                new TrafficPreviewRow("Telegram.exe", 105.38m, "KiB", 38.91m, "KiB", 144.28m, "KiB"),
                new TrafficPreviewRow("FoxitReader.exe", 0m, "B", 22.81m, "KiB", 22.81m, "KiB"),
                new TrafficPreviewRow("taskhostw.exe", 5.20m, "KiB", 8.82m, "KiB", 14.02m, "KiB"),
                new TrafficPreviewRow("Throne.exe", 921m, "B", 250m, "B", 1.14m, "KiB"),
                new TrafficPreviewRow("svchost.exe", 374m, "B", 446m, "B", 820m, "B")
            };
        }

        // Binding paths and SortMemberPath are strings in XAML; keep them through packing.
        [Obfuscation(Exclude = true, ApplyToMembers = true)]
        private sealed class TrafficPreviewRow
        {
            public TrafficPreviewRow(string name, decimal download, string downloadUnit,
                decimal upload, string uploadUnit, decimal total, string totalUnit)
            {
                Name = name;
                Download = new TrafficPreviewAmount(download, downloadUnit);
                Upload = new TrafficPreviewAmount(upload, uploadUnit);
                Total = new TrafficPreviewAmount(total, totalUnit);
            }

            public string Name { get; }
            public TrafficPreviewAmount Download { get; }
            public TrafficPreviewAmount Upload { get; }
            public TrafficPreviewAmount Total { get; }
            public decimal DownloadBytes => Download.Bytes;
            public decimal UploadBytes => Upload.Bytes;
            public decimal TotalBytes => Total.Bytes;
        }

        [Obfuscation(Exclude = true, ApplyToMembers = true)]
        private sealed class TrafficPreviewAmount
        {
            public TrafficPreviewAmount(decimal value, string unit)
            {
                ValueText = value.ToString("0.00", CultureInfo.InvariantCulture);
                Unit = unit;
                Bytes = value * (unit == "MiB" ? 1048576m : unit == "KiB" ? 1024m : 1m);
            }

            public string ValueText { get; }
            public string Unit { get; }
            public decimal Bytes { get; }
        }
    }
}
