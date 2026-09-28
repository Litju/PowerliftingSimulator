# GAM-13 V2-3B Gate A — Unloaded Standing

**Result: FAIL.** Fresh Unity 6000.3.22f1 PlayMode process; 0 kg setup; 500 samples from validated physics tick 0. The test was intentionally stopped at Gate A. Gate B (25 kg standing), the heavier standing ladder, lifecycle, and strength calibration were not run.

The tick-0 substrate gate passed: the bar was inactive, the athlete had 16 finite dynamic bodies and 15 valid joints, both plantar surfaces matched the platform, no active collider pair penetrated, and the standing command was primed. Both foot contact detectors reported bilateral support in all 500 standing samples.

The failure develops in the existing bounded V2 AP balance correction. AP COM error is `5.0e-6 m` on tick 1. The ankle balance offset first reaches its `-0.2618 rad` bound on tick 38 while AP error is `0.0474 m`; the error then grows. Pelvis height falls to `0.1432 m` by tick 142, trunk pitch reaches `1.5569 rad` by tick 140, horizontal COM speed reaches `1.8895 m/s`, and the minimum support margin reaches `-0.935 m`. The maximum modeled active-channel drive pressure is `1.3626` on tick 1, in swing/YZ. These are modeled demand fractions, not solver torque.

Maximum joint-anchor separation is `0.0212 m` at tick 138, after posture collapse; the first sample is `0.0024 m`. With no bar or saddle in this gate, this evidence identifies the bounded V2 balance correction as the observed limiting mechanism. It does not establish ConfigurableJoint solver error as the dominant cause and does not support ArticulationBody escalation.

No controller bounds, gains, intrinsic strength, or load outcomes were changed. The sequential qualification gate blocks further standing and lifecycle work.
