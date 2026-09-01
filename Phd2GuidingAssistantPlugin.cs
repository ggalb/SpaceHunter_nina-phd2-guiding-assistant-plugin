using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace NINA.Plugin.Phd2GuidingAssistant {

    /// <summary>
    /// Plugin entry point and DataContext for the Options -> Plugins -> "PHD2 Guiding Assistant" tab.
    ///
    /// Holds only the knobs that make sense to set once and leave alone (see Options.xaml). The
    /// per-run knobs (GA duration, target Dec, meridian offset, end action) live on the sequence
    /// item itself (see SequenceItems/RunGuidingAssistant.cs) so different sequences/targets can
    /// use different values.
    /// </summary>
    // Exported under both contracts: IPluginManifest is what N.I.N.A. itself looks for (options tab
    // DataContext, manifest metadata); the concrete-type export is what lets RunGuidingAssistant's
    // [ImportingConstructor] pull in this same shared instance for the plugin-wide defaults. Without
    // the second export, MEF cannot satisfy that parameter at all - not "falls back to a default",
    // the whole part fails composition, and the instruction silently never appears in the Sequencer.
    [Export(typeof(IPluginManifest))]
    [Export(typeof(Phd2GuidingAssistantPlugin))]
    public class Phd2GuidingAssistantPlugin : PluginBase, INotifyPropertyChanged {
        private readonly IPluginOptionsAccessor pluginSettings;
        private readonly IProfileService profileService;

        [ImportingConstructor]
        public Phd2GuidingAssistantPlugin(IProfileService profileService) {
            this.profileService = profileService;
            this.pluginSettings = new PluginOptionsAccessor(profileService, Guid.Parse(this.Identifier));
        }

        public override Task Teardown() {
            return base.Teardown();
        }

        // ---- Mount positioning -------------------------------------------------

        public double BacklashApproachDeg {
            get => pluginSettings.GetValueDouble(nameof(BacklashApproachDeg), 1.0);
            set { pluginSettings.SetValueDouble(nameof(BacklashApproachDeg), value); RaisePropertyChanged(); }
        }

        public double PositionToleranceDeg {
            get => pluginSettings.GetValueDouble(nameof(PositionToleranceDeg), 3.0);
            set { pluginSettings.SetValueDouble(nameof(PositionToleranceDeg), value); RaisePropertyChanged(); }
        }

        public int SlewTimeoutSec {
            get => pluginSettings.GetValueInt32(nameof(SlewTimeoutSec), 180);
            set { pluginSettings.SetValueInt32(nameof(SlewTimeoutSec), value); RaisePropertyChanged(); }
        }

        public int HomeTimeoutSec {
            get => pluginSettings.GetValueInt32(nameof(HomeTimeoutSec), 300);
            set { pluginSettings.SetValueInt32(nameof(HomeTimeoutSec), value); RaisePropertyChanged(); }
        }

        // ---- PHD2 / Guiding Assistant ------------------------------------------

        public int GuideStartTimeoutSec {
            get => pluginSettings.GetValueInt32(nameof(GuideStartTimeoutSec), 180);
            set { pluginSettings.SetValueInt32(nameof(GuideStartTimeoutSec), value); RaisePropertyChanged(); }
        }

        public int ApplyWaitSec {
            get => pluginSettings.GetValueInt32(nameof(ApplyWaitSec), 300);
            set { pluginSettings.SetValueInt32(nameof(ApplyWaitSec), value); RaisePropertyChanged(); }
        }

        public int MaxStarLost {
            get => pluginSettings.GetValueInt32(nameof(MaxStarLost), 8);
            set { pluginSettings.SetValueInt32(nameof(MaxStarLost), value); RaisePropertyChanged(); }
        }

        public bool NoClearPhd2Displays {
            get => pluginSettings.GetValueBoolean(nameof(NoClearPhd2Displays), false);
            set { pluginSettings.SetValueBoolean(nameof(NoClearPhd2Displays), value); RaisePropertyChanged(); }
        }

        // ---- Safety --------------------------------------------------------------

        public bool NoStopTrackingWhenUnsafe {
            get => pluginSettings.GetValueBoolean(nameof(NoStopTrackingWhenUnsafe), false);
            set { pluginSettings.SetValueBoolean(nameof(NoStopTrackingWhenUnsafe), value); RaisePropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
