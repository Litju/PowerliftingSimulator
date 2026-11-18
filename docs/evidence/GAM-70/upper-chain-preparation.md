# GAM-70 upper-chain preparation

## Gate A — current command path at d81b25f

- `DeadliftAttemptRuntime` places the qualified B2 sumo setup with both grips already attached. Its post-step target rises from zero at tick 100 to 75% of half the bar weight at tick 200, about 276 N per hand on the 75 kg bar. Drive alone starts the pull because `RequireBraceAndGrip` is false. Brace and Grip are sampled, with no independent shoulder action in this runtime.
- `DeadliftPretensionLoop` closes on the mean upward hand-on-bar force. It integrates the target error into a bounded interpolation of two contact-consistent static torque references. This transfers load from the plates to the athlete's feet while grounded. The single rigid bar body and the two six-DOF `BenchGrip` welds have no modeled elastic slack or grip take-up travel.
- `DeadliftSetupHold` composes the setup orientation with the static torque-derived angular bias and ankle regulator offsets. Its existing identification seam postmultiplies the assembled reference. When Drive first enters Pulling, the hold switches to the measured hand-load reference. `DeadliftPullController` ramps hip and knee extension and a signed trunk offset, governs pull for COM displacement, and confirms separation from measured plate unloading and upward motion. No dedicated shoulder extension/adduction request or measured preparation readiness gates this handoff.
- Each powered shoulder is an upper-arm-to-thorax ball joint. The current `PoweredJointController` writes XYAndZ, with finite X twist and YZ swing drives, 130 Nm maximum force each at full activation and unit capacity scale. The shoulder profile is 500 spring, 55 damper, 2.5 rad/s target rate. Authored limits are X ±105° and Y/Z 165°. Twist and swing modeled demand ratios are drive diagnostics. They do not resolve Y and Z into independently capped torques.
- The world axes are +X athlete-right, +Y up, +Z forward. For a shoulder-to-elbow direction `a`, the world axis `a × (-Z)` gives a posterior component to the distal-arm motion. `a × medial` gives an adduction component, with medial +X on the left and −X on the right. Convert either axis into the current child joint basis using the inverse of `childRotation * JointSpace`. A postmultiplied logical reference uses that local axis. The sole joint writer rate-limits the logical request and converts it to Unity's inverse target rotation. The shoulder experiment checks the realized sign in the assembled chain.

Original and opt-in proximity paths retain their Gate 9 disposition. The original is the station default. Gate 9's five fresh onsets yielded 4/5 good lifts on the original path and 0/5 on proximity.

## Gate B — grounded shoulder-to-bar transfer

Unity 6000.3.22f1 PlayMode, `GROUNDED_SHOULDER_REFERENCE_TRANSFER`, passed. Three fresh production B2 plants used the same rig, 75 kg bar, existing servo and quiet Drive history. The only experimental command was the two shoulder references, changed at tick 300. The probe used the shoulder-to-forearm-COM line as the distal-arm direction. Means use ticks 341–420. Minima and maxima cover ticks 301–420, including the transition.

| Bilateral request | Measured up per hand | Net hand-on-bar Z | Shin gap | Bar Z minus shoulder Z | Min foot / plate | Min capture margin | Max bar rise |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Quiet | 270.5 N | +2.7 N | 45.18 mm | −13.57 mm | 758.7 / 94.5 N | 139.0 mm | 0.000 mm |
| +2° extension | 264.6 N | −27.9 N | 42.93 mm | −18.65 mm | 741.1 / 77.1 N | 134.7 mm | 0.288 mm |
| −2° extension | 277.2 N | +28.0 N | 47.27 mm | −6.61 mm | 720.6 / 80.8 N | 136.3 mm | 0.229 mm |

The projected extension axis in J was (+0.977, +0.211, +0.007) on the left and (−0.977, −0.214, +0.007) on the right. The corresponding medial axes were (−0.205, +0.979, −0.001) and (+0.208, −0.978, −0.002). This qualifies the mostly-twist extension input in B2. The mostly-swing medial direction is a geometric mapping only.

