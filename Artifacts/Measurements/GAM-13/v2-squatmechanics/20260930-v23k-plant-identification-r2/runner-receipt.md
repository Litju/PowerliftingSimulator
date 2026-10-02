# GAM-13 V2-3K plant identification

FRESH_RESET_PAIRED_ARMS=true
LOAD_KG=140
HELD_DESCENT_PHASE=0.04
NORMAL_V2_BALANCE_LOOP_ACTIVE=true
PRODUCTION_PHYSICS_CHANGED_DURING_IDENTIFICATION=false
RESPONSE_WINDOW_TICKS=1..10 after each arm's own tick-0 baseline
G=mean paired COM-z displacement difference / per-joint target difference
ALLOCATION_RULE=preserve the measured ankle-only COM-z response at the 15-degree input bound; split that response proportional to |G| times symmetric measured joint headroom; signs make all three channels restore rearward COM
STRENGTH_SCALE_AUTHORITY=5.13 pre-calibration qualification authority; not final calibrated strength
MAX_AP_CORRECTION_RAD=0.26180 (15 degrees)
DERIVED_ALLOCATION ankle=0.147069618 hip=-0.5582632 trunk_counter=-0.302018434 target_max_abs_deg=2.2060442/8.373948/4.5302763
MEASURED_AUTHORITY_REFERENCE_G=0.00378211075m/rad combined_gain=0.00378211075m/rad headroom_capacity=0.00673257m
TRACE=E:\Data\Projects\PowerliftingSimulator\Artifacts\Measurements\GAM-13\v2-squatmechanics\20260930-v23k-plant-identification-r2\paired-plant-trace.csv
SUMMARY=E:\Data\Projects\PowerliftingSimulator\Artifacts\Measurements\GAM-13\v2-squatmechanics\20260930-v23k-plant-identification-r2\paired-plant-summary.csv

| Channel | G COM-z (m/rad) | Bar Δz (m) | COP ΔAP (m) | Sign for forward COM response | Symmetric headroom (rad) |
|---|---:|---:|---:|---|---:|
|ankle|-0.00378211052|-0.000137171155|0.00680830562|NEGATIVE_TARGET|0.7100109|
|hip|0.00263771252|-7.25477948E-05|0.00045684725|POSITIVE_TARGET|0.9937694|
|trunk|-0.005805405|-0.000410686422|0.00109288027|NEGATIVE_TARGET|0.5376258|
