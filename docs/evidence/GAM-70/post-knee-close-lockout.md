# GAM-70 post-knee close-bar primitive

Unity 6000.3.22f1, Windows PlayMode. GAM70_POST_KNEE_TO_CLOSE_LOCKOUT and GAM70_POST_KNEE_SUSPENDED_PRELOAD use a matched prepared Drive at tick 284, accepted pull 301, physical separation 314 and confirmed separation 316. Both preserve the qualified through-knee prefix to tick 538. Realized bar rise is 456.55 mm from confirmed separation at the switch, and the conservative prefix substep clearance remains 10.04 mm with zero shaft-leg impulse.

At tick 538 the planner closes a new reference about the measured planted feet and welded grip frames. It matches initial bar velocity, ends at the isolated close-bar shaft target, and resolves the joint progression through the existing powered channels. It applies the anchored system-COM reference and contact-valid COP. The new interpolated reference has 91.95 mm conservative planned shaft clearance and 0.525 mm / 0.001 degree maximum closure residual. The governor checks both feet, the active contact margin, the nominal reserve and actual modeled ankle X/Z demand. The stock path serves only as the preparation prefix's reference and the external comparison.

| Matched arm | Result |
| --- | --- |
| Existing realized-posture hand-load feedforward | Entered controller lockout at 711. First foot edge at 713. Both feet unloaded at 776. No DOWN. |
| Planned suspended preload blended at the measured knee passage | Entered controller lockout at 711. Both soles retained 290 mm loaded spans throughout. Referee DOWN at 762. |

The suspended member is solved on each planned closed posture. It preserves finite torque bounds and includes the welded grip moment required on the model. The preload A/B shares the same geometry, COP controller, governor, joint writer, capacities, friction and material settings. In the first arm, the realized-posture feedforward drove ankle X bias to about -17 N m entering lockout, compared with -3.5 N m at the isolated endpoint. Its first edge preceded ankle saturation. The second arm used planned equilibrium offsets and preserved physical contact through the stop. This shows the importance of keeping the torque preload consistent with the closed commanded posture in this trial.

The admitted ascent and lockout show zero shaft-leg impulse, positive swept clearance, and at least 758/761 N on the left/right feet after the knee passage. Model demand peaks after the switch were 107.4/85.0 N m at ankle X (450 N m ceilings), 14.64/10.84 N m at ankle Z (60 N m), 373.7 N m at abdomen X (390 N m), and 83.0/86.9 N m at shoulder swing (130 N m). Peak realized left/right ankle roll magnitudes were 3.18/3.20 degrees. The last 30 ticks before DOWN had mean gap to the nearer thigh of 95.84 mm, mean shaft height 0.9842 m and bar speed at most 17.7 mm/s.

| Realized ascent shaft height | Stock nearer-thigh mean | Close path nearer-thigh mean |
| --- | ---: | ---: |
| 0.725-0.750 m | 112.78 mm | 111.65 mm |
| 0.800-0.825 m | 147.64 mm | 123.78 mm |
| 0.875-0.900 m | 186.95 mm | 133.19 mm |
| 0.950-0.975 m | 203.71 mm | 123.59 mm |
| 0.977-0.994 m | 220.33 mm | 104.86 mm |

The prepared primitive ends at referee DOWN and has not lowered the bar in this fixture. The stock runtime onsets 301 and 305 passed again, with the original DOWN ticks and grip release after return.
