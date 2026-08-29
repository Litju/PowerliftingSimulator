# Repository Constitution

## Purpose

This repository is the canonical implementation of Powerlifting Simulator.

## Design authority

`POWERLIFTING_SIMULATOR_MASTER_SPEC_V1` is the frozen design authority. It is installed at [`docs/master-spec/POWERLIFTING_SIMULATOR_MASTER_SPEC_V1/`](master-spec/POWERLIFTING_SIMULATOR_MASTER_SPEC_V1/).

The installed bundle is immutable. Repository-specific amendments must be separate, explicit records; the master specification is never silently edited to fit an implementation.

## Implementation authority

Repository code and tests implement the master specification. Measured repository and runtime evidence can expose a false assumption, but cannot silently redesign the architecture.

## Model-to-code protocol

Every work wave follows:

```text
frozen spec
→ implementation
→ automated tests
→ physical/visual evidence where applicable
→ review
→ commit/PR
→ acceptance
```

## Architecture-change protocol

If measured repository evidence falsifies a design assumption, record:

```text
OBSERVATION
EXPECTED
ACTUAL
MINIMAL_REPRODUCTION
MEASURED_EVIDENCE
AFFECTED_SPEC_CONTRACT
PROPOSED_AMENDMENT
```

Architecture changes require explicit owner review and an amendment/ADR. Implementation must not silently redesign the master architecture.

## Invariants

- **Determinism:** Everything that can reasonably be done deterministically should be deterministic.
- **Simplicity:** Use the simplest model that satisfies the physical and game contract.
- **Physics/product:** This is a game. Scientific rigor means correct equations, units, assumptions, bounded models, and honest claims—not maximum model complexity.
- **Exercise domains:** Squat, bench press, and deadlift remain independent mechanical domains. Shared substrate is allowed; shared exercise semantics are not.
- **Physical authority:** One final physical authority exists for each physical body/joint.
- **PhysX:** PhysX owns forward physical motion.
- **Human-centric product:** The visible human athlete is the player-facing product center.
- **Claims:** Scientific claims may not exceed observability and model validation.
- **Replay:** Replay is recorded-state playback, not physics re-simulation.
- **Rules:** Rules consume authoritative observations. Presentation never determines lift truth.
- **Visual quality:** Visual quality is a release gate.
- **Testing:** Every shipping model requires executable acceptance tests.
- **Research promotion:** Research complexity cannot enter shipping without explicit evidence-backed promotion.

## GAM-1 scope lock

GAM-1 establishes repository authority only. It does not implement gameplay, physics, humanoid or barbell systems, rules, UI, audio, cameras, replay, career, assemblies, architecture packages, coordinates, units, or the deterministic test harness.
