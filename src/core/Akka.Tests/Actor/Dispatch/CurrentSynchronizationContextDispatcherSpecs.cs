using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Xunit;

namespace Akka.Tests.Actor.Dispatch
{
    /// <summary>
    /// Used to reproduce and verify fix for https://github.com/akkadotnet/akka.net/issues/2172
    /// </summary>
    public class CurrentSynchronizationContextDispatcherSpecs : AkkaSpec
    {
        private static Config _config = ConfigurationFactory.ParseString(@"
            akka.actor.deployment {
               /some-ui-actor{
                dispatcher = akka.actor.synchronized-dispatcher
               }
        ");

        public CurrentSynchronizationContextDispatcherSpecs() : base(_config) { }

        [Fact]
        public void CurrentSynchronizationContextDispatcher_should_start_without_error_Fix2172()
        {
            var uiActor = Sys.ActorOf(EchoActor.Props(this), "some-ui-actor");
            uiActor.Tell("ping");
            ExpectMsg("ping");
        }
    }
}
