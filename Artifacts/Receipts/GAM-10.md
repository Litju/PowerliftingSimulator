# GAM-10 qualification receipt

MISSION=POWERLIFTING_SIMULATOR_GAM_10_SQUAT_DOMAIN_AND_REFERENCE_MOTION
STATUS=PASS_WITH_OWNER_REVIEW
BASE_HEAD=9da4838236e70e31cc502224240ee51f43c7d8a2
QUALIFIED_HEAD=165982d0ae9b65a23f902bbbdc8938c75a76deca

PROFILE_ID=CANONICAL_POWERLIFTING_SQUAT_V1

STATE_MACHINE=SETUP, UNRACK, WALKOUT, SETTLE, SQUAT_COMMAND, DESCENT, BOTTOM, REVERSAL, ASCENT, STICKING, LOCKOUT, RACK_COMMAND, RERACK, COMPLETE, FAILURE

PHASE_CONVENTION=s_q in [0,1]; 0=standing/lockout; 1=canonical legal-bottom; descent increases; ascent decreases

REFERENCE_WAYPOINTS=STANDING, QUARTER_DESCENT, NEAR_PARALLEL, LEGAL_BOTTOM, EARLY_ASCENT, STICKING, LOCKOUT

REFERENCE_OWNER=SquatReferencePreview / dedicated preview hierarchy only

PHYSICAL_RIG_WRITES=0
PHYSICAL_ANIMATOR_WRITES=0
PHYSICAL_BAR_COUPLING=NO

LEGAL_DEPTH_SOURCE=bilateral landmark geometry; RULE_DERIVED_GAME_PROXY; max(leftDepthM,rightDepthM) <= -0.005 m

LEFT_BOTTOM_DEPTH_M=-0.05905228853225708
RIGHT_BOTTOM_DEPTH_M=-0.05905228853225708
DEPTH_MARGIN_M=0.005

REFERENCE_CONTINUITY=C0 PASS; piecewise cubic Hermite curves; max key-pose value discontinuity 0.0; C1 within segments
REVERSAL_CONTINUITY=PASS; reversal held at phase-rate zero; reversal pose discontinuity 0.0
RENDER_RATE_INDEPENDENCE=PASS; fixed 0.01 s reference tick with accumulator; render rate does not define s_q

EDITMODE=focused 7/7 PASS; full 55/55 PASS
PLAYMODE=focused 3/3 PASS; full 32/32 PASS

MASTER_SPEC=68 files; hashes PASS; dependencies PASS
DIFF_CHECK=PASS

VISUAL_EVIDENCE=Artifacts/Evidence/GAM-10/GAM-10-standing.png; GAM-10-quarter-descent.png; GAM-10-near-parallel.png; GAM-10-legal-bottom.png; GAM-10-early-ascent.png; GAM-10-sticking.png; GAM-10-lockout.png; GAM-10-depth-landmarks.png
MEASUREMENT=Artifacts/Measurements/GAM-10-squat-reference.json

KNOWN_LIMITATIONS=Biomechanically informed game calibration, not motion-capture ground truth; not optimal, subject-specific, clinical, inverse-dynamics, or muscle-force analysis; landmarks are stable rule proxies rather than measurement-grade anatomy; preview does not execute physical squat, balance correction, or bar-on-back coupling; loaded runtime sticking detection is deferred.

OWNER_ACCEPTED=NO_PENDING_OWNER_REVIEW

NEXT_ACTION=OWNER SQUAT REFERENCE REVIEW
