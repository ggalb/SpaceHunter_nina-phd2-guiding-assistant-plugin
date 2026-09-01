using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Equipment.Interfaces.Mediator;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Phd2GuidingAssistant.Core {

    /// <summary>
    /// Slews the mount to a fixed Dec near the meridian in two legs (south of the target, then
    /// north onto it) so the final move always clears Dec backlash, and stands it down afterwards.
    ///
    /// The two-leg approach and the safety re-check between legs both trace back to a real
    /// incident: the observatory went unsafe mid-slew, N.I.N.A.'s own unsafe handling stopped the
    /// slew and commanded a park, and the second leg was issued straight over the top of it two
    /// seconds later - sending the mount back onto the GA position while N.I.N.A. sat waiting for
    /// an AtPark that would never come. Re-checking safety AND the mount's actual position (not
    /// just "did Slewing go false", which is equally true if someone else stopped us) before
    /// committing to the second leg is what prevents a repeat.
    /// </summary>
    internal sealed class MountPositioning {
        private readonly ITelescopeMediator telescopeMediator;
        private readonly SafetyGuard safety;
        private readonly double backlashApproachDeg;
        private readonly double positionToleranceDeg;
        private readonly TimeSpan slewTimeout;
        private readonly TimeSpan homeTimeout;

        public MountPositioning(ITelescopeMediator telescopeMediator, SafetyGuard safety,
                double backlashApproachDeg, double positionToleranceDeg, TimeSpan slewTimeout, TimeSpan homeTimeout) {
            this.telescopeMediator = telescopeMediator;
            this.safety = safety;
            this.backlashApproachDeg = backlashApproachDeg;
            this.positionToleranceDeg = positionToleranceDeg;
            this.slewTimeout = slewTimeout;
            this.homeTimeout = homeTimeout;
        }

        private static double NormalizeHours(double h) {
            while (h < 0) h += 24.0;
            while (h >= 24.0) h -= 24.0;
            return h;
        }

        private Coordinates TargetCoordinates(double meridianOffsetDeg, double dec) {
            var info = telescopeMediator.GetInfo();
            var lst = info.SiderealTime;
            if (lst < 0 || lst >= 24) {
                throw new Phd2GuidingAssistantException(FailureStage.PositionCheck,
                    $"SiderealTime became invalid ({lst}) while computing the target.");
            }
            var ra = NormalizeHours(lst - meridianOffsetDeg / 15.0);
            return new Coordinates(ra, dec, Epoch.JNOW, Coordinates.RAType.Hours);
        }

        public async Task SlewToCalibrationSpotAsync(double targetDec, double meridianOffsetDeg, CancellationToken token) {
            var info = telescopeMediator.GetInfo();

            if (info.AtPark) {
                // A parked mount at startup means one of two very different things. On a roll-out
                // rig it means the user forgot to unpark. Under a roof it can mean the observatory
                // shut and parked the mount deliberately, in which case unparking it is exactly the
                // wrong move. There is no way to tell the difference from here - but if a safety
                // monitor is connected at all, this is a roof-like setup, so refuse rather than guess.
                if (safety.IsConfigured) {
                    safety.FlagUnsafe("the mount was already parked at startup and a SafetyMonitor is connected");
                    throw new Phd2GuidingAssistantException(FailureStage.Unsafe,
                        "Mount is parked and a SafetyMonitor is connected. Refusing to unpark it: a parked mount " +
                        "may mean the observatory is shut, not that it was left parked by mistake. Unpark it deliberately and rerun.");
                }
                Logger.Warning("Mount is parked - unparking. (No SafetyMonitor connected, so this is assumed to be a rig with no roof.)");
                await telescopeMediator.UnparkTelescope(new Progress<ApplicationStatus>(), token);
                await Task.Delay(2000, token);
            }

            info = telescopeMediator.GetInfo();
            if (!info.TrackingEnabled) {
                if (!info.CanSetTrackingEnabled) {
                    throw new Phd2GuidingAssistantException(FailureStage.MountCapability,
                        "Tracking is off and the driver reports it cannot be set. The Guiding Assistant needs a tracking mount.");
                }
                Logger.Info("Enabling tracking.");
                telescopeMediator.SetTrackingEnabled(true);
                await Task.Delay(2000, token);
            }

            // --- Leg 1: south of the target, guaranteeing the final move is north.
            var decSouth = targetDec - backlashApproachDeg;
            var target1 = TargetCoordinates(meridianOffsetDeg, decSouth);
            Logger.Info($"Slew 1/2 (backlash approach): RA {target1.RA:F4}h  Dec {decSouth:F2}deg");
            await SlewAsync(target1, token);

            await Task.Delay(2000, token);

            // The check that matters: re-verify safety AND actual position before committing to
            // leg 2. Mount-WaitSlew (Slewing -> false) cannot distinguish "my slew finished" from
            // "someone else stopped my slew" - both look identical from here.
            safety.AssertSafe("between the two slew legs");

            var decAfterLeg1 = telescopeMediator.GetInfo().Declination;
            if (Math.Abs(decAfterLeg1 - decSouth) > positionToleranceDeg) {
                safety.FlagUnsafe($"slew leg 1 did not arrive - Dec is {decAfterLeg1:F2} deg, expected {decSouth:F2} deg");
                throw new Phd2GuidingAssistantException(FailureStage.Unsafe,
                    $"Slew leg 1 did not arrive: Dec is {decAfterLeg1:F2} deg, expected {decSouth:F2} deg " +
                    $"(tolerance {positionToleranceDeg} deg). Our slew was stopped by something else - most likely a " +
                    "safety handler taking the mount. Standing off without issuing leg 2.");
            }

            // --- Leg 2: north onto the target. This is the backlash-clearing move.
            var target2 = TargetCoordinates(meridianOffsetDeg, targetDec); // recomputed - LST has advanced
            Logger.Info($"Slew 2/2 (north, clears Dec backlash): RA {target2.RA:F4}h  Dec {targetDec:F2}deg");
            await SlewAsync(target2, token);

            safety.AssertSafe("after the slew");

            // Sanity check. Should never fire; guards against a wildly wrong pointing model
            // putting us on the wrong side of the meridian.
            info = telescopeMediator.GetInfo();
            var ha = info.SiderealTime - info.Coordinates.RA;
            while (ha < -12) ha += 24;
            while (ha > 12) ha -= 24;
            var haDeg = ha * 15.0;

            Logger.Info($"Position after slew: Dec {info.Declination:F2}deg  HA {haDeg:F2}deg (+ = west)  SideOfPier={info.SideOfPier}");

            if (Math.Abs(info.Declination - targetDec) > positionToleranceDeg) {
                throw new Phd2GuidingAssistantException(FailureStage.PositionCheck,
                    $"Dec is {info.Declination:F2}deg, expected {targetDec:F2}deg (tolerance {positionToleranceDeg}deg).");
            }
            if (Math.Abs(haDeg - meridianOffsetDeg) > positionToleranceDeg) {
                throw new Phd2GuidingAssistantException(FailureStage.PositionCheck,
                    $"Hour angle is {haDeg:F2}deg, expected {meridianOffsetDeg:F2}deg west (tolerance {positionToleranceDeg}deg).");
            }
            Logger.Info("Position sanity check passed.");
        }

        private async Task SlewAsync(Coordinates target, CancellationToken token) {
            using var timeoutCts = new CancellationTokenSource(slewTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token);
            bool ok;
            try {
                ok = await telescopeMediator.SlewToCoordinatesAsync(target, linked.Token);
            } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                throw new Phd2GuidingAssistantException(FailureStage.Slew, $"Slew timed out after {slewTimeout.TotalSeconds}s.");
            }
            if (!ok) throw new Phd2GuidingAssistantException(FailureStage.Slew, "Slew rejected or failed.");
        }

        /// <summary>
        /// Stands the mount down per <paramref name="endAction"/>, honouring the driver's actual
        /// capabilities. Re-verifies safety and mount state immediately before moving anything - we
        /// are about to act on a picture of the world that may be several minutes old.
        /// </summary>
        public async Task<bool> StandDownAsync(EndAction endAction, CancellationToken token) {
            if (safety.Unsafe) {
                Logger.Error($"Stand-down SUPPRESSED: the observatory is unsafe ({safety.UnsafeReason}). Leaving the mount exactly where it is.");
                return true;
            }
            if (!safety.IsSafeNow()) {
                safety.FlagUnsafe(safety.UnsafeReason);
                Logger.Error($"Stand-down SUPPRESSED: became unsafe during the run ({safety.UnsafeReason}). Not moving the mount.");
                return true;
            }

            var info = telescopeMediator.GetInfo();
            if (info.AtPark) {
                Logger.Error("Stand-down SUPPRESSED: the mount is parked, and it was not parked when this run started. Something parked it - most likely an unsafe condition. Leaving it alone.");
                return true;
            }
            if (info.Slewing) {
                Logger.Error("Mount reports Slewing = true before stand-down - another client may have taken it. Not issuing a competing command.");
                return true;
            }

            var action = endAction;
            if (action == EndAction.Auto) {
                action = info.CanFindHome ? EndAction.Home : info.CanPark ? EndAction.Park : EndAction.None;
                Logger.Info($"EndAction 'Auto' resolved to '{action}' from the driver's capabilities.");
            }

            switch (action) {
                case EndAction.None:
                    Logger.Warning("EndAction = None - leaving the mount where it is.");
                    return true;

                case EndAction.Home:
                    if (!info.CanFindHome) {
                        Logger.Error("EndAction = Home but the driver reports it cannot find home.");
                        return false;
                    }
                    Logger.Info("Sending mount home (FindHome) ...");
                    using (var cts = new CancellationTokenSource(homeTimeout))
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cts.Token)) {
                        try {
                            var ok = await telescopeMediator.FindHome(new Progress<ApplicationStatus>(), linked.Token);
                            if (ok) Logger.Info("Mount is at home.");
                            return ok;
                        } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                            Logger.Error($"FindHome did not complete within {homeTimeout.TotalSeconds}s.");
                            return false;
                        }
                    }

                case EndAction.Park:
                    if (!info.CanPark) {
                        Logger.Error("EndAction = Park but the driver reports it cannot park.");
                        return false;
                    }
                    Logger.Info("Parking mount ...");
                    using (var cts = new CancellationTokenSource(homeTimeout))
                    using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cts.Token)) {
                        try {
                            var ok = await telescopeMediator.ParkTelescope(new Progress<ApplicationStatus>(), linked.Token);
                            if (ok) Logger.Info("Mount is parked.");
                            return ok;
                        } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                            Logger.Error($"Park did not complete within {homeTimeout.TotalSeconds}s.");
                            return false;
                        }
                    }
            }
            return false;
        }

        /// <summary>The one mount operation the unsafe path performs. Halts motion without commanding a position, so it cannot move the mount into anything.</summary>
        public void StopTracking() {
            try { telescopeMediator.SetTrackingEnabled(false); }
            catch (Exception ex) { Logger.Warning($"Could not stop tracking on the unsafe path: {ex.Message}"); }
        }
    }

    public enum EndAction {
        Auto,
        Home,
        Park,
        None
    }
}
