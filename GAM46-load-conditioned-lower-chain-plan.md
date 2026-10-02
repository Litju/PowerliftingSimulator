# GAM-46 LC1 load-conditioned lower-chain plan

## Authority and base

- Linear issue: `GAM-46`
- Repository: `E:\Data\Projects\PowerliftingSimulator`
- Branch: `work/gam-13-squat-load-calibration`
- Start/frozen predecessor head: `4a308cf3e590580d4eb6e3155aaab70bbb99d859`
- Unity: `6000.3.22f1`
- Candidate count: exactly one (`LC1`)

## Candidate freeze

Let `L` be physical bar load in kilograms:

```text
u = clamp((L - 170) / 60, 0, 1)
alpha(L) = u*u*(3 - 2*u)
```

Only the existing F2 lower-chain contribution is multiplied:

```text
ankle = alpha(L) * existing F2 ankle contribution
knee  = alpha(L) * existing F2 knee contribution
hip   = alpha(L) * existing F2 hip contribution
```

The existing F2 phase law and fade are unchanged. F2 abdomen/thorax remain
unchanged. Therefore `L <= 170` has no F2 lower-chain contribution,
`170 < L < 230` uses the smooth cubic introduction, and `L >= 230` uses the
full existing F2 lower-chain contribution. The 170 kg and 230 kg anchors are
not moved.

The implementation is one deterministic multiplier in the existing
experimental feed-forward seam. It adds no candidate branch, controller state,
per-load gains, impedance, capacity, rule/tolerance change, saddle change,
plant change, balance-gain change, or direct force/torque/velocity/transform
assistance. No LC2 or sweep is permitted.

## Qualification loads

Transition region:

```text
155, 170, 180, 190, 200, 215, 230 kg
```

Full standing set:

```text
25, 60, 100, 140, 155, 170, 180, 190, 200, 215, 230, 270, 300 kg
```

Fresh canonical repeats (three each):

```text
25, 60, 140, 170, 300 kg
```

Lifecycle seal: canonical `25 kg` repeated three times. Guard-semantics
diagnostic: one run at `170 kg`, with canonical-pose semantics remaining the
production authority and historical-target-deflection semantics diagnostic
only.

## Gates

All existing GAM-12/GAM-44 standing and lifecycle gates remain unchanged.

### 25 kg start and lifecycle

- three consecutive valid start samples;
- no `START_BAR_MOTION` blocker;
- legal Squat command chronology;
- legal depth;
- physical lockout;
- `P3 = NO_PHYSICAL_FAILURE`;
- accepted P2/P3/terminal semantics;
- complete trace coverage and deterministic repeats.

Compare against the GAM-45 P2 evidence: raw/applied ankle authority, bar
linear/angular motion, zero-crossing/chatter behavior, and posture/support
margins. Passing three ticks by chance is insufficient; the low-load
oscillatory pathology must be materially removed.

### Standing

Every load must satisfy the existing corrected standing gate, including finite
observations, upright posture, retained support, valid saddle, capture-hull
margin, COM speed, canonical-pose error, joint-limit proximity, and sustained
drive-saturation limits. The 300 kg case uses this same gate.

Record alpha, lower-chain preload, pose, deflection, hull capture, COM speed,
raw/applied ankle authority, guard scale, hip/trunk strategy, modeled demand
and saturation, joint limits, saddle linear/angular occupancy, and
support/contact.

### Heavy-load regression

There must be no regression at 230/270/300 kg against the qualified GAM-44
F2+S1 evidence. This is a comparison gate, not permission to retune anchors or
tolerances.

## Stop and decision rules

1. If the 25 kg start/lifecycle phase fails, record `WINNER=NONE` and stop.
2. If any transition load fails, record `WINNER=NONE` and stop; do not move
   anchors.
3. Run the full standing set, repeats, lifecycle seal, and guard diagnostic
   only after the preceding phase passes.
4. `WINNER=LC1` requires every phase, deterministic repeats, material removal
   of low-load oscillation, and no heavy-load regression. Otherwise
   `WINNER=NONE`.
5. Do not start GAM-14, merge GAM-13, or promote a production spec from
   standing-only evidence.

## Required evidence and validation

- LC1 implementation/spec artifact;
- 25 kg start/lifecycle comparison and oscillation analysis;
- transition-region evidence;
- full standing qualification;
- canonical repeats;
- lifecycle x3;
- guard diagnostic;
- `ADR-GAM46-lower-chain-integration-decision.md`;
- focused start/lifecycle tests;
- GAM-46 contract tests;
- MasterSpec verification;
- exact diff review;
- if LC1 wins: full EditMode, default PlayMode, and lifecycle x3.

## Claim ceiling

LC1 is deterministic game-control calibration using external load as a
scheduling variable. It is not a biological recruitment model. The 170/230 kg
anchors are project-evidence boundaries, not human thresholds. Zero crossings
describe observed oscillatory behavior but do not alone prove a mathematical
stable limit cycle.
