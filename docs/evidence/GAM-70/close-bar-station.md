# GAM-70 close-bar station composition

Unity 6000.3.22f1, Windows PlayMode. GAM70_RUNTIME_CLOSE_BAR_ATTEMPT uses a deterministic Drive held from tick 284. CLOSE_BAR_STATION_USES_HELD_KEYBOARD_DRIVE exercises the committed station, its Input System keyboard binding and both frame-sliced reference solves. The player holds W at the grounded preparation cue. The station waits at the last accepted preparation tick until Drive arrives. The Input System test confirms that real Drive remains the command source throughout ascent, with the intent override empty. It releases the synthetic W after DOWN. The original scripted prepared rehearsal remains separately selectable.

The prepared athlete started pulling at tick 301. The dynamic 75 kg bar physically separated at 314, with confirmation at 316. It crossed the qualified knee state at 538, entered controller lockout at 711, and earned referee DOWN at 762. The nearer-thigh gap averaged 95.66 mm over the controller lockout window, compared with 176.96 mm settled stock. The post-knee receipt reports a separate matched-realized-height comparison. The prepared rejoin result of 174.53 mm remains a separate control.

## Controlled return

Simple reverse traversal lost support at tick 960 when switching the controller on return through the knee. Preserving the contact-aware preload below the knee delayed loss to 976. A lowering-only joint-velocity feedforward A/B lost support at 958. Braking at the measured knee waypoint, then retaining the reverse prefix, kept bilateral broad support through that crossing and lost support lower down at tick 1085. That lower failure first narrowed the sole patch before modeled drive ceilings were crossed.

The qualified return settles at the measured knee waypoint using the existing deceleration and contact-aware control. It checks the motionless bar threshold, bilateral load, and active and possible support before resuming. The recovered lower posture and shaft height match the established stock return around progress 0.369. Beyond that measured boundary the reverse prefix had already diverged by up to 5.8 degrees at the ankles. The return blends into the qualified lower reference before that divergence. This handoff runs after DOWN and below the knee. The close ascent and lockout use their own continuation.

In the complete attempt, conservative swept shaft clearance remained positive, the minimum sampled gap was 1.78 mm, and bar-leg impulse remained zero. Minimum airborne foot load across ascent and return was 558.8 N and the nominal capture reserve was at least 70.0 mm. First plate contact was at tick 1198. At release on tick 1466 the plates carried 665.8 N and bar speed was about 0.10 mm/s. The referee recorded GOOD LIFT, with both grips attached through the return.

Launch the Windows station with -deadliftCloseLockout and optionally -deadliftSideView. Hold W or Up from the preparation cue through DOWN. The stock path remains the default. The close path's additional qualification is for the admitted prepared state and onset.

## Regression and standalone receipts

| Onset | Stock, default acceleration | Close-bar composition |
| ---: | --- | --- |
| 301 | GOOD LIFT, DOWN 834 | GOOD LIFT, DOWN 762, release 1466 |
| 302 | FootMoved at 665, no DOWN | Support loss at 780, 96.03 mm mean gap in controller lockout, no DOWN |
| 305 | GOOD LIFT, DOWN 839 | Bar-leg impulse at 452, before knee passage |
| 320 | GOOD LIFT, DOWN 855 | Bar-leg impulse at 465, referee ThighSupport |
| 350 | GOOD LIFT, DOWN 884 | Support loss at 445, before knee passage |

Stock onsets are first Drive. The prepared controls held the same Drive from tick 284 and delayed acceptance with EarliestPullTick. The visible close-bar station waits for input at the measured preparation cue and leaves Drive under the keyboard binding. Onset 301 is the admitted close-bar window.

The final focused PlayMode batch passed seven of eight methods, including the stock station, prepared full lift, onset sweep, endpoint, contact A/B and one-hand load-path control. The existing legacy GATE3_PLAYER_PULL_BREAKS_THE_GROUNDED_BAR_AND_NO_PULL_DOES_NOT method reproduced the known full-Drive support loss. Its no-pull and insufficient-pull controls both recorded zero bar rise. The single-grip and abort arms executed. The single-grip arm tilted, and the configured abort came after separation. See the earlier [Gate 9 receipt](gate9-bar-proximity.txt). All ten deadlift EditMode checks passed, including all six referee checks.

Windows x64 build succeeded from ec6a03cb6aeb894d55147a535f2fef03c5735b00 using Unity 6000.3.22f1. The managed deadlift assembly SHA-256 is 8421BEE9F1ABC3EA81757AF2F3E6705EA49341C066DA3A35857F3A78CE63C529. The standalone close-bar station initialized and its window responded. The executable and raw evidence remain in ignored Builds/GAM70/CloseLockout and Logs/GAM70. The owner hardware-keyboard rehearsal is pending. The Windows desktop connection failed with a missing native pipe and the player is left at the preparation cue.
