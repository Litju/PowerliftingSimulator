# GAM-70 prepared transition reconstruction

Fresh Unity 6000.3.22f1 plants used the production B2 setup, 75 kg bar, original ascent path, stock drive capacities and the same 100 Hz tick owner. The quiet arm received Drive at 301. The prepared arm received Drive at 284 and first accepted PULL at 301. Both used the same hand-load reference after accepting PULL. The normal PlayMode good-lift regression also passed for onsets 301 and 305, with DOWN at 834 and 839.

Run `DeadliftTransitionTests.GAM70_PREPARED_TRANSITION_RECONSTRUCTION` explicitly. Each arm writes two ignored files under `Logs/GAM70`. The state CSV records post-step bodies and joint anchor positions and velocities, hand and bar grip anchors, 3D shaft/capsule gaps, hand-on-bar forces, plate impulses converted to normal force, same-tick foot normal/COP/loaded extent, and the loaded-sole convex hull and capture point. The channel CSV separates geometric plan, requested target, rate-limited target, measured joint motion, static reference, and each active channel's model demand and ceiling. The pelvis pose and nominal bar position come from the path at the current progress. The planned limbs and grip anchors use forward kinematics at the interpolated joint targets. Phase and commands are from the pre-step; forces, positions and velocities are from the completed step. Motor demand is a model diagnostic, and the solver torque also includes constraint reactions.

| Arm and landmark | Tick | Bar Y / Z (mm) | Left / right shaft-to-shin (mm, 3D) | Left / right foot (N) | Left / right plate (N) | Net hand Z (N) | Capture margin (mm) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Quiet, accepted PULL | 301 | 223.07 / 20.00 | 45.20 / 45.18 | 764 / 763 | 94 / 92 | +0.53 | 139.1 |
| Prepared, accepted PULL | 301 | 223.33 / 17.91 | 42.92 / 42.79 | 777 / 769 | 82 / 88 | −31.71 | 140.5 |
| Quiet, physical separation | 317 | 223.22 / 19.86 | 44.92 / 44.94 | 881 / 883 | 0 / 0 | +7.94 | 138.4 |
| Prepared, physical separation | 314 | 223.52 / 16.74 | 41.69 / 41.62 | 878 / 880 | 0 / 0 | −24.76 | 141.7 |
| Quiet, confirmed separation | 319 | 223.53 / 19.87 | 44.79 / 44.79 | 890 / 892 | 0 / 0 | +9.56 | 138.3 |
| Prepared, confirmed separation | 316 | 223.78 / 16.25 | 41.10 / 41.07 | 889 / 891 | 0 / 0 | −21.61 | 142.4 |
| Quiet, matched absolute tick | 382 | 334.29 / 23.56 | 18.17 / 17.79 | 873 / 877 | 0 / 0 | +3.57 | 142.1 |
| Prepared, first leg impulse | 382 | 331.89 / 25.81 | 0.15 / 0.60 | 805 / 927 | 0 / 0 | +73.57 | 15.8 |

Physical separation is the first rising sample above 5 mm/s with zero normal reaction on both plates. The controller confirms from two consecutive prior-step measurements. The quiet arm completed the knee region at tick 519 with no bar-leg impulse and minimum realized L/R shaft-to-shin gaps of 1.35/0.56 mm. The prepared trace stopped on the first leg impulse, 126.4 N at 382. The older −1.5 mm report used sagittal projection. The current 3D gaps are from collider poses after the contact impulse and measure positive sub-millimetre separation at the end of that same tick.

At PULL the prepared bar was already 2.09 mm posterior to quiet and the smaller shin gap was 2.39 mm tighter. At confirmation the prepared bar was 3.62 mm posterior to quiet at its own confirmation and 3.75 mm posterior to the plan, while the left shoulder/pelvis/hip/knee/ankle differed from their nominal planned positions by 1.64/1.69/1.65/1.37/0.60 mm in 3D. Both prepared feet were loaded over the full measured long axis. The initial discrepancy is a different dynamic closed-chain state and force history before any offset release. The initial gap is still positive.

The runtime cleared the 2° preparation factors after tick 316. Across 316→317 the requested shoulder rotation changed 1.976° bilaterally, the applied rotation moved 1.433°/1.432°, and the realized rotation moved 0.014° on each side. The applied targets moved another 0.525° on the following tick. Quiet demand changed continuously across its own boundary with 0.058° requests on the first moving tick. Net prepared hand Z crossed from −21.61 N to +17.52 N across the release, while the bar still moved posteriorly. This identifies a real command and force discontinuity, with the offset-specific contribution to the force still to be isolated.

