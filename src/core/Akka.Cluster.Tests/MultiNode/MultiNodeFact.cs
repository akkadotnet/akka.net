using System;
using Xunit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class MultiNodeFactAttribute : FactAttribute
    {
        public static Lazy<bool> ExecutedByMultiNodeRunner =
            new Lazy<bool>(() =>
            {
                var args = Environment.GetCommandLineArgs();
                if (args.Length == 0) return false;
                var firstArg = args[0];
                return firstArg.Contains("Akka.MultiNodeTestRunner") 
                    || firstArg.Contains("Akka.NodeTestRunner");
            });

        public override string Skip
        {
            get
            {
                return ExecutedByMultiNodeRunner.Value
                    ? null
                    : "Must be executed by multi-node test runner";
            }
            set { base.Skip = value; }
        }
    }
}
