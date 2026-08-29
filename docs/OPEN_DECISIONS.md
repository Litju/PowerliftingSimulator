# Open Decisions

This list contains only decisions that remain unresolved by the frozen master specification or by current repository evidence.

| ID | Decision | Current evidence | Closure gate |
| --- | --- | --- | --- |
| OD-001 | Exact canonical visible-athlete import, hierarchy, proportions, asset package, and license evidence | The master specification names Quaternius `Superhero_Male_FullBody`; this ground-zero repository contains no humanoid asset or provenance record. | Asset import and provenance review before the physical-athlete wave. |
| OD-002 | Repository integration of the isolated manually stepped `PhysicsScene` | The master architecture requires one isolated scene at 100 Hz, but the repository currently contains only the URP sample scene and no runtime bootstrap. | GAM-2 reality map and the first physics fixture. |
| OD-003 | Joint frames, drive modes/gains, solver settings, coupling degrees of freedom, and 100 Hz performance | These are explicitly repository-verification/calibration points; no gameplay or physics implementation exists yet. | Measured fixtures on the reference Windows hardware. |
| OD-004 | Current IPF rule/equipment/scoring locators and any federation-branding usage | The design baseline is IPF Technical Rule Book v3 effective 2026-03-01, but source locators and branding permissions are not repository records yet. | Source/provenance audit before rules ship. |
| OD-005 | Reference hardware, build backend, release platform details, and third-party art/audio/font inventory | The design targets Windows desktop but the project has no release or asset inventory yet. | Release-readiness and licensing milestones. |
| OD-006 | Exact package and assembly freeze for implementation | GAM-1 intentionally performs a read-only package audit; Cinemachine and Animation Rigging are not installed, and assembly boundaries are not created. | GAM-2 owner review. |

These decisions are not permission to start GAM-2 work on this branch.
