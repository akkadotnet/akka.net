using Akka.MultiNodeTestRunner.Shared.Sinks;

namespace Akka.MultiNodeTestRunner.Shared
{
    /// <summary>
    /// Global state for hanging onto the exit code used by the process.
    /// 
    /// The <see cref="SinkCoordinator"/> sets this value once during shutdown.
    /// </summary>
    public static class ExitCodeContainer
    {
        public static int ExitCode = 0;
    }
}
