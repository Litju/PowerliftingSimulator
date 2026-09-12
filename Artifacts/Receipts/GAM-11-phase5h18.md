# GAM-11 Phase 5H18 — Post-H17 0 kg and 25 kg Residual Remeasurement and Hip Equilibrium Decision

Start `df0761a` · Checkpoint `checkpoint/gam11-pre-h18-post-h17-residual-identification`

**The post-H17 0 kg and 25 kg physical squat residuals have been remeasured and definitively characterized.** In Phase 5H17, the 105 kg numerical regression was isolated and bounded to pre-H16 platform collapse by establishing a load domain guard on spine preload. In this phase (5H18), comprehensive dynamic telemetry, static hold feasibility, rate-dependence discrimination, gravitational generalized moment analysis, and spine-hip perturbation coupling were conducted to determine the nature of the remaining hip error.

The investigation conclusively proves that the remaining hip tracking error is **`CASE H18-B: DYNAMIC_REFERENCE_TRACKING_LAG`** during the primary descent and ascent, with a secondary quasi-static gravity equilibrium droop appearing only in deep squat positions ($s_q \ge 0.80$). Crucially, the dynamic hip error exhibits complete sign reversal between descent and ascent (hysteresis of $+8.64^\circ$ on descent vs $-14.68^\circ$ on ascent at $s_q = 0.40$), and slowing the phase progression by 50% reduces peak hip tracking error by 36%. Therefore, applying a static equilibrium preload table was explicitly **rejected** as it cannot correct hysteretic tracking lag and would severely aggravate ascent errors. Production physics code remains untouched (`PRODUCTION_FIX_APPLIED = NO`).

---

## 1. Executive Summary and Decisive Questions

| Question | Finding | Evidence |
|---|---|---|
| **Did hip residual survive H17 unchanged?** | **YES** | Descent errors at 0 kg / 25 kg replicate H16 baseline within $\pm 0.1^\circ$ ($s_q 0.25$: $+7.19^\circ$, $s_q 0.55$: $+4.63^\circ$, $s_q 0.80$: $+1.40^\circ$). |
| **Largest remaining tracking defect?** | **Knee (`shank`)** | Peak error $+21.97^\circ$ at $s_q 0.45$, RMS $12.36^\circ$ (25 kg). Hip is 2nd largest (Peak $-15.31^\circ$, RMS $8.38^\circ$). |
| **Is hip error static droop or dynamic lag?** | **DYNAMIC_REFERENCE_TRACKING_LAG** | Dynamic error reverses sign completely between descent ($+8.6^\circ$) and ascent ($-14.7^\circ$). At $s_q 0.10 - 0.40$, dynamic errors ($+7^\circ$ to $+8^\circ$) collapse to near-zero ($-0.3^\circ$ to $-3.5^\circ$) in static holds. |
| **Does balance controller command hip?** | **NO** | `hip balance offset = 0.00°` across all phases. Nominal target equals final target. Actuator lags pure nominal trajectory. |
| **Does phase rate modulate hip error?** | **YES** | Halving phase rate drops peak hip error from $-14.73^\circ$ to $-9.42^\circ$ (36.0% reduction). |
| **Is static hip preload authorized?** | **NO** | Static bias cannot address hysteretic dynamic lag and would worsen ascent tracking. |

---

## 2. Dynamic Residual Ranking (Post-H17 Baseline)

Full dynamic cycles (800 ticks, 16.0 s) were recorded at 50 Hz in `Artifacts/Measurements/GAM-11/H18/post-h17-dynamic-residual-0kg.csv` and `post-h17-dynamic-residual-25kg.csv`:

### 0 kg Barbell Load (Bodyweight Unloaded Squat):
- **Knee (`shank`):** Peak $+21.14^\circ$ at $s_q 0.45$ descent; RMS $12.04^\circ$.
- **Hip (`thigh`):** Peak $-14.86^\circ$ at $s_q 0.39$ ascent; RMS $8.07^\circ$.
- **Ankle (`foot`):** Peak $-9.23^\circ$ at $s_q 0.46$ descent; RMS $5.75^\circ$.
- **World Trunk:** Peak $+5.41^\circ$ at $s_q 0.34$ descent; RMS $2.93^\circ$.
- **Thorax:** Peak $-2.05^\circ$ at $s_q 0.12$ descent; RMS $0.99^\circ$.
- **Abdomen:** Peak $-1.29^\circ$ at $s_q 0.13$ descent; RMS $0.66^\circ$.

### 25 kg Barbell Load (Production Qualified Baseline):
- **Knee (`shank`):** Peak $+21.97^\circ$ at $s_q 0.45$ descent; RMS $12.36^\circ$. (Rank 1 defect)
- **Hip (`thigh`):** Peak $-15.31^\circ$ at $s_q 0.42$ ascent; RMS $8.38^\circ$. (Rank 2 defect)
- **Ankle (`foot`):** Peak $-9.32^\circ$ at $s_q 0.47$ descent; RMS $5.88^\circ$. (Rank 3 defect)
- **World Trunk:** Peak $+5.55^\circ$ at $s_q 0.34$ descent; RMS $2.95^\circ$.
- **Thorax:** Peak $-2.88^\circ$ at $s_q 0.13$ descent; RMS $1.32^\circ$.
- **Abdomen:** Peak $-1.38^\circ$ at $s_q 0.14$ descent; RMS $0.72^\circ$.

