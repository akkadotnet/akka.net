//-----------------------------------------------------------------------
// <copyright file="GetChildBenchmark.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;
using FluentAssertions;
using static Akka.Benchmarks.Configurations.BenchmarkCategories;

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

            protected override void PreStart()
            {
                if (Self.Path.Name.Length > 1)
                {
                    // recursively create children using the previous name segments
                    var nextName = new string(Self.Path.Name.Skip(1).ToArray());
                    Context.ActorOf(Props.Create(() => new Child()), nextName);
                }
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

        private readonly ActorWithChild.Get _getMessage = new("food");
        private readonly ActorWithChild.Create _createMessage = new("food");

        private IActorContext _cell;
        private RepointableActorRef _repointableActorRef;
        private LocalActorRef _localActorRef;
        private VirtualPathContainer _virtualPathContainer;

        private readonly List<string> _rpChildQueryPath = ["food", "ood", "od"];
        private readonly List<string> _lclChildQueryPath = ["ood", "od", "d"];
        private readonly List<string> _virtualPathContainerQueryPath = ["foo"];
        
        [GlobalSetup]
        public async Task Setup()
        {
            _timeout = TimeSpan.FromMinutes(1);
            _system = ActorSystem.Create("system");
            _parentActor = _system.ActorOf(Props.Create(() => new ActorWithChild()), "parent");
            _localActorRef = (LocalActorRef)await _parentActor.Ask<IActorRef>(_createMessage, _timeout);
            
            _cell = _parentActor.As<ActorRefWithCell>().Underlying.As<ActorCell>();
            _repointableActorRef = (RepointableActorRef)_parentActor;

            var exp = _system.As<ExtendedActorSystem>();

            var vPath = exp.Guardian.Path / "testTemp";
            _virtualPathContainer =
                new VirtualPathContainer(exp.Provider, vPath, exp.Guardian, exp.Log);

            _virtualPathContainer.AddChild("foo",
                new EmptyLocalActorRef(exp.Provider, vPath / "foo", exp.EventStream));
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public void ResolveChild()
        {
            _cell.Child(_getMessage.Name);
        }
        
        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public void Resolve3DeepChildRepointableActorRef()
        {
            _repointableActorRef.GetChild(_rpChildQueryPath);
        }
        
        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public void Resolve3DeepChildLocalActorRef()
        {
            _localActorRef.GetChild(_lclChildQueryPath);
        }
        
        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public void ResolveVirtualPathContainer()
        {
            _virtualPathContainer.GetChild(_virtualPathContainerQueryPath);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _system.Terminate().Wait();
        }
    }
}
