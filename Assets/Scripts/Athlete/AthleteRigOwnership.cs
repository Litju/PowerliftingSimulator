using UnityEngine;

namespace PowerliftingSimulator.Athlete
{
    public sealed class AthleteRigOwnership : MonoBehaviour
    {
        [SerializeField] private Animator referenceRigAnimator;
        [SerializeField] private Transform visibleRigRoot;
        [SerializeField] private Transform physicalRigRoot;

        public Animator ReferenceRigAnimator => referenceRigAnimator;

        public Transform VisibleRigRoot => visibleRigRoot;

        public Transform PhysicalRigRoot => physicalRigRoot;

        public bool PhysicalRigImplemented => physicalRigRoot != null;

        public void ConfigureForCalibration(Animator referenceAnimator, Transform visibleRoot)
        {
            referenceRigAnimator = referenceAnimator;
            visibleRigRoot = visibleRoot;
            physicalRigRoot = null;
        }
    }
}