The +2° requested and applied offsets realized +0.690° left and +0.693° right. Reverse polarity realized −0.700° and −0.690°. Across all trials, shoulder X/YZ demand peaked at 0.144/0.182 of the respective ceilings, X limit occupancy at 0.181, and realized swing at 102.31° against the authored 165° swing limits. The drive provided measurable authority with headroom. The force and geometry responses reversed with the command.

The upward load stayed close to the quiet control, within 6.7 N per hand across the perturbations. Load matching is approximate because the same bounded servo responds to the new closed-chain equilibrium. In the positive trial both hands pulled posteriorly (left −18.3 N, right −9.6 N). The first component of the raw grip torque changed from +2.46/−1.55 Nm to +0.77/−4.46 Nm. Gate A2 found that the torque frame and point could not be validated, so only the force has a qualified world-frame interpretation. Left/right COP changes relative to tick 300 were (−4.54, +0.80) and (+4.30, +0.73) mm in world X/Z, versus (−2.77, −2.06) and (+2.59, −2.03) mm in the quiet trial. Both feet and both plates stayed loaded. The test demonstrates grounded shoulder-to-bar coupling with a modest setup gap change. It does not qualify adduction actuation or improved ascent support.

## Gate C — opt-in coordinated preparation

`DeadliftAttemptRuntime.UpperChainPreparation` enables the candidate before initialization. The existing Drive intent requests preparation on the grounded plant. Both trunk joints keep their existing setup posture regulation and C4 load bias. The grounded servo keeps its qualified setpoint schedule. The shoulder contribution is the bilateral +2° reference from Gate B in each measured child joint frame. `PoweredJointController` applies the rate limit and remains the physical joint writer.

The handoff checks both plate normals against the existing separation threshold, both feet above the loaded-foot threshold, longitudinal COP inside each loaded contact extent, and the existing 40 mm capture margin. Each hand must carry at least 95% of the grounded target and together pull at least 10 N posteriorly. Both shoulders must realize at least 0.5° in the tested direction, both trunk joints must stay within 3° of setup, and their modeled drive demands must be below the ceilings. These are B2 experiment thresholds. They do not encode muscle strength.

The existing measured hand-load reference takes ownership when the pull leaves Grounded. The shoulder offset remains through Pulling and clears at measured separation, release, or abort. A rearmed pull uses the already-established measured hand-load owner. Ankle gains, geometry, solver settings, drive calibration, grip constraints, and both paths were retained.

## Gate D — complete attempts

Unity 6000.3.22f1, `GAM70_UPPER_CHAIN_PREPARATION_AB`, passed. Drive was held from tick 301 in all four fresh runtime plants. The PlayMode assertion verifies the baseline dispositions, readiness delay, and measured bar separation. It does not treat the candidate's failed lifts as passing gameplay outcomes.

| Path | Preparation | Pull / separation | Referee and return |
| --- | --- | --- | --- |
| Original | Control | 301 / 319 | Good lift, Down 834, controlled return 1451, grips released |
| Original | Candidate | 318 / 333 | FootMoved 684, lockout phase 704, no Down or controlled return |
| Proximity | Control | 301 / 319 | FootMoved 646, no lockout, Down, or controlled return |
| Proximity | Candidate | 318 / 333 | FootMoved 640, no lockout, Down, or controlled return |

At the first prepared Pulling tick, hands carried 277.8/281.6 N up and −32.5 N combined Z, versus 275.5/274.6 N and +0.5 N for the controls. Both plate loads exceeded 85 N. The shin gap was 42.7 mm against 45.2 mm. Peak shoulder/lumbar demand from Pulling through Separated was 0.23/0.86 of the ceilings versus 0.23/0.93 for controls. Prepared bar break retained at least 884.8 N on the lighter foot on the separation tick. This is a successful grounded transfer and bar break.

