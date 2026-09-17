# GAM-13 heavy-load standing-control architecture review

Date: 2026-09-17  
Branch: `work/gam-13-squat-load-calibration`  
Starting head: `e512d141e64fe33dfa186c549738c529e16ba3c3`  
Unity: `6000.3.22f1`  
Claim class: `GAME_ENGINE_CONTROL_CALIBRATION`

## Observation

The prior GAM-13 25 kg physical baseline remains the reference evidence; the
post-change full GAM-12 lifecycle was not re-run before this blocked handoff.
The candidate Stage-A standing hold passes at 25 kg, but
candidate load-general feedforward still loses credible standing/setup control
at 60 kg and 300 kg. The failure happens before a meaningful squat attempt;
it is not a finite-capacity or P3 detector result.

## Discriminating evidence

The deterministic Stage-A fixture used fresh production scenes, the physical
saddle, dynamic feet, the production balance loop, a 6 s setup hold, and fixed
gates: support persistence, upright posture, capture margin, COM speed, foot
pitch, posture error, joint-limit proximity, saddle separation, and finite
values.

| Candidate | 25 kg | 60 kg | 140 kg | 170 kg | 300 kg |
|---|---:|---:|---:|---:|---:|
| seed impedance 1x | PASS | FAIL | FAIL | FAIL | FAIL |
| fixed impedance 2x | PASS | FAIL | PASS | PASS | FAIL |
| fixed impedance 3x | PASS | FAIL | PASS | PASS | FAIL |
| fixed impedance 4x | diagnostic FAIL | FAIL | diagnostic not run | diagnostic not run | FAIL |
| fixed impedance 8x | diagnostic FAIL | FAIL | diagnostic not run | diagnostic not run | FAIL |

At the 3x candidate, the completed closed-loop balance-gain grid tested COP
tracking gains 0.1, 0.25, 0.5, 0.75, 1, 1.5, 2, 3, and 4 with a measured
target-to-COP seed of 0.30 m/rad. Every 60 kg candidate failed the standing
gate. The guard-off diagnostic also failed, so simply removing the guard is not
a valid repair.

Fresh perturbation measurements on the 3x plant gave approximate ankle-target
to COP slopes of 0.462, 0.340, 0.275, 0.262, and 0.186 m/rad at 25, 60, 140,
170, and 300 kg respectively. This directly rejects algebraically reusing the
old 0.20446 m/rad value after the impedance change and shows that a single
constant inversion is not yet qualified.

The 3x candidate also failed with the standing spine-bias grid
`3:3; 5:5; 7:6; 9:8; 11:10; 12:12` degrees at 60 kg. The tested lower-chain
candidate (+5° ankle, +2° knee, +2° hip) did not restore support at 60/300 kg.
Across failures, modeled demands remained finite and below the actuator
ceiling before the posture/balance collapse; no NaN/Inf or load-scripted result
was observed.

## Architecture decision status

The intended architecture remains the correct direction:

- external load stays in the physical bodies and observations;
- impedance is fixed across loads;
- equilibrium compensation is bounded target-space feedforward;
- dynamic balance is feedback only;
- athlete capacity is independent of bar load;
- P1/P2/P3/P4 truth ownership remains unchanged.

The current candidate architecture is not qualified for the requested domain.
The blocker is quantitative, not a lack of another speculative tuning value:

`60 kg and 300 kg do not establish and hold upright supported setup on the
fixed candidate plant under bounded target-space compensation; the COP loop
oscillates/withdraws ankle authority and the athlete falls backward before an
attempt.`

No master-spec hash was changed, no PR/merge was created, and no GAM-14 work
was started. The next authorized work must either identify a physically valid
whole-body equilibrium/balance substrate or prove a rig/contact/topology
defect. It must not resume capacity calibration from the collapsed traces.
