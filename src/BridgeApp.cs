using System;
using System.IO;
using Autodesk.Revit.UI;

namespace RevitAIBridge
{
    /// <summary>
    /// Entry point. Watches the bridge folder for run.flag and raises an
    /// external event so the task runs inside a valid Revit API context.
    ///
    /// No port, no server, no config file that needs to persist.
    /// </summary>
    public class BridgeApp : IExternalApplication
    {
        private const int DebounceMilliseconds = 400;
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

        private FileSystemWatcher _watcher;
        private ExternalEvent _taskEvent;
        private string _root;
        private string _revitVersion;
        private DateTime _lastRaisedUtc = DateTime.MinValue;
        private DateTime _lastHeartbeatUtc = DateTime.MinValue;
        private readonly object _gate = new object();

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                _root = BridgePaths.EnsureRoot();
                _revitVersion = application.ControlledApplication.VersionNumber;

                var handler = new TaskHandler(_root, new RoslynScriptExecutor());
                _taskEvent = ExternalEvent.Create(handler);

                _watcher = new FileSystemWatcher(_root)
                {
                    Filter = BridgePaths.FlagFileName,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                    IncludeSubdirectories = false
                };

                _watcher.Created += OnFlagTouched;
                _watcher.Changed += OnFlagTouched;
                _watcher.Renamed += OnFlagTouched;
                _watcher.EnableRaisingEvents = true;

                // Idling fires continuously while Revit is open and not busy,
                // so this is the "still alive" pulse — bridge_status.json's
                // updated_utc otherwise only moved at startup and after a
                // task, going stale during long idle stretches even though
                // the watcher was fine the whole time. There is no ribbon
                // button for this tool by design, so the timestamp is the
                // only signal a human (or another agent) has that it's on.
                application.Idling += OnIdling;

                BridgeStatus.Write(_root, BridgeState.Idle, _revitVersion);
                _lastHeartbeatUtc = DateTime.UtcNow;

                // A flag left behind by a crash is deliberately ignored, but a
                // flag written while Revit was closed is a legitimate queued
                // task, so pick it up on startup.
                if (File.Exists(BridgePaths.Flag(_root)))
                    RaiseDebounced();

                return Result.Succeeded;
            }
            catch (Exception)
            {
                // Never prevent Revit from starting because the bridge failed.
                return Result.Succeeded;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                application.Idling -= OnIdling;

                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Created -= OnFlagTouched;
                    _watcher.Changed -= OnFlagTouched;
                    _watcher.Renamed -= OnFlagTouched;
                    _watcher.Dispose();
                    _watcher = null;
                }

                _taskEvent?.Dispose();
                _taskEvent = null;
            }
            catch (Exception)
            {
                // Shutting down regardless.
            }

            return Result.Succeeded;
        }

        private void OnIdling(object sender, Autodesk.Revit.UI.Events.IdlingEventArgs e)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastHeartbeatUtc) < HeartbeatInterval)
                return;

            _lastHeartbeatUtc = now;

            var docTitle = (sender as UIApplication)?.ActiveUIDocument?.Document?.Title;
            BridgeStatus.Write(_root, BridgeState.Idle, _revitVersion, docTitle);
        }

        private void OnFlagTouched(object sender, FileSystemEventArgs e) => RaiseDebounced();

        /// <summary>
        /// FileSystemWatcher fires several times for one logical write.
        /// ExternalEvent.Raise is safe to call from this worker thread.
        /// </summary>
        private void RaiseDebounced()
        {
            lock (_gate)
            {
                var now = DateTime.UtcNow;
                if ((now - _lastRaisedUtc).TotalMilliseconds < DebounceMilliseconds)
                    return;

                _lastRaisedUtc = now;
            }

            try
            {
                _taskEvent?.Raise();
            }
            catch (Exception)
            {
                // Revit refuses the raise while a modal dialog is open. The
                // next flag write will try again.
            }
        }
    }
}
