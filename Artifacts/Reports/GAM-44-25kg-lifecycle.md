# GAM-44 canonical 25 kg lifecycle

Candidate: `F2+S1`  
Result: `NOT_ELIGIBLE`

The standing pre-window passed, but the canonical lifecycle did not finalize after the harness limit of 2200 authoritative ticks. The final observed state was:

```text
lifecycle=START_WINDOW
adapter=SETUP
s_q=0.000
```

No lifecycle record was produced, so legal depth, physical lockout, `P3=NO_PHYSICAL_FAILURE`, and accepted P2/P3/terminal semantics are `NOT_OBSERVED`, not passes.

Evidence: `Artifacts/Evidence/GAM-44/lifecycle/F2-S1-phase3-playmode.xml` and `Artifacts/Measurements/GAM-44/lifecycle/phase3-F2-S1-standing.csv`.

This is a final-eligibility failure for the candidate, not a reason to invent another F/S arm or tune the standing interaction.
