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
        // Geometric contact (rule input) next to the force diagnostics above.
        public float HeadGapM, ShouldersGapM, ButtocksGapM, LeftFootGapM, RightFootGapM;
        public bool HeadContact, ShouldersContact, ButtocksContact, LeftFootPlanted, RightFootPlanted;
        // Wrist channels: requested vs applied vs realized, binding swing/twist
        // demand per channel and the finite ceiling (maximumForce) it is against.
        public Vector3 LeftWristRequestedDeg, LeftWristAppliedDeg, LeftWristRealizedDeg;
        public Vector3 RightWristRequestedDeg, RightWristAppliedDeg, RightWristRealizedDeg;
        public float LeftWristTwistDemand, LeftWristSwingDemand, RightWristTwistDemand, RightWristSwingDemand;
        public float LeftWristCeilingNm, RightWristCeilingNm;
        // Foot-loop actuation: the leg reference depth (m, + = into the floor)
        // the loop commanded this tick, against its saturation bounds.
        public float LeftLegDepthM, RightLegDepthM;
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
                "lShoulderTq,lElbowTq,lWristTq,rShoulderTq,rElbowTq,rWristTq,spotter," +
                "headGap,shouldersGap,buttocksGap,lFootGap,rFootGap,headContact,shouldersContact,buttocksContact,lFootPlanted,rFootPlanted," +
                "lWristReqX,lWristReqY,lWristReqZ,lWristAppX,lWristAppY,lWristAppZ,lWristRealX,lWristRealY,lWristRealZ," +
                "rWristReqX,rWristReqY,rWristReqZ,rWristAppX,rWristAppY,rWristAppZ,rWristRealX,rWristRealY,rWristRealZ," +
                "lWristTwistDemand,lWristSwingDemand,rWristTwistDemand,rWristSwingDemand,lWristCeilingNm,rWristCeilingNm,lLegDepth,rLegDepth");
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
                b.Append(s.SpotterActive ? 1 : 0).Append(',');
                foreach (float v in new[] { s.HeadGapM, s.ShouldersGapM, s.ButtocksGapM, s.LeftFootGapM, s.RightFootGapM })
                    b.Append(F(v)).Append(',');
                foreach (bool v in new[] { s.HeadContact, s.ShouldersContact, s.ButtocksContact, s.LeftFootPlanted, s.RightFootPlanted })
                    b.Append(v ? 1 : 0).Append(',');
                V(b, s.LeftWristRequestedDeg); V(b, s.LeftWristAppliedDeg); V(b, s.LeftWristRealizedDeg);
                V(b, s.RightWristRequestedDeg); V(b, s.RightWristAppliedDeg); V(b, s.RightWristRealizedDeg);
                b.Append(F(s.LeftWristTwistDemand)).Append(',').Append(F(s.LeftWristSwingDemand)).Append(',')
                    .Append(F(s.RightWristTwistDemand)).Append(',').Append(F(s.RightWristSwingDemand)).Append(',')
                    .Append(F(s.LeftWristCeilingNm)).Append(',').Append(F(s.RightWristCeilingNm)).Append(',')
                    .Append(F(s.LeftLegDepthM)).Append(',').Append(F(s.RightLegDepthM)).AppendLine();
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