By tick 328 both longitudinal loaded extents had collapsed from about 290 mm to less than 0.1 mm near the forward edge. The abdomen X demand first exceeded its ceiling at 328 and peaked at 1.616 at 331. Prepared ankle-roll demand peaked at 0.654 by contact. At tick 382 the prepared knee centres were at +20.85/+25.16 mm world Z, versus a mean of −16.39 mm for quiet. The prepared path still planned 6.42/6.41 mm clearance, while its realized bar was 23.89 mm below its plan. Thus the first leg contact is associated with substantial realized whole-chain deviation, including forward knee travel and bar-rise lag. The capture margin crossed the 40 mm progress-governor threshold at 379 while both feet still carried load. This reconstruction establishes the order of events. The offset-only A/B is needed to isolate causation.


## Bounded shoulder-release A/B

Run `DeadliftTransitionTests.GAM70_PREPARED_SHOULDER_RELEASE_AB` explicitly. Both fresh prepared plants used Drive 284, PULL 301 and physical/confirmed separation 314/316. Their recorded states and joint commands matched through 316. From tick 317, the continuous arm reuses the exact two 2° preparation factors through the existing identification seam on the sole shoulder command source. The comparison ends at tick 374, the first contact in either arm. The released arm's independent reference trace continues to its own contact at 382.

| Measurement | Existing release | Continuous 2° factor |
| --- | ---: | ---: |
| Tick 317 requested change, per shoulder | 1.976° | 0.061° |
| Tick 317 net hand Z | +17.52 N | −19.17 N |
| Tick 328 left / right loaded long-axis width | 0.073 / 0.001 mm | 290 / 290 mm |
| Peak abdomen X demand through tick 374 | 1.616× ceiling | 0.905× ceiling |
| Peak ankle Z demand through tick 374 | 0.654× ceiling | 0.186× ceiling |
| Tick 374 bar Z, realized / plan | 13.03 / 19.76 mm | 1.74 / 19.76 mm |
| Tick 374 shin 3D gaps L/R | 14.55 / 12.15 mm | −0.20 / +0.001 mm |
| Tick 374 bar-leg normal force | 0 N | 230.86 N |
| Tick 374 foot loads L/R | 881 / 968 N | 797 / 886 N |
| Tick 374 capture margin | 79.3 mm | 148.7 mm |

The offset-only intervention eliminated the release transient and its early edge loading. The continuous arm retained full fore–aft contact extent through its first leg impulse, with both support and all modeled drive ceilings intact. Its shaft had migrated 18.0 mm behind the geometric reference, which still predicted 13.1 mm of clearance per shin. Holding the offset therefore failed the clearance hypothesis independently of the release-induced rocking. The nominal B2 path and the prepared plant's initial state and subsequent shoulder loading do not reproduce the same knee/shaft relationship. Both Gate 1 and Gate 2 PlayMode methods passed as diagnostic checks. A continued constant offset earns no clearance or gameplay qualification.


## Closed reference from the prepared state — first primitive

`GAM70_PREPARED_CLOSED_FIRST_SEGMENT` takes the actual prepared bar, feet, pelvis and joint rotations at confirmed separation. The rig's existing forward kinematics and least-squares routine close the joint paths over the measured sole poses and the two welded grip frames. The reference bar rises at its measured separation X/Z. The clearance residual changes joint coordination using the finite shaft and four leg capsules. The first reference is the measured state projected onto the joint-anchor topology. A smooth command bridge starts from the prior hold, then hands over to the closed geometric reference while the bounded shoulder factor tapers continuously. Existing hand-load and ankle feedback remain composed by the only joint writer.

The closure uses 15 intervals over the first 150 mm of nominal rise. At keys and interpolants, the minimum planned gap was 23.94 mm. Bounding relative capsule travel between those samples left 22.30 mm minimum swept clearance. The worst interpolated foot/grip position residual was 0.304 mm and orientation residual was 0.002°. The live trace includes both PhysX substeps via platform contact callbacks, with the exact expected sample count. Rigid interpolation between the substep poses has a conservative swept clearance of 10.05 mm.

