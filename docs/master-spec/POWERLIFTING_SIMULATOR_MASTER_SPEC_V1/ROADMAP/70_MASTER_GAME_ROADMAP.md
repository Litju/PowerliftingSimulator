# Master Game Roadmap

**Document ID:** `PSMS-RM-70`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `00_READ_ME_FIRST.md`, `ENGINEERING/62_TESTING_AND_TERMINUS.md`, `ENGINEERING/67_ARCHITECTURE_DECISIONS.md`

## Repository verification

- Map roadmap milestones to current Git/issue tracker state.
- Confirm branch/worktree/CI policies before execution.
- Owner approves milestone sequencing and stop rules.

## Roadmap principle

Ship one vertical, visible, physically coherent, independently qualified product slice at a time. No milestone starts by
rewriting everything, and no future lift is used to excuse an incomplete current one.

## Historical baseline

- **M1 — simulation/rules vertical slice:** completed; preserve qualified rule/meet concepts.
- **M2 — presentation vertical slice:** completed with limitations; actual Quaternius humanoid, venue, camera, HUD,
  lights, audio, and presentation flow are product foundations.
- **M3 historical research:** useful but overcomplicated; preserve findings, not shipping architecture.

## Forward milestones

| Milestone | Product outcome | Hard dependency | Exit |
|---|---|---|---|
| M3 | physical humanoid + complete squat | M1/M2 foundations | `M3_SQUAT=PASS` |
| M4A | complete bench press | M3 shared athlete substrate | `M4A_BENCH=PASS` |
| M4B | complete conventional deadlift | M3 substrate; independent domain | `M4B_DEADLIFT=PASS` |
| M5 | full meet and broadcast | all three lifts | `M5_MEET_BROADCAST=PASS` |
| M6 | replay and sports-science-style analysis | stable traces from all lifts | `M6_REPLAY_ANALYSIS=PASS` |
| M7 | career/product loop | M5/M6 | `M7_PRODUCT_LOOP=PASS` |
| M8 | polish/performance/release | all prior | `M8_RELEASE=PASS` |
| M9 optional | post-launch/research | released V1 | not required |

## Cross-milestone invariants

- one physical authority;
- actual humanoid;
- no hidden support;
- one physical bar;
- fixed-step causal order;
- separate lift domains;
- finite capacity;
- PhysX realized motion;
- versioned rule truth;
- immutable traces/state replay;
- scientific claim ceiling;
- deterministic tests/mutations;
- visual/performance/build evidence;
- no next milestone before owner acceptance.

## Branch/work discipline

Each milestone begins from accepted main, uses one bounded branch/worktree according to repository policy, has a frozen
mission receipt, and ends with clean qualification. Research spikes do not silently merge into shipping architecture.

## Architecture freeze before coding

Luna first performs a repository reality map:

1. exact Unity/project/package versions;
2. scenes/prefabs/assemblies;
3. humanoid hierarchy/asset/license;
4. existing M1/M2 systems/tests;
5. physics/control/bar/trace/rule writers;
6. build/CI/hooks;
7. current failures and baseline captures.

It then writes an implementation-delta plan against this bundle. It may adapt names/locations, not invariants.

## Milestone cadence

Each milestone uses:

1. **Reality and deletion map**
2. **Pure contracts/tests**
3. **Small physical fixtures**
4. **Vertical scenario**
5. **Load/failure/rule matrix**
6. **Visual integration**
7. **Performance/build**
8. **Adversarial/mutation audit**
9. **Receipt and owner review**

## Stop rules

Stop and report rather than broaden scope when:

- repository contradicts a critical assumption;
- asset mapping/license unavailable;
- a forbidden second authority cannot be removed safely;
- a physical fixture fails;
- a current rule cannot be verified;
- performance cannot meet the fixed target without architectural decision;
- tests pass but visual evidence fails;
- source/claim/license audit fails.

A stop report includes smallest reproduction, evidence, likely cause, options, and recommended decision.

## Dependency graph

```text
M1 rules ─┐
          ├─> M3 shared athlete + squat ─> M4A bench ─┐
M2 pres ──┘                         └────> M4B deadlift ├─> M5 meet
                                                        └─> M6 replay/analysis
M5 + M6 ─> M7 career/product loop ─> M8 release
```

Bench and deadlift are serial in implementation for risk control, not mechanically dependent on each other.

## Product risk register

| Risk | Mitigation |
|---|---|
| articulated PhysX instability | fixture-first, simple topology, 100 Hz, finite drives, overlap checks |
| visible character defects | actual-asset binding and mandatory video gates |
| closed-chain grip instability | compliant DOFs, no projection, isolated asymmetry fixtures |
| scope creep into robotics | ADRs, no custom solver, vertical terminus |
| fake scientific claims | typed provenance and release audit |
| rule drift | versioned official source and tests |
| performance regression | budgets every milestone, standalone p95/p99 |
| save/replay incompatibility | versioned immutable schemas and migration |
| tests green but game unplayable | human playtest/visual/owner gates |
| third-party license issue | inventory before release |

## Final roadmap status

This roadmap is implementation-ready as design authority. Repository-dependent decisions remain open and are explicitly
listed in PSMS-ENG-67. The coding model must close them with evidence rather than inventing values.
