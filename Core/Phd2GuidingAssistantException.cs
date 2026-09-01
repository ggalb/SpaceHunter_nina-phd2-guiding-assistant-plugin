using System;

namespace NINA.Plugin.Phd2GuidingAssistant.Core {

    /// <summary>Which phase of the routine failed - carried only for logging; the Sequencer surfaces Message itself.</summary>
    internal enum FailureStage {
        Safety,
        Phd2NotConnected,
        Phd2NotCalibrated,
        MountNotConnected,
        MountCapability,
        Slew,
        PositionCheck,
        NoGuideStar,
        GuidingSettle,
        GaWindowNotFound,
        GaNeverStarted,
        StarLostOrAlert,
        NoApplyButtons,
        ApplyDidNotTakeEffect,
        StandDown,
        Unsafe
    }

    internal sealed class Phd2GuidingAssistantException : Exception {
        public FailureStage Stage { get; }

        public Phd2GuidingAssistantException(FailureStage stage, string message) : base(message) {
            Stage = stage;
        }
    }
}
