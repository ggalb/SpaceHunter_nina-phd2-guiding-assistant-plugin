using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Equipment.MyGuider.PHD2;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Plugin.Phd2GuidingAssistant.Core;
using NINA.Profile.Interfaces;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.Phd2GuidingAssistant.SequenceItems {

    /// <summary>
    /// Slews to a fixed Dec near the meridian, guides on the existing PHD2 calibration, runs the
    /// Guiding Assistant for a fixed duration, applies its minimum-move recommendations, and stands
    /// the mount down. Intended to run once per night, after polar alignment and before imaging.
    ///
    /// Run length, target Dec, meridian offset and end action live here (per-instance, so different
    /// sequences/targets can use different values). Everything else - timeouts, safety behaviour,
    /// how PHD2's displays get cleared - is a plugin-wide default on the Options page, since those
    /// rarely need to differ between runs.
    /// </summary>
    [ExportMetadata("Name", "Run PHD2 Guiding Assistant")]
    [ExportMetadata("Description", "Slews to a calibration spot, guides on the existing PHD2 calibration, runs the Guiding Assistant, applies its recommendations, and stands the mount down.")]
    [ExportMetadata("Icon", "PHD2SVG")]
    [ExportMetadata("Category", "Guider")]
    [Export(typeof(NINA.Sequencer.SequenceItem.ISequenceItem))]
    [JsonObject(MemberSerialization.OptIn)]
    public partial class RunGuidingAssistant : SequenceItem, IValidatable {

        // PHD2's own manual (https://openphdguiding.org/man/Tools.htm#Guiding_Assistant) says to use
        // "the same area you should use for calibration", and the calibration guidance
        // (https://openphdguiding.org/man/Basic_use.htm#Automatic_Calibration) gives an exact
        // figure - not a guess of ours:
        //
        //   "the scope is pointing within 20 degrees of Dec = 0 (near the celestial equator) and at
        //    least 60 degrees above the nearest east/west horizon (i.e. within 2 hours of the
        //    celestial meridian)"
        //
        // i.e. |Dec| <= 20 deg and |meridian offset| <= 30 deg (2 hours). PHD2 also states this is
        // advisory, not a hard limit: measuring elsewhere is "subject to somewhat more measurement
        // uncertainty" - so both are non-blocking warnings (Notification.ShowWarning below) rather
        // than validation failures, since an unusual value can be a deliberate choice, e.g. measuring
        // nearer an actual imaging target. Dec beyond +/-90 is the one hard failure: that is not a
        // coordinate at all, not just an unwise one.
        private const double NearPoleWarningDec = 20.0;
        private const double LargeMeridianOffsetWarningDeg = 30.0;

        private readonly ITelescopeMediator telescopeMediator;
        private readonly IGuiderMediator guiderMediator;
        private readonly ISafetyMonitorMediator safetyMonitorMediator;
        private readonly IProfileService profileService;
        private readonly Phd2GuidingAssistantPlugin pluginOptions;

        [ImportingConstructor]
        public RunGuidingAssistant(ITelescopeMediator telescopeMediator, IGuiderMediator guiderMediator,
                ISafetyMonitorMediator safetyMonitorMediator, IProfileService profileService, Phd2GuidingAssistantPlugin pluginOptions) {
            this.telescopeMediator = telescopeMediator;
            this.guiderMediator = guiderMediator;
            this.safetyMonitorMediator = safetyMonitorMediator;
            this.profileService = profileService;
            this.pluginOptions = pluginOptions;
        }

        public RunGuidingAssistant(RunGuidingAssistant copyMe)
                : this(copyMe.telescopeMediator, copyMe.guiderMediator, copyMe.safetyMonitorMediator, copyMe.profileService, copyMe.pluginOptions) {
            CopyMetaData(copyMe);
        }

        [ObservableProperty]
        [property: JsonProperty]
        private int gaSeconds = 130;

        [ObservableProperty]
        [property: JsonProperty]
        private double targetDec = 0.0;

        [ObservableProperty]
        [property: JsonProperty]
        private double meridianOffsetDeg = 5.0;

        [ObservableProperty]
        [property: JsonProperty]
        private EndAction endAction = EndAction.Auto;

        [ObservableProperty]
        private IList<string> issues = new List<string>();

        // Scratch inputs for the Alt/Az helper below - deliberately not [JsonProperty]: they are a
        // one-off convenience for picking Target Dec / Meridian offset when the default position is
        // blocked (a tree, a roofline, ...), not a setting worth persisting with the sequence.
        [ObservableProperty]
        private double helperAlt = 45.0;

        [ObservableProperty]
        private double helperAz = 180.0;

        // Set while ComputeFromAltAz is assigning TargetDec/MeridianOffsetDeg, so the per-field
        // OnChanged warnings below stay quiet for that one assignment - ComputeFromAltAz shows its
        // own single consolidated message instead (Alt/Az input plus the resulting Dec/offset). The
        // per-field warnings still fire normally whenever the user types into either field by hand.
        private bool suppressFieldWarnings;

        // Ported from the standalone Convert-AltAzToDecHA.ps1 helper script (Alt/Az -> Dec/HA leg
        // only - N.I.N.A. already knows the site latitude, so there is no reverse/manual-latitude
        // mode to carry over).
        [RelayCommand]
        private void ComputeFromAltAz() {
            var lat = profileService.ActiveProfile.AstrometrySettings.Latitude;
            const double d2R = Math.PI / 180.0;
            const double r2D = 180.0 / Math.PI;
            double Clamp1(double v) => Math.Max(-1.0, Math.Min(1.0, v));

            var p = lat * d2R;
            var a = HelperAlt * d2R;
            var z = HelperAz * d2R;

            var sinDec = Math.Sin(a) * Math.Sin(p) + Math.Cos(a) * Math.Cos(p) * Math.Cos(z);
            var dec = Math.Asin(Clamp1(sinDec));
            var cosDec = Math.Cos(dec);

            var sinHa = -Math.Sin(z) * Math.Cos(a) / cosDec;
            var cosHa = (Math.Sin(a) - Math.Sin(dec) * Math.Sin(p)) / (cosDec * Math.Cos(p));
            var ha = Math.Atan2(sinHa, cosHa) * r2D;

            suppressFieldWarnings = true;
            try {
                TargetDec = Math.Round(dec * r2D, 2);
                MeridianOffsetDeg = Math.Round(ha, 2);
            } finally {
                suppressFieldWarnings = false;
            }

            var outOfGuidance = Math.Abs(TargetDec) > NearPoleWarningDec || Math.Abs(MeridianOffsetDeg) > LargeMeridianOffsetWarningDeg;
            var summary = $"Alt {HelperAlt:F1} deg / Az {HelperAz:F1} deg at latitude {lat:F3} deg -> " +
                $"Target Dec {TargetDec:F2} deg, Meridian offset {MeridianOffsetDeg:F2} deg.";
            if (outOfGuidance) {
                Notification.ShowWarning(summary + " This falls outside PHD2's own guidance for the Guiding Assistant/calibration.");
            } else {
                Notification.ShowInformation(summary);
            }
        }

        // CommunityToolkit.Mvvm calls these automatically right after GaSeconds/TargetDec/... changes
        // via the generated property setters - a live, non-blocking warning as soon as the value is
        // typed, rather than something the user only discovers when the run fails hours later. These
        // are advisories, not validation failures: an unusual Dec or offset can still be exactly what
        // someone wants (e.g. testing the assistant nearer an actual imaging target), so they do not
        // block the instruction the way an unreachable telescope or a too-short GA time does.
        partial void OnTargetDecChanged(double value) {
            if (suppressFieldWarnings) return;
            if (Math.Abs(value) > NearPoleWarningDec) {
                Notification.ShowWarning(
                    $"Target Dec {value:F1} deg is outside PHD2's own guidance of within 20 deg of Dec 0 for " +
                    "the Guiding Assistant / calibration. Guide-star motion scales with cos(Dec), so the " +
                    "minimum-move value recommended here will be less representative of typical imaging " +
                    "declinations.");
            }
        }

        partial void OnMeridianOffsetDegChanged(double value) {
            if (suppressFieldWarnings) return;
            if (Math.Abs(value) > LargeMeridianOffsetWarningDeg) {
                Notification.ShowWarning(
                    $"Meridian offset {value:F1} deg is outside PHD2's own guidance of within 2 hours (30 deg) " +
                    "of the meridian for the Guiding Assistant / calibration. Further out, field rotation and " +
                    "differential flexure start muddying the seeing measurement itself.");
            }
        }

        public override object Clone() {
            return new RunGuidingAssistant(this) {
                GaSeconds = GaSeconds,
                TargetDec = TargetDec,
                MeridianOffsetDeg = MeridianOffsetDeg,
                EndAction = EndAction
            };
        }

        public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token) {
            var runner = new GuidingAssistantRunner(telescopeMediator, guiderMediator, safetyMonitorMediator, profileService, pluginOptions);
            await runner.RunAsync(new RunParameters {
                GaSeconds = GaSeconds,
                TargetDec = TargetDec,
                MeridianOffsetDeg = MeridianOffsetDeg,
                EndAction = EndAction
            }, progress, token);
        }

        public bool Validate() {
            var i = new List<string>();

            if (!telescopeMediator.GetInfo().Connected) {
                i.Add(Loc.Instance["LblTelescopeNotConnected"]);
            }

            var guiderInfo = guiderMediator.GetInfo();
            if (!guiderInfo.Connected) {
                i.Add(Loc.Instance["LblGuiderNotConnected"]);
            } else if (!(guiderMediator.GetDevice() is PHD2Guider)) {
                i.Add("The connected guider must be PHD2.");
            }

            if (GaSeconds < 120) {
                i.Add("PHD2 needs at least 120s of sampling before it offers recommendations - shorter runs will fail after measuring.");
            }

            if (Math.Abs(TargetDec) > 90.0) {
                i.Add($"Target Dec {TargetDec:F1} is not a valid declination (must be between -90 and +90).");
            }

            Issues = i;
            return i.Count == 0;
        }

        public override void AfterParentChanged() {
            Validate();
        }

        public override string ToString() {
            return $"Category: {Category}, Item: {nameof(RunGuidingAssistant)}, GaSeconds: {GaSeconds}, TargetDec: {TargetDec}, MeridianOffsetDeg: {MeridianOffsetDeg}, EndAction: {EndAction}";
        }
    }
}
