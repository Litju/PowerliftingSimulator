# GAM-11 physical squat runtime receipt

## Runtime

- Base: `9cef01d078b64f03f820fae3ef50f61a1f9726da`
- Implementation commit: `99cb3ef`
- Canonical saved scene: `Assets/Scenes/Prototype/SquatPhysicalPrototype.unity`
- Unity: `6000.3.22f1`
- Physics: isolated `PhysicsScene`, 100 Hz, `dt = 0.010 s`
- Production objects: `FoundationBootstrap`, `PhysicalAthleteRig`, `PhysicalBarbell`, `SquatPhysicalAdapter`, finite `SquatBarSaddle`, foot contact detectors, camera, and runtime HUD
- The scene is registered in `EditorBuildSettings` and is instantiated from production scene runtime, not only from NUnit setup.

## Controls

- `Space`: brace / confirm
- `S`: yield / descend
- `W`: drive / ascend
- `A` / `D`: bounded balance bias
- `R`: reload/reset the production scene
- `B`: release the finite saddle for failure inspection
- `1`: unloaded athlete-only mode; `2`: 25 kg; `3`: 105 kg

## Authority checks

- One registered pre-physics callback owns the command-to-drive path: `PhysicalAthleteRig.PrePhysicsStep` calls `SquatPhysicalAdapter.PrepareCommands`, then `PoweredJointController.Step` writes finite joint drives.
- The athlete remains dynamic, including the pelvis; feet are ordinary dynamic contacts and are not pinned.
- The barbell remains one dynamic gravity-enabled `Rigidbody`.
- The saddle is one finite `ConfigurableJoint` with `projectionMode = None`, finite spring/damper/max force, and finite break force/torque.
- No GAM-11 balance `AddForce`/`AddTorque`, stabilization velocity write, transform-driven squat, bar parenting, hidden support, or hand mechanical constraint was added.
- Accumulated foot slip uses actual platform contact-point tangential velocity with a documented 1 cm/s solver-noise floor; the HUD still exposes instantaneous slip speed.

## Qualification

- EditMode focused suite: 5/5 passed (`Artifacts/GAM11-editmode-final.xml`).
- PlayMode production suite: G1 unloaded passed, G2 25 kg passed, G4 mutation gates passed; G3 105 kg is retained as a real finite-physics failure (`Artifacts/GAM11-playmode-final.xml`).
- G1 observed standing pelvis: 1.020 m; bottom: 0.529 m; final lockout: 1.020 m.
- G2 observed bar: 1.443 m standing, 0.970 m bottom, 1.422 m final; saddle stayed attached with approximately 0.012 m maximum logged separation.
- G3 observed bar loss: 1.198 m (1.421 m to 0.223 m); the dynamic coupled athlete/bar deviation remains visible and is not hidden by relaxed thresholds or artificial supports.

## Owner review

Actual editor screenshots are in `Artifacts/Evidence/GAM-11/` for unloaded standing/bottom/ascent, 25 kg standing/bottom/ascent, and 105 kg standing/bottom/ascent. Unity is left open on the canonical scene in Play mode with the HUD controls visible. Owner visual review is required before GAM-11 is marked complete.

Status: `OWNER_VISUAL_REVIEW`
