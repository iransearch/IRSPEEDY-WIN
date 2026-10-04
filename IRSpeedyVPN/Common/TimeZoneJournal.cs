using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;

namespace IRSpeedyVPN.Common
{
    internal sealed class TimeZoneJournal : ITimeZoneJournal
    {
        private readonly string path;
        internal TimeZoneJournal(string path) { this.path = path; }
        public TimeZoneSnapshot Read()
        {
            if (!File.Exists(path)) return null;
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Invalid time-zone recovery file.");
            var snapshot = JsonConvert.DeserializeObject<TimeZoneSnapshot>(File.ReadAllText(path), new JsonSerializerSettings { MaxDepth = 8 });
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.WindowsId)
                || string.IsNullOrWhiteSpace(snapshot.SerializedLocalZone)) throw new InvalidDataException("Invalid saved time zone.");
            return snapshot;
        }
        public void Write(TimeZoneSnapshot snapshot)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(snapshot));
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public void Clear() { File.Delete(path); }
    }
}
