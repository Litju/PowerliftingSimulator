# Runtime Execution Order

**Document ID:** `PSMS-ENG-60`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `02_GAME_ARCHITECTURE.md`, `03_COORDINATES_UNITS_NUMERICS.md`, `08_INPUT_AND_PLAYER_INTENT.md`

## Repository verification

- Inspect every Update/FixedUpdate/LateUpdate and script-execution-order dependency in the repository.
- Find all calls to Physics.Simulate/PhysicsScene.Simulate and all force/torque/transform writers.
- Instrument the existing runtime to prove actual ordering before migration.

## PURPOSE


Freeze a single, testable order for input, lift logic, reference motion, finite drives, PhysX, observation, rules,
failure, snapshots, telemetry, rendering, camera, UI, and audio.


    ## INPUTS


Real-time delta, Input System device events, meet/rule command events, previous immutable physics snapshot, active lift
controller, local PhysicsScene, presentation state.


    ## OUTPUTS


Exactly one simulated fixed tick per tick request; one immutable post-tick snapshot; ordered events; interpolated render
state; presentation updates.


    ## STATE


`PhysicsTickDriver` owns accumulator/tick/scene stepping. `FixedPipeline` owns ordered participants. `SnapshotExchange`
holds previous/current complete snapshots. `PresentationLoop` owns interpolation and render-only systems.


    ## UNITS


Real/render time and simulation time in s; fixed `h=0.010 s`; tick `ulong`.


    ## COORDINATE CONVENTION


No special coordinate math beyond PSMS-03. Order boundaries specify which snapshot/frame is legal to read.


    ## EQUATIONS


Control at tick `k` consumes completed observation `x_k` and produces commands `u_k`; PhysX realizes:

\[
x_{k+1}=\Phi_h(x_k,u_k,c_k),
\]

where `c_k` includes contacts/constraints. Observers derive immutable `o_{k+1}` after the step. This one-tick causal
structure prevents same-tick algebraic loops.

Render time interpolates `x_k,x_{k+1}` and never feeds `x_r` into `\Phi_h`.


    ## ASSUMPTIONS


All physical write paths can be centralized. Unity callbacks are adapters, not implicit architecture. A local PhysicsScene
can be manually simulated in the target Unity version.


    ## APPROXIMATIONS


Some engine-internal contact callbacks occur during simulation; they are buffered and consumed after the step.
Presentation events may be one render frame after the physical event but keep the exact tick timestamp.


    ## GAME CALIBRATIONS


Fixed step 100 Hz; max four normal catch-up ticks; bounded accumulator. Event priorities and script order are constants.
No variable-step fallback.


    ## NUMERICAL IMPLEMENTATION


### Dynamic/render-frame prelude

1. Poll/process Input System in its configured dynamic update.
2. Timestamp action edges and continuous values.
3. Accumulate real time.
4. Execute zero or more complete fixed ticks, bounded.
5. Interpolate the two latest snapshots.
6. Apply visible rig/bar render pose.
7. Apply bounded visual rig corrections.
8. Update camera, UI, audio, VFX.
9. Render.

### One fixed tick

1. `BeginTick`: increment tick/time, clear per-tick event/contact buffers.
2. Drain meet/referee events whose simulation timestamp is due.
3. Sample one `PlayerIntentFrame`.
4. Active lift controller consumes **previous** immutable observation and events.
5. Sample lift-specific reference motion.
6. Evaluate athlete capacity/activation/fatigue request.
7. Configure powered joints and lift-specific couplings; assert one writer.
8. Run initialization/invariant guard.
9. Call `PhysicsScene.Simulate(h)` exactly once.
10. Freeze raw rigid-body/joint/contact state.
11. Run lift-specific biomechanics observer.
12. Run lift-specific physical-completion/state-ready observer.
13. Run rules processor.
14. Run failure detector and freeze safety handoff if latched.
15. Assemble and publish immutable `SimulationSnapshot`.
16. Append trace/telemetry and ordered event log.
17. Queue presentation events.
18. `EndTick`: assert all mandatory stages ran exactly once.

Safety presentation/actuation requested by a failure begins on the next tick after the failure snapshot, so it cannot
erase the decisive state.


    ## PSEUDOCODE

    ```text
    DynamicUpdate(real_dt):
    input_adapter.Capture()
    accumulator.Add(real_dt)
    while accumulator.CanStep() and steps < MAX_CATCHUP:
        physics_tick_driver.StepOne()
        accumulator.Consume(H)
    presentation.Render(snapshot_exchange.previous,
                        snapshot_exchange.current,
                        accumulator.Alpha())

StepOne():
    context = BeginTick()
    context.events = event_queue.DrainDue(context.time)
    context.intent = intent_buffer.Sample(context.interval)

    request = active_lift.ControllerTick(previous_snapshot, context)
    reference = active_lift.SampleReference(request, previous_snapshot)
    capacity = athlete_capacity.Evaluate(request, previous_snapshot)
    physical_adapter.Configure(reference, capacity)
    authority_guard.AssertSingleWriters()

    local_physics_scene.Simulate(H)

    raw = raw_state_collector.Freeze()
    biomech = active_lift.ObserveBiomechanics(raw)
    rule = active_lift.EvaluateRules(raw, biomech, context)
    failure = active_lift.DetectFailure(raw, biomech, rule, context)
    snapshot = snapshot_builder.Build(raw, biomech, rule, failure, context)
    snapshot_exchange.Publish(snapshot)
    trace.Append(snapshot)
    EndTickAssertions()
    ```

    ## UNITY MAPPING


One bootstrap MonoBehaviour receives `Update` and calls pure/runtime services. No lift/joint/rule component owns
`FixedUpdate`. `LateUpdate` or a render-phase component applies visible poses. Unity collision callbacks write only
fixed-size contact buffers tagged with tick. Script Execution Order is minimal and asserted.


    ## FAILURE MODES


Automatic plus manual simulation; multiple FixedUpdates write drives; current state read before PhysX and mislabeled
post-state; rules after safety mutation; presentation event used as command; camera/UI reads mutable transforms; input
edge consumed twice; trace append before observer; reset midway through a tick.


    ## OBSERVABILITY


Per tick stage bitmask and timestamps; writer registry; step caller; snapshot IDs; queue counts; stage duration profiler;
development assertion on missing/duplicate/out-of-order stage.


    ## TELEMETRY


Stage CPU times, catch-up count, accumulator, contact/event counts, step ownership, snapshot publication latency,
presentation interpolation alpha.


    ## TESTS


Expected stage order; one-step/one-snapshot; duplicate Simulate; missing stage; same-tick feedback mutation; safety next
tick; input event once; automatic simulation disabled for scene; render cannot mutate snapshot; reset only at boundary.


    ## MUTATION TESTS


Enable FixedUpdate controller; add second Simulate; run rules before physics; apply safety before snapshot; read render
pose in control; append trace twice; variable step; presentation issues command.


    ## PERFORMANCE CONSIDERATIONS


Each stage has a ProfilerMarker. No allocations in fixed path. Expensive analysis/replay work is deferred after attempt.


    ## CLAIM CLASSIFICATION


Execution order is engineering authority. Exact PhysX internal order is engine implementation and not reconstructed.


    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE


**SHIP_V1:** single causal pipeline above.  
**LATER:** safe jobification of pure observation/analysis stages.  
**RESEARCH:** alternate simulation backends.  
**OUT_OF_SCOPE:** uncontrolled callback-based architecture.
