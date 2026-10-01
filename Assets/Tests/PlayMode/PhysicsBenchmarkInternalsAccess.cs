using System.Runtime.CompilerServices;

// GAM-50: the permanent physics benchmark reuses the sealed GAM-13 runtime
// receipt writer rather than duplicating it.
[assembly: InternalsVisibleTo("PowerliftingSimulator.PhysicsBenchmarks")]
