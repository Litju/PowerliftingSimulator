using System;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    [DisallowMultipleComponent]
    public sealed class SquatRootMotionRelay : MonoBehaviour
    {
        private Animator _animator;
        private Action<Vector3> _receiver;

        public Animator Animator => _animator;

        public void Bind(Action<Vector3> receiver)
        {
            _animator = GetComponent<Animator>();
            _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        }

        private void OnAnimatorMove()
        {
            _receiver?.Invoke(_animator.deltaPosition);
        }
    }
}