Actual rise from confirmed separation was 122.61 mm by tick 385. All 138 solver substep samples were clear, with a minimum actual shaft gap of 10.88 mm and zero bar-leg impulse. The minimum foot loads were 869/865 N from separation through the segment and the minimum capture margin was 140.7 mm over that interval. This passes the first clearance primitive beyond the 90–108 mm rise at which the two preceding arms first contacted a leg. The primitive is enabled only in its PlayMode fixture. The same Unity session passed the normal good-lift regression for the two default onsets. Knee passage and the later lift remain separate qualification steps.


### Knee passage

`GAM70_PREPARED_CLOSED_KNEE_PASSAGE` retains the same first 15 keys and appends keys at the same progress spacing. The two focused tests passed on Unity 6000.3.22f1. The extended plan's minimum sampled gap was 23.80 mm, its minimum swept bound was 22.21 mm, and its largest foot/grip closure residual was 0.304 mm / 0.002°.

The fresh prepared plant reached the knee-region endpoint at tick 538 with 456.55 mm of realized rise from separation. The bar axis was 81.2/81.3 mm above the left/right knee centres. All 444 PhysX substeps had positive shaft/leg clearance with a minimum of 10.89 mm and a 10.04 mm conservative interpolation bound. Bar-leg normal impulse remained zero, left/right minimum foot loads were 809/806 N, and the minimum capture margin was 126.7 mm. This qualifies clearance through the knee region for the tested prepared onset. The probe ends before composing the closer-thigh ascent or lockout.


## Ankle roll after knee clearance

`GAM70_ANKLE_ROLL_POSTURE_RESPONSE` used fresh plants in each arm, with the existing 0.25° joint-Z excitation and three-tick hold. A left-ankle pulse tested both signs, with an unexcited arm and a fresh positive repeat per posture. The full bilateral trace matched between the two positive runs in each posture. The crouched baseline was quiet B2 at tick 301, with a 42.7° torso and 125.4° knees. The closer-bar baseline was the existing proximity arm at progress 0.761, tick 629, with a 75.4° torso, 18.2° knees, ankle roll +5.07°/−5.07° and approximately 75 mm of thigh clearance. This is a closed-loop test with different loads and posture, so the early signed response characterizes the assembled plant locally.

| Posture and input | Applied Z difference at second tick | Realized Z difference | Across-sole COP difference |
| --- | ---: | ---: | ---: |
| Crouched +0.25° | +0.251° | +0.0159° | −1.84 mm |
| Crouched −0.25° | −0.019° | −0.0014° | +0.15 mm |
| Closer-bar +0.25° | +0.248° | +0.0079° | −1.73 mm |
| Closer-bar −0.25° | −0.245° | −0.0077° | +1.70 mm |

Differences are left-ankle samples against the matching unexcited arm. Across uses the world up × sole-longitudinal axis. The target-rate limit clipped the first tick in both postures and most of the negative crouched pulse at the second tick. At that second tick the realized angle per applied degree was roughly 0.06–0.07 in the crouch and 0.03 in the closer-bar posture. The normalized COP response was similar while both soles carried a broad patch. All crouched arms kept at least 758 N on each foot and the full 290 × 130 mm loaded spans. Peak ankle Z demand stayed below 0.056 of the existing ceiling.

| Closer-bar arm | First left fore–aft edge | First Z ceiling crossing | First foot below loaded threshold |
| --- | ---: | ---: | ---: |
| Quiet | 637 | 640 | 643, right 0 N |
| +0.25° and exact repeat | 637 | 642 | 647, right 0 N |
| −0.25° | 637 | 641 | 643, right 20 N |

At tick 637 in all three arms the left foot's loaded long-axis width fell from 290 mm to below 1 mm while the transverse span still measured about 130 mm. The quiet arm later collapsed to transverse edge contact by 642. At 640 its left ankle Z request, applied target and actual angle were −6.02°, +3.46° and +11.67°, with measured Z velocity +1.06 rad/s and modeled demand 1.004 of the 60 N·m ceiling. Foot loads were 2176/519 N. At the same tick the positive pulse arm retained 1565/660 N and its hip centres differed from quiet by 4.77/2.96 mm, versus near-zero hip differences during the initial signed response. Both signs ultimately lost support. This establishes an edge/contact change before saturation, then strongly coupled roll and hip motion. The measurements do not identify a signed gain change that preserves the crouched behavior. No gain, schedule or capacity was changed.


## Composed attempts

Both late-reference probes use the already qualified knee prefix with the same key spacing and interpolation. After that prefix, the existing closed-chain solve continues toward the original path's bar trajectory with an additional gradual 10 mm posterior endpoint preference. PhysX moves the dynamic bar through the original grips. The complete reference stays in PlayMode.

