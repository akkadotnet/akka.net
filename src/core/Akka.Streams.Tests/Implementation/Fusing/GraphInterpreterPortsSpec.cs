//-----------------------------------------------------------------------
// <copyright file="GraphInterpreterPortsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Cancel = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.TestSetup.Cancel;
using OnComplete = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.TestSetup.OnComplete;
using OnError = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.TestSetup.OnError;
using OnNext = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.TestSetup.OnNext;
using RequestOne = Akka.Streams.Tests.Implementation.Fusing.GraphInterpreterSpecKit.TestSetup.RequestOne;

namespace Akka.Streams.Tests.Implementation.Fusing
{
    public class GraphInterpreterPortsSpec : GraphInterpreterSpecKit
    {
        // ReSharper disable InconsistentNaming
        private PortTestSetup.DownstreamPortProbe<int> inlet;
        private PortTestSetup.UpstreamPortProbe<int> outlet;
        private Func<ISet<TestSetup.ITestEvent>> lastEvents;
        private Action stepAll;
        private Action step;
        private Action clearEvents;

        public GraphInterpreterPortsSpec(ITestOutputHelper output = null) : base(output)
        {
        }

        private void Setup(bool chasing)
        {
            var setup = new PortTestSetup(Sys, chasing);
            inlet = setup.In;
            outlet = setup.Out;
            lastEvents = setup.LastEvents;
            stepAll = setup.StepAll;
            step = setup.Step;
            clearEvents = setup.ClearEvents;
        }

