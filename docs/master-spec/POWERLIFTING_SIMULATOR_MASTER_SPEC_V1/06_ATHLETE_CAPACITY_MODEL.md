# Athlete Capacity and Fatigue Model

**Document ID:** `PSMS-06`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `05_POWERED_JOINT_MODEL.md`, `01_PRODUCT_CONSTITUTION.md`

## Repository verification

- Calibrate capacities against the actual rig, bar inertia, timestep, and lift reference motions.
- Record the exact test athlete mass and attribute baseline used for every qualification scenario.
- Do not publish coefficients as measured human strength norms.

## PURPOSE


Provide finite capability that maps player-visible athlete attributes into physical joint-drive authority and makes
light, heavy, grinding, and failed attempts emerge without a binary scripted load threshold.


    ## INPUTS


Athlete mass; global strength; lift specialization; technique; bracing; stability; grip; fatigue/recovery state;
joint family; lift phase; intent activation; optional recent modeled demand.


    ## OUTPUTS


Per-joint available capacity, activation, target-rate quality, balance correction authority, grip coupling capacity,
fatigue increment, and game-facing attribute explanation.


    ## STATE


Persistent athlete profile plus attempt-local fatigue:

`bodyMassKg`, `strength`, `technique`, `bracing`, `stability`, `grip`,
`squatSkill`, `benchSkill`, `deadliftSkill`, family coefficients, and `fatigue_i ∈ [0,1]`.

V1 fatigue is a transparent game state, not a physiological model.


    ## UNITS


Mass kg; capacity N·m; activation and attributes normalized `[0,1]`; time s; fatigue rate s⁻¹; grip coupling force/torque
uses engine drive units and is labeled game calibration.


    ## COORDINATE CONVENTION


Capacity magnitude is joint-frame independent. Direction comes from the lift controller and joint target error.
Left/right values are separate but derived symmetrically unless an authored asymmetry mode is enabled later.


    ## EQUATIONS


Base family capacity:

\[
C_{i,0} = M\,c_{\mathrm{family}(i)}.
\]

Available capacity:

\[
C_i = C_{i,0}
\,S(\text{strength})
\,L_i(\text{lift specialization})
\,B_i(\text{bracing/stability})
\,(1-f_i).
\]

Activation is a bounded first-order response:

\[
\dot A_i =
\begin{cases}
(A_{\mathrm{cmd}}-A_i)/T_{\mathrm{rise}}, & A_{\mathrm{cmd}}>A_i\\
(A_{\mathrm{cmd}}-A_i)/T_{\mathrm{fall}}, & \text{otherwise}.
\end{cases}
\]

Attempt fatigue uses modeled demand `u_i`:

\[
\dot f_i =
k_{\mathrm{fatigue}}\,[\max(0,u_i-u_0)]^p
-k_{\mathrm{recovery}}f_i,
\quad 0\le f_i\le f_{\max}.
\]

This equation is a gameplay response curve. It is not muscle energetics.


    ## ASSUMPTIONS


- A small set of attributes can create a legible game progression.
- Body mass should scale gross available moment in V1.
- Strength changes maximum authority; technique changes reference quality/tolerance more than raw capacity.
- Bracing chiefly affects trunk capacity/reference stability; grip chiefly affects hand-bar coupling.


    ## APPROXIMATIONS


No muscle redundancy, force-length, force-velocity, tendon elasticity, motor-unit recruitment, sex/age-specific norms,
or physiological recovery model is claimed. Angle/velocity capacity curves remain later work unless V1 load calibration
cannot achieve credible sticking/failure without them.


    ## GAME CALIBRATIONS


Provisional family seeds in N·m per kg of athlete mass:

| Family | `c_family` |
|---|---:|
| ankle | 1.30 |
| knee | 3.00 |
| hip | 3.80 |
| lumbar/trunk | 2.80 |
| shoulder | 1.50 |
| elbow | 1.10 |
| wrist/hand stabilization | 0.35 |

