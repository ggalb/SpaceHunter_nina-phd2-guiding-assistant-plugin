# Example Sequences

> **Disclaimer:** The templates below are examples only, provided to illustrate one way of
> wiring this plugin into a startup sequence. They are not validated for your specific mount,
> observatory, or safety hardware. Before running any of them unattended, thoroughly test the
> safety behavior of your own equipment in your own environment. No warranty is provided, and
> no liability is accepted for any damage to equipment resulting from their use.

Three real-world setups showing where "Run PHD2 Guiding Assistant" fits, depending on how
exposed the mount is to a closing roof.

## Under Open Skies

A simple, semi-fixed rig - on a trolley, or fully assembled and carried outside - imaged under
open sky with no roof or dome in the loop. The instruction runs as a plain step in the startup
sequence, right after polar alignment, with no extra safety wrapping beyond N.I.N.A.'s own
global failure handling.

![Under Open Skies](images/under-open-skies.png)

## Under Roof, With Clearance

A fixed rig in an observatory where the mount has full clearance to move once the roof is open.
Minimal safety protocol is needed: the Guiding Assistant step is wrapped in its own instruction
set, `Test Tonight's Seeing Conditions`, with a "When Becomes Unsafe" trigger (stop tracking ->
park -> wait until safe -> unpark) around just that one step.

![Under Roof, With Clearance](images/under-roof-with-clearance.png)

**Template:** [`Test Tonight's Seeing Conditions.template.json`](templates/Test%20Tonight's%20Seeing%20Conditions.template.json)

## Under Roof, No Clearance

A fixed rig where any mount movement - not just the Guiding Assistant run - risks hitting the
roof or walls before it's fully open. Here the whole startup routine (unpark, slew to the
calibration spot, autofocus, home, then run the Guiding Assistant) is wrapped in one safety
envelope, `Start-Up SafetyProtocol`, since every one of those steps moves the mount.

![Under Roof, No Clearance](images/under-roof-no-clearance.png)

**Template:** [`Start-Up SafetyProtocol.template.json`](templates/Start-Up%20SafetyProtocol.template.json)

## Using these templates

1. Download the `.template.json` file(s) above.
2. Copy them into your N.I.N.A. Sequencer templates folder - check **Options -> General ->
   Sequence Templates Folder** for the exact path on your machine (commonly
   `Documents\N.I.N.A\Templates`).
3. Reopen the Sequencer - the template appears in the Templates list, ready to drag into your
   own sequence.
4. Adjust GA time / Target Dec / Meridian offset / End action, and every safety trigger step, to
   match your own rig and site before running - see the disclaimer above.
