# GAM-50 Physics Benchmark V1 — final qualification

**Disposition: `PHYSICS_BENCHMARK_QUALIFIED`**

**Final validated candidate: `776feac0d13dec34c43e02ba88abd576713cf93d`**

**Physics measurement source: `68d30852f02f4005a9e915a2dbb18c4c27abb941`**

The 776feac candidate adds two test-only verification fixes after the final
physics run: `4330bca` updates an obsolete GAM-13 knee-profile assertion, and
`776feac` makes the GAM-12 lifecycle driver maintain player Drive intent and
foot contacts. Production physics files and physics configuration are
unchanged from the 68d3085 measurement source. The full final EditMode and
affected PlayMode suites were rerun on 776feac.

## Benchmark totals

- Physics Benchmark V1: **47 / 47 invocations passed, 0 failed, 0 skipped**.
- 32 benchmark cases; 7,023 recorded metric rows; **863 / 863 gated rows
  passed**; failure matrix has zero rows and no earliest failing layer.
- Fresh-process Editor repeatability: 10/10 at 25 kg and 10/10 at 140 kg;
  outcomes, event ticks, and every per-tick state hash agree within each load.
- Full EditMode: **251 / 251 passed, 0 failed, 0 skipped**.
- GAM-12 rules/failure/lifecycle/observation trace: **4 / 4 passed**.
- GAM-49 depth provider: **2 / 2 passed**.
- Powered-joint actuator contract: **4 / 4 passed**, included in the full
  EditMode count.

The exact XML, Unity logs, per-case raw metrics, full squat traces, and hashes
are retained under `runs/` and `final-verification/`. `results.json` and
`failure-matrix.json` are the aggregated benchmark outputs.

## Repeatability

| Load | Outcome | Legal depth | Reversal | Ascent | Lockout terminal | Failure | Hash rows/process | Sequence SHA-256 |
|---:|---|---:|---:|---:|---:|---|---:|---|
| 25 kg | PHYSICAL_LOCKOUT (10/10) | 347 | 390 | 405 | 731 | NONE; balance onset/latch absent | 731 | `6f9cd363818b8bf18116b6253709a9b9fdf63438b433f86f3b34e1a8e6e6f714` |
| 140 kg | PHYSICAL_LOCKOUT (10/10) | 335 | 392 | 406 | 733 | NONE; balance onset/latch absent | 733 | `56158c44889b03c9c79b7415d7eeb6a948bfed055a5876c2179bc507ae6f6872` |

Each load has categorical agreement 10/10, one unique state-hash sequence,
zero state-hash divergence, and zero spread in legal-depth, reversal, ascent,
lockout entry, lockout qualification, and terminal ticks. Key bar, COM,
support-margin, and peak active-drive metrics have zero observed range; the
population standard deviations are zero or float-roundoff scale. Per-run
values are in `repeatability-summary.json`.

## Canonical physical outcomes

| Load | Physical result | Depth / reversal / ascent / lockout ticks | Qualification interpretation |
|---:|---|---|---|
| 25 kg | PHYSICAL_LOCKOUT | 347 / 390 / 405 / 731 | Easy clean completion. |
| 60 kg | PHYSICAL_LOCKOUT | 341 / 390 / 405 / 731 | Moderate clean completion. |
| 140 kg | PHYSICAL_LOCKOUT | 335 / 392 / 406 / 733 | Heavy completion. |
| 170 kg | PHYSICAL_LOCKOUT | 333 / 393 / 407 / 734 | Near-max completion. The official noise-gated detector returns `NoStickingRegion`; visible recoverable sticking remains a GAM-13 gameplay/controller acceptance item. |
| 300 kg | SETUP_NOT_PHYSICALLY_QUALIFIED at tick 500 | No legal depth, reversal, ascent, or lockout | Physical collapse during loaded setup: bar y=0.223943 m, COM y=0.224578 m, rear support margin −1.214544 m. This is not reported as a mid-ascent stall. |

Per-load terminal bar/COM/support values and active-squat drive peaks are in
`raw/canonical-mechanics.json`; the source traces are linked there.

## Numerical convergence

**`CONVERGED_ENOUGH`** for the production configuration:

- Authoritative game tick **0.010 s**; **2 PhysX substeps at 0.005 s** each;
  athlete solver **255 position / 1 velocity iterations**; strength scale 0.75.
- At loads 0–140 kg, production-vs-0.005 s standing maxima were 0.000154272
  rad tracking delta, 0.000046254 m pelvis-sag delta, and 0.000084974 m AP
  support-margin delta.
- With squat traces aligned to the first DESCENT command, 0.005 s reference
  and production event times agree within 15 ms; paired bar paths differ by
  at most 2.252 mm and bottom bar height by at most 0.184 mm. The absolute
  trace start differs by 0.255 s because the 50-tick setup window uses the
  different tick size.
- Static oracle error approaches the 255-iteration result as position
  iterations rise: mean relative error 0.168429 (56), 0.0579246 (128),
  0.0215102 (192), and 0.00892024 (255); maximum absolute error falls from
  37.9881 Nm to 11.7335 Nm, 6.2559 Nm, and 4.73351 Nm. Production has zero
  gated oracle failures.
- Velocity-iteration 1/2/4 physical output deltas are negligible for measured
  0–140 kg cases. The 2/4 sweep test XMLs are retained as **1/6 passed** each:
  the shared override also changes bar velocity iterations from production 6,
  so five production-configuration assertions fail. This is not represented
  as a passing sweep; no tolerance was changed.
- At 300 kg, dt=0.020 s characterization is materially worse than production:
  pelvis drop 0.876952 m vs 0.827390 m and peak drive-demand fraction 4.64276
  vs 0.533946.

Full run IDs, paired measurements, and this caveat are in
`convergence-summary.json` and `sweeps/`.

## B16: ConfigurableJoint vs ArticulationBody

**KEEP ConfigurableJoint; migration is not authorized.** The gated
ConfigurableJoint arm passes 45/45 metrics against the declared per-joint
realization tolerance `max(5% of analytic expected angle, 0.002 rad)`; the
maximum gated error is 0.0001868 rad. The broader anchor-separation sweep
peaks at 0.0115 mm, far below the declared 2 mm limit.
Production-cell CPU samples are comparable: 0.122–0.174 ms for
ConfigurableJoint and 0.052–0.187 ms for ArticulationBody. The candidate
ArticulationBody arm has no material practical advantage and its driven arm
under the production two-directional friction model is inert / has large
residual motion. No constraint-stretch blocker remains for the qualified
ConfigurableJoint arm.

## Platform scope and claim ceiling

Same-platform repeatability is **qualified for Windows Editor on Unity
6000.3.22f1**, for the tested scenarios and loads. Editor-vs-Windows-standalone
parity is **`BLOCKED_BY_TOOLCHAIN`**: Windows Build Support (IL2CPP) is absent;
`D:\Dev\Unity\6000.3.22f1\Editor\Data\il2cpp\build\deploy` and
`UnityLinker.exe` are missing. Do not infer standalone parity from Editor
results. Exact post-install build/run/compare commands are in the permanent
GAM-50 receipt.

The qualification freezes the shared physical substrate for the measured
contract. It does not qualify visible recoverable sticking at 170 kg, gameplay
controller acceptance, a 300 kg lift, or cross-runtime parity. No unresolved
substrate defect explains the current 170 kg sticking limitation.
