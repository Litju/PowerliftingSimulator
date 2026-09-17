# GAM-12 P3A1 — deterministic physical-failure rationale

Research date: 2026-09-15

This is a bounded semantic check for the P3 game/engine detector. It does not
import human-study thresholds, calibrate heavy loads, or make injury claims.

## Evidence decisions

| Class | Physical evidence | Deterministic game simplification | Calibration status |
|---|---|---|---|
| `BALANCE_LOSS` | Observed support margins plus modeled COM velocity directed outward, persisted | Four canonical AP/ML directional predicates; outside-but-recovering velocity is not an immediate failure | Provisional; GAM-13 may qualify behavior-dependent boundaries |
| `DESCENT_COLLAPSE` | Excessive raw bar/pelvis downward motion plus support/contact loss or hard posture/limit loss, persisted; demand/saturation is retained as corroboration | No collapse from downward speed, Drive absence, or modeled demand alone | Provisional; heavy-load qualification deferred |
| `FAILED_REVERSAL` | Bilateral legal-depth proxy, actual Drive intent, and absent sustained upward bar/pelvis recovery through timeout | Physical bottom is raw motion context; legal bottom is required only for this failure class | Timeout/recovery require GAM-13 calibration |
| `MID_ASCENT_STALL` | Established raw ascent, near-zero raw progress, high modeled demand, no-progress dwell, and no recovery | A recoverable low-velocity sticking interval is explicitly nonterminal | Heavy-load-dependent boundaries require GAM-13 |
| `BAR_REVERSAL` | Established ascent plus persistent raw whole-bar downward displacement beyond the physical noise bound | Separate physical event/record from rule `DOWNWARD_MOVEMENT` | Heavy-load-dependent boundaries require GAM-13 |
| `POSTURE_OR_BAR_LOSS` | Hard trunk game bound, critical joint-limit proximity, saddle break, coupling loss, or separation | Warning posture is diagnostic only; hard posture is a game failure bound, not an injury limit | Saddle bound reuses qualified engine bound; posture is provisional |
| `FAILED_LOCKOUT` | Absence of the required lockout at authoritative attempt termination, after credible entry into the physical standing-height completion region | Qualitatively unlike the streaming failures: elapsed time in the completion region is not irreversible evidence, so this is evaluated once as a terminal postcondition and never from a timer. Physical lockout at any tick forbids the class. Does not use the adapter's state/boolean | Dwell is retained as observational provenance only and requires GAM-13 calibration |

## Measured 25 kg terminal-lockout evidence (P3A1)

One fresh real 25 kg attempt (`Artifacts/Evidence/GAM-12/GAM-12-P3A1-25kg-lockout-diagnostic.csv`,
trace ticks 118-859 at 100 Hz) falsified both timer formulations of
`FAILED_LOCKOUT`:

| Event | Tick | Measured state |
|---|---:|---|
| standing reference | 118 | bar Y 1.403096 m |
| physical descent onset | 172 | |
| bottom | 483 | bar Y 0.849880 m |
| ascent established | 517 | |
| completion-region entry | 747 | height deficit 0.048604 m, max knee 0.760829 rad, max hip 0.642550 rad |
| trunk erectness qualified | 763 | |
| hip erectness qualified | 813 | |
| bilateral knee lock qualified | 831 | |
| physical lockout achieved | 855 | bar linear stillness was the last gate |

The bar height enters the completion band 108 ticks (1.08 s) before lockout is
physically achieved, and every unmet gate is converging monotonically the whole
time. A 60-tick dwell measured from ascent establishment latches at tick 576, and
a 60-tick dwell measured from completion-region entry latches at tick 806; both
are false positives on a lift that completes. Elapsed time is therefore not
irreversible evidence of a failed lockout, so the class became a terminal
postcondition.

## Targeted literature check

Larsen, Kristiansen, and van den Tillaar (2021), *New Insights About the
Sticking Region in Back Squats*, defines a sticking region around a first local
minimum in upward bar velocity during successful 3-RM squats. This supports
separating a recoverable velocity minimum from terminal failure; it does not
provide game thresholds. [DOI 10.3389/fspor.2021.691459](https://doi.org/10.3389/fspor.2021.691459)

Van den Tillaar, Knutli, and Larsen (2020), *The Effects of Barbell Placement on
Kinematics and Muscle Activation Around the Sticking Region in Squats*, likewise
reports event-defined sticking behavior in successful 5-RM powerlifters. The
detector therefore requires dwell/no-progress/no-recovery rather than a single
low velocity. [DOI 10.3389/fspor.2020.604177](https://doi.org/10.3389/fspor.2020.604177)

Hof, Gazendam, and Sinke (2005), *The condition for dynamic stability*, extends
static COM-over-support reasoning with COM velocity through the extrapolated
COM/margin-of-stability model. The detector uses the qualitative position-plus-
velocity principle in the repository's modeled support bounds; those bounds are
not an exact biological BOS/COP or clinical fall-risk measure. [DOI 10.1016/j.jbiomech.2004.03.025](https://doi.org/10.1016/j.jbiomech.2004.03.025)

## Claim ceiling and limitations

All classifications are deterministic interpretations of this simulation's
copied post-physics values. `MaximumModeledDemand` and `DriveSaturated` are
game-model diagnostics; support/contact quantities are engine/runtime or
engineering-derived observations; joint angles and trunk bounds are game
proxies. No field is treated as measured athlete torque, GRF/COP, injury risk,
muscle failure, tissue failure, or a clinical balance metric. The numeric P3
values are synthetic/domain qualification values and remain independent of the
GAM-13 load-response ladder.
