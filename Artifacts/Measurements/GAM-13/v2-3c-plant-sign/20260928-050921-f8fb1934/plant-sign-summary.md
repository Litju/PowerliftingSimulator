# GAM-13 V2-3C AP plant-sign probe

- Unity: `6000.3.22f1`
- Substrate: cleaned unloaded `SquatPhysicalPrototype`; each arm loaded a fresh scene, reset to tick 0, and recaptured the canonical standing COM reference.
- Probe: AP feedback contribution disabled at ankle, hip, and trunk target allocations; ML feedback remained enabled; both ankle logical AP targets received only the listed ±1° residual.
- Measurement window: post-physics ticks 1–5 at 100 Hz (0.05 s), declared before the run.
- COP source: Unity contact-impulse estimate. Tick 0 has no simulated contact impulse and is recorded as unavailable; geometric plantar registration is recorded separately.
- Demand source: command-side modeled drive-channel pressure; it is not measured drive torque. Solver torque is labeled separately as an engine solver diagnostic.

## Paired measurement

- θ+ = `0.0174532924` rad; θ− = `-0.0174532924` rad.
- Mean COP_AP (+1°) = `-0.06317337` m.
- Mean COP_AP (−1°) = `-0.0975587443` m.
- COP_AP (+) − COP_AP (−) = `0.0343853757` m.
- G_ap = ΔCOP_AP / Δθ = `0.98506844` m/rad.
- Paired-midpoint responses: +1° `0.0171926916` m; −1° `-0.0171926841` m. This midpoint is derived from the two requested arms; no zero-target arm was added.
- Paired COP difference standard error = `0.009542162` m; signal/scatter = `3.60352063`.
- First post-physics COM_AP acceleration: +1° `0.0172294863` m/s² (forward); −1° `0.105448514` m/s² (forward).
- Maximum COM_AP displacement in window = `0.0005748272` m.
- Bilateral foot contact persisted for every post-physics sample: `True`.
- Target realization within 0.1°: `True`.
- Clear magnitude (≥2 mm pair separation and ≥3× paired standard error): `True`.
- Opposite-signed paired-midpoint responses: `True`.

## Classification: `POSITIVE_ANKLE_TARGET_MOVES_COP_FORWARD`

Classification requires complete COP samples, persistent bilateral contact, target realization, the predeclared early window, opposite-signed centered responses, and the clear-signal threshold. This classification identifies the measured local target-to-COP derivative; it does not claim drive torque from modeled demand.

## Tick-1 top-three active drive channels

PLUS_1_DEG:
- `right_upper_arm` `SWING_YZ`: normalized demand `1.36255562`, position torque contribution `0/618.0233/938.4224` Nm, velocity torque contribution `0/-355.214783/-68.56829` Nm, maximumForce `666.9` Nm.
- `left_upper_arm` `SWING_YZ`: normalized demand `1.36177361`, position torque contribution `0/-623.628052/933.432` Nm, velocity torque contribution `0/365.133667/-62.8300552` Nm, maximumForce `666.9` Nm.
- `right_upper_arm` `TWIST`: normalized demand `0.448497444`, position torque contribution `-427.5557/0/0` Nm, velocity torque contribution `128.452713/0/0` Nm, maximumForce `666.9` Nm.

MINUS_1_DEG:
- `right_upper_arm` `SWING_YZ`: normalized demand `1.3625617`, position torque contribution `0/617.974854/938.4674` Nm, velocity torque contribution `0/-355.301/-68.56835` Nm, maximumForce `666.9` Nm.
- `left_upper_arm` `SWING_YZ`: normalized demand `1.36178792`, position torque contribution `0/-623.5985/933.461243` Nm, velocity torque contribution `0/365.2272/-62.8128624` Nm, maximumForce `666.9` Nm.
- `right_upper_arm` `TWIST`: normalized demand `0.449433982`, position torque contribution `-427.5743/0/0` Nm, velocity torque contribution `127.846786/0/0` Nm, maximumForce `666.9` Nm.

Full channel components and contributions are in the paired `*-tick1-top3-drive-channels.csv` files. `position_error_torque_*` and `velocity_error_torque_*` are modeled terms; `solver_torque_*` is a separate engine solver diagnostic.
