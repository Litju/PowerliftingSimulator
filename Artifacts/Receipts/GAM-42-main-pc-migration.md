# GAM-42 main-PC migration receipt

```text
MIGRATION_ISSUE=GAM-42
PARENT_ISSUE=GAM-13
MISSION=GAM42_MAIN_PC_SETUP_AND_REPRODUCTION_CLOSEOUT
MODE=MIGRATION_SETUP_REPRODUCTION_ONLY
SOURCE_REPOSITORY=https://github.com/Litju/PowerliftingSimulator.git
TARGET_PATH=E:\Data\Projects\PowerliftingSimulator
START_HEAD=4cf5fc5fe9bd7e668e363da35cf4656202da233f
BRANCH=work/gam-13-squat-load-calibration
HEAD=f77d7cae1b3684d03ac1b101b5f8dfb1ea6e033e
ORIGIN_MAIN=4cf5fc5fe9bd7e668e363da35cf4656202da233f
WORKTREE_COUNT=1
WORKTREE_STATUS=CLEAN

UNITY_VERSION=6000.3.22f1
UNITY_REVISION=1c726e1fb402
UNITY_EDITOR_PATH=D:\Dev\Unity\6000.3.22f1\Editor\Unity.exe
OS=Microsoft Windows 11 Pro 10.0.26200 (64-bit)
CPU=AMD Ryzen 5 5600G with Radeon Graphics; 6 cores / 12 logical processors
GPU=AMD Radeon(TM) Graphics; driver 31.0.21921.1000
RAM=31.40 GB installed
GRAPHICS_API=Direct3D 12 [level 12.1]

GIT_VERSION=2.53.0.windows.3
GIT_LFS_VERSION=3.7.1
LFS_MATERIALIZED=PASS
STAGE_A_PNG_COUNT=12

MASTER_SPEC=PASS; MASTER_SPEC_FILES=68; HASHES=PASS; DEPENDENCIES=PASS
FOCUSED_GAM13_EDITMODE=PASS; 3/3; fresh XML result Passed

25KG_MAIN_PC=STABLE_STANDING
60KG_MAIN_PC=SETUP_COLLAPSE
300KG_MAIN_PC=SETUP_COLLAPSE

CROSS_DEVICE_CLASSIFICATION=SAME_CLASSIFICATION for 25 kg, 60 kg, and 300 kg
GRAPHICS_REPRODUCTION=PASS; 1/1; real GPU; 12 PNGs generated and decoded; telemetry finite after first physics observation

PRODUCTION_FILES_CHANGED=NO
GAM13_TUNING_PERFORMED=NO
GAM13_STATUS_UNCHANGED=YES
GAM14_STATUS_UNCHANGED=YES
TRACKED_CHURN_REMOVED=YES
MIGRATION_DECISION=PASS
```

## Authority gates

- `git fetch --prune origin` completed; `origin/work/gam-13-squat-load-calibration` resolved to `f77d7cae1b3684d03ac1b101b5f8dfb1ea6e033e`.
- The candidate was checked out directly; `origin/main` remained `4cf5fc5fe9bd7e668e363da35cf4656202da233f`.
- `git lfs pull` materialized all 12 committed Stage-A PNGs. Each is a valid 1280x720 PNG, larger than 1,000 bytes, and not an LFS pointer.
- Required report, completion receipt, ADR, and `ProjectSettings/ProjectVersion.txt` were present.
- Unity first import/compile completed with exit code 0, `Tundra build success`, and no compile-error markers.
- The machine-local Git checkout policy was set to `core.autocrlf=false`, `core.eol=lf` because the system-wide CRLF policy made all 67 MasterSpec hashes fail despite LF-normalized content matching the manifest. No spec content changed.

## Reproduction fixture

