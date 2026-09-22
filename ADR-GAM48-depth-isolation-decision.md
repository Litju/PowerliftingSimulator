# ADR-GAM48 — squat observation authority and depth-isolation decision

Date: 2026-09-22
Authority: Linear `GAM-48`
Qualified branch: `work/gam-13-squat-load-calibration`

## Decision

```text
GAM48_STATUS=COMPLETE
ROOT_DOMAIN=TRACKING_CONTACT_ACTUATION_REALIZATION
CAUSAL_CLASSIFICATION=SUPPORTED_BUT_NOT_UNIQUE
NEXT_AUTHORIZED_PHYSICS_GATE=GAM48-FOLLOWUP-LOW-LOAD-REALIZATION-DISCRIMINATION
```

The canonical GAM-10 reference is legal, and the same current-head FK path
maps its nominal bottom to the same legal depth. The actual 25 kg physical
realization remains shallow. This establishes a realization-domain blocker but
does not identify a unique subcause among tracking, contact coordination, and
finite actuation/control realization. No physics tuning is authorized by this
ADR.

## ESTABLISHED

- Persistent foot authority is coherent: active platform collision pairs are
  idempotent through Enter/Stay/Exit, reset/load clears per-step buffers without
  manufacturing no-contact, and aggregate support can use the latest persistent
  geometry when a sleeping/static step has no new manifold.
- The qualified start run is copied into the trace before Squat. P2 evaluates
  that same contiguous start context; P3 receives the explicit Squat boundary
  and standing reference and ignores pre-command settling motion.
- Gate 3 fresh-process isolation passed. `HOLD_1.00_FULL == C0_FULL` passed at
  absolute tolerance `1e-5`; both arms matched numeric and categorical fields.
  Two fresh canonical baseline processes also matched at `1e-5` and in P2/P3
  classification.
- Current-head GAM-10 reference qualification passed. Reference bottom depth
  was `-0.07631308 m` on both sides, with bilateral legal depth and planted
  feet.
- FK-only mapping reproduced `-0.07631308 m` on both sides within `1e-5 m`,
  with no dynamics or PhysX step in the decomposition.
- Corrected full-composition held bottom remained shallow:
  `0.04317536 m` settled worst-side depth, support retained, finite control,
  not legal. C0 and HOLD were numerically equivalent.
- The corrected dynamic 25 kg lifecycle reached physical descent, physical
  bottom, ascent, and physical lockout; P3 was `NO_PHYSICAL_FAILURE` with
  `LEGAL_BOTTOM` missing. P2 was `EVALUABLE/NO_LIFT` with
  `INSUFFICIENT_DEPTH` only.

## SUPPORTED

- The remaining blocker is physical target realization: the legal FK target is
  not realized by the dynamic physical athlete at the canonical bottom.
- Control-state evidence is consistent with, but does not uniquely identify,
  target tracking error, contact coordination, and finite actuation/realization
  as competing sublayers. The dynamic baseline reached balance saturation on
  55 samples and drive saturation on 11 samples; C0/HOLD settled control was
  finite with no saturation ticks.
- C1 and C2 are diagnostic composition arms only. C1 settled at `0.420322478
  m` and C2 at `0.428600818 m`; both lost support throughout the settled
  report, so neither establishes a balance or preload causal winner.

## REJECTED

- Observation false-no-contact as the canonical dynamic cause: corrected
  dynamic support was retained and the P2 support violation disappeared.
- Pre-command settling as P3 descent: the bounded detector regression rejects
  it.
- `ROOT_DOMAIN=DYNAMIC_TRACKING_TIMING`: the held full bottom remained shallow
  after the fixed settle window.
- `ESTABLISHED_CAUSE=DYNAMIC_BALANCE_DEPTH_BIAS`: C1 was not a supported/legal
  held bottom.
- `ESTABLISHED_CAUSE=EQUILIBRIUM_PRELOAD_DEPTH_BIAS`: C2 remained shallow and
  unsupported.
- `SUPPORTED_BLOCKER=NOMINAL_REFERENCE_OR_TRACKING_MAPPING`: the independent
  reference and FK mapping are legal.
- Any biological GRF/COP, muscle, internal-force, or clinical claim.

## UNRESOLVED

The evidence cannot distinguish a unique cause within the realization domain:

- nominal joint target tracking/closed-chain reconciliation;
- contact realization and support coordination under the physical target;
- finite actuator authority/timing and its interaction with the target.

These remain an explicit hypothesis set. Correlated depth, support, demand, and
saturation observations are not treated as causal evidence.

## Exact next authorized physics gate

`GAM48-FOLLOWUP-LOW-LOAD-REALIZATION-DISCRIMINATION` is the only next authorized
physics gate. It is limited to the canonical 25 kg case and must keep the 5x
plant, LC1, S1, F2 spine law, solver, capacity, bar model, balance bounds,
rules, tolerances, and direct-assistance prohibition frozen. It may add
read-only target/actual, contact, and finite-authority diagnostics and
predeclared low-load discriminating fixtures; it may not tune parameters,
introduce support, write transforms/velocities/forces, or begin heavy-load
calibration. GAM-13 is not started or merged by this ADR.

## Claim ceiling and evidence

Claims are limited to deterministic Unity/PhysX engine observations, FK
geometry, and game-derived support/depth/control proxies. No real-world
biomechanical or causal control claim is made.

- Gate 1: commit `735e034`, support-authority regressions 3/3 PASS.
- Gate 2: commit `bd34a5f`, lifecycle EditMode 18/18 PASS and fresh-process
  qualifier/P2/P3 PlayMode PASS.
- Gate 3: commit `f259ddd`, ten isolated Unity processes and equivalence receipt.
- Gate 4: current-head GAM-10 qualification XML, FK decomposition, corrected
  C0/C1/C2 summaries/traces, and corrected dynamic baseline artifacts under
  `Artifacts/Measurements/GAM-48/`.
