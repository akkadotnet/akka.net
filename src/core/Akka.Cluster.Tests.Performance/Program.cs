using NBench;

namespace Akka.Cluster.Tests.Performance
{
    class Program
    {
        static int Main(string[] args)
        {
            return NBenchRunner.Run<Program>();
        }
    }
}