These are calibration seeds only. Baseline normalized attributes are `0.50`; mappings use bounded affine or smoothstep
curves with no single attribute exceeding a 1.5× multiplier in V1. Saturation/fatigue parameters are tuned after unloaded,
60 kg, moderate, heavy, and supra-max scenarios are stable.


    ## NUMERICAL IMPLEMENTATION


Pure deterministic C# domain computation at 100 Hz. Clamp every state. Use precomputed profile multipliers.
Fatigue does not update during menu/replay. Meet recovery and career recovery are separate slower game systems and
must not mutate an active attempt except through an immutable starting profile.


    ## PSEUDOCODE

    ```text
    EvaluateCapacity(profile, attempt_state, demand, intent, h):
    for joint in powered_joints:
        base = profile.body_mass_kg * family_coefficient[joint.family]
        strength = map_attribute(profile.strength)
        specialization = map_attribute(profile.skill_for(current_lift))
        support = support_multiplier(joint.family, profile.bracing, profile.stability)
        available = base * strength * specialization * support * (1 - fatigue[joint])

        activation[joint] = first_order(
            activation[joint], intent.activation_for(joint),
            rise_time, fall_time, h)

        fatigue_rate = kf * pow(max(0, demand[joint] - demand_threshold), p)
        fatigue[joint] = clamp(
            fatigue[joint] + h * (fatigue_rate - kr * fatigue[joint]),
            0, max_attempt_fatigue)

        capacity[joint] = max(0, available)
    return capacity, activation, fatigue
    ```

    ## UNITY MAPPING


`AthleteProfile` and `CapacityCalibration` are immutable ScriptableObject authoring inputs copied into pure runtime
records. `AthleteCapacityRuntime` has no Unity callbacks; `PhysicsTickDriver` calls it once before drives are set.
Grip output configures the lift-specific grip adapter, not a generic hand teleport.


    ## FAILURE MODES


Capacities too high produce animation-like invincibility; too low cause unloaded collapse; body mass double-counting;
attribute with no mechanical mapping; fatigue runaway; recovery during active tick from another system; left/right drift;
binary `load > max` failure; published coefficient treated as human norm.


    ## OBSERVABILITY


For every joint/family, expose base, all multipliers, activation, fatigue, final capacity, demand, and saturation dwell.
The game UI shows aggregated, understandable labels; diagnostic UI shows the decomposition.


    ## TELEMETRY


Attempt summary: peak/mean demand by family, time above 1.0 demand, fatigue start/end, grip reserve, bracing reserve.
Never label these as measured muscle activation or true physiological fatigue.


    ## TESTS


- Capacity increases monotonically with strength.
- Zero strength multiplier cannot support unloaded motion.
- Identical profile/input is deterministic.
- Moderate load succeeds while calibrated supra-max emerges as stall/failure.
- Failure is not selected directly by comparing load to a threshold.
- Technique changes tracking/balance quality without silently multiplying every capacity.
- Fatigue remains bounded and resets/restores according to scenario contract.
- All attribute UI labels trace to at least one runtime variable.


    ## MUTATION TESTS


Set infinite capacity; hard-code success by load; remove body-mass scaling; let fatigue become negative; multiply all
attributes into all capacities; update profile mid-attempt; call physiological fatigue; couple visual strain directly
to success truth.


    ## PERFORMANCE CONSIDERATIONS


Linear in joint count; no allocation; all curves pre-sampled or simple arithmetic. Aggregate telemetry once per tick and
write event summaries on transitions.


    ## CLAIM CLASSIFICATION


All coefficients, fatigue, activation, and attributes: `BIOMECHANICALLY_INFORMED_GAME_CALIBRATION`.
Modeled drive demand: `ENGINEERING_DERIVED`. True muscle strength/fatigue/activation: `NOT_OBSERVABLE`.


    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE


**SHIP_V1:** finite family capacity, activation lag, small attempt-fatigue response, seven legible attributes.  
**LATER:** angle/velocity capacity, competition recovery, athlete archetypes.  
**RESEARCH:** validation against human kinetics or strength datasets.  
**OUT_OF_SCOPE:** individualized physiological prediction and medical guidance.
