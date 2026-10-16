using System.Reflection;
using System.Runtime.InteropServices;

// [MANDATORY] Unique plugin identifier. Generated once for this plugin - do not change after
// the first release, or N.I.N.A. will treat every future build as a different plugin.
[assembly: Guid("6f2a9b7e-6c9e-4b7e-9c02-6f2a2d7e9c11")]

[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]

[assembly: AssemblyTitle("Phd2 GA - Measure Seeing Conditions")]
[assembly: AssemblyDescription("Runs PHD2's Guiding Assistant unattended from the N.I.N.A. Sequencer.")]
[assembly: AssemblyCompany("Space Hunter - Georg G Albrecht")]
[assembly: AssemblyProduct("Phd2 GA - Measure Seeing Conditions")]
[assembly: AssemblyCopyright("Copyright © 2026 Space Hunter - Georg G Albrecht")]

// Minimum N.I.N.A. version this plugin was built and tested against.
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.2.0.9001")]

[assembly: AssemblyMetadata("License", "MPL-2.0")]
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]
[assembly: AssemblyMetadata("Repository", "https://github.com/ggalb/SpaceHunter_nina-phd2-guiding-assistant-plugin")]
[assembly: AssemblyMetadata("Homepage", "https://github.com/ggalb/SpaceHunter_nina-phd2-guiding-assistant-plugin")]
[assembly: AssemblyMetadata("Tags", "PHD2,Guiding,Guiding Assistant,Min Move")]
[assembly: AssemblyMetadata("ChangelogURL", "")]
[assembly: AssemblyMetadata("FeaturedImageURL", "https://raw.githubusercontent.com/ggalb/SpaceHunter_nina-phd2-guiding-assistant-plugin/master/assets/Logo.png")]
[assembly: AssemblyMetadata("ScreenshotURL", "")]
[assembly: AssemblyMetadata("AltScreenshotURL", "")]
[assembly: AssemblyMetadata("LongDescription", @"**⚠ WARNING - this plugin moves your mount without you touching it.** Before running it in an unattended sequence, test it where you can watch the mount: see every slew, the end action (Home/Park), and your ""When Becomes Unsafe"" handling (e.g. roof close, park) actually work with your equipment and site. Stay within reach of your mount's stop/power control during the first runs. Use at your own risk - no warranty (MPL-2.0, sections 6-7).

This plugin keeps PHD2's per-axis minimum-move guiding settings matched to the actual seeing and mechanical conditions on a given night - automatically, without anyone driving PHD2's UI by hand. Run it once, early in a session, after guiding has already been calibrated: it measures, recommends, and applies fresh RA/Dec minimum-move values for you. Set it to run at the end of your set-up sequence, before imaging starts.

What it does, step by step:

1. Slews the mount to a fixed Declination near the meridian, approaching from the south so the final move clears Dec backlash.
2. Auto-selects a guide star and starts guiding using the calibration already on file (it never calibrates).
3. Runs the Guiding Assistant for a configurable duration (minimum 120 seconds - PHD2's own documented floor for a statistically meaningful sample; shorter values are rejected by the instruction).
4. Applies every minimum-move recommendation PHD2 offers.
5. Stops capture and stands the mount down (Home/Park/None).

This plugin is built for one specific, narrow job: keeping min-move settings tuned to a given night's conditions, unattended, as part of a normal imaging sequence - not for hardware tuning or diagnostic work. It is not the right tool for polar-alignment quality checks, mechanical backlash adjustment, evaluating a new guide scope/camera, or tuning PHD2's guiding algorithm parameters. For that kind of deep-dive work, run PHD2's Guiding Assistant directly and manually, with full access to its graphs, settings, and history.

Where it points the mount isn't arbitrary - it follows PHD2's own guidance for where calibration and the Guiding Assistant should be run. See PHD2's manual: [Guiding Assistant](https://openphdguiding.org/man/Tools.htm#Guiding_Assistant), [automatic calibration](https://openphdguiding.org/man/Basic_use.htm#Automatic_Calibration), and [Calibration Assistant](https://openphdguiding.org/man/Tools.htm#Calibration_Assistant) for why Declination and meridian offset matter, and what PHD2 itself recommends. The Sequencer instruction warns if your chosen Dec or meridian offset falls outside that guidance.

The Guiding Assistant tool isn't exposed through PHD2's RPC API, so that part is driven the same way a person would - through its window - via Win32 messages addressed by control name and handle, not screen position.

Safety-aware: if N.I.N.A.'s own SafetyMonitor reports unsafe at any point, the routine stops touching the mount immediately and hands off entirely to your own ""When Becomes Unsafe"" sequence handling - it never issues a competing Home/Park command of its own once unsafe.")]

[assembly: ComVisible(false)]
