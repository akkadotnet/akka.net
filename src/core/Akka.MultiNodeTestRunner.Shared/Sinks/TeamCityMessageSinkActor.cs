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
        private ITeamCityTestsSubWriter _teamCityTestSuiteWriter;
        private ITeamCityTestsSubWriter _teamCityFlowWriter;
        private ITeamCityTestWriter _teamCityTestWriter;

        public TeamCityMessageSinkActor(ITeamCityWriter teamCityWriter, string suiteName,
            bool useTestCoordinator) : base(useTestCoordinator)
        {
            _teamCityWriter = teamCityWriter;
            _teamCityTestSuiteWriter = _teamCityWriter.OpenTestSuite(suiteName);
            _teamCityFlowWriter = _teamCityTestSuiteWriter.OpenFlow();
        }

        protected override void AdditionalReceives()
        {
        }

        protected override void HandleTestRunTree(TestRunTree tree)
        {
        }

        protected override void ReceiveFactData(FactData data)
        {
        }

        protected override void HandleNewSpec(BeginNewSpec newSpec)
        {
            _teamCityTestWriter = _teamCityFlowWriter.OpenTest($"{newSpec.ClassName}.{newSpec.MethodName}");
            base.HandleNewSpec(newSpec);
        }

        protected override void HandleEndSpec(EndSpec endSpec)
        {
            _teamCityTestWriter.Dispose();
            base.HandleEndSpec(endSpec);
        }

        //public class TeamCityTestSuiteWriterActor : ReceiveActor
        //{
        //    private readonly ITeamCityWriter _teamCityWriter;
        //    private ITeamCityTestsSubWriter _teamCityTestSuiteWriter;
        //    private IActorRef _teamCityFlowWriter;
        //    public TeamCityTestSuiteWriterActor(ITeamCityWriter teamCityWriter)
        //    {
        //        _teamCityWriter = teamCityWriter;

        //        Receive<BeginNewSpec>(msg => WriteTestSuiteStart(msg));
        //        Receive<LogMessageForTestRunner>(msg => WriteTestStdOut(msg));
        //    }

        //    private void WriteTestSuiteStart(BeginNewSpec beginNewSpec)
        //    {
        //        _teamCityTestSuiteWriter =
        //            _teamCityWriter.OpenTestSuite($"{beginNewSpec.ClassName}.{beginNewSpec.MethodName}");
        //        _teamCityFlowWriter =
        //            Context.ActorOf(Props.Create(() => new TeamCityTestFlowWriterActor(_teamCityTestSuiteWriter)));
        //        _teamCityFlowWriter.Tell(beginNewSpec);
        //    }

        //    private void WriteTestStdOut(LogMessageForTestRunner msg)
        //    {
        //        _teamCityFlowWriter.Tell(msg);
        //    }
        //}

        public class TeamCityTestFlowWriterActor : ReceiveActor
        {
            private readonly ITeamCityTestsSubWriter _teamCityTestSuiteWriter;
            private ITeamCityTestsSubWriter _teamCityFlowWriter;
            private ITeamCityTestWriter _teamCityTestWriter;

            public TeamCityTestFlowWriterActor(ITeamCityTestsSubWriter teamCityTestSuiteWriter)
            {
                _teamCityTestSuiteWriter = teamCityTestSuiteWriter;

                Receive<BeginNewSpec>(msg => WriteTestFlowStart(msg));
                Receive<LogMessageForTestRunner>(msg => WriteTestStdOut(msg));
            }

            private void WriteTestFlowStart(BeginNewSpec beginNewSpec)
            {
                _teamCityFlowWriter =
                    _teamCityTestSuiteWriter.OpenFlow();
                //_teamCityTestWriterActor =
                //    Context.ActorOf(Props.Create(() => new TeamCityTestWriterActor(_teamCityFlowWriter, beginNewSpec)));
            }

            private void WriteTestStdOut(LogMessageForTestRunner msg)
            {
                //_teamCityTestWriterActor.Tell(msg);
            }
        }

        public class TeamCityTestWriterActor : ReceiveActor
        {
            private readonly ITeamCityTestsSubWriter _teamCityFlowWriter;
            private ITeamCityTestWriter _teamCityTestWriter;

            public TeamCityTestWriterActor(ITeamCityTestsSubWriter teamCityFlowWriter, BeginNewSpec beginNewSpec)
            {
                _teamCityFlowWriter = teamCityFlowWriter;
                _teamCityTestWriter =
                    _teamCityFlowWriter.OpenTest($"{beginNewSpec.ClassName}.{beginNewSpec.MethodName}");

                Receive<LogMessageForTestRunner>(msg => WriteTestStandardOut(msg));
            }

            private void WriteTestStandardOut(LogMessageForTestRunner stdOutMessage)
            {
                _teamCityTestWriter.WriteStdOutput(stdOutMessage.Message);
            }
        }
    }

    /// <summary>
    /// <see cref="IMessageSink"/> implementation that writes directly to the console.
    /// </summary>
    public class TeamCityMessageSink : MessageSink
    {
        public TeamCityMessageSink(ITeamCityWriter teamCityWriter, string suiteName)
            : base(Props.Create(() => new TeamCityMessageSinkActor(teamCityWriter, suiteName, true)))
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
