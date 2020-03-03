//-----------------------------------------------------------------------
// <copyright file="RoutersUsageSample.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Event;
using Akka.Routing;
using FluentAssertions;

namespace Akka.Cluster.Metrics.Tests
{
    public class FactorialResult
    {
        public FactorialResult(int n, BigInteger factorial)
        {
            N = n;
            Factorial = factorial;
        }

        public int N { get; }
        public BigInteger Factorial { get; }
    }
    
    // <FactorialBackend>
    public class FactorialBackend : ReceiveActor
    {
        public FactorialBackend()
        {
            ReceiveAsync<int>(n =>
            {
                var sender = Sender;
                return Task.Run(() => Factorial(n)).PipeTo(sender, success: factorial => new FactorialResult(n, factorial));
            });
        }

        private BigInteger Factorial(int n)
        {
            var acc = BigInteger.One;
            for (var i = 0; i <= n; ++i)
                acc *= i;

            return acc;
        }
    }
    // </FactorialBackend>

    // <FactorialFrontend>
    public class FactorialFrontend : ReceiveActor
    {
        private readonly int _upToN;
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly IActorRef _backend = Context.ActorOf(FromConfig.Instance.Props(), "factorialBackendRouter");

        public FactorialFrontend(int upToN, bool repeat)
        {
            _upToN = upToN;

            Receive<FactorialResult>(result =>
            {
                if (result.N != _upToN) 
                    return;
                
                _log.Debug("{0}! = {1}", result.N, result.Factorial);
                if (repeat)
                    SendJobs();
                else
                    Context.Stop(Self);
            });

            Receive<ReceiveTimeout>(_ =>
            {
                _log.Info("Timeout");
                SendJobs();
            });
        }

        protected override void PreStart()
        {
            base.PreStart();

            SendJobs();
            Context.SetReceiveTimeout(10.Seconds());
        }

        private void SendJobs()
        {
            _log.Info("Starting batch of factorials up to [{0}]", _upToN);
            for (var n = 1; n <= _upToN; ++n)
            {
                _backend.Tell(n);
            }
        }
    }
    // </FactorialFrontend>
    
}
