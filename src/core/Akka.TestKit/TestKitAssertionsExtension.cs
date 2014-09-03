using Akka.Actor;

namespace Akka.TestKit
{
    public class TestKitAssertionsExtension : ExtensionIdProvider<TestKitAssertionsProvider>
    {
        private readonly TestKitAssertions _assertions;

        public TestKitAssertionsExtension(TestKitAssertions assertions)
        {
            _assertions = assertions;
        }

        public override TestKitAssertionsProvider CreateExtension(ExtendedActorSystem system)
        {
            return new TestKitAssertionsProvider(_assertions);
        }

        public static TestKitAssertionsProvider For(ActorSystem system)
        {
            return system.GetExtension<TestKitAssertionsProvider>();
        }
    }
}