        // TODO FIXME test failure scenarios

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_properly_transition_on_push_and_pull(bool chasing)
        {
            Setup(chasing);

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(false);
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            inlet.Pull();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            stepAll();

            lastEvents().Should().BeEquivalentTo(new RequestOne(outlet));
            outlet.IsAvailable().Should().Be(true);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            outlet.Push(0);

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            stepAll();

            lastEvents().Should().BeEquivalentTo(new OnNext(inlet, 0));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(true);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(false);
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();

            inlet.Grab().Should().Be(0);

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(false);
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            // Cycle completed
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_drop_ungrabbed_element_on_pull(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            step();
            clearEvents();
            outlet.Push(0);
            step();

            lastEvents().Should().BeEquivalentTo(new OnNext(inlet, 0));

            inlet.Pull();

            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_propagate_complete_while_downstream_is_active(bool chasing)
        {
            Setup(chasing);

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(false);

            outlet.Complete();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(false);
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();

            stepAll();

            lastEvents().Should().BeEquivalentTo(new OnComplete(inlet));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            inlet.Cancel(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            outlet.Complete(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_propagate_complete_while_upstream_is_active(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            stepAll();

            lastEvents().Should().BeEquivalentTo(new RequestOne(outlet));
            outlet.IsAvailable().Should().Be(true);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);

            outlet.Complete();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();

            stepAll();

            lastEvents().Should().BeEquivalentTo(new OnComplete(inlet));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            inlet.Cancel(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            outlet.Complete(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_propagate_complete_while_pull_is_in_flight(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);

            outlet.Complete();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();

            stepAll();

            lastEvents().Should().BeEquivalentTo(new OnComplete(inlet));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            inlet.Cancel(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            outlet.Complete(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_propagate_complete_while_push_is_in_flight(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            stepAll();
            clearEvents();

            outlet.Push(0);

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);

            outlet.Complete();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();

            step();

            lastEvents().Should().BeEquivalentTo(new OnNext(inlet, 0));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(true);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(false);
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Grab().Should().Be(0);
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            step();

            lastEvents().Should().BeEquivalentTo(new OnComplete(inlet));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            outlet.Complete(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_propagate_complete_while_push_is_in_flight_and_keep_ungrabbed_element(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            stepAll();
            clearEvents();

            outlet.Push(0);
            outlet.Complete();
            step();

            lastEvents().Should().BeEquivalentTo(new OnNext(inlet, 0));
            step();

            lastEvents().Should().BeEquivalentTo(new OnComplete(inlet));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(true);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Grab().Should().Be(0);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_propagate_complete_while_push_is_in_flight_and_pulled_after_the_push(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            stepAll();
            clearEvents();

            outlet.Push(0);
            outlet.Complete();
            step();

            lastEvents().Should().BeEquivalentTo(new OnNext(inlet, 0));
            inlet.Grab().Should().Be(0);

            inlet.Pull();
            stepAll();

            lastEvents().Should().BeEquivalentTo(new OnComplete(inlet));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_ignore_pull_while_completing(bool chasing)
        {
            Setup(chasing);

            outlet.Complete();
            inlet.Pull();
            // While the pull event is not enqueue at this point, we should still report the state correctly
            inlet.HasBeenPulled().Should().Be(true);

            stepAll();

            lastEvents().Should().BeEquivalentTo(new OnComplete(inlet));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_propagate_cancel_while_downstream_is_active(bool chasing)
        {
            Setup(chasing);

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(false);

            inlet.Cancel();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            stepAll();

            lastEvents().Should().BeEquivalentTo(new Cancel(outlet));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            outlet.Complete(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            inlet.Cancel(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_propagate_cancel_while_upstream_is_active(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            stepAll();

            lastEvents().Should().BeEquivalentTo(new RequestOne(outlet));
            outlet.IsAvailable().Should().Be(true);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);

            inlet.Cancel();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(true);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            stepAll();

            lastEvents().Should().BeEquivalentTo(new Cancel(outlet));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            outlet.Complete(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            inlet.Cancel(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_propagate_cancel_while_pull_is_in_flight(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);

            inlet.Cancel();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            stepAll();

            lastEvents().Should().BeEquivalentTo(new Cancel(outlet));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            outlet.Complete(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            inlet.Cancel(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_propagate_cancel_while_push_is_in_flight(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            stepAll();
            clearEvents();

            outlet.Push(0);

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);

            inlet.Cancel();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();

            stepAll();

            lastEvents().Should().BeEquivalentTo(new Cancel(outlet));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            outlet.Complete(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            inlet.Cancel(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_ignore_push_while_cancelling(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            stepAll();
            clearEvents();

            inlet.Cancel();
            outlet.Push(0);
            // While the push event is not enqueued at this point, we should still report the state correctly
            outlet.IsAvailable().Should().Be(false);

            stepAll();

            lastEvents().Should().BeEquivalentTo(new Cancel(outlet));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_clear_ungrabbed_element_even_when_cancelled(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            stepAll();
            clearEvents();
            outlet.Push(0);
            stepAll();

            lastEvents().Should().BeEquivalentTo(new OnNext(inlet, 0));

            inlet.Cancel();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            stepAll();
            lastEvents().Should().BeEquivalentTo(new Cancel(outlet));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_ignore_any_completion_if_they_are_concurrent_cancel_first(bool chasing)
        {
            Setup(chasing);

            inlet.Cancel();
            outlet.Complete();

            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_ignore_any_completion_if_they_are_concurrent_complete_first(bool chasing)
        {
            Setup(chasing);

            outlet.Complete();
            inlet.Cancel();

            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_ignore_completion_from_a_push_complete_if_cancelled_while_in_flight(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            stepAll();
            clearEvents();

            outlet.Push(0);
            outlet.Complete();
            inlet.Cancel();

            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_ignore_completion_from_a_push_complete_if_cancelled_after_OnPush(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            stepAll();
            clearEvents();

            outlet.Push(0);
            outlet.Complete();

            step();

            lastEvents().Should().BeEquivalentTo(new OnNext(inlet, 0));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(true);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(false);
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Grab().Should().Be(0);

            inlet.Cancel();
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_not_allow_to_grab_element_before_it_arrives(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            stepAll();
            outlet.Push(0);

            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_not_allow_to_grab_element_if_already_cancelled(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            stepAll();

            outlet.Push(0);
            inlet.Cancel();

            stepAll();

            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_propagate_failure_while_downstream_is_active(bool chasing)
        {
            Setup(chasing);

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(false);

            outlet.Fail(new TestException("test"));

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(false);
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();

            stepAll();

            lastEvents().Should().BeEquivalentTo(new OnError(inlet, new TestException("test")));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            inlet.Cancel(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            outlet.Complete(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_propagate_failure_while_upstream_is_active(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            stepAll();

            lastEvents().Should().BeEquivalentTo(new RequestOne(outlet));
            outlet.IsAvailable().Should().Be(true);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);

            outlet.Fail(new TestException("test"));

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();

            stepAll();

            lastEvents().Should().BeEquivalentTo(new OnError(inlet, new TestException("test")));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            inlet.Cancel(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            outlet.Complete(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_propagate_failure_while_pull_is_in_flight(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);

            outlet.Fail(new TestException("test"));

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();

            stepAll();

            lastEvents().Should().BeEquivalentTo(new OnError(inlet, new TestException("test")));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            inlet.Cancel(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            outlet.Complete(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_propagate_failure_while_push_is_in_flight(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            stepAll();
            clearEvents();

            outlet.Push(0);

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(false);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);

            outlet.Fail(new TestException("test"));

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(true);
            inlet.IsClosed().Should().Be(false);
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();

            step();

            lastEvents().Should().BeEquivalentTo(new OnNext(inlet, 0));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(true);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(false);
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Grab().Should().Be(0);
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            step();

            lastEvents().Should().BeEquivalentTo(new OnError(inlet, new TestException("test")));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();

            outlet.Complete(); // This should have no effect now
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_propagate_failure_while_push_is_in_flight_and_keep_ungrabbed_element(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            stepAll();
            clearEvents();

            outlet.Push(0);
            outlet.Fail(new TestException("test"));
            step();

            lastEvents().Should().BeEquivalentTo(new OnNext(inlet, 0));
            step();

            lastEvents().Should().BeEquivalentTo(new OnError(inlet, new TestException("test")));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(true);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Grab().Should().Be(0);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_ignore_pull_while_failing(bool chasing)
        {
            Setup(chasing);

            outlet.Fail(new TestException("test"));
            inlet.Pull();
            inlet.HasBeenPulled().Should().Be(true);

            stepAll();

            lastEvents().Should().BeEquivalentTo(new OnError(inlet, new TestException("test")));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_ignore_any_failure_completion_if_they_are_concurrent_cancel_first(bool chasing)
        {
            Setup(chasing);

            inlet.Cancel();
            outlet.Fail(new TestException("test"));

            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_ignore_any_failure_completion_if_they_are_concurrent_complete_first(bool chasing)
        {
            Setup(chasing);

            outlet.Fail(new TestException("test"));
            inlet.Cancel();

            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_ignore_any_failure_from_a_push_then_fail_if_cancelled_while_in_flight(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            stepAll();
            clearEvents();

            outlet.Push(0);
            outlet.Fail(new TestException("test"));
            inlet.Cancel();

            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Port_states_should_ignore_any_failure_from_a_push_then_fail_if_cancelled_after_OnPush(bool chasing)
        {
            Setup(chasing);

            inlet.Pull();
            stepAll();
            clearEvents();

            outlet.Push(0);
            outlet.Fail(new TestException("test"));

            step();

            lastEvents().Should().BeEquivalentTo(new OnNext(inlet, 0));
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(true);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(false);
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Grab().Should().Be(0);

            inlet.Cancel();
            stepAll();

            lastEvents().Should().BeEmpty();
            outlet.IsAvailable().Should().Be(false);
            outlet.IsClosed().Should().Be(true);
            inlet.IsAvailable().Should().Be(false);
            inlet.HasBeenPulled().Should().Be(false);
            inlet.IsClosed().Should().Be(true);
            inlet.Invoking(x => x.Pull()).ShouldThrow<ArgumentException>();
            outlet.Invoking(x => x.Push(0)).ShouldThrow<ArgumentException>();
            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }
    }
}
