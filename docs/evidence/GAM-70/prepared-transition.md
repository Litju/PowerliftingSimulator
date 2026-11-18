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
