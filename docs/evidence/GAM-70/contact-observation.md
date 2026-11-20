# GAM-70 loaded floor contacts during the historical proximity trial

Unity 6000.3.22f1, Windows PlayMode. GAM70_CONTACT_SAMPLE_AUDIT uses the existing B2 proximity path, a 75 kg dynamic bar, both welded grips and Drive from tick 301. The trace identifies the command tick, the sample used by the governor, and the newer sample used by the regulator. Normal load and COP integrate the positive platform normal impulses across both PhysX substeps. The nominal sole hull, impulse-bearing hull, per-foot patch, pose, COP targets, integrals and modeled roll demands are recorded in ignored CSVs.

The stock progress governor used contact tick n-2 for command n. The regulator used n-1 after publishing those pending impulses later in command preparation. Both paired with pre-step body state at n-1. The authoritative tick stamps these samples. The rig's PreviousObservation property is a separate render interpolation buffer read.

| Event | Tick | Measurement |
| --- | ---: | --- |
| Left physical contact collapses to the forward edge | 637 | The previous sample spanned 290.00 mm. The loaded longitudinal extent dropped below 0.001 mm. |
| Along-sole command uses an unreachable COP | 638 | The left target was 163.72 mm along the world-projected sole axis and the loaded edge was at 328.91 mm. The integral moved from -0.56 degrees to -1.36 degrees. |
| Governor reads the first edge sample | 639 | Nominal margin 131.90 mm, active margin 51.14 mm. |
| First left ankle Z modeled demand above ceiling | 640 | 60.22 N m against 60 N m. The physical support mode changed first. |
| Nominal and active margins disagree at the stock 40 mm threshold | 642 | 120.36 mm nominal, 17.81 mm active. The across-sole target was outside the loaded extent and progression continued. |
| A foot falls below the 50 N loaded threshold | 643 | Actual post-step pressure sample. Shaft-to-leg normal reaction remained zero through this event. |

The loaded hull bounds the normal COP of the sampled contact set. The possible sole footprint includes locations that could bear load after a contact-mode change. Recoverable motion also depends on unilateral forces, available friction, contact moments, dynamics and powered authority. The capture-point estimate provides a diagnostic for this posture.

The existing RUNTIME_PLAYER_DEADLIFT_IS_A_GOOD_LIFT passed in the instrumented editor run. Stock onsets 301 and 305 earned GOOD LIFT, with DOWN at ticks 834 and 839 and both grips released after return.

## Matched correction

The fresh A/B switched on the correction only after matching the historical closer-bar arm at progress 0.76. Both runs started Drive at tick 301. The corrected governor and regulator both consumed n-1, every checked projected target was in its loaded hull, and both axes stayed within the measured extents. At the first left fore-aft edge, the applied target was 328.91 mm along the sole and the integral leaked from 0.194 to 0.175 degrees under the existing leak. The edge still occurred at physical tick 637. Modeled left ankle Z demand still first crossed 60 N m at tick 640 (60.32 N m). The active capture margin went negative for command 643 and the governor held progression. A foot fell below 50 N at tick 646, three ticks later than the baseline. The historical path still lacks a stable lockout.

The corrected governor uses bilateral loading and the sign of the active capture margin alongside the existing nominal reserve. The positive nominal reserve remains a possible-contact margin. Projection into each actual loaded hull bounds the commanded normal COP for that sampled contact set, including a single point or line. The integrator applies its existing leak and skips newly integrating error when the required axis reference is clipped. With no measured support, the controller holds the previous reference offset and invalidates the unavailable COP. This validity correction remains opt-in and uses the existing gains, profiles, limits and solver settings. Stock onset 301 and 305 passed again in the same Unity session.
