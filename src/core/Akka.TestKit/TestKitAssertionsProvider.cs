using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// Contains <see cref="TestKitAssertions"/>.
    /// </summary>
    public class TestKitAssertionsProvider : IExtension
    {
        private readonly TestKitAssertions _assertions;

        public TestKitAssertionsProvider(TestKitAssertions assertions)
        {
            _assertions = assertions;
        }

        public TestKitAssertions Assertions { get { return _assertions; } }
    }
}