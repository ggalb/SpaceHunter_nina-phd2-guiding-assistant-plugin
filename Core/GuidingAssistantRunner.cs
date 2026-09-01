using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Equipment.MyGuider.PHD2;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Profile.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Phd2GuidingAssistant.Core {

    internal sealed class RunParameters {
        public int GaSeconds;
        public double TargetDec;
        public double MeridianOffsetDeg;
        public EndAction EndAction;
    }

    /// <summary>
    /// The end-to-end routine: position the mount, guide on the existing calibration, run PHD2's
    /// Guiding Assistant, apply its recommendations, and stand the mount down. A straight port of
    /// the standalone script's MAIN block onto N.I.N.A.'s own mediators, with N.I.N.A.'s Logger and
    /// cancellation token replacing the script's own log file and process exit codes.
    /// </summary>
    internal sealed class GuidingAssistantRunner {
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IGuiderMediator guiderMediator;
        private readonly SafetyGuard safety;
        private readonly Phd2GuidingAssistantPlugin options;
        private readonly IProfileService profileService;

        private IntPtr gaWindow = IntPtr.Zero;

        public GuidingAssistantRunner(ITelescopeMediator telescopeMediator, IGuiderMediator guiderMediator,
                ISafetyMonitorMediator safetyMonitorMediator, IProfileService profileService, Phd2GuidingAssistantPlugin options) {
            this.telescopeMediator = telescopeMediator;
            this.guiderMediator = guiderMediator;
            this.profileService = profileService;
            this.options = options;
            this.safety = new SafetyGuard(safetyMonitorMediator);
        }

        public async Task RunAsync(RunParameters p, IProgress<ApplicationStatus> progress, CancellationToken token) {
            void Report(string status) {
                Logger.Info(status);
                progress.Report(new ApplicationStatus { Status = status });
            }

            var mount = new MountPositioning(telescopeMediator, safety, options.BacklashApproachDeg,
                options.PositionToleranceDeg, TimeSpan.FromSeconds(options.SlewTimeoutSec), TimeSpan.FromSeconds(options.HomeTimeoutSec));

            try {
                safety.LogConfigurationOnce();
                safety.AssertSafe("at startup");

                // ---------- 1. PHD2 pre-flight ----------------------------------
                Report("Checking PHD2 calibration...");
                if (!guiderMediator.GetInfo().Connected) {
                    throw new Phd2GuidingAssistantException(FailureStage.Phd2NotConnected, "The guider is not connected in N.I.N.A.");
                }
                if (!(guiderMediator.GetDevice() is PHD2Guider phd2)) {
                    throw new Phd2GuidingAssistantException(FailureStage.Phd2NotConnected, "The connected guider is not PHD2.");
                }
                if (!await Phd2Extras.GetCalibrated(phd2)) {
                    throw new Phd2GuidingAssistantException(FailureStage.Phd2NotCalibrated,
                        "PHD2 has no calibration data. This routine will not calibrate - run a calibration first.");
                }
                Logger.Info("PHD2 is calibrated - existing calibration will be reused.");

                var raParam = await Phd2Extras.GetMinMoveParamName(phd2, "ra");
                var decParam = await Phd2Extras.GetMinMoveParamName(phd2, "dec");
                var raBefore = await Phd2Extras.GetMinMove(phd2, "ra", raParam);
                var decBefore = await Phd2Extras.GetMinMove(phd2, "dec", decParam);
                Logger.Info($"Min-move before: RA={raBefore} ({raParam})  Dec={decBefore} ({decParam})");

                // ---------- 2. Position the mount -------------------------------
                if (!telescopeMediator.GetInfo().Connected) {
                    throw new Phd2GuidingAssistantException(FailureStage.MountNotConnected, "The mount is not connected in N.I.N.A.");
                }
                if (!telescopeMediator.GetInfo().CanSlew) {
                    throw new Phd2GuidingAssistantException(FailureStage.MountCapability, "The mount driver reports it cannot slew to coordinates.");
                }
                safety.AssertSafe("before the slew");
                Report($"Slewing to Dec {p.TargetDec}, {p.MeridianOffsetDeg} deg west of the meridian...");
                await mount.SlewToCalibrationSpotAsync(p.TargetDec, p.MeridianOffsetDeg, token);

                // ---------- 3. Start guiding -------------------------------------
                // N.I.N.A.'s own guiderMediator.StartGuiding has its own internal retry policy
                // inside PHD2Guider (observed: up to 3 attempts at 120s each, ~6 minutes total)
                // that we do not control and that does not know about MaxStarLost - that setting
                // only ever applies once guiding has actually settled and the GA run is under way.
                // The original script bounded the whole find-star/guide/settle phase with one
                // deadline (SettleTimeout + 60s) and failed fast so the mount could be stood down
                // promptly on a closed dome or daytime test. Restore that bound here, using a
                // timeout distinct from the caller's real cancellation token so a deliberate
                // Sequencer cancel is still left alone (mount untouched) while our own timeout
                // is treated as a genuine failure that triggers recovery/stand-down.
                using var guideCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                guideCts.CancelAfter(TimeSpan.FromSeconds(options.GuideStartTimeoutSec));
                try {
                    Report("Auto-selecting a guide star...");
                    if (!await guiderMediator.AutoSelectGuideStar(guideCts.Token)) {
                        throw new Phd2GuidingAssistantException(FailureStage.NoGuideStar, "No suitable guide star was found.");
                    }

                    Report("Starting guiding (existing calibration, no recalibration)...");
                    if (!await guiderMediator.StartGuiding(false, progress, guideCts.Token)) {
                        throw new Phd2GuidingAssistantException(FailureStage.GuidingSettle, "Guiding failed to start or settle.");
                    }
                } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                    throw new Phd2GuidingAssistantException(FailureStage.GuidingSettle,
                        $"Guiding did not start and settle within {options.GuideStartTimeoutSec}s (no guide star / lid closed / daytime, etc). Standing down.");
                }
                Logger.Info("Guiding settled.");

                // ---------- 4. Guiding Assistant ---------------------------------
                Report("Opening the Guiding Assistant...");
                var phdHwnd = GuidingAssistantAutomation.FindPhd2MainWindow(TimeSpan.FromSeconds(15));
                if (phdHwnd == IntPtr.Zero) {
                    throw new Phd2GuidingAssistantException(FailureStage.GaWindowNotFound, "Could not find the PHD2 main window.");
                }
                gaWindow = GuidingAssistantAutomation.OpenGuidingAssistant(phdHwnd);
                GuidingAssistantAutomation.EnsureBacklashUnchecked(gaWindow, TimeSpan.FromSeconds(5));

                await RunMeasurementAsync(p.GaSeconds, progress, token);

                await ApplyRecommendationsAsync(raParam, decParam, raBefore, decBefore, token);

                GuidingAssistantAutomation.CloseWindow(gaWindow);
                gaWindow = IntPtr.Zero;

                // ---------- 6. Stand down ----------------------------------------
                Report("Stopping capture and standing the mount down...");
                await guiderMediator.StopGuiding(token);
                await Task.Delay(2000, token);

                if (!options.NoClearPhd2Displays) {
                    GuidingAssistantAutomation.ClearPhd2Displays(phdHwnd);
                }

                safety.AssertSafe("before stand-down");
                if (!await mount.StandDownAsync(p.EndAction, token)) {
                    throw new Phd2GuidingAssistantException(FailureStage.StandDown, "Mount failed to reach its end-of-run position.");
                }

                Report("PHD2 Guiding Assistant routine completed successfully.");
            } catch (Exception ex) when (!(ex is OperationCanceledException)) {
                await RecoverAsync(mount, p.EndAction, token);
                throw;
            } finally {
                if (safety.Unsafe && !options.NoStopTrackingWhenUnsafe) {
                    mount.StopTracking();
                }
                if (gaWindow != IntPtr.Zero) {
                    GuidingAssistantAutomation.CloseWindow(gaWindow);
                }
            }
        }

        /// <summary>Runs the fixed-duration measurement, aborting early on a star lost too many times, a PHD2 error alert, or the observatory going unsafe.</summary>
        private async Task RunMeasurementAsync(int gaSeconds, IProgress<ApplicationStatus> progress, CancellationToken token) {
            // Guiding is already active when the dialog opens, so PHD2 auto-starts measurement:
            // 'Start' is disabled and 'Stop' is enabled. Waiting for 'Start' to become enabled would
            // hang forever, so detect whichever state we are actually in.
            var measuring = false;
            var detectDeadline = DateTime.UtcNow.AddSeconds(60);
            var positionalNoted = false;

            while (DateTime.UtcNow < detectDeadline) {
                var startBtn = GuidingAssistantAutomation.GetControlAny(gaWindow, GuidingAssistantCaptions.GAStartPatterns);
                var stopBtn = GuidingAssistantAutomation.GetControlAny(gaWindow, GuidingAssistantCaptions.GAStopPatterns);

                if (startBtn == null && stopBtn == null) {
                    var byPos = GuidingAssistantAutomation.GetStartStopByPosition(gaWindow);
                    startBtn = byPos.Start;
                    stopBtn = byPos.Stop;
                    if (!positionalNoted && (startBtn != null || stopBtn != null)) {
                        Logger.Warning("No Start/Stop caption matched. Identified them by position instead.");
                        positionalNoted = true;
                    }
                }

                if (stopBtn?.Enabled == true) {
                    Logger.Info("Guiding Assistant auto-started measurement (guiding was already active).");
                    measuring = true;
                    break;
                }
                if (startBtn?.Enabled == true) {
                    Logger.Info("Guiding Assistant is idle - clicking Start.");
                    GuidingAssistantAutomation.ClickControl(startBtn.Value);
                    measuring = true;
                    break;
                }
                await Task.Delay(500, token);
            }

            if (!measuring) {
                GuidingAssistantAutomation.LogControls(gaWindow, "controls inside the Guiding Assistant dialog");
                throw new Phd2GuidingAssistantException(FailureStage.GaNeverStarted,
                    "The Guiding Assistant never entered the measuring state (neither Start nor Stop became enabled).");
            }

            using var tap = new Phd2EventTap();
            try {
                await tap.ConnectAsync(profileService.ActiveProfile.GuiderSettings.PHD2ServerUrl,
                    profileService.ActiveProfile.GuiderSettings.PHD2ServerPort, token);
            } catch (Exception ex) {
                Logger.Warning($"Could not open a secondary PHD2 connection to watch for StarLost/Alert events during the run: {ex.Message}");
            }

            Logger.Info($"Measuring for {gaSeconds}s.");
            var end = DateTime.UtcNow.AddSeconds(gaSeconds);
            var lastNote = DateTime.UtcNow;
            while (DateTime.UtcNow < end) {
                tap.Pump();
                if (tap.StarLostCount > options.MaxStarLost) {
                    throw new Phd2GuidingAssistantException(FailureStage.StarLostOrAlert,
                        $"Guide star lost {tap.StarLostCount} times during the Guiding Assistant run (limit {options.MaxStarLost}).");
                }
                if (tap.LastErrorAlert != null) {
                    throw new Phd2GuidingAssistantException(FailureStage.StarLostOrAlert,
                        $"PHD2 raised an error alert during the Guiding Assistant run: {tap.LastErrorAlert}");
                }

                safety.AssertSafe("during the Guiding Assistant run");

                var remaining = (int)(end - DateTime.UtcNow).TotalSeconds;
                if ((DateTime.UtcNow - lastNote).TotalSeconds >= 30 && remaining > 0) {
                    progress.Report(new ApplicationStatus { Status = $"Guiding Assistant running - {remaining}s remaining." });
                    lastNote = DateTime.UtcNow;
                }
                await Task.Delay(1000, token);
            }

            // Re-check immediately before Stop: if the backlash box got ticked while we were
            // measuring, Stop would start a declination backlash run instead of finishing.
            GuidingAssistantAutomation.EnsureBacklashUnchecked(gaWindow);
        }

        /// <summary>Clicks Stop, waits for PHD2 to render recommendations, applies every Apply button offered, and verifies the clicks actually took effect.</summary>
        private async Task ApplyRecommendationsAsync(string raParam, string decParam, double? raBefore, double? decBefore, CancellationToken token) {
            var buttonsBeforeStop = new HashSet<IntPtr>(
                GuidingAssistantAutomation.GetChildControls(gaWindow).Where(c => c.ClassName == "Button").Select(c => c.Handle));

            var stopBtn = GuidingAssistantAutomation.GetControlAny(gaWindow, GuidingAssistantCaptions.GAStopPatterns)
                          ?? GuidingAssistantAutomation.GetStartStopByPosition(gaWindow).Stop;
            if (stopBtn?.Enabled == true) {
                Logger.Info("Clicking Stop.");
                GuidingAssistantAutomation.ClickControl(stopBtn.Value);
            } else {
                Logger.Warning("'Stop' button not available - the Guiding Assistant may have ended by itself.");
            }

            GuidingAssistantAutomation.NoteExtendedSampling();

            // The Apply buttons are created only once recommendations render, so poll for them.
            var applyBtns = new List<ControlInfo>();
            var deadline = DateTime.UtcNow.AddSeconds(options.ApplyWaitSec);
            var lastNote = DateTime.UtcNow;
            var foundByCaption = false;

            while (DateTime.UtcNow < deadline) {
                applyBtns = GuidingAssistantAutomation.GetControlsAny(gaWindow, GuidingAssistantCaptions.GAApplyPatterns);
                if (applyBtns.Count > 0) { foundByCaption = true; break; }

                var fresh = GuidingAssistantAutomation.GetChildControls(gaWindow)
                    .Where(c => c.ClassName == "Button" && !c.IsCheckbox && !buttonsBeforeStop.Contains(c.Handle))
                    .ToList();
                if (fresh.Count > 0) { applyBtns = fresh; break; }

                if ((DateTime.UtcNow - lastNote).TotalSeconds >= 30) {
                    Logger.Info($"Still waiting for the recommendations to appear - {(int)(deadline - DateTime.UtcNow).TotalSeconds}s left.");
                    lastNote = DateTime.UtcNow;
                }
                await Task.Delay(1000, token);
            }

            Logger.Info(foundByCaption
                ? $"Found {applyBtns.Count} apply button(s)."
                : $"No apply caption matched. Found {applyBtns.Count} newly-created button(s) instead.");

            var applied = 0;
            foreach (var b in applyBtns) {
                if (b.Enabled) {
                    GuidingAssistantAutomation.ClickControl(b);
                    applied++;
                    await Task.Delay(600, token);
                } else {
                    Logger.Warning("An 'Apply' button was present but disabled - skipped.");
                }
            }
            Logger.Info($"Clicked {applied} 'Apply' button(s).");
            if (applied == 0) {
                GuidingAssistantAutomation.LogControls(gaWindow, "controls inside the Guiding Assistant dialog");
                throw new Phd2GuidingAssistantException(FailureStage.NoApplyButtons, "The Guiding Assistant offered no Apply buttons.");
            }

            // One retry for anything still enabled - a posted BM_CLICK can be missed while the
            // dialog is rebuilding its recommendations panel.
            await Task.Delay(1000, token);
            foreach (var b in GuidingAssistantAutomation.GetControlsAny(gaWindow, GuidingAssistantCaptions.GAApplyPatterns)) {
                if (b.Enabled) {
                    Logger.Warning("An apply button is still enabled - clicking it again.");
                    GuidingAssistantAutomation.ClickControl(b);
                    await Task.Delay(800, token);
                }
            }

            if (!(guiderMediator.GetDevice() is PHD2Guider phd2)) return;
            var raAfter = await Phd2Extras.GetMinMove(phd2, "ra", raParam);
            var decAfter = await Phd2Extras.GetMinMove(phd2, "dec", decParam);
            Logger.Info($"Min-move after:  RA={raAfter}  Dec={decAfter}");

            // Success is "the values moved" OR "at least one clicked button went from enabled to
            // disabled" (proof the click landed) - NOT "did the numbers change". PHD2 frequently
            // recommends values a rig is already using, in which case applying them changes
            // nothing, correctly.
            var nowEnabled = GuidingAssistantAutomation.GetControlsAny(gaWindow, GuidingAssistantCaptions.GAApplyPatterns)
                .ToDictionary(b => b.Handle, b => b.Enabled);
            var wentDisabled = 0;
            var stillEnabled = 0;
            foreach (var b in applyBtns) {
                if (nowEnabled.TryGetValue(b.Handle, out var enabled)) {
                    if (enabled) stillEnabled++; else wentDisabled++;
                } else {
                    wentDisabled++; // button gone entirely - certainly acted upon
                }
            }

            var valuesChanged = raAfter != raBefore || decAfter != decBefore;
            if (!valuesChanged) {
                if (wentDisabled == 0) {
                    GuidingAssistantAutomation.LogControls(gaWindow, "controls inside the Guiding Assistant dialog");
                    throw new Phd2GuidingAssistantException(FailureStage.ApplyDidNotTakeEffect,
                        $"Min-move values unchanged and none of the {applyBtns.Count} 'Apply' button(s) responded - the clicks did not take.");
                }
                Logger.Warning(stillEnabled > 0
                    ? $"Min-move values unchanged. {wentDisabled} button(s) responded, {stillEnabled} still enabled - PHD2 most likely recommended values this rig already uses."
                    : "Min-move values are unchanged, but every 'Apply' button responded. PHD2 simply recommended the values already in use.");
            }
        }

        /// <summary>Best-effort cleanup on failure. Never re-homes/parks when the observatory is flagged unsafe - the observatory's own safety system owns the mount at that point.</summary>
        private async Task RecoverAsync(MountPositioning mount, EndAction endAction, CancellationToken token) {
            try { await guiderMediator.StopGuiding(CancellationToken.None); } catch { }

            if (safety.Unsafe) {
                Logger.Error($"UNSAFE - not homing, not parking, not slewing. The mount is being left exactly where it stands. Reason: {safety.UnsafeReason}");
                return;
            }

            try {
                Logger.Info("Recovery: standing the mount down.");
                using var recoveryCts = new CancellationTokenSource(TimeSpan.FromSeconds(options.HomeTimeoutSec + 30));
                await mount.StandDownAsync(endAction, recoveryCts.Token);
            } catch (Exception ex) {
                Logger.Error($"Recovery stand-down failed: {ex.Message}");
            }
        }
    }
}
