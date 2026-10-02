# Raw benchmark evidence

The `B*.json` files are the one-per-case outputs referenced by the final `results.json`. `repeatability-summary.json` contains the per-process 25 kg and 140 kg records and hash summaries. `canonical-mechanics.json` records the five final load outcomes and lockout/failure metrics; `B14-170-sticking-detector.json` records the official detector result; `B16-disposition.json` summarizes the ConfigurableJoint/ArticulationBody comparison alongside its full raw metrics. Full Unity logs, XML, qualification traces, actuator diagnostics, runtime contracts, and per-tick hashes are retained under `../runs/<run-id>/`.

The original benchmark executions were run at physics source SHA `68d3085`; final candidate `776feac` contains only verification-test corrections after those runs. The production physics source/configuration is unchanged between those SHAs. The final-scale convergence sweeps and final required suites are under `../sweeps/` and `../final-verification/`.
