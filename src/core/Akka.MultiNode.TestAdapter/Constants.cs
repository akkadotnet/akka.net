using System;

namespace Akka.MultiNode.TestAdapter
{
    /// <summary>
    /// ExecutorMetadata
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Executor URI used by this test adapter
        /// </summary>
        public const string ExecutorUriString = "executor://MultiNodeExecutor";

        public static readonly Uri ExecutorUri = new Uri(ExecutorUriString);
    }
}