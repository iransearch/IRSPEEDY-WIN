using System;
using System.IO;
using System.Text;
using v2rayN.Handler;
using v2rayN.Mode;

namespace IRSpeedyVPN.Services.Hysteria
{
    public static class ConfigGenerator
    {
        private static readonly string DefaultTempDir =
            Path.Combine(Path.GetTempPath(), "IRSpeedy", "Hysteria","tmp");

        public static string GenerateConfigYaml(VmessItem item, int socksPort)
        {
            var sb = new StringBuilder();

            // server
            if (item.portEnd > item.port && item.port > 0)
            {
                sb.AppendLine($"server: {item.address}:{item.port}-{item.portEnd}");
            }
            else
            {
                var p = item.port > 0 ? item.port : 443;
                sb.AppendLine($"server: {item.address}:{p}");
            }

            sb.AppendLine();
            sb.AppendLine($"auth: {item.password ?? ""}");

            // tls - always include for hysteria2 (it requires TLS)
            bool hasSni = !string.IsNullOrWhiteSpace(item.sni);
            bool hasInsecure = !string.IsNullOrWhiteSpace(item.allowInsecure);
            if (hasSni || hasInsecure)
            {
                sb.AppendLine();
                sb.AppendLine("tls:");
                if (hasSni)
                    sb.AppendLine($"  sni: {item.sni}");
                if (hasInsecure)
                {
                    bool insecure;
                    bool.TryParse(item.allowInsecure, out insecure);
                    if (insecure || item.allowInsecure == "1")
                        sb.AppendLine("  insecure: true");
                }
            }

            // obfs
            if (!string.IsNullOrWhiteSpace(item.obfs_param))
            {
                var obfsType = !string.IsNullOrWhiteSpace(item.obfs) ? item.obfs : "salamander";
                sb.AppendLine();
                sb.AppendLine("obfs:");
                sb.AppendLine($"  type: {obfsType}");
                sb.AppendLine($"  {obfsType}:");
                sb.AppendLine($"    password: {item.obfs_param}");
            }

            // socks5
            sb.AppendLine();
            sb.AppendLine("socks5:");
            sb.AppendLine($"  listen: 127.0.0.1:{socksPort}");

            sb.AppendLine();
            sb.AppendLine("fastOpen: true");
            sb.AppendLine("lazy: true");

            return sb.ToString();
        }

        public static string WriteConfigFile(VmessItem item, int socksPort)
        {
            if (!Directory.Exists(DefaultTempDir))
                Directory.CreateDirectory(DefaultTempDir);

            var yaml = GenerateConfigYaml(item, socksPort);
            var filePath = Path.Combine(DefaultTempDir, $"hy2_{socksPort}.yaml");
            File.WriteAllText(filePath, yaml, Encoding.UTF8);
            File.SetAttributes(filePath, FileAttributes.Hidden);
            return filePath;
        }

        public static string WriteConfigFileFromLink(string link, int socksPort, out string msg, out string address)
        {
            var item = ShareHandler.ImportFromConfigLink(link, out msg);
            address = item?.address;
            if (item == null) return null;
            return WriteConfigFile(item, socksPort);
        }

        public static void CleanupConfigFile(string configPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath))
                    File.Delete(configPath);
            }
            catch { }
        }
        public static void CleanupAllConfigFile()
        {
            try
            {
                Directory.Delete(DefaultTempDir,true);
            }
            catch { }
        }
    }
}
