# GAM-70 regression, provenance and Windows build (contract v2)

**Setup.** Unity 6000.3.22f1 on Windows, GPU (no `-nographics`). The default PlayMode suite was run with no filter, so `[Explicit]` tests are skipped. Raw XML and logs are in `D:\Dev\UnityLogs\GAM70\qual`.

**Baseline.** The same suite was run in a separate worktree at `main` `d04edb3` (Merge GAM-69). Only `main` sets the provenance of a failure.

## Suites

| Suite | Branch `bb21b4a` | `main` `d04edb3` |
| --- | --- | --- |
| EditMode (all assemblies) | 313: 310 passed, 0 failed, 3 skipped (pre-existing GAM-44 Explicit) | — |
| PlayMode, default suite | 421: 292 passed, **27 failed**, 102 skipped | 316: 243 passed, **25 failed**, 48 skipped |
| Deadlift PlayMode, all 50 non-explicit cases | 49 passed, 1 failed | not present |
| Referee EditMode (`DeadliftReferee*`) | all passed | not present |

## Failures on the branch that are not on `main`

| Test | Cause | State |
| --- | --- | --- |
| `GATE7_PLAYER_DEADLIFT_ATTEMPT_IS_A_GOOD_LIFT` (stock path) | `e19b998` lowered the roll clamp from 8 to 5.5° for the whole regulator. The test had passed at `ea076f2`. | **Fixed** in `68a2308`: 5.5° now applies only to the hips-through attempt. |
| 7 × `GAM12SquatAttemptLifecycleIntegrationTests` and the GAM-63/65/68 scene boot | `47c478a` made `DeadliftPrototype` scene 0, so these tests opened the deadlift station. | **Fixed** in `bb21b4a`: the deadlift scene is appended. |
| `GAM12 … PLAYER_F_YIELD_AND_DRIVE_KEYS_ADVANCE_SQUAT_PHASES`: "Yield has no resolved controls" | Uncommitted local `ProjectSettings.asset` churn. A player build adds the project-wide input actions to `preloadedAssets`. | **Not a commit regression.** It passes with the committed `ProjectSettings.asset` (A/B, same worktree). The local file was restored byte-exact. |
| `CLOSE_BAR_STATION_USES_HELD_KEYBOARD_DRIVE` | The v2 post-knee window. W at the Ready cue gives PULL 158, then corrected post-knee admission, then FootMoved at tick 430 (31 mm), DownwardMovement, and NO LIFT. At `ea076f2` (v1) the same test gave PULL 160, DOWN 627, GOOD LIFT. | **Open. Blocks owner acceptance.** See `reference-cache-admission.md#causal-trace-of-the-post-knee-failures`. |

## Failures shared with `main`

These 25 tests fail identically on `main`, so they predate this branch:

- `T0_ACTUATOR_FIXTURE_CONFIG_PARITY`.
- GAM-13: `V23B_TICK_ZERO_RECEIPT`, `V2_CANONICAL_MAPPED_STATES`, `V23D_TARGET_SPACE`.
- GAM-63/65/68 squat depth ("failed to reach depth", "Expected: BOTTOM").
- `G1_MUTATION_GATES`, `H17_04`.
- `PhysicalStandingClosedLoopTests` `R1` and `S2`, `N0_NOMINAL_ONLY_STARTUP_TRANSIENT`.
- Three `SquatMotionVisualQualificationTests` and `T1_THIGH_ABDOMEN` ("Input cannot arrive after its tick has been sampled").
- `UPPER_LIMB_PERFORMANCE_AND_STABILITY_BUDGET`.
- Six `AthletePhysicsBenchmarks` B12/B13.

## Windows player

The player was built with the il2cpp-capable 6000.3.22f1 editor using `GAM70DeadliftSceneBuilder.BuildWindowsPlayerFromCommittedScenes`. Source was HEAD `bb21b4a`, with the committed project and render settings in place during the build. The owner's local files were restored byte-exact afterwards.

- `Builds/GAM70/Windows/PowerliftingSimulator.exe`. The build is ignored by git, so it is not in the repository.
- `PowerliftingSimulator.Deadlift.Unity.dll`: SHA-256 `E0F9E3A3ED7C9F8B468E69BF3E7390496E949B0A5317D39073BE4335BD6D3404`.
- Artifact `GAM70CloseBarReference.json`: SHA-256 `c606940d6431257bc4a0be98cdb9e68f06e5462b2655c33cef7f5d4c9e6733e5`, contract v2.
- **Launch smoke test.** The station admits the artifact (no fingerprint refusal) and is ready in 560 ms with "Tap Space to brace". The window keeps responding.
- **Not claimed:** any lift on the Windows player, and owner hardware-keyboard acceptance. The keyboard station test above predicts NO LIFT at the default press timing.

## Unity settings churn (disposition)

These files are rewritten by the editor or by the player build. A build reproduced the same set exactly. None of them is committed, and the local copies are left untouched:

- `.vscode/settings.json`: line endings, and a solution name generated for the worktree;
- the `Assets/Settings` URP prefilter and volume entries;
- `ProjectSettings.asset`: `preloadedAssets` (project-wide input actions), static batching and a scripting define;
- `UnityConnectSettings.asset`;
- `SceneTemplateSettings.json`.

Restore the committed `ProjectSettings.asset` before running PlayMode. Its build-added `preloadedAssets` entry fails `PLAYER_F_YIELD_AND_DRIVE_KEYS_ADVANCE_SQUAT_PHASES`.
