using System;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace RevitAIBridge
{
    /// <summary>
    /// Globals exposed to every script — the variables current_task.csx can use
    /// without any setup: Doc, UiDoc, UiApp, App.
    /// </summary>
    public sealed class ScriptGlobals
    {
        public UIApplication UiApp { get; set; }
        public Application App { get; set; }
        public UIDocument UiDoc { get; set; }
        public Document Doc { get; set; }
    }

    /// <summary>
    /// Compiles and runs current_task.csx as a real C# script with the full
    /// Revit API in scope. Scripts manage their own transactions — this
    /// executor does not wrap one, so a read-only query needs none and a
    /// model edit must open its own via `using (var t = new Transaction(Doc, "...")) { ... }`.
    /// </summary>
    internal sealed class RoslynScriptExecutor : IScriptExecutor
    {
        private static readonly ScriptOptions Options = ScriptOptions.Default
            .AddReferences(
                typeof(object).Assembly,
                typeof(Enumerable).Assembly,
                typeof(Document).Assembly,
                typeof(UIApplication).Assembly,
                typeof(RoslynScriptExecutor).Assembly)
            .AddImports(
                "System",
                "System.Linq",
                "System.Collections.Generic",
                "System.Text",
                "Autodesk.Revit.DB",
                "Autodesk.Revit.UI",
                "Autodesk.Revit.ApplicationServices");

        public string Execute(UIApplication uiApp, string source)
        {
            var globals = new ScriptGlobals
            {
                UiApp = uiApp,
                App = uiApp.Application,
                UiDoc = uiApp.ActiveUIDocument,
                Doc = uiApp.ActiveUIDocument?.Document
            };

            var result = CSharpScript.EvaluateAsync<object>(source ?? string.Empty, Options, globals)
                .GetAwaiter()
                .GetResult();

            if (result == null)
                return "(script completed, no return value)";

            return Convert.ToString(result, CultureInfo.InvariantCulture) ?? result.ToString();
        }
    }
}
