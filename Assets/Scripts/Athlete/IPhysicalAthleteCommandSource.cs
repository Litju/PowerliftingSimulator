using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Athlete
{
    public interface IPhysicalAthleteCommandSource
    {
        void PrepareCommands(
            PhysicalObservation previousObservation,
            SimulationTime time,
            PlayerIntentFrame intent,
            PoweredJointController poweredController);
    }
}
