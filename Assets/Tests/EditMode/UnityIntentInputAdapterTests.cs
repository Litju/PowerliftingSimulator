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

        [Test]
        public void A_LATE_DELIVERED_EVENT_TAKES_EFFECT_AT_THE_START_OF_ITS_RENDER_INTERVAL()
        {
            Type adapterType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(
                    "PowerliftingSimulator.Foundation.Unity.UnityIntentInputAdapter"))
                .First(type => type != null);
            MethodInfo delivery = adapterType.GetMethod("DeliveryTimestamp", BindingFlags.Static | BindingFlags.NonPublic);
            var domain = new InputTimeDomain();
            domain.AdvanceRenderInterval(10.0);
            domain.AdvanceRenderInterval(10.5);

            // The domain's own contract is unchanged: an event stamped before the interval is rejected.
            Assert.Throws<InvalidOperationException>(() => domain.Map(9.9));
            double late = (double)delivery.Invoke(null, new object[] { 9.9, domain.RenderIntervalRealStartSeconds, 10.5 });
            Assert.That(late, Is.EqualTo(10.0));
            Assert.That(domain.Map(late), Is.EqualTo(domain.Map(10.0)));
            Assert.That((double)delivery.Invoke(null, new object[] { 10.2, 10.0, 10.5 }), Is.EqualTo(10.2));
        }
    }
}
