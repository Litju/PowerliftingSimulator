# GAM-13 top-tier control references

These references support the methodology and interpretation of the GAM-13
plant-identification report. They are control/system-identification sources,
not evidence for human biomechanics.

1. Forssell, U.; Ljung, L. (1999). “Closed-loop identification revisited.”
   *Automatica*, 35(7), 1215–1241. DOI:
   [10.1016/S0005-1098(99)00022-9](https://doi.org/10.1016/S0005-1098(99)00022-9).
   Relevance: closed-loop data have regulator-dependent identification issues;
   plant identification and controller qualification must remain separate.

2. Schoukens, J.; Ljung, L. (2019). “Nonlinear System Identification: A
   User-Oriented Road Map.” *IEEE Control Systems*, 39(6), 28–99. DOI:
   [10.1109/MCS.2019.2938121](https://doi.org/10.1109/MCS.2019.2938121).
   Relevance: a local linear model can be useful in a nonlinear system only
   within a validated operating region; excitation amplitude, model structure,
   and validation boundaries matter.

3. Doyle, J. C.; Glover, K.; Khargonekar, P. P.; Francis, B. A. (1989).
   “State-Space Solutions to Standard H2 and H-infinity Control Problems.”
   *IEEE Transactions on Automatic Control*, 34(8), 831–847. DOI:
   [10.1109/9.29425](https://doi.org/10.1109/9.29425).
   Relevance: robust control is a closed-loop stability/performance problem,
   not merely inversion of a nominal static gain.

4. Van Overschee, P.; De Moor, B. (1997). “Closed loop subspace system
   identification.” *Proceedings of the 36th IEEE Conference on Decision and
   Control*. DOI: [10.1109/CDC.1997.657851](https://doi.org/10.1109/CDC.1997.657851).
   Relevance: MIMO closed-loop identification requires methods that preserve
   dynamic state information rather than relying on scalar static slopes.

5. Åström, K. J.; Murray, R. M. (2008). *Feedback Systems: An Introduction
   for Scientists and Engineers*. Princeton University Press. Publisher record:
   [Princeton University Press catalog](https://assets.press.princeton.edu/catalogs/math10.pdf).
   Relevance: standard reference for feedback structure, stability reasoning,
   loop sensitivity, and the distinction between plant behavior and controller
   behavior.
