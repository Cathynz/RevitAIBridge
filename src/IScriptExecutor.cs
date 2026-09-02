using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitAIBridge
{
    /// <summary>
    /// Everything behind this interface is the part we have not spiked yet.
    /// The plumbing (watcher, external event, folder contract) does not care
    /// how the script actually runs, so Roslyn can be dropped in later without
    /// touching anything else.
    /// </summary>
    internal interface IScriptExecutor
    {
        /// <param name="uiApp">A valid Revit API context, supplied by the external event.</param>
        /// <param name="source">Contents of current_task.csx.</param>
        /// <returns>Text to place in the OK payload of last_result.txt.</returns>
        string Execute(UIApplication uiApp, string source);
    }

    /// <summary>
    /// Placeholder executor. Proves the whole loop end to end — watcher, event,
    /// API context, atomic result — without compiling anything.
    ///
    /// Recognizes one built-in diagnostic command, REPORT_COORDS:&lt;name filter&gt;,
    /// so link positioning can be checked through the folder contract instead of
    /// the Revit UI. Everything else still just echoes.
    /// Replace once the Roslyn-in-Revit spike is settled.
    /// </summary>
    internal sealed class EchoScriptExecutor : IScriptExecutor
    {
        private const string ReportCoordsPrefix = "REPORT_COORDS:";

        public string Execute(UIApplication uiApp, string source)
        {
            var doc = uiApp.ActiveUIDocument?.Document;
            var trimmed = (source ?? string.Empty).Trim();

            if (trimmed.StartsWith(ReportCoordsPrefix, StringComparison.OrdinalIgnoreCase))
                return ReportLinkCoordinates(doc, trimmed.Substring(ReportCoordsPrefix.Length).Trim());

            var lines = source == null ? 0 : source.Split('\n').Length;

            return
                "Echo executor — no script was compiled.\n" +
                "Revit version: " + uiApp.Application.VersionNumber + "\n" +
                "Active document: " + (doc?.Title ?? "(none)") + "\n" +
                "Task length: " + (source?.Length ?? 0) + " chars, " + lines + " lines";
        }

        private static string ReportLinkCoordinates(Document doc, string nameFilter)
        {
            if (doc == null)
                return "No active document.";

            var sb = new StringBuilder();
            var found = 0;

            var links = new FilteredElementCollector(doc)
                .OfClass(typeof(RevitLinkInstance))
                .Cast<RevitLinkInstance>();

            foreach (var link in links)
            {
                var name = ElementTypeName(doc, link);
                if (!Matches(name, nameFilter))
                    continue;

                found++;
                AppendReport(sb, doc, "RevitLink: " + name, link.GetTotalTransform());
            }

            var imports = new FilteredElementCollector(doc)
                .OfClass(typeof(ImportInstance))
                .Cast<ImportInstance>();

            foreach (var import in imports)
            {
                var name = ElementTypeName(doc, import);
                if (!Matches(name, nameFilter))
                    continue;

                found++;
                AppendReport(sb, doc, "CADLink: " + name, import.GetTransform());
            }

            if (found == 0)
                return "No link found matching '" + nameFilter + "'.";

            return sb.ToString();
        }

        private static string ElementTypeName(Document doc, Element instance)
        {
            var type = doc.GetElement(instance.GetTypeId());
            return type?.Name ?? instance.Name ?? string.Empty;
        }

        private static bool Matches(string name, string nameFilter) =>
            string.IsNullOrEmpty(nameFilter) ||
            (!string.IsNullOrEmpty(name) && name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0);

        private static void AppendReport(StringBuilder sb, Document doc, string label, Transform transform)
        {
            var origin = transform.Origin;
            var rotationDeg = Math.Atan2(transform.BasisX.Y, transform.BasisX.X) * 180.0 / Math.PI;

            sb.Append(label).Append('\n');
            sb.Append("  Internal origin (ft): ").Append(FormatXyz(origin)).Append('\n');
            sb.Append("  Rotation from project X: ").Append(rotationDeg.ToString("F4", CultureInfo.InvariantCulture)).Append(" deg\n");

            var projectLocation = doc.ActiveProjectLocation;
            if (projectLocation != null)
            {
                var position = projectLocation.GetProjectPosition(origin);
                sb.Append("  Shared coordinates — E: ").Append(FormatFeet(position.EastWest))
                  .Append(", N: ").Append(FormatFeet(position.NorthSouth))
                  .Append(", Elev: ").Append(FormatFeet(position.Elevation))
                  .Append(", Angle: ").Append((position.Angle * 180.0 / Math.PI).ToString("F4", CultureInfo.InvariantCulture)).Append(" deg\n");
            }

            sb.Append('\n');
        }

        private static string FormatXyz(XYZ p) =>
            "(" + FormatFeet(p.X) + ", " + FormatFeet(p.Y) + ", " + FormatFeet(p.Z) + ")";

        private static string FormatFeet(double value) => value.ToString("F4", CultureInfo.InvariantCulture);
    }
}