A first implementation used algebraically equivalent interpolation arithmetic for the prefix. It diverged in hand reaction at tick 335, then reached a negative capture margin at tick 498 with 323 mm of rise. Restoring the qualified interpolation made all 259 recorded state rows through tick 538 match exactly when appending the late keys. The restored composed run cleared all bar-leg contact, reached 802 mm peak rise and entered the controller's Lockout phase at 711.

The referee remained in Ascent. During 155 ticks at progress 1, the slowest observed bar speed was 37.4 mm/s, above the 20 mm/s motionless threshold. The post-knee peak roll demand was 0.320 of the ceiling. At tick 865 the capture margin became −0.55 mm while both feet still carried 864/835 N. The bar was moving 76.6 mm/s rearward and the smaller thigh gap averaged 186 mm across controller lockout, compared with about 177 mm for the original good-lift control. The modest posterior preference therefore failed both support and thigh-gap acceptance. This failure differs from the proximity arm's roll saturation and foot unloading.


The second probe added a late geometric preference for the athlete-plus-bar COM to finish 25 mm ahead of the original plan's COM, with a shoulders-back preference. It retained the same 10 mm bar endpoint preference and the identical recorded prefix through tick 538. The minimum 3D shaft/leg gap stayed 10.89 mm with zero contact impulse. The left patch first reached an edge at 605. The run stopped at 616 with 728 mm peak rise, right foot load zero, a −275.5 mm capture margin and left ankle X/Z demand 1.91/1.20 times the existing ceilings. This moved the failure earlier, before the lockout phase. The geometry-only COM preference failed as a late stability correction.

The full attempts remain unqualified. They earned no DOWN or controlled return, and the smaller realized lockout thigh gap was not demonstrated. The onset sweep and owner-visible Windows rehearsal are pending a complete judged correction. The station still runs the original default path, and both experimental mechanisms remain off. The original default controls at 301 and 305 continued to produce GOOD LIFT and release the grips after return.


## Stock lockout rejoin

The reference preserves the measured-state knee path. After the last clear key it blends to the original joint path, finishing the blend at progress 0.80 or 0.94. The endpoints return the exact branch targets. A trial that called quaternion interpolation with zero weight still changed the early reference numerically at tick 317, and later faulted FootMoved. Returning the qualified prefix directly preserved the measured trajectory through tick 538.

Both fresh tick-301 prepared runs earned GOOD LIFT with 803 mm peak rise and zero bar-leg impulse. The two blends earned DOWN at 858 and 857, with controlled return and grip release at 1435 and 1228. Minimum shaft gap was 10.89 mm and the minimum capture margins were 58.6 and 56.9 mm. For the 0.80 arm the feet carried at least 561/562 N while the plates were below 600 N after separation. The 0.80 and 0.94 paths had 176.99 and 176.98 mm mean realized lockout thigh gaps, effectively the same as the original 176.96 mm. This qualifies a complete physical composition at the matched onset. A closer realized thigh gap is still a separate result.


### Closer lockout on the recovered path

The early signed shoulder probe ramped a ±0.5° extension reference during the stock rejoin's late ascent. Positive extension lost support at tick 686. Negative extension completed GOOD LIFT at DOWN 858 and return 1545, but increased mean realized lockout thigh gap from 176.99 to 181.41 mm. Timing the same intervention during moving ascent therefore failed qualification.

The settled variant waits for the controller's Lockout phase and measured bar speed below 45 mm/s. It measures both shoulder extension axes in that posture and ramps the bounded targets over 35 ticks, then tapers them out on lowering. The 0.25° and 0.50° fixtures both earned DOWN at 856 and completed GOOD LIFT. The 0.50° variant delivered the larger reduction and released the grips at 1440. Its mean whole-lockout thigh gap was 174.53 mm versus the original 176.96 mm. Over the last 30 lockout ticks it averaged 172.60 versus 176.92 mm, and the final pre-DOWN gap was 173.54 versus 177.58 mm. Minimum bilateral airborne foot loads were 562/558 N, minimum shaft gap 10.89 mm, and minimum capture margin 57.1 mm. The bar remained dynamic and both grip and floor constraints kept their original authority.


### Opt-in runtime and matched onset sweep

