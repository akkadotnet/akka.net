//-----------------------------------------------------------------------
// <copyright file="EntitiesSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests
{
    public class EntitiesSpec : AkkaSpec
    {
        public EntitiesSpec(ITestOutputHelper helper) : base(helper)
        {
        }

        private Shard.Entities NewEntities(bool rememberingEntities)
        {
            return new Shard.Entities(
                NoLogger.Instance,
                rememberingEntities: rememberingEntities,
                verboseDebug: false,
                failOnIllegalTransition: true);
        }

        [Fact]
        public void Entities_should_start_empty()
        {
            var entities = NewEntities(rememberingEntities: false);
            entities.ActiveEntityIds.Should().BeEmpty();
            entities.Count.Should().Be(0);
            entities.ActiveEntities.Should().BeEmpty();
        }

        [Fact]
        public void Entities_should_set_already_remembered_entities_to_state_RememberedButNotStarted()
        {
            var entities = NewEntities(rememberingEntities: true);
            var ids = ImmutableHashSet.Create("a", "b", "c");
            entities.AlreadyRemembered(ids);
            entities.ActiveEntities.Should().BeEmpty();
            entities.Count.Should().Be(3);
            foreach (var id in ids)
            {
                entities.EntityState(id).Should().BeOfType<Shard.RememberedButNotCreated>();
            }
        }

        [Fact]
        public void Entities_should_set_state_to_remembering_start()
        {
            var entities = NewEntities(rememberingEntities: true);
            entities.RememberingStart("a", null);
            entities.EntityState("a").Should().Be(new Shard.RememberingStart(ImmutableHashSet<IActorRef>.Empty));
            entities.PendingRememberedEntitiesExist.Should().BeTrue();
            var (starts, stops) = entities.PendingRememberEntities;
            starts.Keys.Should().Contain("a");
            stops.Should().BeEmpty();

            // also verify removal from pending once it starts
            entities.AddEntity("a", ActorRefs.Nobody);
            entities.PendingRememberedEntitiesExist.Should().BeFalse();
            entities.PendingRememberEntities.Start.Should().BeEmpty();
        }

        [Fact]
        public void Entities_should_set_state_to_remembering_stop()
        {
            var entities = NewEntities(rememberingEntities: true);
            entities.RememberingStart("a", null); // need to go through remembering start to become active
            entities.AddEntity("a", ActorRefs.Nobody); // need to go through active to passivate
            entities.EntityPassivating("a"); // need to go through passivate to remember stop
            entities.RememberingStop("a");
            entities.EntityState("a").Should().BeOfType<Shard.RememberingStop>();
            entities.PendingRememberedEntitiesExist.Should().BeTrue();
            var (starts, stops) = entities.PendingRememberEntities;
            stops.Should().Contain("a");
            starts.Should().BeEmpty();

            // also verify removal from pending once it stops
            entities.RemoveEntity("a");
            entities.PendingRememberedEntitiesExist.Should().BeFalse();
            entities.PendingRememberEntities.Stop.Should().BeEmpty();
        }

        [Fact]
        public void Entities_should_fully_remove_an_entity()
        {
            var entities = NewEntities(rememberingEntities: true);
            entities.RememberingStart("a", null); // need to go through remembering start to become active
            entities.AddEntity("a", ActorRefs.Nobody); // need to go through active to passivate
            entities.EntityPassivating("a"); // needs to go through passivating to be removed
            entities.RememberingStop("a"); // need to go through remembering stop to become active
            entities.RemoveEntity("a");
            entities.EntityState("a").Should().BeOfType<Shard.NoState>();
            entities.ActiveEntities.Should().BeEmpty();
            entities.ActiveEntityIds.Should().BeEmpty();
        }

        [Fact]
        public void Entities_should_add_an_entity_as_active()
        {
            var entities = NewEntities(rememberingEntities: false);
            var @ref = ActorRefs.Nobody;
            entities.AddEntity("a", @ref);
            entities.EntityState("a").Should().Be(new Shard.Active(@ref));
        }

        [Fact]
        public void Entities_should_look_up_actor_ref_by_id()
        {
            var entities = NewEntities(rememberingEntities: false);
            var @ref = ActorRefs.Nobody;
            entities.AddEntity("a", @ref);
            entities.EntityId(@ref).Should().Be("a");
        }

        [Fact]
        public void Entities_should_set_state_to_passivating()
        {
            var entities = NewEntities(rememberingEntities: false);
            var @ref = ActorRefs.Nobody;
            entities.AddEntity("a", @ref);
            entities.EntityPassivating("a");
            entities.EntityState("a").Should().Be(new Shard.Passivating(@ref));
        }
    }
}
