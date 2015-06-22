using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Routing;
using Akka.TestKit;
using Akka.Util.Internal;

namespace Akka.Remote.Tests
{
    /// <summary>
    ///     Tests for <see cref="RemoteRouterConfig" />
    /// </summary>
    public class DualNodeSpec : AkkaSpec
    {
        private const string RemoteSetting = @"
            akka.test.timefactor = 1
            akka.test.single-expect-default = 6s #to help overcome issues with GC pauses on build server
            akka.remote.retry-gate-closed-for = 1 s #in the event of a Sys <--> System2 whoosh (both tried to connect to eachother), retry quickly
            akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            akka.remote.helios.tcp {
                hostname = localhost
                port = 0
            }            
        ";


        protected ActorSystem Sys2 { get; private set; }
        protected int Port { get; private set; }
        protected string SysName { get; private set; }
        protected Address IntendedRemoteAddress { get; private set; }
        protected TestProbe Sys2Probe { get; private set; }

        public DualNodeSpec(string system1Config, string system2Confg)
            : base(RemoteSetting + system1Config)
        {
            // ReSharper disable once PossibleInvalidOperationException
            Port = Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress.Port.Value;
            SysName = Sys.Name;
            var sys2Name = "Other" + SysName;
            var sys2Conf =
                ConfigurationFactory.ParseString(
                RemoteSetting +
                system2Confg
                .Replace("${sys2Name}", sys2Name)
                .Replace("${sys1Name}", SysName)
                .Replace("${port}", Port.ToString()));

            Sys2 = ActorSystem.Create(sys2Name, sys2Conf.WithFallback(Sys.Settings.Config));

            IntendedRemoteAddress = Address.Parse("akka.tcp://${sysName}@localhost:${port}"
                .Replace("${sysName}", SysName)
                .Replace("${port}", Port.ToString()));

            Sys2Probe = new TestProbe(Sys2, Assertions);
        }        

        protected override void AfterTermination()
        {
            Shutdown(Sys2);
        }
    }
}