The existing `GAM13_STAGE_A_FIXED_PLANT_STANDING_QUALIFICATION` fixture was run
with `GAM13_STAGE_A_LOADS=25,60,300` only. It used a fresh
`SquatPhysicalPrototype` scene per case, 100 settle ticks plus 500 measured
fixed ticks, the committed candidate, default impedance `1x`, default balance
loop, and no hidden GAM-13 overrides. The fixture's tracked CSV was preserved
outside the repository and restored after capture.

| Load | Classification | Min pelvis (m) | Max trunk pitch (rad) | Min capture margin (m) | Max COM speed (m/s) | Min contacts | Support lost | Max posture error (deg) | Min guard scale | Saturation fraction | All finite |
|---:|---|---:|---:|---:|---:|---:|---|---:|---:|---:|---|
| 25 kg | STABLE_STANDING | 0.977666736 | 0.145423636 | 0.107856154 | 0.006005795 | 8 | false | 1.443443 | 0.912665 | 0.000 | true |
| 60 kg | SETUP_COLLAPSE | 0.117729664 | 1.615525600 | -1.343156220 | 2.365807 | 0 | true | 30.333817 | 0.000000 | 1.000 | true |
| 300 kg | SETUP_COLLAPSE | 0.097853920 | 2.920749660 | -1.265862110 | 2.279837 | 0 | true | 77.704760 | 0.000000 | 0.004 | true |

Support/contact persistence is represented by minimum support contacts and the
`Support lost` latch above. Drive saturation is the measured fraction of the
500 post-settle ticks at or above full saturation. The main-PC reproduction CSV
and Stage-A log were written to the migration-local external directory:
`D:\Dev\UnityLogs\GAM-42\`.

## Source-device comparison

The committed source-device report is
`Artifacts/Reports/GAM-13-full-report.md`. Its corresponding Stage-A results
were 25 kg stable, 60 kg failed, and 300 kg failed. The main-PC values differ
in exact PhysX trajectory and tick-level metrics, but every required probe has
the same physical classification. No material environment divergence was
observed, so no three-repeat divergence audit was required.

## Graphics reproduction

`GAM13_STAGE_A_CAPTURE_CURRENT_CANDIDATE_FRAMES` passed `1/1` without
`-nographics` on AMD Radeon(TM) Graphics through Direct3D 12. It generated the
12 expected Stage-A PNGs and 12 telemetry rows. The initial `t=0` trunk value
is `NaN` before the first physics observation, as designed; all later telemetry
values were finite. Generated graphics output was copied to the migration-local
external directory and committed canonical evidence was not changed.

## PNG/LFS audit

| PNG | LFS object | Bytes |
|---|---|---:|
| `load-025kg-t0000.png` | `5a6d6fa3b6` | 217719 |
| `load-025kg-t0100.png` | `373340826d` | 222313 |
| `load-025kg-t0250.png` | `7090b5b1f4` | 221859 |
| `load-025kg-t0500.png` | `728180f5c6` | 222237 |
| `load-060kg-t0000.png` | `0baf126db7` | 226775 |
| `load-060kg-t0100.png` | `d7a9308c49` | 212352 |
| `load-060kg-t0250.png` | `cd7373186f` | 193920 |
| `load-060kg-t0500.png` | `8432266bcd` | 188107 |
| `load-300kg-t0000.png` | `3e5ff4748e` | 234441 |
| `load-300kg-t0100.png` | `d952734928` | 213821 |
| `load-300kg-t0250.png` | `48bcaff040` | 196113 |
| `load-300kg-t0500.png` | `55e14124d5` | 214044 |

## Tracked churn and handoff

Unity-generated `ProjectSettings/ProjectSettings.asset` churn, the untracked
`ProjectSettings/SceneTemplateSettings.json`, and the generated tracked
`Artifacts/Measurements/GAM-13/stage-a-standing-final.csv` were removed from
the final tree. No production parameter, physics, control, P1/P2/P3/P4, or
existing GAM-13 scientific evidence content was changed.

GAM-13 remains **In Progress**. GAM-14 remains **Backlog**.

`GAM-13 may resume on this MAIN PC from the reproduced Stage-A state.`
