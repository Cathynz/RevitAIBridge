using System;
using System.Text;

namespace RevitAIBridge
{
    internal enum BridgeState
    {
        Idle,
        Busy,
        Error
    }

    /// <summary>
    /// Writes bridge_status.json. An agent reads this before writing a task —
    /// a stale updated_utc means Revit is closed.
    /// </summary>
    internal static class BridgeStatus
    {
        public const int ContractVersion = 1;

        public static void Write(string root, BridgeState state, string revitVersion, string documentTitle = null)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"state\": \"").Append(state.ToString().ToLowerInvariant()).Append("\",\n");
            sb.Append("  \"revit_version\": \"").Append(BridgePaths.EscapeJson(revitVersion)).Append("\",\n");
            sb.Append("  \"document\": \"").Append(BridgePaths.EscapeJson(documentTitle ?? string.Empty)).Append("\",\n");
            sb.Append("  \"updated_utc\": \"").Append(BridgePaths.UtcNow()).Append("\",\n");
            sb.Append("  \"contract_version\": ").Append(ContractVersion).Append('\n');
            sb.Append("}\n");

            try
            {
                BridgePaths.WriteAtomic(BridgePaths.Status(root), sb.ToString());
            }
            catch (Exception)
            {
                // Status is advisory. Never let a heartbeat failure break the task loop.
            }
        }
    }
}
