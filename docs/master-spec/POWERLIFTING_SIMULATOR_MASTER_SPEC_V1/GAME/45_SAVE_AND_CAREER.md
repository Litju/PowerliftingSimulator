# Save and Career

**Document ID:** `PSMS-GAME-45`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `GAME/40_MEET_SYSTEM.md`, `GAME/42_ATHLETE_PROGRESSION.md`, `GAME/44_REPLAY_SYSTEM.md`

## Repository verification

- Inspect existing save/settings code and platform paths.
- Verify target Windows filesystem atomic-replace behavior.
- Run destructive fault-injection tests before release.

## Purpose

Persist athlete, career, settings, records, meets, unlocks, and replay references safely across releases without storing
mutable Unity assets or allowing partial/corrupt writes.

## Save domains

```text
ProfileSave
 ├─ SaveHeader
 ├─ AthleteProfile
 ├─ CareerState
 ├─ ProgressionLedger
 ├─ MeetHistory[]
 ├─ PersonalRecords
 ├─ Unlocks
 ├─ Settings
 ├─ ReplayIndex
 └─ MigrationHistory
```

Raw large traces/replays are separate files referenced by content hash. A save remains usable when optional replay data is
deleted.

## Header

- schema version;
- product/build version;
- platform;
- creation/update timestamps;
- profile ID;
- progression/calibration/ruleset versions;
- checksum;
- last clean transaction ID.

No secret/API key or personal telemetry is stored.

## Atomic write

1. Serialize deterministic DTO to a temporary file.
2. Flush and compute checksum.
3. Read back and validate.
4. Rotate current save to backup.
5. Atomically replace current file where supported.
6. Append transaction receipt.
7. Keep at least one previous valid backup.

Never mutate tracked project assets or `EditorBuildSettings`.

## Career loop

- choose training/meet;
- select athlete/load/focus;
- perform session;
- finalize deterministic session receipt;
- apply progression once;
- update records/finances/reputation/unlocks;
- save transaction;
- return to planning.

A crash during an attempt restores the state before that attempt unless an immutable judgment/receipt had already been
committed.

## Records

Store exact load, lift, ruleset, athlete/calibration versions, assists, outcome, date/career time, trace/replay hash.
Separate:

- training best;
- meet best;
- total;
- challenge scenario record;
- assisted/unassisted categories.

Do not compare incompatible rulesets/calibrations without a warning.

## Career content V1

- rookie-to-championship season structure;
- training sessions and local/regional/national-style meets using fictional branding;
- budget/resource choices kept light;
- athlete attributes/readiness/progression;
- equipment/cosmetic unlocks;
- venue tiers;
- rival/opponent results;
- records and goals.

No gambling, real federation license, real athlete likeness, medical injury simulation, or pay-to-win.

## Migration

Every schema migration is pure, ordered, idempotent, and backed up. Unsupported future version fails with a readable
message and preserves bytes. Progression coefficients are not retroactively reapplied; ledger receipts prevent duplication.

## Data formats

Human-readable JSON is preferred for V1 profile/career DTOs with deterministic field conventions and checksum.
Replay/snapshot files may use a compact binary container with a JSON header. Decimal display is locale-aware; stored
numbers use invariant culture.

## Privacy

Local-first. Optional analytics/online sync is later and opt-in. Save path may contain a generated profile ID, not the
user's real name by default. Crash reports and replay sharing require explicit consent.

## Tests

Round trip; invariant culture; atomic interruption at every step; checksum failure and backup recovery; migration chain;
future version rejection; duplicate progression receipt; replay missing; settings-only update; crash before/after meet
receipt; save path permissions; no project settings mutation; deterministic canonical serialization.

## Scope

**SHIP_V1:** local profile/career, atomic saves, migration, backups, records, replay index.  
**LATER:** cloud sync and multiple careers.  
**RESEARCH:** none.  
**OUT_OF_SCOPE:** personally identifying accounts or server-authoritative competition.
