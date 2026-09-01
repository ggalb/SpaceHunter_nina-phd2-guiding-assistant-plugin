using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;

namespace NINA.Plugin.Phd2GuidingAssistant.Core {

    /// <summary>
    /// Design principle carried over unchanged from the original script: when the observatory goes
    /// unsafe, this plugin stops touching the mount entirely. It does not try to home or park it
    /// "safely" - the observatory's own safety system owns the mount at that moment, and a
    /// competing command is worse than no command. The unsafe path is: stop guiding, close the GA
    /// window if open, leave the mount exactly where it is (only sidereal tracking is stopped, per
    /// <see cref="MountPositioning.StopTracking"/>), and fail the instruction.
    ///
    /// This is simpler than the standalone script's Safety-Connect/Test-Safe machinery because
    /// there is no separate polling connection to go stale: N.I.N.A.'s own SafetyMonitorInfo is
    /// read directly, in-process, so every check is as fresh as N.I.N.A.'s own view of the monitor.
    /// </summary>
    internal sealed class SafetyGuard {
        private readonly ISafetyMonitorMediator safetyMonitorMediator;
        private bool loggedConfiguration;

        public bool Unsafe { get; private set; }
        public string UnsafeReason { get; private set; } = string.Empty;

        public SafetyGuard(ISafetyMonitorMediator safetyMonitorMediator) {
            this.safetyMonitorMediator = safetyMonitorMediator;
        }

        public bool IsConfigured => safetyMonitorMediator.GetInfo()?.Connected ?? false;

        public void LogConfigurationOnce() {
            if (loggedConfiguration) return;
            loggedConfiguration = true;
            Logger.Info(IsConfigured
                ? "Safety: a SafetyMonitor is connected in N.I.N.A. - it will be checked before every mount movement."
                : "Safety: no SafetyMonitor is connected in N.I.N.A. This run has NO observatory safety checking beyond the mount-state check before stand-down. Fine on a rig with no roof; not fine under one.");
        }

        public bool IsSafeNow() {
            if (Unsafe) return false;
            if (!IsConfigured) return true;

            var info = safetyMonitorMediator.GetInfo();
            if (!info.IsSafe) {
                UnsafeReason = "N.I.N.A.'s SafetyMonitor reports IsSafe = false";
                return false;
            }
            return true;
        }

        public void AssertSafe(string stage) {
            if (IsSafeNow()) return;
            var reason = UnsafeReason;
            FlagUnsafe(reason);
            throw new Phd2GuidingAssistantException(FailureStage.Unsafe,
                $"UNSAFE {stage}: {reason}. Standing off - the mount will NOT be moved, and is being left exactly " +
                "where it is for the observatory's own safety handling to deal with.");
        }

        public void FlagUnsafe(string reason) {
            Unsafe = true;
            UnsafeReason = reason;
        }
    }
}
