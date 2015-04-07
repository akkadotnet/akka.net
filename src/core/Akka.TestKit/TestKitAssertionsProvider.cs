using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// Contains <see cref="ITestKitAssertions"/>.
    /// </summary>
    public class TestKitAssertionsProvider : IExtension
    {
        private readonly ITestKitAssertions _assertions;

        public TestKitAssertionsProvider(ITestKitAssertions assertions)
        {
            _assertions = assertions;
        }

        public ITestKitAssertions Assertions { get { return _assertions; } }
    }
}