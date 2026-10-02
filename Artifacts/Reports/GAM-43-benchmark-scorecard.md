# GAM-43 benchmark scorecard

`WINNER=NONE`

## Frozen campaign

* Plant: 5x GAM-7 load-bearing stiffness, sqrt(5) damping, current Saddle V2.
* Development loads: 25 / 60 / 140 / 170 kg in Phase 1; 25 / 60 / 140 / 170 / 300 kg in Phase 2.
* Holdouts: 100 / 155 kg in Phase 1; 230 / 270 kg in Phase 2.
* Phase-1 winner: F1, frozen before Phase 2.
* Phase-2 winner: NONE; Phase 3 stopped by the benchmark rule.

## Phase 1

| Candidate | Development | Holdout | Worst canonical pose | Worst hull margin | Worst drive demand | Worst joint limit | Status |
|---|---|---|---:|---:|---:|---:|---|
| F0 current | 3/4 | 2/2 | 10.0461 deg | 0.0715645 m | 0.1113 | 0.2218 | rejected at 170 kg |
| F1 spine-only re-ID | 4/4 | 2/2 | 3.0635 deg | 0.0672906 m | 0.2872 | 0.0628 | selected F* |
| F2 whole-body re-ID | 4/4 | 2/2 | 3.3678 deg | 0.0718015 m | 0.3573 | 0.0713 | eligible, not selected |

F1 had the strongest overall worst-case headroom among eligible arms and the
simpler architecture. F2's whole-body law produced a much larger 25 kg raw
ankle-demand excursion (`10.8715` bound fractions) despite passing the binary
hard gates.

## Phase 2

| Candidate | 25/60/140/170 standing | 300 kg | 230 kg holdout | 270 kg holdout | Status |
|---|---|---|---|---|---|
| S0: 50 kN/m, 3 kN*s/m, 50 mm | pass | setup valid only | FAIL | FAIL | rejected |
| S1: 50 kN/m, 3 kN*s/m, 100 mm | pass | setup valid only | FAIL | FAIL | rejected |
| S2: 75 kN/m, 3,674.234614 N*s/m, 50 mm | pass | setup valid only | FAIL | FAIL | rejected |

At 230 kg the best observed signed hull margin among the saddle arms was still
`-1.65539014 m` (S2), against the required `> 0.01 m`. At 270 kg the best was
`-1.70010066 m` (S2). The holdout failures also included support loss and
upright failure; changing only the authorized saddle property did not produce
an eligible architecture.

## Phase 3 stop

* Integrated candidate: `NOT_RUN` (`F1 + S*` does not exist).
* Three-repeat integrated standing: `NOT_RUN`.
* 25 kg lifecycle: `NOT_RUN`.
* Posture-guard semantics diagnostic: `NOT_RUN`.

The stop is required by GAM-43 after Phase 2 produces no eligible S*. No
integrated, lifecycle, or guard claim is made.

Evidence:

* Phase 1: `Artifacts/Measurements/GAM-13/GAM43/phase1/`
* Phase 2: `Artifacts/Measurements/GAM-13/GAM43/phase2/`
* Raw completion XML: `Artifacts/Measurements/GAM-13/GAM43/raw/`

## Validation

* Master-spec verifier: PASS (`68` files, hashes PASS, dependencies PASS).
* Focused `GAM13HeavyLoadControlContractTests`: PASS, `3/3`.
* All Phase-1 and Phase-2 benchmark Unity runner XMLs: PASS; expected candidate
  rejection is recorded in the CSV gate rows, not as a test-runner error.
