// //-----------------------------------------------------------------------
// // <copyright file="GetChildBenchmark.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using FluentAssertions;

namespace Akka.Benchmarks.Actor
{
    /// <summary>
    /// Used to measure how quickly an <see cref="IActorContext.Child"/> call can be executed in the wild.
    /// </summary>
    [Config(typeof(MicroBenchmarkConfig))]
    public class GetChildBenchmark
    {
        #region classes
        public sealed class Child : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                
            }
        }
        
        public sealed class ActorWithChild : UntypedActor
        {
            public sealed class Get
            {
                public Get(string name)
                {
                    Name = name;
                }

                public string Name { get; }
            }

            public sealed class Create
            {
                public Create(string name)
                {
                    Name = name;
                }

                public string Name { get; }
            }

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case Get g:
                    {
                        var child = Context.Child(g.Name);
                        Sender.Tell(child);
                        break;
                    }
                    case Create c:
                    {
                        var child = Context.ActorOf(Props.Create(() => new Child()), c.Name);
                        Sender.Tell(child);
                        break;
                    }
                    default:
                        Unhandled(message);
                        break;
                }
            }
        }
        
        #endregion
        
        private TimeSpan _timeout;
        private ActorSystem _system;
        private IActorRef _parentActor;

        private ActorWithChild.Get _getMessage = new ActorWithChild.Get("foo");
        private ActorWithChild.Create _createMessage = new ActorWithChild.Create("foo");

        private ActorCell _cell;
        
        [GlobalSetup]
        public async Task Setup()
        {
            _timeout = TimeSpan.FromMinutes(1);
            _system = ActorSystem.Create("system");
            _parentActor = _system.ActorOf(Props.Create(() => new ActorWithChild()), "parent");
            await _parentActor.Ask<IActorRef>(_createMessage, _timeout);
            
            _cell = _parentActor.As<ActorRefWithCell>().Underlying.As<ActorCell>();
        }

        [Benchmark]
        public void ResolveChild()
        {
            _cell.TryGetSingleChild(_getMessage.Name, out var child);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _system.Terminate().Wait();
        }
    }
}