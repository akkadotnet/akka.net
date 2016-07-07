//-----------------------------------------------------------------------
// <copyright file="GraphInterpreterPortsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        private readonly PortTestSetup.DownstreamPortProbe<int> inlet;
        private readonly PortTestSetup.UpstreamPortProbe<int> outlet;
        private readonly Func<ISet<TestSetup.ITestEvent>> lastEvents;
        private readonly Action stepAll;
        private readonly Action step;
        private readonly Action clearEvents;

        public GraphInterpreterPortsSpec(ITestOutputHelper output = null) : base(output)
        {
            var setup = new PortTestSetup(Sys);
            inlet = setup.In;
            outlet = setup.Out;
            lastEvents = setup.LastEvents;
            stepAll = setup.StepAll;
            step = setup.Step;
            clearEvents = setup.ClearEvents;
        }

        // TODO FIXME test failure scenarios

        [Fact]
        public void Port_states_should_properly_transition_on_push_and_pull()
        {
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

        [Fact]
        public void Port_states_should_drop_ungrabbed_element_on_pull()
        {
            inlet.Pull();
            step();
            clearEvents();
            outlet.Push(0);
            step();

            lastEvents().Should().BeEquivalentTo(new OnNext(inlet, 0));

            inlet.Pull();

            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Fact]
        public void Port_states_should_propagate_complete_while_downstream_is_active()
        {
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

        [Fact]
        public void Port_states_should_propagate_complete_while_upstream_is_active()
        {
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

        [Fact]
        public void Port_states_should_propagate_complete_while_pull_is_in_flight()
        {
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

        [Fact]
        public void Port_states_should_propagate_complete_while_push_is_in_flight()
        {
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

        [Fact]
        public void Port_states_should_propagate_complete_while_push_is_in_flight_and_keep_ungrabbed_element()
        {
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

        [Fact]
        public void Port_states_should_propagate_complete_while_push_is_in_flight_and_pulled_after_the_push()
        {
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

        [Fact]
        public void Port_states_should_ignore_pull_while_completing()
        {
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

        [Fact]
        public void Port_states_should_propagate_cancel_while_downstream_is_active()
        {
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

        [Fact]
        public void Port_states_should_propagate_cancel_while_upstream_is_active()
        {
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

        [Fact]
        public void Port_states_should_propagate_cancel_while_pull_is_in_flight()
        {
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

        [Fact]
        public void Port_states_should_propagate_cancel_while_push_is_in_flight()
        {
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

        [Fact]
        public void Port_states_should_ignore_push_while_cancelling()
        {
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

        [Fact]
        public void Port_states_should_clear_ungrabbed_element_even_when_cancelled()
        {
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

        [Fact]
        public void Port_states_should_ignore_any_completion_if_they_are_concurrent_cancel_first()
        {
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

        [Fact]
        public void Port_states_should_ignore_any_completion_if_they_are_concurrent_complete_first()
        {
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

        [Fact]
        public void Port_states_should_ignore_completion_from_a_push_complete_if_cancelled_while_in_flight()
        {
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

        [Fact]
        public void Port_states_should_ignore_completion_from_a_push_complete_if_cancelled_after_OnPush()
        {
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

        [Fact]
        public void Port_states_should_not_allow_to_grab_element_before_it_arrives()
        {
            inlet.Pull();
            stepAll();
            outlet.Push(0);

            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Fact]
        public void Port_states_should_not_allow_to_grab_element_if_already_cancelled()
        {
            inlet.Pull();
            stepAll();

            outlet.Push(0);
            inlet.Cancel();

            stepAll();

            inlet.Invoking(x => x.Grab()).ShouldThrow<ArgumentException>();
        }

        [Fact]
        public void Port_states_should_propagate_failure_while_downstream_is_active()
        {
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

        [Fact]
        public void Port_states_should_propagate_failure_while_upstream_is_active()
        {
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

        [Fact]
        public void Port_states_should_propagate_failure_while_pull_is_in_flight()
        {
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

        [Fact]
        public void Port_states_should_propagate_failure_while_push_is_in_flight()
        {
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

        [Fact]
        public void Port_states_should_propagate_failure_while_push_is_in_flight_and_keep_ungrabbed_element()
        {
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

        [Fact]
        public void Port_states_should_ignore_pull_while_failing()
        {
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

        [Fact]
        public void Port_states_should_ignore_any_failure_completion_if_they_are_concurrent_cancel_first()
        {
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

        [Fact]
        public void Port_states_should_ignore_any_failure_completion_if_they_are_concurrent_complete_first()
        {
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

        [Fact]
        public void Port_states_should_ignore_any_failure_from_a_push_then_fail_if_cancelled_while_in_flight()
        {
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

        [Fact]
        public void Port_states_should_ignore_any_failure_from_a_push_then_fail_if_cancelled_after_OnPush()
        {
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