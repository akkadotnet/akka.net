
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Dispatch
{
    public class ChannelExecutorSpec : AkkaSpec
    {
        [Fact]
        public void Should_Successfully_Create_Channel_Executor_ActorSystem()
        {
            var config = @"
                          akka.actor.default-dispatcher = { 
                              executor = channel-executor
                              # channel-executor.priority = normal
                          }";
            var actorSytem = ActorSystem.Create("ce", config);
        }
    }
}
