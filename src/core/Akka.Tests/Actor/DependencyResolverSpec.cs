#region copyright
//-----------------------------------------------------------------------
// <copyright file="DependencyResolverSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Actor
{
    public sealed class CustomDependencyResolver : DependencyResolver
    {
        public override void ConfigureDependencies(ExtendedActorSystem system, IServiceCollection services)
        {
            base.ConfigureDependencies(system, services);

            services.AddTransient<DependencyResolverSpec.ITransientActorDependency, DependencyResolverSpec.TransientActorDependency>();
            services.AddScoped<DependencyResolverSpec.IScopedActorDependency, DependencyResolverSpec.ScopedActorDependency>();
            services.AddSingleton<DependencyResolverSpec.ISingletonActorDependency, DependencyResolverSpec.SingletonActorDependency>();
            
            // actors
            services.AddTransient<DependencyResolverSpec.TestActor2>();
            services.AddTransient<DependencyResolverSpec.TestActor3>();
            services.AddTransient<DependencyResolverSpec.TestActor4>();
        }
    }
    
    public class DependencyResolverSpec : AkkaSpec
    {
        private static readonly Config TestConfig = @"
            akka.loglevel = DEBUG
            akka.dependency-resolver-class = ""Akka.Tests.Actor.CustomDependencyResolver, Akka.Tests""";
        
        public DependencyResolverSpec(ITestOutputHelper output) : base(output, TestConfig)
        {
        }

        [Fact]
        public void Props_dynamic_syntax_must_work_with_actors_having_default_constructors()
        {
            // this actor has default constructor but props is created using dynamic syntax
            // (it will be resolved using System.ServiceResolver).
            var props = Props.Create<TestActor1>();
            var actor = Sys.ActorOf(props);
            
            actor.Tell("hello");
            ExpectMsg("hello");
        }

        [Fact]
        public void Props_dynamic_syntax_must_work_with_actors_for_transient_scope()
        {
            var props = Props.Create<TestActor2>();
            var actor = Sys.ActorOf(props);

            actor.Tell("A");
            actor.Tell("B");
            
            ExpectMsg("Transient A 1");
            ExpectMsg("Transient B 1");
            
            actor.Tell("boom"); // trigger actor reset
            actor.Tell("C");
            actor.Tell("D");
            
            // after reset transient scoped dependency should be recreated - counter incremented
            ExpectMsg("Transient C 2");
            ExpectMsg("Transient D 2");
        }
        
        [Fact]
        public void Props_dynamic_syntax_must_work_with_actors_for_scoped_scope()
        {
            var props = Props.Create<TestActor3>();
            var actor1 = Sys.ActorOf(props);

            actor1.Tell("A");
            actor1.Tell("B");
            
            ExpectMsg("Scoped A 1");
            ExpectMsg("Scoped B 1");
            
            actor1.Tell("boom"); // trigger actor reset
            actor1.Tell("C");
            actor1.Tell("D");
            
            // after reset scoped dependency should remain the same
            ExpectMsg("Scoped C 1");
            ExpectMsg("Scoped D 1");
            
            var actor2 = Sys.ActorOf(props);
            
            // scoped dependency is unique per ActorContext - so a new actor must have its new instance
            actor2.Tell("E");
            actor2.Tell("F");
            
            ExpectMsg("Scoped E 2");
            ExpectMsg("Scoped F 2");
        }
        
        [Fact]
        public void Props_dynamic_syntax_must_work_with_actors_for_singleton_scope()
        {
            // singleton dependencies are only singletons in scope of actor system
            using (var sys2 = ActorSystem.Create("sys2", TestConfig.WithFallback(TestKit.Xunit2.TestKit.DefaultConfig)))
            {
                var props = Props.Create<TestActor4>();
                var a1 = Sys.ActorOf(props);
                var b1 = Sys.ActorOf(props);
                var p1 = this.CreateTestProbe();

                var a2 = sys2.ActorOf(props);
                var b2 = sys2.ActorOf(props);
                var p2 = new TestProbe(sys2, new XunitAssertions());
                
                // first actor system
                a1.Tell("A", p1.Ref);
                p1.ExpectMsg("Singleton A AkkaSpec");
                b1.Tell("B", p1.Ref);
                p1.ExpectMsg("Singleton B AkkaSpec");
                a1.Tell("boom");
                a1.Tell("C", p1.Ref);
                p1.ExpectMsg("Singleton C AkkaSpec");
                
                // second actor system
                a2.Tell("A", p2.Ref);
                p2.ExpectMsg("Singleton A sys2");
                b2.Tell("B", p2.Ref);
                p2.ExpectMsg("Singleton B sys2");
                a2.Tell("boom");
                a2.Tell("C", p2.Ref);
                p2.ExpectMsg("Singleton C sys2");
            }
        }

        public interface ITransientActorDependency
        {
            string GetResponse(string message);
        }
        
        public interface IScopedActorDependency
        {
            string GetResponse(string message);
        }
        
        public interface ISingletonActorDependency
        {
            string GetResponse(string message);
        }
        
        public class TransientActorDependency : ITransientActorDependency
        {
            private static int _incarnation = 0;
            public static int Incarnation => Volatile.Read(ref _incarnation);
            
            public TransientActorDependency()
            {
                Interlocked.Increment(ref _incarnation);
            }

            public string GetResponse(string message) => $"Transient {message} {_incarnation}";
        }
        
        public class ScopedActorDependency : IScopedActorDependency
        {
            private static int _incarnation = 0;
            public static int Incarnation => Volatile.Read(ref _incarnation);

            public ScopedActorDependency()
            {
                Interlocked.Increment(ref _incarnation);
            }

            public string GetResponse(string message) => $"Scoped {message} {_incarnation}";
        }
        
        public class SingletonActorDependency : ISingletonActorDependency
        {
            private readonly string _name;

            public SingletonActorDependency(ActorSystem system)
            {
                _name = system.Name;
            }

            public string GetResponse(string message) => $"Singleton {message} {_name}";
        }
        
        public class TestActor1 : ReceiveActor
        {
            public TestActor1()
            {
                ReceiveAny(m => Sender.Tell(m));
            }
        }
        
        public class TestActor2 : ActorBase
        {
            private readonly ITransientActorDependency _dependency;

            public TestActor2(ITransientActorDependency dependency)
            {
                _dependency = dependency;
            }

            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case "boom": throw new Exception("BOOM!");
                    case string s: 
                        Sender.Tell(_dependency.GetResponse(s));
                        return true;
                    default: return false;
                }
            }
        }
        
        public class TestActor3 : ActorBase
        {
            private readonly IScopedActorDependency _dependency;

            public TestActor3(IScopedActorDependency dependency)
            {
                _dependency = dependency;
            }
            
            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case "boom": throw new Exception("BOOM!");
                    case string s: 
                        Sender.Tell(_dependency.GetResponse(s));
                        return true;
                    default: return false;
                }
            }
        }
        
        public class TestActor4 : ActorBase
        {
            private readonly ISingletonActorDependency _dependency;

            public TestActor4(ISingletonActorDependency dependency)
            {
                _dependency = dependency;
            }
            
            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case "boom": throw new Exception("BOOM!");
                    case string s: 
                        Sender.Tell(_dependency.GetResponse(s));
                        return true;
                    default: return false;
                }
            }
        }
    }
}