using System;
using System.IO;
using Autodesk.Revit.UI;

namespace RevitAIBridge
{
    /// <summary>
    /// Raised by the file watcher, executed by Revit on its own thread with a
    /// valid API context. This is the Revit equivalent of the Civil 3D bridge
    /// acting on Application.Idle — but event driven rather than polled.
    /// </summary>
    internal sealed class TaskHandler : IExternalEventHandler
    {
        private readonly string _root;
        private readonly IScriptExecutor _executor;

        public TaskHandler(string root, IScriptExecutor executor)
        {
            _root = root;
            _executor = executor;
        }

        public string GetName() => "RevitAI Bridge Task Handler";

        public void Execute(UIApplication uiApp)
        {
            var version = uiApp.Application.VersionNumber;
            var docTitle = uiApp.ActiveUIDocument?.Document?.Title;

            // Consume the flag first. A task that brings Revit down must not
            // re-run on the next launch.
            TryDeleteFlag();

            string source;
            try
            {
                source = File.ReadAllText(BridgePaths.Task(_root));
            }
            catch (Exception ex)
            {
                WriteResult("ERROR", "Could not read " + BridgePaths.TaskFileName + "\n" + Describe(ex));
                BridgeStatus.Write(_root, BridgeState.Error, version, docTitle);
                return;
            }

            BridgeStatus.Write(_root, BridgeState.Busy, version, docTitle);

            try
            {
                var output = _executor.Execute(uiApp, source);
                WriteResult("OK", output ?? string.Empty);
                BridgeStatus.Write(_root, BridgeState.Idle, version, docTitle);
            }
            catch (Exception ex)
            {
                // Verbose on purpose. The agent reads this to correct itself.
                WriteResult("ERROR", Describe(ex));
                BridgeStatus.Write(_root, BridgeState.Error, version, docTitle);
            }
        }

        private void TryDeleteFlag()
        {
            try
            {
                var flag = BridgePaths.Flag(_root);
                if (File.Exists(flag))
                    File.Delete(flag);
            }
            catch (Exception)
            {
                // If the flag cannot be removed the watcher may re-fire, which
                // the debounce in BridgeApp will absorb.
            }
        }

        private void WriteResult(string status, string payload)
        {
            try
            {
                BridgePaths.WriteAtomic(BridgePaths.Result(_root), status + "\n" + payload + "\n");
            }
            catch (Exception)
            {
                // Nowhere left to report to.
            }
        }

        private static string Describe(Exception ex)
        {
            var text = ex.GetType().FullName + ": " + ex.Message;

            if (ex.InnerException != null)
                text += "\n  inner: " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message;

            if (!string.IsNullOrEmpty(ex.StackTrace))
                text += "\n" + ex.StackTrace;

            return text;
        }
    }
}
