# GAM-70 planted-foot and welded-grip Jacobian

GAM70_PLANTED_WELDED_HIP_JACOBIAN evaluates the measured B2 plant on Unity 6000.3.22f1. Each column maps a physical coordinate velocity to point velocity and world angular velocity. A revolute channel contributes axis cross lever arm and the same axis to angular velocity. The independent channels come from the physical parent tree, anchored joint frames and ankle X/Z decomposition. The pelvis and bar each have six velocity coordinates. The passive neck is outside the constrained task and is omitted.

The 24 closure rows hold both foot poses fixed and match the world velocities of each hand and its welded bar anchor. The constrained task adds nine rows for shaft orientation and lateral position, realized shaft height, fixed knee angles and constant sagittal and frontal hip-to-shoulder lean. These are local tangent constraints applied to measured poses. The force inequalities, friction, joint-limit tangent inequalities and finite actuator budgets require separate evaluation.

| Realized posture | Hip Z (m) | Closure rank / nullity without Z, with Z | Fixed-height rank / nullity without Z, with Z | Projected forward-hip norm without Z / with Z |
| --- | ---: | --- | --- | --- |
| Loaded B2, tick 300 | -0.32855 | 24/18, 24/20 | 33/9, 33/11 | 0.00000318 / 0.022889 |
| Stock first controller lockout, tick 690 | -0.28385 | 24/18, 24/20 | 33/9, 33/11 | 0.00001145 / 0.375718 |
| Historical closer ascent, tick 629 | -0.21556 | 24/18, 24/20 | 33/9, 33/11 | 0.00001149 / 0.339157 |

Translation coordinates are in metres, rotation coordinates in radians. The norm uses the Euclidean metric in those coordinates. The row-space calculation reorthogonalizes with a relative rank tolerance of 1e-6 and checks the projected vector against all closure rows. The smallest values include live anchor and joint-frame drift. Grip closure error was under 0.10 mm in all three samples.

At the historical closer sample, one minimum-norm local solution for +10 mm/s hip Z at fixed bar height calls for left/right ankle Z rates +0.441/-0.442 deg/s and ankle X rates -0.498/-0.497 deg/s. Hip X is about +0.25 deg/s bilaterally, hip Y about +0.062/-0.062 deg/s and hip Z about -0.211/+0.212 deg/s. Abdomen and thorax X contribute -0.305/-0.157 deg/s and shoulder X about +0.56/-0.56 deg/s. Freezing the knee flexion satisfies the tangent lockout task. The solved bar moves forward by about 6 mm/s, so this particular projected direction only recovers about 4 mm/s of relative hip-to-bar Z. It does not optimize shaft/capsule surface distance. The crouched B2 fixed-height motion requires materially higher trunk/arm coordination (abdomen 11.1 deg/s, thorax -7.6 deg/s for the same hip speed).

Retain the current physical joint topology. Bilaterally fixed feet and welded grips admit a finite local forward-hip direction through existing ankle Z and X, hip and trunk channels. The historical trial realized about +5.07/-5.07 degrees of ankle Z at tick 629 and later exhausted the support patch before exceeding its 60 N m modeled roll ceiling. Those facts leave finite-travel endpoint feasibility and corrective contact wrench open.

Escamilla et al. and Hanen et al. measured three-dimensional sumo technique and joint moments. Their observations motivate a three-dimensional model but supply no rig-specific coupling calibration. Rajagopal et al. represent the knee with flexion-dependent secondary kinematics and distinguish ankle and subtalar articulations. The present evidence does not require adding either mechanism.

- [Escamilla et al., 2000](https://pubmed.ncbi.nlm.nih.gov/10912892/)
- [Hanen et al., 2025](https://doi.org/10.3389/fbioe.2025.1597209)
- [Rajagopal et al., 2016](https://pubmed.ncbi.nlm.nih.gov/27392337/)
- [Lynch and Park, Modern Robotics, constrained dynamics and contact wrenches](https://hades.mech.northwestern.edu/images/d/d7/MR-temporary.pdf)
