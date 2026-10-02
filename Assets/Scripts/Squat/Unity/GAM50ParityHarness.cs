using System;
using System.Globalization;
using System.Text;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    /// <summary>
    /// GAM-50 Editor/standalone parity. One deterministic squat driven only
    /// through the production intent buffer (Brace, Yield, Drive) with manual
    /// authoritative ticks, recording a per-tick physical trace and a
    /// bit-exact body-state hash. The same routine runs from a PlayMode test
    /// in the Editor and from "-gam50Parity -gam50ParityLoad KG
    /// -gam50ParityOutput PATH" in a Windows player.
    /// </summary>
    public sealed class GAM50ParityHarness : MonoBehaviour
    {
        public const int MaximumTicks = 2000;
        public const int SettleTicks = 150;
        public const int BraceTicks = 10;
        public const int LockoutHoldTicks = 200;

        private const string ParityArgument = "-gam50Parity";
        private const string LoadArgument = "-gam50ParityLoad";
        private const string OutputArgument = "-gam50ParityOutput";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void SpawnForCommandLine()
        {
            if (HasArgument(ParityArgument))
                new GameObject("GAM50ParityHarness").AddComponent<GAM50ParityHarness>();
        }

        private System.Collections.IEnumerator Start()
        {
            if (!HasArgument(ParityArgument))
                yield break;
            // Let the scene's controllers initialise exactly as in play.
            for (int frame = 0; frame < 30; frame++)
            {
                SquatPhysicalPrototypeController candidate = FindFirstObjectByType<SquatPhysicalPrototypeController>();
                if (candidate != null && candidate.IsInitialized)
                    break;
                yield return null;
            }
            int exit = 0;
            try
            {
                float load = float.Parse(ArgumentValue(LoadArgument) ?? "140", CultureInfo.InvariantCulture);
                string output = ArgumentValue(OutputArgument) ?? "gam50-parity.csv";
                string trace = Run(load, "standalone");
                PhysicsBenchmarkEvidence.WriteText(output, trace);
                Debug.Log("GAM50_PARITY_DONE " + output);
            }
            catch (Exception exception)
            {
                Debug.LogError("GAM50_PARITY_FAILED " + exception);
                exit = 1;
            }
            Application.Quit(exit);
        }

        /// <summary>Runs the scripted squat in the loaded production scene and returns the CSV trace.</summary>
        public static string Run(float loadKg, string runtimeLabel)
        {
            FoundationBootstrap bootstrap = FindFirstObjectByType<FoundationBootstrap>();
            SquatPhysicalPrototypeController controller = FindFirstObjectByType<SquatPhysicalPrototypeController>();
            PhysicalAthleteRig rig = FindFirstObjectByType<PhysicalAthleteRig>();
            PhysicalBarbell barbell = FindFirstObjectByType<PhysicalBarbell>();
            if (bootstrap == null || controller == null || rig == null || barbell == null || !controller.IsInitialized)
                throw new InvalidOperationException("The squat scene is not initialised.");
            controller.enabled = false;
            bootstrap.enabled = false;
            // Same reset sequence in every runtime: unloaded, then the load.
            controller.SetLoad(0f);
            controller.SetLoad(loadKg);
            FoundationRuntime runtime = bootstrap.Runtime;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            var csv = new StringBuilder();
            csv.Append("# runtime=").Append(runtimeLabel).Append(" unity=").Append(Application.unityVersion)
                .Append(" platform=").Append(Application.platform).Append(" load_kg=")
                .Append(loadKg.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            csv.Append("tick,state,sq,bar_y,bar_vy,com_x,com_z,pelvis_y,left_knee_rad,right_knee_rad,left_hip_rad,state_hash\n");

            int lockoutTick = -1;
            for (int i = 0; i < MaximumTicks; i++)
            {
                SquatState state = controller.Adapter.State;
                double inputTime = runtime.CurrentTime.SimulationTimeSeconds + 0.25d * SimulationConstants.FixedDeltaTimeSeconds;
                bool brace = i >= SettleTicks && i < SettleTicks + BraceTicks;
                bool yieldInput = i >= SettleTicks + BraceTicks &&
                    (state == SquatState.SQUAT_COMMAND || state == SquatState.DESCENT || state == SquatState.SETUP);
                bool driveInput = state == SquatState.BOTTOM || state == SquatState.REVERSAL ||
                    state == SquatState.ASCENT || state == SquatState.STICKING;
                runtime.InputBuffer.SetContinuous(IntentAction.Brace, brace ? 1f : 0f, inputTime);
                runtime.InputBuffer.SetContinuous(IntentAction.Yield, yieldInput ? 1f : 0f, inputTime);
                runtime.InputBuffer.SetContinuous(IntentAction.Drive, driveInput ? 1f : 0f, inputTime);
                if (runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds) != 1)
                    throw new InvalidOperationException("Expected exactly one authoritative tick per frame.");
                controller.LeftFootContact?.PhysicsTickUpdate(dt);
                controller.RightFootContact?.PhysicsTickUpdate(dt);

                SquatObservationSnapshot snapshot = controller.ObservationCollector.LastSnapshot;
                Vector3Value com = snapshot.Support.SystemComWorldMeters;
                csv.Append(snapshot.SimulationTick.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(snapshot.State).Append(',').Append(F(snapshot.Sq)).Append(',')
                    .Append(F(snapshot.Bar.PositionWorldMeters.Y)).Append(',')
                    .Append(F(snapshot.Bar.LinearVelocityWorldMetersPerSecond.Y)).Append(',')
                    .Append(F(com.X)).Append(',').Append(F(com.Z)).Append(',')
                    .Append(F(snapshot.PelvisPositionWorldMeters.Y)).Append(',')
                    .Append(F(Twist(rig, "left_shank"))).Append(',').Append(F(Twist(rig, "right_shank"))).Append(',')
                    .Append(F(Twist(rig, "left_thigh"))).Append(',')
                    .Append(StateHash(rig, barbell).ToString("x16", CultureInfo.InvariantCulture)).Append('\n');

                if (lockoutTick < 0 && controller.Adapter.LockoutReached)
                    lockoutTick = i;
                if (lockoutTick >= 0 && i - lockoutTick >= LockoutHoldTicks)
                    break;
            }
            return csv.ToString();
        }

        private static float Twist(PhysicalAthleteRig rig, string jointId)
        {
            PoweredJointController.PoweredJointRuntime joint = rig.PoweredController.GetJoint(jointId);
            return joint.HasPostPhysicsDiagnostic
                ? PoweredJointController.SignedTwistRadians(joint.PostPhysicsDiagnostic.ActualRelative, Vector3.right)
                : float.NaN;
        }

        private static ulong StateHash(PhysicalAthleteRig rig, PhysicalBarbell barbell)
        {
            ulong hash = 1469598103934665603UL;
            void Mix(float value)
            {
                uint bits = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
                for (int b = 0; b < 4; b++)
                {
                    hash ^= (bits >> (8 * b)) & 0xFF;
                    hash *= 1099511628211UL;
                }
            }
            void MixBody(Rigidbody body)
            {
                Vector3 p = body.position; Quaternion r = body.rotation;
                Vector3 v = body.linearVelocity; Vector3 w = body.angularVelocity;
                Mix(p.x); Mix(p.y); Mix(p.z); Mix(r.x); Mix(r.y); Mix(r.z); Mix(r.w);
                Mix(v.x); Mix(v.y); Mix(v.z); Mix(w.x); Mix(w.y); Mix(w.z);
            }
            foreach (PhysicalSegmentRecipe recipe in PhysicalAthleteDefinition.Segments)
                MixBody(rig.Segments[recipe.Id].Body);
            if (barbell.Body != null && barbell.Body.gameObject.activeInHierarchy)
                MixBody(barbell.Body);
            return hash;
        }

        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static bool HasArgument(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        private static string ArgumentValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
    }
}
