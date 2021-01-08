//-----------------------------------------------------------------------
// <copyright file="GraphInterpreterFailureModesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Streams.Stage;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Cancel = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.TestSetup.Cancel;
using PreStart = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.TestSetup.PreStart;
using PostStop = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.TestSetup.PostStop;
using OnError = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.TestSetup.OnError;

namespace Akka.Streams.Tests.Implementation.Fusing
{
    public class GraphInterpreterFailureModesSpec : GraphInterpreterSpecKit
    {
        // ReSharper disable InconsistentNaming
        private readonly FailingStageSetup.DownstreamPortProbe<int> downstream;
        private readonly FailingStageSetup.UpstreamPortProbe<int> upstream;
        private readonly Lazy<GraphStageLogic> stage;
        private readonly Func<Exception> testException;
        private readonly Func<ISet<TestSetup.ITestEvent>> lastEvents;
        private readonly Action stepAll;
        private readonly Action failOnNextEvent;
        private readonly Action failOnPostStop;
        private readonly Action clearEvents;

        public GraphInterpreterFailureModesSpec(ITestOutputHelper output = null) : base(output)
        {
            var setup = new FailingStageSetup(Sys);
            downstream = setup.Downstream;
            upstream = setup.Upstream;
            stage = setup.Stage;
            testException = setup.TestException;
            lastEvents = setup.LastEvents;
            stepAll = setup.StepAll;
            failOnNextEvent = setup.FailOnNextEvent;
            failOnPostStop = setup.FailOnPostStop;
            clearEvents = setup.ClearEvents;
        }

        [Fact]
        public void GraphInterpreter_should_handle_failure_on_OnPull()
        {
            lastEvents().Should().Equal(new PreStart(stage.Value));

            downstream.Pull();
            failOnNextEvent();
            stepAll();

            lastEvents()
                .Should()
                .BeEquivalentTo(new Cancel(upstream), new OnError(downstream, testException()), new PostStop(stage.Value));
        }

        [Fact]
        public void GraphInterpreter_should_handle_failure_on_OnPush()
        {
            lastEvents().Should().Equal(new PreStart(stage.Value));

            downstream.Pull();
            stepAll();
            clearEvents();
            upstream.Push(0);
            failOnNextEvent();
            stepAll();

            lastEvents()
                .Should()
                .BeEquivalentTo(new Cancel(upstream), new OnError(downstream, testException()), new PostStop(stage.Value));
        }

        [Fact]
        public void GraphInterpreter_should_handle_failure_on_OnPull_while_cancel_is_pending()
        {
            lastEvents().Should().Equal(new PreStart(stage.Value));

            downstream.Pull();
            downstream.Cancel();
            failOnNextEvent();
            stepAll();

            lastEvents()
                .Should()
                .BeEquivalentTo(new Cancel(upstream), new PostStop(stage.Value));
        }

        [Fact]
        public void GraphInterpreter_should_handle_failure_on_OnPush_while_complete_is_pending()
        {
            lastEvents().Should().Equal(new PreStart(stage.Value));

            downstream.Pull();
            stepAll();
            clearEvents();
            upstream.Push(0);
            upstream.Complete();
            failOnNextEvent();
            stepAll();

            lastEvents()
                .Should()
                .BeEquivalentTo(new OnError(downstream, testException()), new PostStop(stage.Value));
        }

        [Fact]
        public void GraphInterpreter_should_handle_failure_on_OnUpstreamFinish()
        {
            lastEvents().Should().Equal(new PreStart(stage.Value));

            upstream.Complete();
            failOnNextEvent();
            stepAll();

            lastEvents()
                .Should()
                .BeEquivalentTo(new OnError(downstream, testException()), new PostStop(stage.Value));
        }

        [Fact]
        public void GraphInterpreter_should_handle_failure_on_OnUpstreamFailure()
        {
            lastEvents().Should().Equal(new PreStart(stage.Value));

            upstream.Fail(new TestException("another exception")); // this is not the exception that will be propagated
            failOnNextEvent();
            stepAll();

            lastEvents()
                .Should()
                .BeEquivalentTo(new OnError(downstream, testException()), new PostStop(stage.Value));
        }

        [Fact]
        public void GraphInterpreter_should_handle_failure_on_OnDownstreamFinish()
        {
            lastEvents().Should().Equal(new PreStart(stage.Value));

            downstream.Cancel();
            failOnNextEvent();
            stepAll();

            lastEvents()
                .Should()
                .BeEquivalentTo(new Cancel(upstream), new PostStop(stage.Value));
        }

        [Fact]
        public void GraphInterpreter_should_handle_failure_in_PreStart()
        {
            var setup = new FailingStageSetup(Sys, true);

            setup.StepAll();

            setup.LastEvents()
                .Should()
                .BeEquivalentTo(new Cancel(setup.Upstream), new OnError(setup.Downstream, setup.TestException()),
                    new PostStop(setup.Stage.Value));
        }

        [Fact]
        public void GraphInterpreter_should_handle_failure_in_PostStop()
        {
            lastEvents().Should().Equal(new PreStart(stage.Value));

            upstream.Complete();
            downstream.Cancel();
            failOnPostStop();

            EventFilter.Error("Error during PostStop in [stage]").ExpectOne(() =>
            {
                stepAll();
                lastEvents().Should().BeEmpty();
            });
        }
    }
}
