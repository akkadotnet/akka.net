using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.MultiNodeTestRunner.Shared.Reporting;
using JetBrains.TeamCity.ServiceMessages;
using JetBrains.TeamCity.ServiceMessages.Write.Special;

namespace Akka.MultiNodeTestRunner.Shared.Sinks
{
    public class TeamCityMessageSinkActor : TestCoordinatorEnabledMessageSink
    {
        private readonly ITeamCityWriter _teamCityWriter;
        public TeamCityMessageSinkActor(ITeamCityWriter teamCityWriter, bool useTestCoordinator) : base(useTestCoordinator)
        {
            _teamCityWriter = teamCityWriter;
        }

        protected override void AdditionalReceives()
        {
            throw new NotImplementedException();
        }

        protected override void HandleTestRunTree(TestRunTree tree)
        {
            throw new NotImplementedException();
        }

        protected override void ReceiveFactData(FactData data)
        {
            throw new NotImplementedException();
        }

        protected override void HandleNewSpec(BeginNewSpec newSpec)
        {
            var teamCityTestSuiteActor =
                Context.ActorOf(Props.Create(() => new TeamCityTestSuiteActor(_teamCityWriter)));
            teamCityTestSuiteActor.Tell(newSpec);
            base.HandleNewSpec(newSpec);
        }
    }

    public class TeamCityTestSuiteActor : ReceiveActor
    {
        private readonly ITeamCityWriter _teamCityWriter;
        private ITeamCityTestsSubWriter _teamCityTestSuiteWriter;
        public TeamCityTestSuiteActor(ITeamCityWriter teamCityWriter)
        {
            Receive<BeginNewSpec>(msg => WriteTestSuiteStart(msg));
        }

        private void WriteTestSuiteStart(BeginNewSpec beginNewSpec)
        {
            _teamCityTestSuiteWriter =
                _teamCityWriter.OpenTestSuite($"{beginNewSpec.ClassName}.{beginNewSpec.MethodName}");
        }
    }

    public class TeamCityTestFlowWriter : ReceiveActor
    {
        private readonly ITeamCityTestsSubWriter _teamCityTestSuiteWriter;

        public TeamCityTestFlowWriter(ITeamCityTestsSubWriter teamCityTestSuiteWriter)
        {
            _teamCityTestSuiteWriter = teamCityTestSuiteWriter;

            Receive<BeginNewSpec>(msg => WriteTestFlowStart(msg));
        }

        private void WriteTestFlowStart(BeginNewSpec beginNewSpec)
        {
            
        }
    }

    /// <summary>
    /// <see cref="IMessageSink"/> implementation that writes directly to the console.
    /// </summary>
    public class TeamCityMessageSink : MessageSink
    {
        public TeamCityMessageSink(ITeamCityWriter teamCityWriter)
            : base(Props.Create(() => new TeamCityMessageSinkActor(teamCityWriter, true)))
        {
        }

        protected override void HandleUnknownMessageType(string message)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("Unknown message: {0}", message);
            Console.ResetColor();
        }
    }
}
