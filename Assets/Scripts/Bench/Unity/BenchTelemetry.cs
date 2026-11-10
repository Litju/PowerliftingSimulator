using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace PowerliftingSimulator.Bench.Unity
{
    /// <summary>One tick of bench diagnostics: what was commanded and what physically happened.</summary>
    public struct BenchTelemetrySample
    {
        public ulong Tick;
        public double TimeS;
        public BenchMotionPhase Phase;
        public BenchRefereeState RefereeState;
        public float LiftS, PressS;
        public Vector3 BarPosition, BarVelocity, BarAcceleration, BarAngularVelocity;
        public Vector3 LeftHandForceRaw, RightHandForceRaw, LeftHandTorqueRaw, RightHandTorqueRaw;
        public Vector3 BarContactForceWorld;
        public float LeftGripSeparationM, RightGripSeparationM, LeftSlipM, RightSlipM;
        public float HeadN, ShouldersN, AbdomenN, ButtocksN, LeftFootN, RightFootN, FeetOnBenchN;
        public float BarChestN, BarBeltN, BarRackN, BarSafetyN;
        public float LeftElbowFlexDeg, RightElbowFlexDeg;
        public float[] ArmCommandedDeg;   // shoulder angle, elbow, wrist per side (applied target rotation angle)
        public float[] ArmRealizedDeg;
        public float[] ArmDemand;          // modeled drive demand / maximumForce
        public float[] ArmSolverTorqueNm;  // |currentTorque| in joint space
        public bool SpotterActive;
        public BenchCommand Command;
    }

    public sealed class BenchTelemetry
    {
        private readonly List<BenchTelemetrySample> _samples = new List<BenchTelemetrySample>(4096);

        public IReadOnlyList<BenchTelemetrySample> Samples => _samples;

        public void Add(in BenchTelemetrySample sample) => _samples.Add(sample);

        public void Clear() => _samples.Clear();

        public string ToCsv()
        {
            var b = new StringBuilder();
            CultureInfo c = CultureInfo.InvariantCulture;
            b.AppendLine("tick,t,phase,referee,command,liftS,pressS,barX,barY,barZ,barVx,barVy,barVz,barAx,barAy,barAz,barWx,barWy,barWz," +
                "lHandFx,lHandFy,lHandFz,rHandFx,rHandFy,rHandFz,lHandTx,lHandTy,lHandTz,rHandTx,rHandTy,rHandTz,barContactFx,barContactFy,barContactFz," +
                "lGripSep,rGripSep,lSlip,rSlip,headN,shouldersN,abdomenN,buttocksN,lFootN,rFootN,feetOnBenchN,barChestN,barBeltN,barRackN,barSafetyN," +
                "lElbowDeg,rElbowDeg,lShoulderCmd,lElbowCmd,lWristCmd,rShoulderCmd,rElbowCmd,rWristCmd," +
                "lShoulderReal,lElbowReal,lWristReal,rShoulderReal,rElbowReal,rWristReal," +
                "lShoulderDemand,lElbowDemand,lWristDemand,rShoulderDemand,rElbowDemand,rWristDemand," +
                "lShoulderTq,lElbowTq,lWristTq,rShoulderTq,rElbowTq,rWristTq,spotter");
            foreach (BenchTelemetrySample s in _samples)
            {
                b.Append(s.Tick).Append(',').Append(s.TimeS.ToString("F3", c)).Append(',').Append(s.Phase).Append(',').Append(s.RefereeState).Append(',').Append(s.Command).Append(',');
                b.Append(F(s.LiftS)).Append(',').Append(F(s.PressS)).Append(',');
                V(b, s.BarPosition); V(b, s.BarVelocity); V(b, s.BarAcceleration); V(b, s.BarAngularVelocity);
                V(b, s.LeftHandForceRaw); V(b, s.RightHandForceRaw); V(b, s.LeftHandTorqueRaw); V(b, s.RightHandTorqueRaw); V(b, s.BarContactForceWorld);
                b.Append(F(s.LeftGripSeparationM)).Append(',').Append(F(s.RightGripSeparationM)).Append(',').Append(F(s.LeftSlipM)).Append(',').Append(F(s.RightSlipM)).Append(',');
                foreach (float v in new[] { s.HeadN, s.ShouldersN, s.AbdomenN, s.ButtocksN, s.LeftFootN, s.RightFootN, s.FeetOnBenchN, s.BarChestN, s.BarBeltN, s.BarRackN, s.BarSafetyN, s.LeftElbowFlexDeg, s.RightElbowFlexDeg })
                    b.Append(F(v)).Append(',');
                foreach (float[] block in new[] { s.ArmCommandedDeg, s.ArmRealizedDeg, s.ArmDemand, s.ArmSolverTorqueNm })
                {
                    for (int i = 0; i < 6; i++)
                        b.Append(block == null ? "nan" : F(block[i])).Append(',');
                }
                b.Append(s.SpotterActive ? 1 : 0).AppendLine();
            }
            return b.ToString();
        }

        public void WriteCsv(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, ToCsv());
        }

        private static string F(float v) => v.ToString("G6", CultureInfo.InvariantCulture);

        private static void V(StringBuilder b, Vector3 v) =>
            b.Append(F(v.x)).Append(',').Append(F(v.y)).Append(',').Append(F(v.z)).Append(',');
    }
}
