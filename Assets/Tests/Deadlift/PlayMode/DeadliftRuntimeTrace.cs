using System.Globalization;
using System.Text;
using PowerliftingSimulator.Deadlift.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>Compact per-tick trace of a <see cref="DeadliftAttemptRuntime"/>, for comparing two hosts of the same attempt.</summary>
    internal sealed class DeadliftRuntimeTrace
    {
        public readonly StringBuilder Rows = new StringBuilder("tick,phase,s,held,intent,barY,barVy,plates,footMin,margin,hRef,regL,regR,pelvisX,pelvisY,pelvisZ,dt\n");

        public void Attach(DeadliftAttemptRuntime r)
        {
            r.TickCompleted += rt =>
            {
                Vector3 p = rt.Pelvis.position;
                Rows.AppendLine(string.Join(",", new[]
                {
                    rt.Tick.ToString(CultureInfo.InvariantCulture), rt.Pull.Phase.ToString(), F(rt.Pull.LiftProgress01),
                    rt.Pull.LiftHeld ? rt.Pull.LiftHoldReason.Replace(' ', '-') : "-", F(rt.Pull.Intent01), F(rt.Bar.position.y), F(rt.Bar.linearVelocity.y),
                    F(rt.PlateLeftN + rt.PlateRightN), F(rt.FootLoadMinN), F(rt.Monitor.MarginM), F(rt.Hold.HandLoadReference.HandPullN),
                    F(rt.Regulator.OffsetDegFor("left_foot")), F(rt.Regulator.OffsetDegFor("right_foot")),
                    F(p.x), F(p.y), F(p.z), F(Time.fixedDeltaTime)
                }));
            };
        }

        private static string F(float v) => v.ToString("G7", CultureInfo.InvariantCulture);
    }
}
