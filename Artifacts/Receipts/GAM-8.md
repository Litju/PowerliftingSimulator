MISSION=POWERLIFTING_SIMULATOR_GAM_8_PHYSICAL_BARBELL_AND_STATE_SEAM
LINEAR_ISSUE=GAM-8

BASE_HEAD=f072b6d7becc0976c62665f9fa59b46fb48a041a
FINAL_HEAD=fe728c13324b555b12fa1422495086c72177c9ee
FINAL_HEAD_SEMANTICS=qualified repair implementation head; receipt and equipment skill are sealed in a later bookkeeping commit
BRANCH=work/gam-8-physical-barbell

UNITY_VERSION=6000.3.22f1
PHYSICS_STEP_S=0.01

IPF_RULEBOOK=IPF Technical Rule Book
IPF_EFFECTIVE_DATE=01 March 2026
IPF_VERSION=3
IPF_SOURCE_URL=https://www.powerlifting.sport/fileadmin/ipf/data/rules/technical-rules/english/2026_IPF_Technical_Rulebook__effective_01_March_2026__v3.pdf

BAR_BODY_ID=barbell
AUTHORITATIVE_RIGIDBODY_COUNT=1

BAR_OVERALL_LENGTH_M=2.200
COLLAR_FACE_SPACING_M=1.310
SHAFT_DIAMETER_M=0.029
SLEEVE_DIAMETER_M=0.050
RING_SPACING_M=0.810

BARE_BAR_MASS_KG=20.0
COLLAR_MASS_EACH_KG=2.5
BASE_BARBELL_MASS_KG=25.0

LOADING_SOLVER=Exact finite symmetric heaviest-first solver; one shared deterministic BAR layout calculates plate start/centers/faces, plate-stack outer face, load-dependent collar center/outer face, and remaining sleeve clearance; rejects non-finite, below-base, asymmetric, unsolvable, and sleeve-overflow loads without silent rounding.
PLATE_INVENTORY=25x4, 20x4, 15x4, 10x4, 5x8, 2.5x8, 1.25x8 maximum pairs per side; GAME_CALIBRATION.
COLLAR_LAYOUT=Per side: inner fixed shoulder/collar face -> heaviest-first plate stack -> removable 2.5 kg collar -> remaining sleeve; layout positions are shared by visuals, aggregate plate collider, collar collider, compound inertia, and measurement artifact.

LOAD_25_PLAN=25 kg total: bare 20 kg + collars 5 kg; no plates.
LOAD_105_PLAN=105 kg total: 25 kg + 15 kg per side, heavier plate innermost.
LOAD_205_PLAN=205 kg total: 25 kg + 25 kg + 25 kg + 15 kg per side, heavier plates innermost.
LOAD_25_LAYOUT=plate start ±0.705 m; stack outer face ±0.705 m; collar center ±0.725 m; collar outer face ±0.745 m; remaining clearance 0.355 m.
LOAD_105_LAYOUT=plate centers ±0.7325/±0.775 m; stack outer face ±0.790 m; collar center ±0.810 m; collar outer face ±0.830 m; remaining clearance 0.270 m.
LOAD_205_LAYOUT=plate centers ±0.7325/±0.7875/±0.8425/±0.885 m; stack outer face ±0.900 m; collar center ±0.920 m; collar outer face ±0.940 m; remaining clearance 0.160 m.

MASS_MODEL=20 kg bare shaft/sleeves from a shared effective-density equivalent-cylinder model, plus two exact 2.5 kg collars and exact authored plate face masses.
COMPOUND_INERTIA_MODEL=Aligned solid-cylinder inertia on BAR +X plus the parallel-axis theorem; final principal inertia is assigned to Rigidbody.inertiaTensor with identity tensor rotation.
LOAD_25_INERTIA=(0.0138765, 14.1037254, 14.1037254) kg*m^2; COM BAR=(0,0,0); collars at ±0.725 m.
LOAD_105_INERTIA=(1.7928764, 60.5070114, 60.5070114) kg*m^2; COM BAR=(0,0,0); collars at ±0.810 m.
LOAD_205_INERTIA=(4.3241262, 134.7254639, 134.7254639) kg*m^2; COM BAR approximately=(-0.000000009,0,0); collars at ±0.920 m.
INERTIA_UPDATED=PASS; 2.5 kg collar components use the actual load-dependent BAR centers for 105/205 kg and remain finite/positive.

BAR_DYNAMIC=YES
BAR_USE_GRAVITY=YES
SCRIPTED_MOTION=NO
PHYSICAL_FLEX=NO

