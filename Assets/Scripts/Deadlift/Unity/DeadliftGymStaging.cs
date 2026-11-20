using PowerliftingSimulator.Squat.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// Presentation only. Moves the decorative gym so its deadlift platform
    /// bay sits on the authoritative platform at the origin and hides the
    /// decorative deadlift bar there. The gym strips its own colliders; it
    /// cannot support or touch anything physical.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public sealed class DeadliftGymStaging : MonoBehaviour
    {
        private void Start()
        {
            PowerliftingGymEnvironment gym = GetComponent<PowerliftingGymEnvironment>();
            if (gym == null)
                return;
            gym.EnsureEnvironmentBuilt();
            Transform bay = gym.DeadliftStationRoot;
            if (bay == null)
                return;
            Vector3 offset = bay.position;
            transform.position -= new Vector3(offset.x, 0f, offset.z);
            bay.gameObject.SetActive(false);
        }
    }
}
