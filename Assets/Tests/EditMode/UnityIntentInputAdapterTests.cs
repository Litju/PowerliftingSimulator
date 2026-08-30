using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Tests
{
    public sealed class UnityIntentInputAdapterTests
    {
        [Test]
        public void RESET_REJECTS_CALLBACKS_FROM_PREVIOUS_REAL_TIME_EPOCH()
        {
            Type adapterType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(
                    "PowerliftingSimulator.Foundation.Unity.UnityIntentInputAdapter"))
                .First(type => type != null);
            object adapter = Activator.CreateInstance(
                adapterType,
                new object[] { null, new IntentBuffer(), new InputTimeDomain() });
            adapterType.GetMethod("Reset", BindingFlags.Instance | BindingFlags.Public)
                .Invoke(adapter, null);

            Type pendingInputType = adapterType.GetNestedType(
                "PendingInput",
                BindingFlags.NonPublic);
            ConstructorInfo constructor = pendingInputType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(IntentAction),
                    typeof(IntentEdgeKind),
                    typeof(float),
                    typeof(double),
                    typeof(bool),
                    typeof(long)
                },
                null);
            object staleInput = constructor.Invoke(new object[]
            {
                IntentAction.Confirm,
                IntentEdgeKind.Pressed,
                0f,
                -1d,
                true,
                0L
            });
            MethodInfo queue = adapterType.GetMethod(
                "Queue",
                BindingFlags.Instance | BindingFlags.NonPublic);
            queue.Invoke(adapter, new[] { staleInput });

            FieldInfo pendingEdgeCount = adapterType.GetField(
                "_pendingEdgeCount",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That((int)pendingEdgeCount.GetValue(adapter), Is.EqualTo(0));
        }
    }
}
