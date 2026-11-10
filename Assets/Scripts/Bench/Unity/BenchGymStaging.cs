using PowerliftingSimulator.Squat.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Bench.Unity
{
    /// <summary>
    /// Presentation only. Moves the decorative gym so its bench bay sits on
    /// the authoritative bench at the origin and hides the decorative bench,
    /// rack and display bar there. The gym has no colliders (it strips its
    /// own); it cannot support or touch anything physical.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public sealed class BenchGymStaging : MonoBehaviour
    {
        private void Start()
        {
            PowerliftingGymEnvironment gym = GetComponent<PowerliftingGymEnvironment>();
            if (gym == null)
                return;
            gym.EnsureEnvironmentBuilt();
            Transform bay = gym.BenchStationRoot;
            if (bay == null)
                return;
            Vector3 offset = bay.position;
            transform.position -= new Vector3(offset.x, 0f, offset.z);
            bay.gameObject.SetActive(false);
        }
    }
}
