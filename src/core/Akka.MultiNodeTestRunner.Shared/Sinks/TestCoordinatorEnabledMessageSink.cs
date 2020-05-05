//-----------------------------------------------------------------------
// <copyright file="TestCoordinatorEnabledMessageSink.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.MultiNodeTestRunner.Shared.Reporting;

namespace Akka.MultiNodeTestRunner.Shared.Sinks
{
    /// <summary>
    /// A <see cref="MessageSinkActor"/> implementation that is capable of using a <see cref="TestRunCoordinator"/> for 
    /// test run summaries and other purposes.
    /// </summary>
    public abstract class TestCoordinatorEnabledMessageSink : MessageSinkActor
    {
        protected IActorRef TestCoordinatorActorRef;
        protected bool UseTestCoordinator;

        protected TestCoordinatorEnabledMessageSink(bool useTestCoordinator)
        {
            UseTestCoordinator = useTestCoordinator;
            Receive<SinkCoordinator.RequestExitCode>(code =>
            {
                if (UseTestCoordinator)
                {
                    var sender = Sender;
                    TestCoordinatorActorRef.Ask<TestRunTree>(new TestRunCoordinator.RequestTestRunState())
                        .ContinueWith(task =>
                        {
                            return new SinkCoordinator.RecommendedExitCode(task.Result.Passed.GetValueOrDefault(false)
                                ? 0
                                : 1);
                        }, TaskContinuationOptions.ExecuteSynchronously)
                            .PipeTo(sender, Self);
                }
            });
        }

        protected override void PreStart()
        {
            //Fire up a TestRunCoordinator instance and subscribe to FactData messages when they arrive
            if (UseTestCoordinator)
            {
                TestCoordinatorActorRef = Context.ActorOf<TestRunCoordinator>();
                TestCoordinatorActorRef.Tell(new TestRunCoordinator.SubscribeFactCompletionMessages(Self));
            }
        }

        protected abstract void ReceiveFactData(FactData data);

        protected override void HandleNewSpec(BeginNewSpec newSpec)
        {
            if (UseTestCoordinator)
            {
                TestCoordinatorActorRef.Tell(newSpec);
            }
        }

        protected override void HandleEndSpec(EndSpec endSpec)
        {
            if (UseTestCoordinator)
            {
                TestCoordinatorActorRef.Tell(endSpec);
            }
        }

        protected override void HandleNodeMessageFragment(LogMessageFragmentForNode logMessage)
        {
            if (UseTestCoordinator)
            {
                var nodeMessage = new MultiNodeLogMessageFragment(logMessage.When.Ticks, logMessage.Message,
                   logMessage.NodeIndex, logMessage.NodeRole);

                TestCoordinatorActorRef.Tell(nodeMessage);
            }
        }

        protected override void HandleRunnerMessage(LogMessageForTestRunner node)
        {
            if (UseTestCoordinator)
            {
                var runnerMessage = new MultiNodeTestRunnerMessage(node.When.Ticks, node.Message, node.LogSource,
                    node.Level);

                TestCoordinatorActorRef.Tell(runnerMessage);
            }
        }

        protected override void HandleNodeSpecPass(NodeCompletedSpecWithSuccess nodeSuccess)
        {
            if (UseTestCoordinator)
            {
                var nodeMessage = new MultiNodeResultMessage(DateTime.UtcNow.Ticks, nodeSuccess.Message,
                    nodeSuccess.NodeIndex, nodeSuccess.NodeRole, true);

                TestCoordinatorActorRef.Tell(nodeMessage);
            }
        }

        protected override void HandleNodeSpecFail(NodeCompletedSpecWithFail nodeFail)
        {
            if (UseTestCoordinator)
            {
                var nodeMessage = new MultiNodeResultMessage(DateTime.UtcNow.Ticks, nodeFail.Message,
                    nodeFail.NodeIndex, nodeFail.NodeRole, false);

                TestCoordinatorActorRef.Tell(nodeMessage);
            }
        }

        protected override void HandleTestRunEnd(EndTestRun endTestRun)
        {
            if (UseTestCoordinator)
            {
                var sender = Sender;
                TestCoordinatorActorRef.Ask<TestRunTree>(endTestRun)
                    .ContinueWith(tr =>
                    {
                        var testRunTree = tr.Result;
                        return new BeginSinkTerminate(testRunTree, sender);
                    }, TaskContinuationOptions.ExecuteSynchronously)
                    .PipeTo(Self);
            }
        }

        protected override void HandleSinkTerminate(BeginSinkTerminate terminate)
        {
            HandleTestRunTree(terminate.TestRun);
            base.HandleSinkTerminate(terminate);
        }
    }
}