| Path and arm | Setup shin / thigh | Below knee | Knee passage | Upper thigh | Lockout |
| --- | ---: | ---: | ---: | ---: | ---: |
| Original control | 45 / 241 | 22 / 174 | 48 / 104 | 314 / 173 | 444 / 177 |
| Original prepared | 45 / 241 | 20 / 174 | 47 / 104 | 311 / 172 | 403 / 131* |
| Proximity control | 45 / 241 | 30 / 174 | 109 / 115 | 331 / 131* | — |
| Proximity prepared | 45 / 241 | 20 / 174 | 59 / 102 | 291 / 89* | — |

Mean sagittal capsule surface gaps in mm. *The window includes failed support and the ensuing fall. Its lower gap cannot qualify the lift. All four arms recorded 0 N bar-on-leg contact. The original control held at least 557.7 N on the lighter foot after separation and at least 90.9 mm capture margin during lifting.

On the original prepared path, ankle-roll demand first crossed its ceiling at tick 676, progress 0.954. A foot unloaded at tick 683 with shoulder demand at 0.77 and lumbar demand at 0.64. Capture margin turned negative on the next evaluation. On the proximity path, preparation moved the first roll saturation from progress 0.790 to 0.737 and the first zero foot load from 0.801 to 0.750. The shoulder still had headroom at foot loss (0.39 of ceiling), with no bar-on-leg contact. The proximate failure was lower-chain support loss late in ascent. The experiment does not show that shoulder preparation removes the Gate 9 reachability or balance constraint.

### Pull-onset control

The prepared arms both accepted pulling at tick 318. Fresh controls first given Drive at tick 318 also failed on both paths. The original delayed control broke at 336, first lost foot load at 684, and received FootMoved at 685. The prepared original broke three ticks earlier, lost foot load at 683, and received FootMoved at 684. The delayed control reproduced the near-lockout support failure with zero shoulder perturbation. This demonstrates that the readiness delay alone can move the original path into a failing onset. It does not establish that the shoulder reference caused the late failure.

On proximity, the delayed control broke at 336, first lost foot load at progress 0.926, and received FootMoved at 710. The prepared arm lost load at progress 0.750. Matching effective pull onset therefore does not explain the earlier proximity support loss in the prepared arm. The identically scheduled path and the same ankle regulator saw different physical initial states and bar-break histories. The trace isolates the observed failure to foot roll and load shedding with shoulder headroom. It does not assign that upstream sensitivity to a single joint.

### Readiness lead at the qualified pull tick

The final pair used a test-only Drive onset of 284, 17 ticks ahead of the qualified original onset. Both prepared plants accepted Drive at tick 301 and broke the bar at 316. Each had about 283 N up per hand and −31.7 N net Z at handoff. Both first contacted a leg at tick 382, before loss of foot support or ankle-roll saturation. Measured shin gap was −1.5/−1.9 mm (original/proximity) and bar-on-leg force was 126/361 N. The referee registered ThighSupport using its bar-on-leg channel. The capture margin was still positive at first contact and turned negative by tick 394. Both lifts stopped around 205 mm of rise and returned uncontrolled.

Matching the pull tick removed the readiness-delay explanation for this pair. The prepared liftoff started 3 ticks earlier and with about 3.7 mm less shin clearance than the original tick-301 control at break. This pair first failed during below-knee bar-on-leg contact. The first pair lost support much later as ankle roll saturated. The data do not isolate retention versus removal of the shoulder offset or the effects of the different closed-chain state at break.

### Disposition and verification

Keep the candidate disabled for gameplay. Grounded transfer is real, with a roughly 2–3 mm shin-gap gain in these trials, but the complete attempts did not improve dynamic support or produce a good lift. The original station default and independent proximity flag retain their Gate 9 conditions. No candidate Windows build or owner gameplay verification was performed.

Five focused PlayMode methods passed on Unity 6000.3.22f1. The three complete-attempt experiments covered the prepared paths, onset-matched controls, and the readiness lead. The grounded shoulder probe passed, and the default runtime's normal regression reproduced good lifts at 301 and 305 with Down at 834 and 839. Raw traces remain in the ignored Logs/GAM70 directory.
