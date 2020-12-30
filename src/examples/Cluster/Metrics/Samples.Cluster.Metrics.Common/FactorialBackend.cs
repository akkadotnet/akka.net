using System.Numerics;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;

namespace Samples.Cluster.Metrics.Common
{
    public class FactorialBackend : ReceiveActor
    {
        public FactorialBackend()
        {
            var log = Context.GetLogger();

            Receive<int>(n =>
            {
                log.Info($"{Self.Path} received factorial job [{n}]");

                Factorial(n).PipeTo(Sender);
            });
        }

        private async Task<(int, BigInteger)> Factorial(int n)
        {
            var i = n;
            var accumulator = new BigInteger(1);

            while (i > 1)
            {
                accumulator *= --i;
            }

            await Task.Delay(1000);

            return (n, accumulator);
        }
    }
}
