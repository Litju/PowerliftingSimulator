# Athlete Progression

**Document ID:** `PSMS-GAME-42`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `06_ATHLETE_CAPACITY_MODEL.md`, `GAME/41_GAMEPLAY_AND_DIFFICULTY.md`

## Repository verification

- Inspect any existing athlete/profile/save fields before migration.
- Calibrate progression against a complete career simulation and prevent runaway attribute combinations.
- Review all player-facing language to avoid physiological or medical claims.

## PURPOSE


Turn play, training choices, meet outcomes, and recovery into understandable long-term growth while keeping the physical
capacity model finite and explicitly game-calibrated.


    ## INPUTS


Completed session/meet records, training focus, successful/failed loads, modeled demand exposure, technical-quality score,
career time, recovery resources, current athlete profile.


    ## OUTPUTS


Progress points, bounded attribute changes, temporary readiness, unlocks, profile history, and an immutable starting
capacity profile for the next attempt.


    ## STATE


Persistent attributes: body mass, strength, technique, bracing, stability, grip, and lift specializations. Career state
tracks experience, reputation, money/resources if enabled, equipment cosmetics, recovery/readiness, records, and
progression version.


    ## UNITS


Attributes normalized `[0,1]`; body mass kg; career time days/weeks; readiness/fatigue dimensionless game values;
loads kg. No physiological units are implied.


    ## COORDINATE CONVENTION


Not spatial. Lift specialization is separate for squat, bench, and deadlift; it is not a generic exercise multiplier.


    ## EQUATIONS


A session stimulus for attribute `a`:

\[
S_a=\sum_{attempts} w_{lift,a}\,
\operatorname{clip}(u_{family}-u_{min},0,u_{cap})
\,Q_{tech}\,Q_{completion},
\]

where `u` is modeled drive demand and `Q` terms are bounded game quality factors.

A diminishing-return update:

\[
a_{new}=a+(1-a)k_a\,\frac{S_a}{S_a+H_a}
-k_{detraining}D_a.
\]

Temporary readiness:

\[
R_{t+1}=\operatorname{clip}(R_t+r_{recovery}-c_{session},R_{min},1).
\]

All terms are game calibration. Progress is applied between sessions; active-attempt capacity never changes from a save
or progression callback.


    ## ASSUMPTIONS


A compact progression model supports career decisions. Players expect specificity: squat practice should not identically
improve bench/deadlift. Diminishing returns prevent unbounded strength.


    ## APPROXIMATIONS


This is not training prescription, supercompensation, fatigue physiology, or individual response prediction. Body-mass
change is an explicit career choice and cosmetic/physical profile update, not a diet model.


    ## GAME CALIBRATIONS


Attribute changes per session are small; cap weekly gain; require technique exposure for skill; grip emphasis mostly
affects deadlift/bench coupling; bracing affects squat/deadlift trunk reserve; stability affects bounded correction.
Readiness can reduce starting capacity modestly but never below a playable floor in assisted modes.


    ## NUMERICAL IMPLEMENTATION


Pure deterministic progression processor called only after a session/meet receipt. Use versioned coefficients and
migration. Clamp all values; append an audit event. Recompute physical profile only before scene setup.


    ## PSEUDOCODE

    ```text
    ApplySessionProgress(profile, session_receipt):
    require session_receipt.finalized
    for attribute in attributes:
        stimulus = sum(weighted_attempt_exposure(attribute))
        gain = (1-profile[attribute]) * k[attribute] * stimulus/(stimulus+half[attribute])
        loss = detraining(profile, career_clock, attribute)
        profile[attribute] = clamp(profile[attribute] + gain - loss, 0, 1)

    profile.readiness = update_readiness(...)
    profile.version += 1
    append_progress_audit(profile, session_receipt.hash)
    return profile
    ```

    ## UNITY MAPPING


Pure domain service and serializable records. ScriptableObjects contain immutable balance constants; player profile data
is never stored by mutating project assets.


    ## FAILURE MODES


Active attempt changes capacity; one lift trains all equally; infinite compounding; negative/NaN attributes; save reload
duplicates gains; progression called physiology; failed numerical attempts grant stimulus; coefficient migration missing.


    ## OBSERVABILITY


Progress receipt lists each input attempt, demand/quality contribution, coefficient version, gain/loss, readiness change,
and unlock. UI presents cause-and-effect without fake precision.


    ## TELEMETRY


Session stimulus by attribute/lift, before/after values, readiness, records, coefficient/version, source receipt hash.


    ## TESTS


Monotonic/diminishing gains; specialization separation; caps; deterministic replay; duplicate receipt idempotence;
active-attempt immutability; save migration; no NaN; career simulation bounds over many seasons.


    ## MUTATION TESTS


Apply per physics tick; use load only; make all lift weights one; remove clamp/diminishing returns; duplicate receipt;
label adaptation as physiological fact.


    ## PERFORMANCE CONSIDERATIONS


Tiny post-session computation; no runtime concern.


    ## CLAIM CLASSIFICATION


Entire progression/readiness/stimulus model: `GAME_CALIBRATION`. It is not training advice or human adaptation prediction.


    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE


**SHIP_V1:** seven attributes, lift specialization, readiness, records/unlocks.  
**LATER:** coaches/program templates and richer economy.  
**RESEARCH:** data-calibrated adaptation.  
**OUT_OF_SCOPE:** real training prescription, medical or nutrition guidance.
