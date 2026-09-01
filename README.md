# PHD2 Guiding Assistant (N.I.N.A. Plugin)

A [N.I.N.A.](https://nighttime-imaging.eu/) Sequencer plugin that runs PHD2's
Guiding Assistant unattended, keeping PHD2's per-axis minimum-move guiding
settings matched to the actual seeing and mechanical conditions on a given
night - without anyone driving PHD2's UI by hand.

Run it once, early in a session, after guiding has already been calibrated:
it measures, recommends, and applies fresh RA/Dec minimum-move values for
you. Set it to run at the end of your set-up sequence, before imaging starts.

## What it does

1. Slews the mount to a fixed Declination near the meridian, approaching
   from the south so the final move clears Dec backlash.
2. Auto-selects a guide star and starts guiding using the calibration
   already on file (it never calibrates).
3. Runs the Guiding Assistant for a configurable duration (minimum 120s -
   PHD2's own documented floor for a statistically meaningful sample).
4. Applies every minimum-move recommendation PHD2 offers.
5. Stops capture and stands the mount down (Home / Park / None).

Where it points the mount follows PHD2's own guidance for where calibration
and the Guiding Assistant should be run - see PHD2's manual on the
[Guiding Assistant](https://openphdguiding.org/man/Tools.htm#Guiding_Assistant)
and [automatic calibration](https://openphdguiding.org/man/Basic_use.htm#Automatic_Calibration).
The Sequencer instruction warns if your chosen Dec or meridian offset falls
outside that guidance.

Safety-aware: if N.I.N.A.'s own SafetyMonitor reports unsafe at any point,
the routine stops touching the mount immediately and hands off entirely to
your own "When Becomes Unsafe" sequence handling.

This plugin is built for one specific, narrow job - keeping min-move
settings tuned to a given night's conditions, unattended, as part of a
normal imaging sequence. It is not a tool for polar-alignment checks,
backlash adjustment, evaluating new guiding hardware, or tuning PHD2's
guiding algorithm parameters. For that kind of deep-dive work, run PHD2's
Guiding Assistant directly and manually.

## Requirements

- N.I.N.A. 3.2.0.9001 or later
- PHD2 as the connected guider (this plugin only works with PHD2)
- An existing PHD2 calibration for the current pier side

## Building

The project references N.I.N.A.'s own assemblies directly from a local
install rather than a NuGet package, so no network access is needed to
build:

```
<NinaInstallDir>C:\Program Files\N.I.N.A. - Nighttime Imaging 'N' Astronomy</NinaInstallDir>
```

Update `NinaInstallDir` in the `.csproj` if your install lives elsewhere,
then build with the .NET SDK (net8.0-windows) or Visual Studio. Debug
builds copy the plugin straight into N.I.N.A.'s plugin folder
(`%LocalAppData%\NINA\Plugins\<version>\PHD2 Guiding Assistant`), so an
N.I.N.A. restart is all that's needed to pick up changes.

## License

[Mozilla Public License 2.0](LICENSE)
