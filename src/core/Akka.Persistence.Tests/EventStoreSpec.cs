//-----------------------------------------------------------------------
// <copyright file="EventStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Tests
{
    public class EventStoreSpec : PersistenceSpec
    {
        #region internal classes

        public interface IEvent { }

        public sealed class Incremented : IEvent
        {
            public Incremented(int delta)
            {
                Delta = delta;
            }

            public int Delta { get; }
        }

        #endregion

        public const string Pid = "p-1";
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private readonly PersistenceExtension _persistence;
        private readonly IEventStore<int, IEvent> _store;

        public EventStoreSpec(ITestOutputHelper output) : base(output: output)
        {
            _persistence = Persistence.Instance.Apply(Sys);
            _store = _persistence.GetEventStore<int, IEvent>(Pid);
        }

        [Fact]
        public void EventStore_must_be_able_to_persist_events() => Run(async () =>
        {
            await _store.PersistEvent(new Incremented(1));
        });

        [Fact]
        public void EventStore_must_be_able_to_replay_events() => Run(async () =>
        {

        });

        [Fact]
        public void EventStore_must_delete_events() => Run(async () =>
        {

        });

        private void Run(Func<Task> fn) => Task.Run(fn).Wait(Timeout);
    }
}