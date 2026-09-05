# ASCOM mount capability matrix

Ground truth collected by running `Dump-AscomCapabilities.ps1` against
each driver. **Rows should come from an actual driver dump, not from
documentation or articles** — the whole point is that vendor docs and
secondhand write-ups disagree with what drivers actually report.

```powershell
.\Dump-AscomCapabilities.ps1 -ProgId 'ASCOM.GS.Sky.Telescope' -AppendTo '.\mount-capabilities.md'
```

The dump is read-only: it never slews, parks, homes, or changes
tracking. Safe on live hardware.

## ⚠️ Capability flags are NOT static — dump with hardware connected

Established 2026-07-27. The iOptron driver reports `CanFindHome = False`
when connected with **no mount attached**, yet N.I.N.A.'s "Find home"
instruction demonstrably works on a real CEM70EC — and N.I.N.A. calls
`ITelescope.FindHome()`, which it would not offer if the flag were
false. The driver evidently only reports its true capabilities once it
knows which mount is on the other end.

Two consequences:

1. **Any row dumped without hardware is provisional** and marked ⚠️ here.
2. **The plugin queries capabilities at runtime, after connecting to the
   actual mount** — never from a lookup table, and never from this
   document. This matrix is for understanding the landscape and designing
   fallbacks, not for runtime decisions.

## Why this matters

The **PHD2 Guiding Assistant plugin** calls `Unpark()`,
`SlewToCoordinatesAsync()` and `FindHome()`. That works on an EQ6-R Pro
through GS Server. On a mount with no home sensor `FindHome` is refused
outright and `End action = Auto` falls back to Park instead — which
*latches* the mount, so the sequence must unpark afterwards. This matrix
records which drivers need which fallback.

## The matrix

| Mount / driver | ProgID | Alignment | EqSystem | FindHome | Park | Unpark | SlewAsync | SetTracking | PulseGuide |
|---|---|---|---|---|---|---|---|---|---|
| Sky-Watcher EQ6-R Pro (GS Server) ✅ | `ASCOM.GS.Sky.Telescope` | German Polar (GEM) | Topocentric | yes | yes | yes | yes | yes | yes |
| Alpaca Telescope Simulator | `ASCOM.Simulator.Telescope` | German Polar (GEM) | Topocentric | yes | yes | yes | yes | yes | yes |
| iOptron CEM/GEM/HEM/HAE/HAZ/SkyHunter | `ASCOM.iOptron2017.Telescope` | German Polar (GEM) | Topocentric | no ⚠️ | yes | yes | yes | yes | yes |
| ZWO AM3/AM5/AM7 | `ASCOM.ASIMount.Telescope` | German Polar (GEM) | Topocentric | yes | yes | yes | yes | yes | yes |
| EQMOD HEQ5/6 (Synta) ✅ | `EQMOD.Telescope` | German Polar (GEM) | **J2000** | **no ⚠️** | yes | yes | yes | yes | yes |
<!-- rows appended by Dump-AscomCapabilities.ps1 go below this line -->

Legend: `yes` / `no` / `n/i` (property not implemented by the driver).
✅ = dumped with real hardware connected. ⚠️ = dumped idle, provisional.

## Per-mount notes

Capability flags don't capture everything. Record here anything the
matrix can't express — whether `FindHome()` blocks or returns
immediately, what "home" physically means, whether `SideOfPier` is
meaningful, how `Sync` behaves, and any driver quirks.

### Sky-Watcher EQ6-R Pro — GS Server (`ASCOM.GS.Sky.Telescope`) ✅

- **Dumped 2026-07-27 with the mount connected** — driver v1.2.2.4,
  interface version 4. Real site and sidereal time reported, `AtHome` =
  True from the previous night's run, `SideOfPier` = pierEast. This is
  the reference row: everything the plugin needs is supported.
- ⚠️ **`Connected` state appears to be SHARED across ASCOM clients, not
  per-client.** This was initially recorded the other way round, on the
  strength of this single dump, and that was wrong. Two full script runs
  later the same day settled it: run 1 took 8 s to connect and released
  its handle; run 2, with N.I.N.A. connected, found `Connected` already
  true and skipped connecting entirely. A per-client model would have
  had run 2 connect afresh. The likely explanation for this dump needing
  to connect is that the hardware link was simply down at the time.
  **Consequence:** the plugin never sets `Connected = false`, since
  doing so could drop N.I.N.A.'s own link mid-sequence.
- `CanSetRightAscensionRate` and `CanSetDeclinationRate` both True,
  unlike the iOptron and ZWO drivers.
- Tracking rates: sidereal, king, lunar, solar — returned as raw enum
  integers rather than names, unlike the ASCOM simulator.
- Axis rates: continuous 0–3.5 deg/sec on both axes.
- Verified working end to end, 2026-07-26.
- `FindHome()` returns immediately and must be polled via `AtHome` and
  `Slewing`; homing took ~28 s in practice.
- Home = counterweight down, pointing at the pole. The EQ6-R has no home
  sensor; GSS uses its stored zero position.
