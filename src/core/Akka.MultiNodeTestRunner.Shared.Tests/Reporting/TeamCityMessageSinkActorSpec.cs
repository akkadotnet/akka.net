using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNodeTestRunner.Shared.Sinks;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using JetBrains.TeamCity.ServiceMessages;
using JetBrains.TeamCity.ServiceMessages.Write.Special;

namespace Akka.MultiNodeTestRunner.Shared.Tests.Reporting
{
    public class TeamCityMessageSinkActorSpec : AkkaSpec
    {
        private readonly ITestOutputHelper _output;
        public TeamCityMessageSinkActorSpec(ITestOutputHelper output) : base(output, ConfigurationFactory.ParseString(@"
            akka {
                loglevel = ""INFO""
                stdout-loglevel = ""INFO""
            }"))
        {
            _output = output;
        }

        [Fact]
        public void TeamCityMessageSink_should_handle_mntr_spec_start()
        {
            var writer = new TeamCityServiceMessages().CreateWriter(str => _output.WriteLine(str));
            var tcSink = Sys.ActorOf(Props.Create(() => new TeamCityMessageSinkActor(writer, "assemblyName", false)));
            var msg = new BeginNewSpec("TestClassName", "TestMethodName", new List<NodeTest>());
            tcSink.Tell(msg);
        }

        [Fact]
        public void TeamCityMessageSink_should_handle_mntr_test_stdout()
        {
            var writer = new TeamCityServiceMessages().CreateWriter(str => _output.WriteLine(str));
            var tcSink = Sys.ActorOf(Props.Create(() => new TeamCityMessageSinkActor(writer, "assemblyName", false)));
            var beginNewSpec = new BeginNewSpec("TestClassName", "TestMethodName", new List<NodeTest>());
            var stdOut = new LogMessageForTestRunner("TestStdOut", LogLevel.InfoLevel, DateTime.UtcNow, String.Empty);
            tcSink.Tell(beginNewSpec);
            tcSink.Tell(stdOut);
        }
    }
}
