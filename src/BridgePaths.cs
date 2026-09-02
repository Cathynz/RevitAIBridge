using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace RevitAIBridge
{
    /// <summary>
    /// Single source of truth for the folder contract. See CONTRACT.md.
    /// </summary>
    internal static class BridgePaths
    {
        public const string TaskFileName = "current_task.csx";
        public const string FlagFileName = "run.flag";
        public const string ResultFileName = "last_result.txt";
        public const string StatusFileName = "bridge_status.json";

        private const string RootEnvVar = "REVITAI_BRIDGE_ROOT";

        /// <summary>
        /// Resolves the bridge root and guarantees it exists.
        /// LocalApplicationData is chosen over ApplicationData so the folder is
        /// not part of a roaming profile.
        /// </summary>
        public static string EnsureRoot()
        {
            var custom = Environment.GetEnvironmentVariable(RootEnvVar);

            var root = string.IsNullOrWhiteSpace(custom)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "RevitAIBridge")
                : custom.Trim();

            Directory.CreateDirectory(root);
            return root;
        }

        public static string Task(string root) => Path.Combine(root, TaskFileName);
        public static string Flag(string root) => Path.Combine(root, FlagFileName);
        public static string Result(string root) => Path.Combine(root, ResultFileName);
        public static string Status(string root) => Path.Combine(root, StatusFileName);

        /// <summary>
        /// Writes via a temporary file then moves into place, so a reader never
        /// sees a half-written file.
        /// </summary>
        public static void WriteAtomic(string path, string content)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content, new UTF8Encoding(false));

            if (File.Exists(path))
                File.Delete(path);

            File.Move(tmp, path);
        }

        public static string UtcNow() =>
            DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        /// <summary>
        /// Minimal JSON string escaping. Avoids taking a serialiser dependency
        /// into Revit's AppDomain for four fields.
        /// </summary>
        public static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length + 8);
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }
    }
}
