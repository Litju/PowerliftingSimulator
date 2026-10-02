# ADR-GAM48 — Gate 4b depth isolation

Date: 2026-09-23
Authority: Linear GAM-48 Gate 4b owner review
Run: 20260923-221554
Branch: work/gam-13-squat-load-calibration

GATE4B_STATUS=COMPLETE_WITH_NON_UNIQUE_CAUSAL_CLASSIFICATION
ROOT_DOMAIN=NON_IDENTIFYING
ROOT_SUBDOMAIN=REFERENCE_RULE_LANDMARK_BASIS_AND_PHYSICAL_REALIZATION
NEXT_AUTHORIZED_GATE=GAM48-FOLLOWUP-LOW-LOAD-REALIZATION-DISCRIMINATION

## ESTABLISHED

- The current-head GAM-10 reference qualification passed 4/4. Its calibrated crease/top-offset depth is left -0.07631308 m, right -0.07631314 m, worst -0.07631308 m, and legal.
- The production squat rule measures physical hip and knee ConfigurableJoint anchors. On that same rule-proxy metric, the canonical reference joint centers are +0.0225417614 m on both sides and shallow. The reference preview and production rule-proxy measurements differ by 0.0988549 m.
- Runtime physical-target FK consumed the adapter's exact phase-1 nominal/composition quaternions, the real physical rig's neutral parent-child orientations and joint-space bases, production anchors, and production joint ownership. It did not call the reference solver to construct any physical target. Read-only and repeatability checks passed.
- Fresh-process C0_FULL and HOLD_1.00_FULL passed support/finite-control checks and matched across every reported side, stage, and layer delta at the frozen 1e-5 m tolerance.

| State / delta | Left (m) | Right (m) | Worst side (m) |
|---|---:|---:|---:|
| D_REF, rule-anchor proxy | 0.0225417614 | 0.0225417614 | 0.0225417614 |
| D_NOMINAL_TARGET | 0.02254184 | 0.02254184 | 0.02254184 |
| D_NOMINAL_TARGET - D_REF_GAM10_CALIBRATED_LANDMARKS | +0.09885492 | +0.09885498 | +0.09885492 |
| D_GRAVITY_TARGET | 0.02254184 | 0.02254184 | 0.02254184 |
| D_BALANCE_TARGET | 0.02226769 | 0.0222676918 | 0.0222676918 |
| D_FINAL_TARGET | 0.02226769 | 0.0222676918 | 0.0222676918 |
| D_APPLIED_TARGET | 0.02226769 | 0.0222676918 | 0.0222676918 |
| D_ACTUAL | 0.0431753546 | 0.0401087478 | 0.0431753546 |
| Nominal mapping error, D_NOMINAL_TARGET - D_REF | 0.0000000782 | 0.0000000782 | 0.0000000782 |
| Gravity composition displacement | 0 | 0 | 0 |
| Balance composition displacement | -0.0002741497 | -0.0002741478 | -0.0002741478 |
| Full composition displacement | -0.0002741497 | -0.0002741478 | -0.0002741478 |
| Rate-limit displacement | 0 | 0 | 0 |
| Physical realization error, D_ACTUAL - D_APPLIED_TARGET | +0.0209076647 | +0.0178410560 | +0.0209076647 |

The depth rule threshold is -0.005 m. D_ACTUAL is 48.175 mm shallower than that threshold. The applied target is already 27.268 mm shallower than the threshold under the production rule-proxy metric.

- Settled maximum MODELED_DRIVE_DEMAND was 0.266502559 in both arms; MODELED_DRIVE_DEMAND_HIGH was false at the existing 0.95 threshold. This does not measure the internal PhysX drive-force clamp.
- The maximum final-to-applied angular error was 0.0200017449 rad on each foot target. The rule depth uses hip/knee anchors, so their settled angular lag produced 0 m rate-limit depth displacement.
- The dynamic 25 kg attempt had physical descent, physical bottom, ascent, and physical lockout. Its corrected P3 receipt reports P3_COMPLETION_PREREQUISITES_MISSING=NONE and P3_LEGAL_BOTTOM_SEEN=false; P3 was EVALUABLE/NO_PHYSICAL_FAILURE while P2 reported INSUFFICIENT_DEPTH.

## SUPPORTED

- The physical athlete realizes a depth 20.908 mm shallower on the worst side than its applied-target FK geometry. Physical realization materially contributes under the production rule-proxy metric.
- The current canonical reference legality and production rule legality use different landmark bases. The canonical reference is legal under the calibrated crease/top offsets and shallow under the joint-anchor rule proxy. This reference-to-rule landmark contract changes the bottom depth before gravity/balance composition.

## REJECTED

- A material nominal joint-space mapping error under the matched joint-anchor metric: measured worst-side error is 0.000078 mm.
- Gravity, balance, or full target composition as a material depth cause: each measured displacement is below the predeclared 5 mm threshold.
- Rate limiting as a material depth cause: D_APPLIED_TARGET equals D_FINAL_TARGET in the rule depth metric.
- The previous unique ROOT_DOMAIN=TRACKING_CONTACT_ACTUATION_REALIZATION conclusion. Gate 4b shows a reference/rule landmark-basis mismatch plus a material physical-realization gap.
- Legal bottom as a P3 physical-completion prerequisite.
- MODELED_DRIVE_DEMAND_HIGH as evidence that the PhysX drive-force clamp was reached.
- Joint.currentTorque / solver constraint torque as direct drive torque.

## UNRESOLVED

- A unique ROOT_SUBDOMAIN cannot be assigned from this evidence. The canonical reference and production rule proxy disagree by 98.855 mm, the nominal target is already shallow under the production rule metric, and physical realization adds a further 20.908 mm worst-side error.
- The measured realization gap does not distinguish target tracking, contact coordination, finite actuation, or their interaction. Modeled demand and solver constraint torque do not identify an internal drive-force clamp.
- The exact next authorized gate is GAM48-FOLLOWUP-LOW-LOAD-REALIZATION-DISCRIMINATION. It must keep physics and rules frozen, first use a matched landmark contract, and add only predeclared read-only discriminators. No tuning, heavy-load calibration, GAM-14 work, or GAM-13 merge is authorized by this ADR.
- Claim ceiling: deterministic Unity/PhysX observations and calibrated game rule-proxy geometry; no validated biological or universal biomechanics claim.