### Residual Hierarchy:
$$\text{Knee } (21.97^\circ) > \text{Hip } (15.31^\circ) > \text{Ankle } (9.32^\circ) > \text{Trunk } (5.55^\circ) > \text{Thorax } (2.88^\circ) > \text{Abdomen } (1.38^\circ)$$

---

## 3. Static vs Dynamic Discrimination (H1 vs H2)

To discriminate between **Hypothesis 1 (Quasi-Static Gravity Droop)** and **Hypothesis 2 (Dynamic Tracking Lag)**, static hold feasibility tests were conducted across 7 discrete squat phases ($s_q \in \{0.00, 0.10, 0.25, 0.40, 0.55, 0.80, 1.00\}$) holding posture for 3.0 s (150 ticks) at 0 kg and 25 kg.

Data recorded in `Artifacts/Measurements/GAM-11/H18/hip-static-feasibility.csv`:

| Phase $s_q$ | Dynamic Descent Error (25 kg) | Static Hold Error (25 kg) | Dynamic Ascent Error (25 kg) | Hysteresis Span | Mechanism |
|---|---|---|---|---|---|
| **0.00** | $+0.00^\circ$ | $+0.00^\circ$ | $+0.00^\circ$ | $0.00^\circ$ | Upright Equilibrium |
| **0.10** | $+8.20^\circ$ | **$-0.28^\circ$** | $-5.31^\circ$ | $13.51^\circ$ | Pure Dynamic Lag |
| **0.25** | $+7.19^\circ$ | **$-1.77^\circ$** | $-10.32^\circ$ | $17.51^\circ$ | Pure Dynamic Lag |
| **0.40** | $+8.64^\circ$ | **$-3.46^\circ$** | $-14.68^\circ$ | $23.32^\circ$ | Pure Dynamic Lag |
| **0.55** | $+4.63^\circ$ | **$-4.51^\circ$** | $-14.15^\circ$ | $18.78^\circ$ | Dynamic Lag + Emerging Gravity Droop |
| **0.80** | $+1.40^\circ$ | **$-5.12^\circ$** | $-8.60^\circ$ | $10.00^\circ$ | Mixed (Opposing Lag vs Static Droop) |
| **1.00** | $-0.83^\circ$ | **$-5.22^\circ$** | $-0.83^\circ$ | $0.00^\circ$ | Quasi-Static Gravity Droop |

### Key Observations:
1. **Sign Reversal (Hysteresis):** During descent, actual hip angle lags behind the flexing target ($\theta_{\text{actual}} < \theta_{\text{target}} \implies \text{Error} > 0$). During ascent, actual hip angle lags behind the extending target ($\theta_{\text{actual}} > \theta_{\text{target}} \implies \text{Error} < 0$).
2. **Dynamic Error Collapse in Static Holds:** In the shallow-to-mid range ($s_q 0.10 - 0.40$), where dynamic descent error peaks at $+8.64^\circ$, holding the target static causes the error to immediately collapse to $-0.28^\circ$ to $-3.46^\circ$.
3. **Deep Squat Transition ($s_q \ge 0.80$):** In deep squat positions, a true static gravitational droop of $\approx -5.2^\circ$ emerges due to the large horizontal cantilever moment ($\tau_{g,\text{hip}} \approx +53.8$ Nm). However, during descent at $s_q 0.80$, dynamic tracking lag actually counteracts gravity droop, resulting in a net positive error ($+1.40^\circ$).

---

## 4. Phase Rate Dependence and Target Rate Analysis

In `H18_06_PHASE_RATE_DEPENDENCE_DISCRIMINATOR`, the squat cycle duration was doubled (phase progression rate halved from 100% to 50%):

- **100% Phase Rate (Standard 8.0 s cycle):** Peak hip error = $-14.73^\circ$.
- **50% Phase Rate (Slow 16.0 s cycle):** Peak hip error = $-9.42^\circ$.
- **Reduction:** $5.31^\circ$ ($36.0\%$ reduction).

This definitive rate dependence proves that the actuator drives lack sufficient bandwidth or velocity feedforward to track the kinematic reference velocity $\dot{\theta}_{\text{hip}}(t)$.

### Target Rate Gap:
In `H18_03_HIP_TARGET_RATE_CONSISTENCY`:
- **With Balance Controller OFF:** Hip kinematic derivative gap is minimal ($1.87^\circ/\text{s}$). Commanded target tracks nominal trajectory.
- **With Balance Controller ON:** Hip target receives zero balance offset (`balanceOffset = 0.00°`). However, coupled body accelerations and ankle corrections induce high-frequency motion requiring higher torque slew rates.

---

## 5. Generalized Gravitational Hip Moment

In `H18_04_HIP_GRAVITATIONAL_GENERALIZED_MOMENT`, generalized gravitational moments about the bilateral hip joint axes were computed across the 7 hold phases:

