---
name: powerlifting-equipment
description: durable shared equipment authority for the Powerlifting Simulator
---

# Purpose

Keep shared competition-equipment facts stable across later lift domains. This
skill records the verified GAM-8 barbell authority without inventing grip,
rack, or lift-specific behavior.

# Authority and Rulebook

The rule authority is the official [IPF Technical Rule Book](https://www.powerlifting.sport/fileadmin/ipf/data/rules/technical-rules/english/2026_IPF_Technical_Rulebook__effective_01_March_2026__v3.pdf),
effective 01 March 2026, version 3. The direct bounds used here are overall
length <= 2.20 m, collar-face spacing 1.31-1.32 m, shaft diameter 0.028-0.029
m, sleeve diameter 0.050-0.052 m, bar plus collars 25 kg, machined ring
spacing 0.810 m, collars 2.5 kg each, largest disc diameter <= 0.45 m,
20 kg-and-over thickness <= 0.06 m, and 15 kg-and-under thickness <= 0.03 m.
The 25/20/15 kg colors are red/blue/yellow; 10 kg and under may use any color.

# Canonical Bar Geometry

The GAM-8 project calibration selects overall length 2.200 m, collar-face
spacing 1.310 m, shaft diameter 0.029 m, sleeve diameter 0.050 m, rings at
x_BAR +/-0.405 m, collar faces at +/-0.655 m, and sleeve ends at +/-1.100 m.
These selected dimensions are `GAME_CALIBRATION_FROM_SOURCE_RANGE`, not exact
manufacturer or IPF product dimensions.

# BAR Frame and Landmarks

The rigid bar longitudinal axis is local +X_BAR. Neutral pose aligns +X_BAR to
world +X; world +Y is up and +Z is athlete-forward. The authoritative
landmarks are center, left/right rings, left/right collar faces, and left/right
sleeve ends. Consumers can use `PhysicalBarbell.GetWorldPointFromBarX` or
`GetWorldLandmark` to obtain world points from the same bar body.

# Loading Solver

The finite prototype inventory is 25, 20, 15, 10, 5, 2.5, and 1.25 kg plates
with explicit pair limits in `BarbellPrototypeConfiguration`. The deterministic
solver subtracts the 25 kg base, splits the remainder equally, and consumes
available denominations heaviest-first. It rejects non-finite, below-base,
non-symmetric, and unsolvable requests without silent rounding. The canonical
review loads are 25 kg (no plates), 105 kg (25 + 15 per side), and 205 kg
(25 + 25 + 25 + 15 per side).

# Mass and Compound Inertia

The base is 20 kg of bare shaft/sleeves plus two 2.5 kg collars. Shaft and
sleeve cylinder volumes receive one effective density scaled to 20 kg; plate
face masses are exact authored values. Each aligned cylinder uses
`I_x = 1/2 m r^2` and `I_y = I_z = m(3r^2 + L^2)/12`; axial offsets add the
parallel-axis term to the transverse axes. Symmetric loads keep COM at BAR
origin within numerical tolerance, and the resulting mass and inertia are
assigned to the one root Rigidbody.

# Collision Model

The bar root has one dynamic Rigidbody. Shaft, sleeves, shoulders, and one
convex aggregate plate MeshCollider per loaded side are child colliders; plate
renderers remain individual presentation children with no Rigidbody. Contact
friction and restitution are explicit `GAME_CALIBRATION` values, and the bar
uses discrete collision detection after the tested elevated drop path.

# Authoritative Rigidbody Ownership

The runtime-created `Barbell_GAM8_Authoritative` root is moved into the
existing authoritative PhysicsScene and registered through
`FoundationRuntime.RegisterBody` as body ID `barbell`. Physical trials keep
`isKinematic = false` and `useGravity = true`; reset/freeze operations are
explicit authoring boundaries only. PhysX owns forward motion.

# Generic Coupling Seam

Later systems may receive the authoritative `Rigidbody` and a BAR-local
attachment point. GAM-8 does not implement grip, back coupling, rack
constraints, bench unrack, deadlift grip, or squat placement.

# Observation Identity

The physical observation identifies the bar as `barbell`. The full registered
body collection is owned by the foundation observation boundary; this skill
does not create a parallel bar snapshot or duplicate trace model.

# Prototype Review Workflow

Use the existing `PhysicalAthletePhysics` scene. Inspect 25 kg, load 105 kg,
load 205 kg, reset and drop, apply the same off-center diagnostic impulse to
25/205 kg, inspect COM/landmarks/inertia axes, record a short trace, show its
presentation-only trail, write the measurement artifact, and clear the trace.

# Known Approximation / Claim Ceiling

This is a rigid engineering-primitive V1 with approximate colliders and an
equivalent mass distribution. It does not claim physical flex, manufacturer
geometry, contact-force measurements, rack mechanics, grip mechanics, lift
coupling, or a complete replay product.

# Last Verified

2026-08-31 on Unity 6000.3.22f1: exact 25/105/205 loading, load-dependent
mass/inertia, one-body ownership, gravity/drop, same-impulse response, BAR
landmarks, all-body observation, bounded trace, and recorded-state presentation
seam were exercised by the GAM-8 qualification test.
