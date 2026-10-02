# GAM-13 V2-3D 60 kg standing gate

**FAIL** — fresh Unity `6000.3.22f1` process 21328 ran one 500-tick qualification test. The trace covers ticks 1–500; bilateral contacts, the start window, the Squat command, and bar attachment qualified.

The first failed assertion in the gate was **“The setup is not upright”**: minimum pelvis height was `0.11234688 m`, below the `0.90 m` standing requirement. Other final physical limits also failed: maximum trunk pitch `1.417 rad` (limit `0.70`), maximum COM speed `2.015 m/s` (limit `0.25`), minimum COM/support margin `-1.132 m` (limit `-0.02`), and saddle limit occupancy `1.237` (limit `0.95`). Maximum joint-anchor separation was `0.0243 m` (limit `0.05 m`); maximum saddle separation was `0.0154 m` (limit `0.02 m`), with `20.98°` relative rotation.

The trace first crossed the trunk-pitch limit at tick 48 (`0.70362 rad`), the rear COM/support margin at tick 117 (`-0.02139 m`), and the saddle occupancy limit at tick 245 (`1.23706`). These are measured gate observations; this receipt does not assign a cause.

The standing ladder stops at 60 kg. No 140/170/300 kg standing gate, V2-4 lifecycle characterization, or V2-5 strength calibration was run.
