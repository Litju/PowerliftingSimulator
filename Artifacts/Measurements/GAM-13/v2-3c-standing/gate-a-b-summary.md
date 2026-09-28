# GAM-13 V2-3C standing gates

Unity `6000.3.22f1`; the unloaded and 25 kg runs each used a fresh Editor process and the existing 500-tick V2 standing qualification.

## Gate A — unloaded

**PASS** — 1/1 test passed, process exit 0, with a complete qualification trace. Bilateral support persisted; pelvis height was 0.976 m, maximum trunk pitch 0.212 rad, maximum COM speed 0.074 m/s, minimum COM/support margin 0.120 m, maximum joint-anchor separation 0.0035 m, and maximum modeled drive demand 0.900. No bar or squat command was expected for the unloaded gate.

## Gate B — 25 kg

**FAIL** — 0/1 tests passed, process exit 2. The standing window, squat command, and bilateral contacts qualified. Reported physical metrics were pelvis height 0.973 m, maximum trunk pitch 0.394 rad, maximum COM speed 0.057 m/s, minimum COM/support margin 0.101 m, maximum joint-anchor separation 0.0039 m, maximum saddle separation 0.0007 m, saddle limit occupancy 0.058, and relative rotation 0.12°.

The failed assertion was: `The AP correction sign was inverted when mapped into joint target space.` The trace and XML are preserved with this run. No heavier standing gate, lifecycle run, or strength calibration was started.

## Next action

Diagnose the 25 kg AP correction-to-ankle-target mapping assertion within the existing V2 path, then rerun fresh-process Gate B after that failure is resolved. The positive unloaded plant result authorized the AP feedback sign change only; it does not authorize a separate target-mapping edit, gain, drive, capacity, or substrate change. Do not resume the load ladder until Gate B passes.