`GAM70_PREPARED_REJOIN_RUNTIME_QUALIFICATION` reproduced the settled 0.50° result through the production runtime. Its fresh stock comparison earned DOWN at 834 and completed GOOD LIFT with 176.96 mm mean thigh gap. The opt-in run earned DOWN at 856 and released at 1440, with 174.53 mm mean gap, 558.4 N minimum airborne foot load, positive swept shaft clearance and zero bar-leg impulse. The stock 301 and 305 regression still passed. In profiling this host, the new opt-in solve at separation took about 12–19 seconds before physics resumed. The unmodified default uses the original reference and avoids that solve.

`GAM70_PREPARED_REJOIN_ONSET_SWEEP` held Drive from 284 and allowed the normal mechanical readiness gate to accept at each specified tick or later. It reached all five requested ticks exactly. The first failure for each fresh plant was:

| PULL tick | Confirmed separation | Result |
| ---: | ---: | --- |
| 301 | 316 | GOOD LIFT, DOWN 856, controlled return and release 1440 |
| 302 | 317 | DownwardMovement at 724, bar 11 mm below peak at −90 mm/s |
| 305 | 320 | Bar-leg impulse at 452 with −0.10 mm realized shaft gap |
| 320 | 336 | Bar-leg impulse at 465, referee ThighSupport at 68.5 N |
| 350 | 368 | Capture margin below zero at 445, before bar-leg contact |

The opt-in correction is qualified at the matched 301 timing. The sweep records limited robustness even when the pose solve itself has positive planned shaft clearance. The stock 301 and 305 controls continue to qualify separately.


### Owner-visible Windows rehearsal

Unity 6000.3.22f1 built the standalone Windows rehearsal into `Builds/GAM70/PreparedRehearsal`. Its visible side-view window ran the scripted Drive preset `-deadliftPreparedRehearsal -deadliftSideView`. The player reported PULL 301, confirmed separation 316, Lockout 711, DOWN 856, bar returned 1433, and grips released with GOOD LIFT at 1440. It measured 803 mm peak rise, 10.89 mm minimum shaft gap, 562.3 N minimum airborne foot load from the regulator's completed contact sample, and 174.53 mm mean realized lockout thigh gap. The live reference solve took 19.52 seconds at separation. The window resumed and stayed open after the result. The desktop capture helper's native pipe was unavailable, so the rehearsal receipt comes from the visible player's own referee and physics log.

The manual experimental flag is `-deadliftPreparedRejoin`. Both prepared modes require an explicit flag. The original Windows build folder and default station behavior retain their original paths.


### Natural Drive-start cross-check

`GAM70_PREPARED_REJOIN_DRIVE_LEAD_SWEEP` varied the Drive start while letting the mechanical readiness gate alone accept PULL. The qualified Drive 284 case is the runtime regression and Windows rehearsal above. The four additional starts reproduced the requested PULL ticks and none completed GOOD LIFT:

| Drive | Accepted PULL | Confirmed separation | First failure |
| ---: | ---: | ---: | --- |
| 285 | 302 | 317 | Bar-leg impulse at 450, 279 mm peak rise |
| 288 | 305 | 320 | Capture margin crossed zero at 396, 168 mm peak rise |
| 303 | 320 | 335 | Bar-leg impulse at 506, −1.11 mm realized shaft gap |
| 333 | 350 | 365 | Foot unloaded at 502, −195.6 mm capture margin |

The natural gate and the common-start sweep both qualify one of the five tested timing cases. Changing the preparation dwell changed the failure modes at the same PULL tick. The current result is a complete reproducible lift and a modest closer-bar improvement at matched 301, with broader prepared timing still open and the game default preserving the original controls.


### Keyboard start and responsive rehearsal

The original Windows rehearsal overrode Drive and ran automatically, although the station also displayed its ordinary W instructions. The rehearsal now waits at tick zero for W or Up, then runs the measured sequence. R resets to the input prompt. The station solves one reference key per rendered frame at separation while the authoritative clock stays at tick 316. The pending input horizon and the previously accepted catch-up time survive that pause.

Unity 6000.3.22f1 passed 17 focused PlayMode checks. The actual station fixture pressed W, saw physics start from tick zero, verified the tick-316 hold across frames, and completed GOOD LIFT with DOWN at 856 and grip release at 1440. The stock station and the original 301/305 controls passed their separate regression. The responsive Windows build answered all 33 window checks sampled during the solve and completed GOOD LIFT on the earlier auto-start pass. The final rebuilt Windows player waited for a hardware key, picked up the keyboard start, and reached DOWN at 856 after a responsive 14.52-second separation solve. That window was closed during lowering. The complete W-triggered return was verified in the station PlayMode fixture.