- Tracking is off after homing.
- Acts as a hub: multiple clients can connect simultaneously. Closing the
  last client's connection drops the hardware link.
- `SideOfPier` reported correctly (`0` = pierEast when pointing west of
  the meridian).

### ASCOM Telescope Simulator (`ASCOM.Simulator.Telescope`)

- Dumped 2026-07-27. Reference row — a driver that supports everything,
  useful as a baseline for comparison.
- Interface version 4, driver 0.5 (Alpaca simulator).
- Supports every capability the plugin uses, so it makes a poor test for
  fallback paths. To exercise those, a driver that *lacks* `FindHome` is
  needed.
- Axis rates: 0–6.67 and 10–20 deg/sec on both axes.
- Tracking rates: sidereal, king, lunar, solar.

### iOptron CEM120/70/40/26, GEM, HEM, HAE, HAZ, SkyHunter (`ASCOM.iOptron2017.Telescope`)

- Dumped 2026-07-27, driver v9.15, interface version 3.
- **Dumped with NO hardware attached** — the driver connected anyway, but
  `SiteLatitude 0`, `SiteLongitude 0`, `pierUnknown` and `Tracking False`
  are idle defaults, not real values. Capability flags are usually static
  per driver, but re-verify against an actual mount before relying on
  them.
- ⚠️ **`CanFindHome` reported False — but this is WRONG for real
  hardware.** Georg owns a CEM70EC and confirms N.I.N.A.'s "Find home"
  instruction works on it, driving the mount to its zero position.
  N.I.N.A. calls `ITelescope.FindHome()`, which it would not offer if
  `CanFindHome` were false. So the driver reports False only while idle
  with no mount attached, and flips to True once connected to real
  hardware. **Needs re-dumping with the CEM70EC connected.**
- `CanSetRightAscensionRate` and `CanSetDeclinationRate` both False.
- Only `driveSidereal` offered — no lunar, solar or king rates.
- Axis rates are nine discrete steps (min = max on each), 0.0042 to
  2.5 deg/sec, rather than the continuous ranges the simulator reports.
- `CanSyncAltAz` False while `CanSync` is True.
- iOptron's "go to zero position" is functionally equivalent to Find
  Home, and that is what `FindHome()` triggers.

### ZWO AM3 / AM5 / AM7 (`ASCOM.ASIMount.Telescope`)

- Dumped 2026-07-27, driver v6.5.27.0, interface version 3. No hardware
  attached, so reported values are idle defaults.
- **`CanFindHome` = True** — confirms the secondhand claim. End-of-run
  homing works as written on the AM series.
- **`SiderealTime` returned `-1`** — an invalid value, with no site
  configured and no mount connected. Worth knowing because the target
  is computed as `RA = SiderealTime - offset`; a bogus LST yields a
  nonsense target. See the safety note below.
- **`CanSetPierSide` = False**, unlike the iOptron and the simulator.
  Consistent with a harmonic mount that need not flip at the meridian.
- `CanSlewAltAz` / `CanSlewAltAzAsync` both False — RA/Dec only.
- Tracking rates: sidereal, lunar, solar. No king rate.
- Axis rates: continuous 0–6 deg/sec on both axes.
- ⚠️ **UNVERIFIED — the park position may be fixed, not user-defined.**
  A ZWO user-forum thread reports that parking under ASCOM moves RA
  counter-clockwise about 90° and Dec counter-clockwise 90°, i.e. a fixed
  orientation rather than one the user sets. Not confirmed by ZWO
  documentation and not tested here. **If true it matters under a roof:**
  an observatory user could not move the park position to suit their
  enclosure and would have to use `Find home` instead, or accept the
  orientation the roof has to clear. Georg is asking AM-series owners.

---

## Desk research — not dumped, no hardware

The sections below are **web research only**, gathered 2026-08-11. No
dump, no hardware, nothing verified here. They are recorded because each
one describes a behaviour that would change how this script's stand-down
should be configured, and because they are the kind of thing that is
expensive to discover on the night. **Treat every line as a lead to
check, not a fact.**

### Software Bisque Paramount / TheSkyX — the flags can be a checkbox

Paramounts are driven through TheSky, and ASCOM programs reach them via
the **ASCOM 2X Mount Adapter**, which makes TheSky look like a standard
ASCOM telescope.

In that adapter's configuration the user explicitly turns on **"Can Find
Home"**, **"Can Set Tracking"** and **"Mount can be Parked"**.

**That is the strongest evidence yet for the warning at the top of this
document.** On this driver the capability flags are not reported by the
hardware at all — they are settings. A dump therefore records how the
user configured the adapter, not what the mount can do. Two identical
Paramounts can report differently.

### Astro-Physics V2 driver / APCC

- **Multiple named park positions.** Park 4 puts the scope on the
  opposite side of the pier facing south to the horizon; Park 5 the same
  but facing north. Others exist.
- **Unpark is configurable.** "Last Parked" is the documented normal
  setting for permanent installations; a portable rig can be set to
  unpark to a fixed park position instead.
- **Parking when already parked returns immediately**, and you cannot
  park to a *different* position without unparking first. Worth knowing:
  a second park command is not a way to reposition.
