using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using Xunit;

namespace DocsExamples.Actors
{
    public class CoordinatedShutdownSpecs
    {
        [Fact]
        public async Task CoordinatedShutdownBuiltInReason()
        {
            #region coordinated-shutdown-builtin
            var actorSystem = ActorSystem.Create("MySystem");

            // shutdown with reason "CLR exit" - meaning the process was being terminated
            // task completes once node has left cluster and terminated the ActorSystem
            Task shutdownTask = CoordinatedShutdown.Get(actorSystem)
                .Run(CoordinatedShutdown.ClrExitReason.Instance);
            await shutdownTask;

            // shutdown reason gets cached here.
            // The`Reason` type can be subclassed with custom properties if needed
            CoordinatedShutdown.Get(actorSystem).ShutdownReason.Should()
                .Be(CoordinatedShutdown.ClrExitReason.Instance);

            #endregion


            actorSystem.WhenTerminated.IsCompleted.Should().BeTrue();
        }
    }
}
