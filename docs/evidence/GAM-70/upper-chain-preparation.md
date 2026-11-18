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

The upward load stayed close to the quiet control, within 6.7 N per hand across the perturbations. Load matching is approximate because the same bounded servo responds to the new closed-chain equilibrium. In the positive trial both hands pulled posteriorly (left −18.3 N, right −9.6 N). Their average grip moments about world X changed from −2.46/+1.55 Nm to −0.77/+4.46 Nm. Left/right COP changes relative to tick 300 were (−4.54, +0.80) and (+4.30, +0.73) mm in world X/Z, versus (−2.77, −2.06) and (+2.59, −2.03) mm in the quiet trial. Both feet and both plates stayed loaded. The test demonstrates grounded shoulder-to-bar coupling with a modest setup gap change. It does not qualify adduction actuation or improved ascent support.