COLLIDER_MODEL=One dynamic root with shaft/sleeve capsule colliders, fixed shoulder box colliders, one convex aggregate plate MeshCollider per loaded side, and one primitive collar BoxCollider per side; individual plate/collar visuals have no Rigidbody.
COLLAR_COLLIDERS=PASS; exactly one BoxCollider per removable collar, child of the authoritative bar Rigidbody, no child Rigidbody.
SLEEVE_CAPACITY_CHECK=PASS; complete per-side stack including the 0.040 m collar is checked against abs(outer face) <= 1.100 m; the 725 kg maximum finite-inventory request is rejected for sleeve overflow.
COLLISION_MODE=Discrete
PLATFORM_CONTACT=Existing PhysicalPlatform_GAM6 authoritative collider; bar contact uses GAME_CALIBRATION friction 0.55/0.65 and restitution 0.02.

GENERIC_COUPLING_SEAM=PhysicalBarbell.Body plus GetWorldPointFromBarX(float) and GetWorldLandmark(BarLandmark); no lift adapter implemented.
BAR_LANDMARKS=Center, left/right rings at +/-0.405 m, left/right collar faces at +/-0.655 m, left/right sleeve ends at +/-1.100 m in BAR.

PHYSICAL_OBSERVATION_BODY_COUNT=17
BAR_OBSERVATION=Stable registered-body observation contains one bodyId=barbell state and preserves PrimaryBody convenience semantics.
OBSERVATION_IMMUTABILITY=PhysicalObservation copies all registered body values into a read-only collection; no Rigidbody, GameObject, or mutable array is exposed.

EXISTING_INPUT_BUFFER_REUSED=YES
NEW_INPUT_BUFFER=NO

ATTEMPT_TRACE=GAM8_ATTEMPT_TRACE_V1; immutable post-physics observation paired with the sampled PlayerIntentFrame.
TRACE_CAPACITY=3000 samples (30 s at 100 Hz); full append fails instead of overwriting.
TRACE_MONOTONIC=YES; matching ticks required and duplicate/out-of-order append fails.
TRACE_PHYSICS_NEUTRALITY=PASS; identical four-tick authored trial produced matching bar observation with recording off and on.

RECORDED_STATE_REPLAY_SEAM=Presentation-only main-scene LineRenderer reads recorded BAR poses; it has no Rigidbody and does not enter the authoritative PhysicsScene.
REPLAY_RESIMULATION=NO

DROP_TEST=PASS; ResetAndDrop releases the dynamic bar above the existing platform, observed a >0.05 m fall in 0.75 s, and captured contact-path evidence.
SAME_IMPULSE_COMPARISON=PASS; identical 80 N*s off-center impulse at BAR x=0.250 m produced translational and rotational response, with 205 kg response below half the 25 kg response.

MEASUREMENT_ARTIFACT=Artifacts/Measurements/GAM-8-physical-barbell.json; valid JSON with rule bounds, calibration, shared 25/105/205 layouts, component inertias, runtime, observations, and trace seam.
VISUAL_EVIDENCE=Artifacts/Evidence/GAM-8/; seven PNGs: 25 kg, 105 kg, 205 kg, collider/COM/inertia landmarks, drop/contact, light/heavy impulse, and recorded trail.

NEW_AUTOMATED_TESTS=8 methods total: 6 EditMode contract tests and 2 integrated PlayMode qualification tests; 4 layout-repair tests added.
TARGETED_EDITMODE=PASS 6/6; layout order/symmetry/clearance, overflow rejection, and load-dependent inertia.
TARGETED_PLAYMODE=PASS 1/1; runtime presentation, aggregate plate collider, and child collar collider alignment.
FULL_EDITMODE=PASS 47/47
FULL_PLAYMODE=PASS 24/24

FOUNDATION_SKILL=.agents/skills/powerlifting-foundation/SKILL.md
FOUNDATION_SKILL_UPDATED=YES; all-body immutable observation, stable order, AttemptTrace timing/capacity, and presentation seam recorded.
EQUIPMENT_SKILL=.agents/skills/powerlifting-equipment/SKILL.md
EQUIPMENT_SKILL_STATUS=CREATED; concise durable shared equipment authority.

MASTER_SPEC=PASS; 68 files, HASHES=PASS, DEPENDENCIES=PASS.
COMPILE=PASS; Unity 6000.3.22f1 targeted and full test imports completed without assembly errors.
DIFF_CHECK=PASS

ATHLETE_REGRESSION=PASS; full PlayMode 24/24, including existing 16-body/14-powered-joint/1-passive-neck coverage.

GRIP_IMPLEMENTED=NO
LIFT_SPECIFIC_COUPLING=NO
RACK_IMPLEMENTED=NO
FULL_REPLAY_IMPLEMENTED=NO

KNOWN_LIMITATIONS=Rigid V1; engineering collider approximations; no flex, rack, bench, physical grip, lift coupling, rule observer, contact-force claim, trace persistence/compression, cryptographic chain, or complete replay product.

PR=https://github.com/Litju/PowerliftingSimulator/pull/7; existing PR updated, not merged.
LINEAR_STATUS=GAM-8 repair pushed to existing PR #7; no merge and GAM-9 not started.

STATUS=PASS_WITH_LIMITATIONS