$$\tau_{g,\text{hip}}(s_q) = \sum_{i \in \text{distal}} m_i \, g \, (x_i - x_{\text{hip}})$$

Recorded in `Artifacts/Measurements/GAM-11/H18/hip-gravity-generalized-moment.csv`:

| Phase $s_q$ | Hip Angle Target (deg) | Hip Moment $\tau_{g,\text{hip}}$ (Nm, 0 kg) | Hip Moment $\tau_{g,\text{hip}}$ (Nm, 25 kg) |
|---|---|---|---|
| **0.00** | $0.00^\circ$ | $+0.00$ | $+0.00$ |
| **0.10** | $7.67^\circ$ | $+6.11$ | $+11.89$ |
| **0.25** | $20.00^\circ$ | $+15.42$ | $+29.40$ |
| **0.40** | $32.00^\circ$ | $+23.88$ | $+44.75$ |
| **0.55** | $43.33^\circ$ | $+28.52$ | $+52.33$ |
| **0.80** | $56.00^\circ$ | $+29.80$ | $+53.84$ |
| **1.00** | $60.00^\circ$ | $+29.84$ | $+53.79$ |

The gravitational cantilever moment plateaus at $\approx +53.8$ Nm at $s_q \ge 0.80$, directly explaining the $-5.2^\circ$ static gravity droop observed in deep static holds.

---

## 6. Spine-Hip Cross-Coupling Sensitivity

In `H18_05_SPINE_HIP_CROSS_COUPLING_AND_SENSITIVITY`, static perturbations $\Delta \theta_{\text{hip}} \in \{-2^\circ, -1^\circ, 0^\circ, +1^\circ, +2^\circ\}$ were applied at $s_q = 0.25$ and $s_q = 0.55$:

Recorded in `Artifacts/Measurements/GAM-11/H18/hip-cross-coupling.csv`:
- **Hip Self-Sensitivity ($J_{\text{hip}}$):** $\approx 1.12^\circ/\text{deg}$ at $s_q 0.25$; $\approx 1.26^\circ/\text{deg}$ at $s_q 0.55$.
- **Spine Cross-Coupling ($J_{\text{abdomen}}$):** $\approx 0.24^\circ/\text{deg}$ at $s_q 0.25$; $\approx 0.55^\circ/\text{deg}$ at $s_q 0.55$.
- **World Trunk Sensitivity ($J_{\text{trunk}}$):** $\approx 1.14^\circ/\text{deg}$ at $s_q 0.25$; $\approx 1.50^\circ/\text{deg}$ at $s_q 0.55$.

The cross-coupling is locally linear and well-conditioned, confirming that hip motion does not induce destabilizing non-linear reactions into the qualified spine controller.

---

## 7. Visual Evidence Captures

Visual side and oblique frames were captured across all 5 key phases at 0 kg and 25 kg, and saved to `Artifacts/Evidence/GAM-11/h18-hip/`:
- Standing ($s_q 0.00$): `h18_0kg_sq0.00_side.png`, `h18_25kg_sq0.00_side.png`
- Descent Initiation ($s_q 0.25$): `h18_0kg_sq0.25_side.png`, `h18_25kg_sq0.25_side.png`
- Mid Descent ($s_q 0.55$): `h18_0kg_sq0.55_side.png`, `h18_25kg_sq0.55_side.png`
- Inflection / Deep Descent ($s_q 0.80$): `h18_0kg_sq0.80_side.png`, `h18_25kg_sq0.80_side.png`
- Full Bottom ($s_q 1.00$): `h18_0kg_sq1.00_side.png`, `h18_25kg_sq1.00_side.png`

All frames confirm continuous foot flat contact, stable bar seating on the trapezius, and absence of visual artifacts or body buckling.

---

## 8. Classification and Control Decision

### Scientific Classification:
**`CASE H18-B: HIP_DYNAMIC_TRACKING_LAG`**
- Primary mechanism: Dynamic reference tracking lag against the time-varying reference trajectory $\theta^*(s_q(t))$.
- Characteristic: Sign-reversing hysteresis span ($> 23^\circ$ at $s_q 0.40$), collapsible under static holds, strongly dependent on phase rate.
- Secondary component: Quasi-static gravitational cantilever droop of $-5.2^\circ$ in deep positions ($s_q \ge 0.80$).

### Control Decision:
- **`PRODUCTION_FIX_APPLIED = NO`**
- **`HIP_EQUILIBRIUM_BIAS_IMPLEMENTED = NO`**
- A static equilibrium bias table is physically unsuitable for hysteretic tracking lag: applying positive bias during descent would worsen negative error during ascent.
- Control design must focus on dynamic tracking enhancements (e.g. velocity feedforward $\tau_{ff} = B_v \dot{\theta}^*$ or dynamic phase-rate modulation).

---

## 9. Next Unit

Linear issue `GAM-11` remains **`IN_PROGRESS`**.
Next unit of work: **`GAM11_PHASE5H19_HIP_DYNAMIC_TARGET_RATE_AND_TRACKING_IDENTIFICATION`**.
