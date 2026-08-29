# State Snapshots and Replay

**Document ID:** `PSMS-ENG-61`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `ENGINEERING/60_RUNTIME_EXECUTION_ORDER.md`, `03_COORDINATES_UNITS_NUMERICS.md`, `GAME/44_REPLAY_SYSTEM.md`

## Repository verification

- Inspect current AttemptTrace/snapshot/replay schemas and storage.
- Measure complete snapshot size and maximum attempt duration.
- Verify reset/restore feasibility for every Rigidbody/ConfigurableJoint/coupling.

## PURPOSE


Define the immutable state boundary used by controllers, rules, failure diagnostics, telemetry, reset evidence, and
recorded-state replay.


    ## INPUTS


Post-physics raw state, lift-specific observations, rule/failure results, events, intent, configuration/ruleset/calibration
IDs.


    ## OUTPUTS


`SimulationSnapshot`, `AttemptTrace`, event stream, hashes, replay container, reset/checkpoint record.


    ## STATE


Snapshot sequence keyed by `attemptId`, `tick`, `simulationTime`; previous/current exchange; fixed-capacity trace; event
ledger; optional baseline/reset snapshot.


    ## UNITS


All fields carry units by schema. Pose m/quaternion, velocities m/s/rad/s, time s, mass kg, angles rad.


    ## COORDINATE CONVENTION


Every pose/vector field declares `W`, body, joint, athlete-local, or BAR. Quaternions normalized. No camera/screen state
in authoritative snapshot.


    ## EQUATIONS


Snapshot hash:

\[
H_k=\operatorname{SHA256}(\operatorname{CanonicalSerialize}(header_k,payload_k)).
\]

Trace chain may use:

\[
C_k=\operatorname{SHA256}(C_{k-1}\Vert H_k)
\]

for evidence integrity. This is not anti-cheat security; it detects accidental mutation/corruption.


    ## ASSUMPTIONS


A complete 100 Hz trace for one attempt fits local memory/storage. State playback is preferred to resimulation.
Snapshots can use fixed arrays because the physical rig topology is frozen.


    ## APPROXIMATIONS


Engine contact manifolds may be summarized rather than fully serialized in normal traces; diagnostic traces can include
more. Floating-point serialization preserves engine values but does not imply cross-platform deterministic resim.


    ## GAME CALIBRATIONS


Maximum active attempt trace initially 30 s = 3000 samples plus setup extension; exact bound tested. Normal snapshot stores
all athlete bodies, bar, key joint/drive diagnostics, lift state/observations/rules/failure. Full contact details are
development-only unless decisive.


    ## NUMERICAL IMPLEMENTATION


Snapshots are immutable value records built after all observers. Use stable field order and explicit version. Never expose
mutable arrays; use pooled builder then freeze/copy into owned storage. Events store exact tick and monotonically ordered
sequence. Replay container has header, channel catalog, snapshots/chunks, events, checksum, optional compression.

Reset is not ordinary replay restore. It applies a baseline state at a tick boundary:

1. stop tick consumption;
2. destroy transient couplings;
3. restore body poses/velocities/sleep states;
4. restore lift/controller/capacity state;
5. recreate verified couplings;
6. clear contact/input/event buffers;
7. assert no overlap and expected configuration;
8. publish a new reset baseline snapshot.


    ## PSEUDOCODE

    ```text
    BuildSnapshot(raw, observations, rules, failure, context):
    builder.Reset(schema_version)
    builder.WriteHeader(attempt_id, tick, time, config_ids)
    builder.WriteAthleteBodies(raw.bodies)
    builder.WriteBar(raw.bar)
    builder.WriteJointAndDriveDiagnostics(raw.joints)
    builder.WriteLiftSpecific(observations)
    builder.WriteIntentAndEvents(context)
    builder.WriteRuleAndFailure(rules, failure)
    snapshot = builder.FreezeImmutable()
    snapshot.hash = CanonicalHash(snapshot)
    return snapshot

LoadReplay(file):
    validate_header_schema_checksum()
    decode_into_read_only_trace()
    validate_monotonic_ticks_events_and_required_channels()
    return trace
    ```

    ## UNITY MAPPING


Plain C# structs/records and binary/JSON serializers; Unity adapters read Rigidbody/joint state. Replays use presentation
proxies, not live physics. Hashing/serialization runs outside the critical step when possible; raw snapshot capture remains
bounded.


    ## FAILURE MODES


Mutable snapshot arrays; missing version/unit/frame; trace uses render state; event order ambiguous; safety overwrites
failure sample; checksum ignored; replay resim; reset leaves velocities/contact buffers; large allocations; save embeds
unbounded traces.


    ## OBSERVABILITY


Snapshot inspector, channel catalog, size report, hash chain, event timeline, schema diff, reset comparison, missing/
invalid channel reasons.


    ## TELEMETRY


Snapshot build time/size, trace memory, serialization/compression time, checksum failures, replay decode/seek, reset diff.


    ## TESTS


Immutability; canonical hash; monotonic ticks; serialization round trip; schema migration/rejection; missing/corrupt chunk;
failure sample preserved; random seek; reset identity; no render/camera state; maximum-length memory.


    ## MUTATION TESTS


Return pooled mutable array; omit units; serialize visible transforms; apply safety then snapshot; ignore checksum;
replay by input; reset only positions; allow duplicate tick.


    ## PERFORMANCE CONSIDERATIONS


Fixed-size arrays/SoA; no per-tick GC; defer hashing/compression if necessary while preserving immutable buffer; target
normal trace tens of MB or less, verified empirically.


    ## CLAIM CLASSIFICATION


A snapshot is authoritative evidence of the game's numerical state, not real-world measurement or cross-platform lockstep.


    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE


**SHIP_V1:** complete immutable snapshots, trace, hash/checksum, reset, state replay.  
**LATER:** chunk/delta compression and sharing.  
**RESEARCH:** deterministic resim comparison.  
**OUT_OF_SCOPE:** blockchain/anti-cheat claims.
