//-----------------------------------------------------------------------
// <copyright file="WeightedRouteesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Pattern;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Akka.Configuration;
using Xunit;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

namespace Akka.Cluster.Metrics.Tests
{
    public class WeightedRouteesSpec : AkkaSpec
    {
        // TODO: Once Artery will be implemented, need to use "akka" value when enabled
        // i.e. : RARP(system).provider.remoteSettings.Artery.Enabled ? "akka" : "akka.tcp"
        private const string Protocol = "akka.tcp";
        
        private readonly Address _a1 = new Address(Protocol, "sys", "a1", 2551);
        private readonly Address _b1 = new Address(Protocol, "sys", "b1", 2551);
        private readonly Address _c1 = new Address(Protocol, "sys", "c1", 2551);
        private readonly Address _d1 = new Address(Protocol, "sys", "d1", 2551);

        private readonly ActorSelectionRoutee _routeeA;
        private readonly ActorSelectionRoutee _routeeB;
        private readonly ActorSelectionRoutee _routeeC;
        private ImmutableArray<Routee> _routees;
        
        private readonly ActorRefRoutee _testActorRoutee;
        
        public WeightedRouteesSpec() 
            : base(ConfigurationFactory.ParseString(@"
                akka.actor.provider = ""cluster""
                akka.remote.classic.netty.tcp.port = 0
                akka.remote.artery.canonical.port = 0
            "))
        {
            _routeeA = new ActorSelectionRoutee(Sys.ActorSelection(new RootActorPath(_a1) / "user" / "a"));
            _routeeB = new ActorSelectionRoutee(Sys.ActorSelection(new RootActorPath(_b1) / "user" / "b"));
            _routeeC = new ActorSelectionRoutee(Sys.ActorSelection(new RootActorPath(_c1) / "user" / "c"));
            _routees = ImmutableArray.Create<Routee>(_routeeA, _routeeB, _routeeC);
            
            _testActorRoutee = new ActorRefRoutee(TestActor);
        }

        [Fact]
        public void WeightedRoutees_should_allocate_weighted_routes()
        {
            var weights = new Dictionary<Address, int>()
            {
                [_a1] = 1,
                [_b1] = 3,
                [_c1] = 10
            }.ToImmutableDictionary();
            var weighted = new WeightedRoutees(_routees, _a1, weights);
            
            weighted[1].Should().Be(_routeeA);
            Range(2, 4).ForEach(i => weighted[i].Should().Be(_routeeB));
            Range(5, 14).ForEach(i => weighted[i].Should().Be(_routeeC));
            weighted.Total.Should().Be(14);
        }

        [Fact]
        public void WeightedRoutees_should_check_boundaries()
        {
            var empty = new WeightedRoutees(ImmutableArray<Routee>.Empty, _a1, ImmutableDictionary<Address, int>.Empty);
            empty.IsEmpty.Should().BeTrue();
            empty.Invoking(e => _ = e.Total).ShouldThrow<IllegalStateException>();
            
            var empty2 = new WeightedRoutees(ImmutableArray.Create<Routee>(_routeeA), _a1, ImmutableDictionary<Address, int>.Empty.Add(_a1, 0));
            empty2.IsEmpty.Should().BeTrue();
            empty2.Invoking(e => _ = e.Total).ShouldThrow<IllegalStateException>();
            empty2.Invoking(e => _ = e[0]).ShouldThrow<IllegalStateException>();
            
            var weighted = new WeightedRoutees(_routees, _a1, ImmutableDictionary<Address, int>.Empty);
            weighted.Total.Should().Be(3);
            weighted.Invoking(e => _ = e[0]).ShouldThrow<ArgumentException>();
            weighted.Invoking(e => _ = e[4]).ShouldThrow<ArgumentException>();
        }

        [Fact]
        public void WeightedRoutees_should_allocate_routees_for_undefined_weight()
        {
            var weights = new Dictionary<Address, int>()
            {
                [_a1] = 1,
                [_b1] = 7,
            }.ToImmutableDictionary();
            var weighted = new WeightedRoutees(_routees, _a1, weights);

            weighted[1].Should().Be(_routeeA);
            Range(2, 8).ForEach(i => weighted[i].Should().Be(_routeeB));
            // undefined, uses the mean of the weights, i.e. 4
            Range(9, 12).ForEach(i => weighted[i].Should().Be(_routeeC));
            weighted.Total.Should().Be(12);
        }

        [Fact]
        public void WeightedRoutees_should_allocate_weighted_local_routees()
        {
            var weights = new Dictionary<Address, int>()
            {
                [_a1] = 2,
                [_b1] = 1,
                [_c1] = 10
            }.ToImmutableDictionary();
            var routees2 = ImmutableArray.Create<Routee>(_testActorRoutee, _routeeB, _routeeC);
            var weighted = new WeightedRoutees(routees2, _a1, weights);
            
            Range(1, 2).ForEach(i => weighted[i].Should().Be(_testActorRoutee));
            Range(3, weighted.Total).ForEach(i => weighted[i].Should().NotBe(_testActorRoutee));
        }

        [Fact]
        public void WeightedRoutees_should_not_allocate_ref_with_weight_zero()
        {
            var weights = new Dictionary<Address, int>()
            {
                [_a1] = 0,
                [_b1] = 2,
                [_c1] = 10
            }.ToImmutableDictionary();
            var weighted = new WeightedRoutees(_routees, _a1, weights);
            
            Range(1, weighted.Total).ForEach(i => weighted[i].Should().NotBe(_routeeA));
        }

        private IEnumerable<int> Range(int from, int to)
        {
            for (var i = from; i <= to; ++i) 
                yield return i;
        }
    }
}
