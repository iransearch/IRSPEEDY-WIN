// Adapted from Moonlight Tunneling at 5268937 (MIT); see docs/third-party.
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace IRSpeedyVPN.Services.SplitTunneling
{
    internal static class AppPathPattern
    {
        // Go RE2 rejects the escaped spaces produced by Regex.Escape on .NET.
        public static string Escape(string value)
        {
            var result = new StringBuilder();
            foreach (char c in value)
            {
                if (@"\.+*?()|[]{}^$".IndexOf(c) >= 0) result.Append('\\');
                result.Append(c);
            }
            return result.ToString();
        }

        public static bool IsLocalPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && path.Length <= 2048
                && Regex.IsMatch(path, @"^[A-Za-z]:\\")
                && path.IndexOfAny(new[] { '\0', '\r', '\n', '*', '?', '"', '<', '>', '|' }) < 0
                && path.IndexOf(':', 2) < 0
                && !Regex.IsMatch(path, @"(?:^|\\)\.{1,2}(?:\\|$)");
        }

        public static string ForApp(SplitTunnelApp app)
        {
            if (app == null || !IsLocalPath(app.Path)) return null;
            if (app.Kind == AppMatchKind.ExactPath)
                return app.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? "(?i)^" + Escape(app.Path) + "$" : null;
            if (!IsLocalPath(app.Root) || app.Root.TrimEnd('\\').Length <= 3) return null;
            if (app.Kind == AppMatchKind.Folder)
                return !string.Equals(app.Path.TrimEnd('\\'), app.Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? null : "(?i)^" + Escape(app.Root.TrimEnd('\\')) + @"\\.+\.exe$";
            if (app.Kind != AppMatchKind.Versioned || string.IsNullOrEmpty(app.ExeName)
                || app.ExeName.IndexOfAny(new[] { '\\', '/', ':', '\r', '\n', '\0' }) >= 0
                || !app.ExeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            return "(?i)^" + Escape(app.Root.TrimEnd('\\')) + @"\\app-[^\\]+\\" + Escape(app.ExeName) + "$";
        }

        public static string NormalizeExecutable(string value)
        {
            try
            {
                var path = System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')));
                return IsLocalPath(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is NotSupportedException || ex is System.Security.SecurityException) { return null; }
        }
    }
}
