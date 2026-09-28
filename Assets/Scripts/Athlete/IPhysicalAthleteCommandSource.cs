using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Athlete
{
    public interface IPhysicalAthleteJointCommandSink
    {
        void ApplyCommand(string childId, JointCommand command, ulong tick = 0);
    }

    public interface IPhysicalAthleteCommandSource
    {
        void PrepareCommands(
            PhysicalObservation previousObservation,
            SimulationTime time,
            PlayerIntentFrame intent,
            IPhysicalAthleteJointCommandSink jointCommandSink);
    }
}
