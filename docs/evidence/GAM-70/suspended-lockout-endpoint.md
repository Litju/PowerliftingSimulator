# GAM-70 suspended close-bar endpoint

Unity 6000.3.22f1, GAM70_SUSPENDED_LOCKOUT_ENDPOINT. The solve produces only a standing endpoint. It closes the existing B2 feet and welded grips, uses the actual shaft and thigh geometry, and checks forearm clearance against the abdomen collider. The initial soft solve missed foot closure by 4.10 mm. With tighter closure, the uncorrected geometric solution overlapped the abdomen by 6.3/6.5 mm at the left/right forearm. The admitted endpoint resolves both residuals with the existing joint frames and authored limits. It is placed before tick zero on a fresh test plant. All bodies and the 75 kg bar are simulated by the normal solver thereafter.

| Trial | Valid consecutive ticks (of 300) | First foot below 50 N | First modeled drive ceiling crossing |
| --- | ---: | ---: | ---: |
| Stock geometry, static reference | 9 | 17 | 12 |
| Stock geometry, static reference and anchored COP | 300 | None | None |
| Closer geometry, static reference | 11 | 20 | 13 |
| Closer geometry, velocity-only COP | 218 | None | None |
| Closer geometry, static reference and anchored COP | 300 | None | None |

The closer solution puts the shaft at Y 0.98203 m and Z -0.04343 m, with planned gap 92.53 mm. The foot-position residual is 0.021 mm and the grip-anchor residual is 0.003 mm. Relative foot and hand rotation residuals are below Unity Quaternion.Angle's resolution in this sample. The original preset B2 feet remain flat, and non-ignored collider pairs are independently checked for penetration. The endpoint uses planned ankle Z of +3.684/-3.684 degrees, below the 4-degree posture allocation that reserves 8 degrees for balance and 3 degrees relative to the 15-degree physical limit. The local Jacobian found that this channel and ankle X coordinate with hip rotation and trunk extension.

In the settled final second the realized left/right shaft-to-thigh gaps average 93.22/92.69 mm. The stock ascent at 0.98146 m reaches 215.73 mm on the closer thigh. This is a matched-height comparison between an isolated endpoint and an ascent sample. The stock 176.96 mm settled lockout occurs higher. For the close hold, final-second bar speed averaged 0.86 mm/s and peaked at 1.37 mm/s. Knee flexion averaged 8.95/8.97 degrees, torso angle 89.32 degrees and shoulder lead 5.66 mm. The grips stayed attached with at most 0.10 mm anchor error. Plate, non-foot floor and bar-to-leg impulses were zero for the admitted close hold. The loaded sole span stayed 290 mm on both sides. Minimum left/right normal loads over all 300 ticks were 841.7/837.8 N.

The explicit static member pairs 858.4 N normals at COPs about 64 mm inside each flat sole with vertical grip forces and paired grip couples that close all three bar moments. The solved bar and system force/moment residuals are below 0.001 N and 0.001 N m. The limiting modeled static drive in this member is shoulder twist, 85.1 of 130 N m. Ankle X needs about 3.5 of 450 N m, while the predicted Z torque is near zero. The chosen contact wrench requires zero friction and zero free contact moment.

Unity measured mean three-dimensional left/right grip forces on the bar of (-1.74, 370.35, -7.67) and (1.69, 365.29, 7.65) N in the last second. The corresponding grip moments were (-3.02, 1.20, -1.16) and (3.04, 0.57, 1.85) N m. The substep readings and bar momentum close to within 0.132 N and 0.026 N m. The measured grip reactions differ from the explicit static member. Net foot tangent forces inferred independently from ankle force and foot momentum were close to zero at rest, with mean (X,Z) of (-0.056,-0.063) and (-0.040,+0.068) N. This inference gives net forces at each sole. It leaves the tangential distribution and free yaw moment unidentified.

The close hold realized mean ankle Z angles of +3.67/-3.65 degrees, with modeled Z demand peaking at 1.33/0.76 N m against 60 N m over all 300 ticks. Joint constraint reactions measure the engine's net transmitted wrench. The modeled drive demand here is the controller's spring/damper diagnostic. The fixed endpoint's static COP reference uses the existing COM-position feedback to recover from displacement. Velocity-only feedback with a fixed COP target allowed backward drift and reached 61 mm/s at tick 300. No anatomical channels, gains, drive capacities or materials changed.

This endpoint is the input to the post-knee test. A complete judgeable attempt requires arriving at it with the bar already moving and returning the bar under control.

- Winter, Biomechanics and Motor Control of Human Movement, ch. 5, for COP, COM and force-platform mechanics.
- [Astrom and Murray, Feedback Systems, PID and integral windup](https://www.cds.caltech.edu/~murray/FBS/)
- [Hibbeler, Engineering Mechanics: Dynamics, impulse and momentum](https://www.pearson.com/en-us/subject-catalog/p/engineering-mechanics-dynamics/P200000013569/9780135426234)
- [Millington, Game Physics Engine Development, rigid-body integration](https://www.routledge.com/Game-Physics-Engine-Development/Millington/p/book/9780123819765) and [Gregory, Game Engine Architecture, real-time simulation and engine ownership](https://gameenginebook.com/)
