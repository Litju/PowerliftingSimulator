# Game Architecture

**Document ID:** `02`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `00_READ_ME_FIRST.md`, `01_PRODUCT_CONSTITUTION.md`

## Repository verification

- None.

## Architectural style

The game uses **layered, snapshot-driven physical gameplay**.

```text
Authoring data
  ├─ athlete profiles
  ├─ lift-specific reference profiles
  ├─ rule profiles
  └─ equipment profiles
        ↓
Fixed-step intent and lift domain
        ↓
Powered physical athlete + equipment
        ↓
PhysX
        ↓
Immutable observation
  ├─ lift-specific state transition
  ├─ rules
  ├─ attempt trace
  └─ presentation snapshot
        ↓
render / audio / UI / replay / save
```

## Assemblies

| Assembly | Responsibility | May write physical state? |
|---|---|---|
| `Powerlifting.Domain` | Value objects, units, IDs, result contracts | No |
| `Powerlifting.Physics` | physical rig, joints, bar, equipment, physics stepping | Only through PhysX setup/drive targets |
| `Powerlifting.Lifts.Squat` | squat phase, reference, contact interpretation, rules | Targets only |
| `Powerlifting.Lifts.Bench` | bench phase, reference, grip/contact interpretation, rules | Targets only |
| `Powerlifting.Lifts.Deadlift` | deadlift phase, slack/ground modes, rules | Targets only |
| `Powerlifting.Game` | meet, career, save, progression | No |
| `Powerlifting.Presentation` | render follower, cameras, UI, audio, VFX | Never |
| `Powerlifting.Telemetry` | observations, traces, metrics, replay | Never |
| `Powerlifting.Tests` | fixtures, oracles, mutations, performance | Test setup only |

Actual assembly names are proposals, not claims about the repository.

## Lifecycle interface

```csharp
public interface IPhysicalLift
{
    LiftKind Kind { get; }
    void BeginAttempt(in AttemptContext context);
    void SetPlayerIntent(in FixedIntent intent);
    PrePhysicsCommand FixedTick(
        in PhysicalObservation previous,
        float dt);
    LiftObservation Observe(
        in PhysicalObservation physical);
    RuleUpdate Evaluate(
        in LiftObservation observation);
    void Reset(in ResetContext context);
}
```

This interface contains lifecycle only. It does not expose generic phases, contacts, reference curves or rule fields.

## Data ownership

- ScriptableObjects are **authoring containers**, not mutable runtime state.
- Runtime profiles are immutable copies validated at scene load.
- State machines own their private state.
- `AttemptTrace` is append-only after attempt start.
- `AttemptResult` is immutable after finalization.
- Save data stores IDs and values, never Unity object references.
- Presentation receives readonly snapshots.

## Event policy

Events communicate finalized facts, not continuously mutable physical state.

Good:

- `CommandIssued`;
- `PhaseChanged`;
- `LiftCompleted`;
- `RuleViolationRecorded`;
- `ReplayAvailable`.

Bad:

- an event every frame containing a mutable Rigidbody;
- presentation asking the rules processor to recompute depth;
- audio events used as state-machine triggers.

## Configuration

All numerical authoring values carry:

- explicit units in field names or typed wrappers;
- source class;
- valid range;
- default;
- calibration version;
- last qualification receipt;
- lift applicability.

Examples:

```text
SquatProfile.depthRuleMargin_m
PoweredJointProfile.maxDrive_Nm
PhysicsProfile.fixedStep_s
ReplayProfile.sampleRate_Hz
```

## Error handling

- Invalid static configuration fails scene qualification, not mid-attempt.
- Runtime non-finite values force a deterministic `SIMULATION_INVALID` result.
- Missing reference curves prevent the relevant lift from starting.
- Missing presentation assets fall back visibly but cannot alter truth.
- Asset-frame and collider-overlap validation runs before physics is enabled.

## Reset architecture

A reset is a transactional operation:

1. stop ticking;
2. clear all lift/runtime state;
3. disable contacts and drives;
4. restore rigid-body poses, velocities and sleep states from a canonical spawn snapshot;
5. restore bar/equipment configuration;
6. perform overlap validation;
7. rebuild contacts with one settle phase;
8. clear trace buffers;
9. re-enable ticking.

No object is teleported during an active competitive attempt.

## Extensibility rule

Additions extend at seams that already express a product concept. They do not create abstraction in anticipation.

- A new venue extends presentation/content.
- A new athlete body creates a new calibrated physical-athlete profile.
- Sumo deadlift creates a separate lift domain.
- A new scoring formula implements a versioned `IScoringFormula`.
- A new rule profile can reuse observation fields, but cannot change the physical simulation retrospectively.

## Architecture fitness tests

- grep/static analyzer proves one `PhysicsScene.Simulate` call site;
- physical bones have Animator write protection during active simulation;
- no lift assembly references another lift assembly;
- rules depend on observation/domain, never presentation;
- replay contains no physics stepping;
- source classes exist for every displayed scientific metric;
- build scripts leave tracked settings unchanged.
