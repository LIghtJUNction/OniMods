using System;
using System.Reflection;
using CycleTrim.Patches;

namespace CycleTrim.BrainBenchmarks
{
    internal static partial class Program
    {
        private delegate bool BusyPickupPrefix(PickupableSensor sensor, Navigator navigator);
        private delegate bool BusyChorePrefix(
            ChoreConsumer consumer,
            ref Chore.Precondition.Context context,
            ref bool result);

        private static T BusyPrefix<T>(string patchName) where T : Delegate
        {
            var patch = typeof(BusyDuplicantChoreThrottlePatch)
                .GetNestedType(patchName, BindingFlags.NonPublic);
            return (T)patch.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)
                .CreateDelegate(typeof(T));
        }

        private static void BusyConsumersKeepIdentityEligibility()
        {
            // Run the linked production Prefix methods. The fixtures supply only
            // game components; they do not implement eligibility or refresh rules.
            var pickup = BusyPrefix<BusyPickupPrefix>("PickupableSensorUpdatePatch");
            var chore = BusyPrefix<BusyChorePrefix>("FindNextChorePatch");
            var navigator = new Navigator { NavGrid = new NavGrid() };
            var robot = BusyConsumer(navigator, isDuplicant: false);
            var robotSensor = new PickupableSensor();
            robotSensor.AddComponent(robot);
            for (var iteration = 0; iteration < 8; iteration++)
            {
                AssertTrue(pickup(robotSensor, navigator), "robot pickup remains native");
                var context = new Chore.Precondition.Context { Marker = 17 };
                var result = true;
                AssertTrue(chore(robot, ref context, ref result), "robot chore remains native");
                AssertTrue(result, "robot eligibility does not overwrite the native result");
                AssertEqual(17L, context.Marker, "robot eligibility preserves chore context");
            }
            AssertEqual(1L, robot.IdentityLookups, "robot identity is classified once");

            var duplicant = BusyConsumer(navigator, isDuplicant: true);
            var duplicantSensor = new PickupableSensor();
            duplicantSensor.AddComponent(duplicant);
            var duplicantContext = new Chore.Precondition.Context { Marker = 19 };
            var duplicantResult = true;
            AssertTrue(pickup(duplicantSensor, navigator), "first duplicant pickup refresh runs");
            AssertTrue(chore(duplicant, ref duplicantContext, ref duplicantResult),
                "first duplicant chore refresh runs");
            AssertFalse(pickup(duplicantSensor, navigator), "busy duplicant pickup is throttled");
            AssertFalse(chore(duplicant, ref duplicantContext, ref duplicantResult),
                "busy duplicant chore is throttled");
            AssertFalse(duplicantResult, "suppressed duplicant scan clears result");
            AssertEqual(0L, duplicantContext.Marker, "suppressed duplicant scan clears context");
            AssertEqual(1L, duplicant.IdentityLookups, "duplicant identity is classified once");

            duplicant.choreDriver.CurrentChore = new IdleChore();
            for (var iteration = 0; iteration < 3; iteration++)
            {
                AssertTrue(pickup(duplicantSensor, navigator), "idle duplicant pickup remains native");
                AssertTrue(chore(duplicant, ref duplicantContext, ref duplicantResult),
                    "idle duplicant chore remains native");
            }

            AssertTrue(pickup(new PickupableSensor(), navigator), "missing consumer remains native");
            AssertTrue(pickup(duplicantSensor, null), "missing navigator remains native");
        }

        private static ChoreConsumer BusyConsumer(Navigator navigator, bool isDuplicant)
        {
            var consumer = new ChoreConsumer
            {
                choreDriver = new ChoreDriver { CurrentChore = new Chore() },
                consumerState = new ChoreConsumerState { navigator = navigator }
            };
            if (isDuplicant)
            {
                consumer.AddComponent(new MinionIdentity());
            }
            return consumer;
        }
    }
}
