# Powerlifting squat domain

Use this project skill for GAM-10 and later work on the powerlifting squat domain and its humanoid reference motion.

## Verified GAM-10 contract

- GAM-10 is a reference-only squat calibration for the actual Quaternius humanoid asset at `Assets/Characters/Athlete/Source/Superhero_Male_FullBody.fbx`.
- The asset identity used for qualification is SHA256 `79344418d754a59730b79d1874752e9592143db34abe8adf138fa9a92a4768e9`.
- The exact squat state vocabulary is `SETUP`, `UNRACK`, `WALKOUT`, `SETTLE`, `SQUAT_COMMAND`, `DESCENT`, `BOTTOM`, `REVERSAL`, `ASCENT`, `STICKING`, `LOCKOUT`, `RACK_COMMAND`, `RERACK`, `COMPLETE`, `FAILURE`.
- `s_q` is normalized to `[0, 1]`: `0` is standing/lockout and `1` is the authored legal-bottom reference. Descent increases `s_q`; ascent decreases it.
- The canonical profile is `CANONICAL_POWERLIFTING_SQUAT_V2_CLOSED_CHAIN`, with claim class `BIOMECHANICALLY_INFORMED_GAME_CALIBRATION`.
- V2 canonical closed-chain reference is authoritative. Feet are fixed from measured plantar anchors, and lower body reconstruction is deterministic/foot-anchored.
- Calibrated anatomical/game joint frames are used for bone axes and kinematics.
- The authored waypoint set is `STANDING`, `QUARTER_DESCENT`, `NEAR_PARALLEL`, `LEGAL_BOTTOM`, `EARLY_ASCENT`, `STICKING`, and `LOCKOUT`.
- Reference motion uses piecewise cubic Hermite curves with endpoint derivatives. Continuity is C0-mandatory and C1-target; raw bone Euler angles are not part of the domain contract.
- The depth proxy is bilateral and conservative using hip-crease and knee-top game proxies: `d_L = y_hip_L - y_kneeTop_L`, `d_R = y_hip_R - y_kneeTop_R`, legal when `max(d_L, d_R) <= -0.005 m`. Its source class is `RULE_DERIVED_GAME_PROXY`, not a claim of judging or biomechanical ground truth.
- Reference motion describes intent only (`Brace`, `Yield`, `Drive`, `Balance`, `Grip`, `Confirm`, `Abort`). There is no physical authority in GAM-10.
- Owner has visually accepted GAM-10 (`OWNER_ACCEPTED=YES`).

## Ownership and safety

- The reference owner is `SquatReferencePreview / measured foot-anchored reference hierarchy only`.
- `physicalAuthorityTouched` must remain `false`.
- Renderer bounds are NEVER reference-root authority; pose root source uses measured plantar foot anchors with fixed standing anchors.
- Do not add physical squat tracking, balance controller, physical bar/back coupling, load path, force or torque control, Rigidbody or collider behavior, physical-hierarchy transform writes, or physical Animator authority to GAM-10.
- Physical tracking, balance, contact, bar/back coupling, and load path belong to GAM-11.

## Qualification baseline

The qualified implementation passed focused PlayMode `4/4`, full EditMode `55/55`, and full PlayMode `33/33`. The master specification verifier reported `68` files, hashes `PASS`, and dependencies `PASS`.

Required GAM-10 outputs are stored under `Artifacts/Evidence/GAM-10/V2-closed-chain/`, with the measurement record at `Artifacts/Measurements/GAM-10-squat-reference-v2.json`, joint calibration at `Artifacts/Measurements/GAM-10-squat-joint-frame-calibration-v2.json`, and the qualification receipt at `Artifacts/Receipts/GAM-10.md`.
