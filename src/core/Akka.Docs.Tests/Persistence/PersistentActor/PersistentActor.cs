﻿// -----------------------------------------------------------------------
//  <copyright file="PersistentActor.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Persistence;

namespace DocsExamples.Persistence.PersistentActor;

public static class PersistentActorSpec
{
    #region PersistActor

    public class Cmd
    {
        public Cmd(string data)
        {
            Data = data;
        }

        public string Data { get; }
    }

    public class Evt
    {
        public Evt(string data)
        {
            Data = data;
        }

        public string Data { get; }
    }

    public class ExampleState
    {
        private readonly ImmutableList<string> _events;

        public ExampleState(ImmutableList<string> events)
        {
            _events = events;
        }

        public ExampleState() : this(ImmutableList.Create<string>())
        {
        }

        public int Size => _events.Count;

        public ExampleState Updated(Evt evt)
        {
            return new ExampleState(_events.Add(evt.Data));
        }

        public override string ToString()
        {
            return string.Join(", ", _events.Reverse());
        }
    }

    public class PersistentActor : UntypedPersistentActor
    {
        private ExampleState _state = new();

        private int NumEvents => _state.Size;

        public override string PersistenceId { get; } = "sample-id-1";

        private void UpdateState(Evt evt)
        {
            _state = _state.Updated(evt);
        }

        protected override void OnRecover(object message)
        {
            switch (message)
            {
                case Evt evt:
                    UpdateState(evt);
                    break;
                case SnapshotOffer snapshot when snapshot.Snapshot is ExampleState state:
                    _state = state;
                    break;
            }
        }

        protected override void OnCommand(object message)
        {
            switch (message)
            {
                case Cmd cmd:
                    Persist(new Evt($"{cmd.Data}-{NumEvents}"), UpdateState);
                    Persist(new Evt($"{cmd.Data}-{NumEvents + 1}"), evt =>
                    {
                        UpdateState(evt);
                        Context.System.EventStream.Publish(evt);
                    });
                    break;
                case "snap":
                    SaveSnapshot(_state);
                    break;
                case "print":
                    Console.WriteLine(_state);
                    break;
            }
        }
    }

    #endregion
}