- APCC's encoder tab offers define-and-find Home plus fixed and variable
  RA and Dec limits.

Reads as the best-provisioned of the four for observatory use — the park
position is genuinely the user's to choose, which is what the README
assumes.

### OnStep

- **Two different operations, easily confused.** *Home/Reset* is manual —
  you move the telescope yourself. *Find/Home* has OnStep move it.
- ⚠️ **Find/Home is only dependable with physical home switches
  installed and home sensing enabled.** Most builds have no switches, and
  in that case the mount must already be aligned for Find/Home to end up
  in the right place.
- **Park is user-defined** — slew to the desired orientation and press
  Set-Park.
- ⚠️ A thread on the WarpAstron forum reports a **`FindHome()`
  implementation issue in the OnStep ASCOM driver**. Not investigated.

**Consequence for this script:** `-EndAction Auto` prefers `FindHome()`.
On an OnStep without home switches that is the less reliable of the two
options, and `-EndAction Park` is the better default there.

### Losmandy Gemini 2 (Gemini.NET ASCOM driver)

⚠️ **The most concerning of the four.**

- Users report that **parking to the Home position makes the driver wait
  a few seconds and then report the mount as parked, while the mount has
  not slewed anywhere.** Parking to CWD or to the current position is
  reported to work.
- A separate report describes park attempting to go *under* the mount.
- A further report describes the ASCOM driver changing the parking-mode
  setting by itself.

**Why this matters here.** Our stand-down guard re-reads `AtPark` and
trusts it. A driver that reports parked without moving defeats that
guard completely — the plugin would see `AtPark = true`, conclude the
observatory's safety system had taken the mount, and stand off. It would
behave correctly on the information it has, and the mount would be
wherever it started.

Nothing in this script can detect that. It belongs in the README as a
general instruction: **confirm where your mount physically ends up, do
not trust the driver's report of it.**

## Safety finding: validate SiderealTime before slewing

The ZWO dump returned `SiderealTime = -1`, and the iOptron returned
`SiteLatitude = 0, SiteLongitude = 0`. Both are placeholder values from a
driver with no mount and no site.

The plugin computes its target as
`RA = SiderealTime - (MeridianOffsetDeg / 15)`. With an invalid sidereal
time it would compute a meaningless RA and slew there. The post-slew
sanity check would catch the error, but only *after* the mount has moved.

**Fix needed:** validate before the first slew that `SiderealTime` is
within 0–24 and that site latitude/longitude are plausible, and abort
with a clear exit code if not. Added to the hardening list.

### EQMOD / EQASCOM (`EQMOD.Telescope`) ✅

**Dumped 2026-09-04 with the mount connected** — `EQMOD HEQ5/6`, EQASCOM
V2.00w, interface version 2. Real site and sidereal time reported,
`SideOfPier` = pierEast.

EQASCOM is an older, unmaintained driver for **Synta** mounts — so all
Sky-Watcher and Orion models, and a few others. Most users have moved to
GS Server, which is maintained, but not all.

**It cannot find home. `CanFindHome = False`.** This is the first driver
in this matrix confirmed on hardware to lack it, and the consequences are
worth understanding because they are quiet rather than loud.

| End action | What happens on EQMOD |
|---|---|
| `Home` | **Error, and the mount stays where the run finished.** The stand-down refuses, having found `CanFindHome = false`. |
| `Auto` | Falls back to **Park**. |
| `Park` | Parks. |

All three verified on hardware, 2026-09-04.

**The important part.** EQMOD's park destination is chosen in the
driver's own Park tab, and its first option is called *"Park at Home
position"*. With that selected, `Auto` sends the mount to the home
position **and parks it there**. So it looks exactly like a successful
home — the mount is physically where you expect — and it is **latched**.
Nothing moves afterwards until something unparks it.

There is no way to have both. EQMOD offers one park destination at a
time and no separate home, so the choice is:

- **Roof with clearance** → set the driver's park to *Park at Home
  position*
- **Roof without clearance** → set the driver's park to your predefined
  safe position

With EQMOD the decision moves out of the N.I.N.A. sequence and into the
driver. That is simpler than a mount with both — **one destination to
verify against your roof, not two** — but it means the sequence must
always issue `Unpark Scope` after this instruction, whichever you chose.

Other notes:

- **`EquatorialSystem = J2000`**, where GS Server reports Topocentric.
  Coordinates computed from `SiderealTime` are of-date, so they sit about
  0.4° from what this driver expects at epoch 2026. Well inside the 3°
  post-slew tolerance, but it is the first J2000 driver dumped here.
- `CanSetPark = True` — the driver would allow software to move the park
  position. The plugin does not, and should not.
- `CanSetPierSide = False`, `CanSlewAltAz = False`.
- Only `driveSidereal` offered. No lunar, solar or king rate.
- Axis rates: continuous 0–3.3425 deg/sec on both axes.

## Contributing a dump

If you run a different mount, running the script and sending the output
would be genuinely useful. It connects, reads properties, prints a
matrix row, and disconnects. It moves nothing.
