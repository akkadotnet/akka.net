//-----------------------------------------------------------------------
// <copyright file="DispatchersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Routing;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Tests.Dispatch
{
    public class DispatchersSpec : AkkaSpec
    {

        #region Test Config
        public static Config DispatcherConfiguration
        {
            get { return ConfigurationFactory.ParseString(@"
                myapp{
                    mydispatcher {
                        throughput = 17
                    }
                    my-pinned-dispatcher {
                        type = PinnedDispatcher
                    }
                    my-fork-join-dispatcher{
                        type = ForkJoinDispatcher
                        throughput = 60
                        dedicated-thread-pool.thread-count = 4
                    }
                    my-other-fork-join-dispatcher{
                        type = ForkJoinDispatcher
                        dedicated-thread-pool.thread-count = 3
                        dedicated-thread-pool.deadlock-timeout = 3s
                    }
                    my-synchronized-dispather{
                        type = SynchronizedDispatcher
                        throughput = 10
                    }
                }
                akka.actor.deployment{
                    /echo1{
                        dispatcher = myapp.mydispatcher
                    }
                    /echo2{
                        dispatcher = myapp.my-fork-join-dispatcher
                    }
                    /pool1{
                        router = random-pool
                        nr-of-instances = 3
                        pool-dispatcher = ${myapp.my-fork-join-dispatcher}
                    }
                }
            "); }
        }

        #endregion

        public DispatchersSpec(ITestOutputHelper helper) : base(DispatcherConfiguration, helper) { }

        

        #region Tests

        [Fact]
        public void Dispatchers_must_use_defined_properties()
        {
            var dispatcher = Lookup("myapp.mydispatcher");
            dispatcher.Throughput.ShouldBe(17);
        }

        [Fact]
        public void Dispatchers_must_use_specific_id()
        {
            var dispatcher = Lookup("myapp.mydispatcher");
            dispatcher.Id.ShouldBe("myapp.mydispatcher");
        }

        [Fact]
        public void Dispatchers_must_complain_about_missing_Config()
        {
            Intercept<ConfigurationException>(() => Lookup("myapp.other-dispatcher"));
        }

        [Fact]
        public void Dispatchers_must_have_one_and_only_one_default_dispatcher()
        {
            var dispatcher = Lookup(Dispatchers.DefaultDispatcherId);
            dispatcher.ShouldBeSame(Sys.Dispatchers.DefaultGlobalDispatcher);
            //dispatcher.ShouldBeSame(Sys.Dispatcher); //todo: add ActorSystem.Dispatcher?
        }

        [Fact]
        public void Dispatchers_must_throw_ConfigurationException_if_type_doesnt_exist()
        {
            Intercept<ConfigurationException>(() =>
            {
                From(ConfigurationFactory.ParseString(@"
                    id = invalid-dispatcher  
                    type = doesntexist    
                ").WithFallback(Sys.Dispatchers.DefaultDispatcherConfig));
            });
        }

        [Fact]
        public void Dispatchers_must_provide_lookup_of_dispatchers_by_id()
        {
            var d1 = Lookup("myapp.mydispatcher");
            var d2 = Lookup("myapp.mydispatcher");
            d1.ShouldBeSame(d2);
        }

        [Fact]
        public void Dispatchers_must_be_used_when_configured_in_explicit_deployments()
        {
            var actor = Sys.ActorOf(Props.Create<DispatcherNameEcho>().WithDispatcher("myapp.mydispatcher"));

            AwaitAssert(() =>
            {
                actor.Tell("what's in a name?");
                var expected = "myapp.mydispatcher";
                var actual = ExpectMsg<string>(TimeSpan.FromMilliseconds(50));
                actual.ShouldBe(expected);
            });
        }

        [Fact]
        public void Dispatchers_must_be_used_in_deployment_configuration()
        {
            var actor = Sys.ActorOf(Props.Create<DispatcherNameEcho>(), "echo1");

            AwaitAssert(() =>
            {
                actor.Tell("what's in a name?");
                var expected = "myapp.mydispatcher";
                var actual = ExpectMsg<string>(TimeSpan.FromMilliseconds(50));
                actual.ShouldBe(expected);
            });
        }

        [Fact]
        public void Dispatchers_must_be_used_in_deployment_configuration_and_trumps_code()
        {
            var actor = Sys.ActorOf(Props.Create<DispatcherNameEcho>().WithDispatcher("my-pinned-dispatcher"), "echo2");

            AwaitAssert(() =>
            {
                actor.Tell("what's in a name?");
                var expected = "myapp.my-fork-join-dispatcher";
                var actual = ExpectMsg<string>(TimeSpan.FromMilliseconds(200));
                actual.ShouldBe(expected);
            });
        }

        [Fact]
        public void Dispatchers_must_use_pool_dispatcher_router_of_deployment_config()
        {
            var pool = Sys.ActorOf(Props.Create<DispatcherNameEcho>().WithRouter(FromConfig.Instance), "pool1");

            AwaitAssert(() => {
                pool.Tell(new Identify(null));
                var routee = ExpectMsg<ActorIdentity>().Subject;
                routee.Tell("what's the name?");
                var expected = "akka.actor.deployment./pool1.pool-dispatcher";
                var actual = ExpectMsg<string>(TimeSpan.FromMilliseconds(50));
                actual.ShouldBe(expected);
            });
        }

        [Fact]
        public void Dispatchers_must_return_separate_instances_of_dispatchers_with_different_ids()
        {
            var d1 = Lookup("myapp.my-fork-join-dispatcher");
            var d2 = Lookup("myapp.my-fork-join-dispatcher");
            var d3 = Lookup("myapp.my-other-fork-join-dispatcher");
            d1.ShouldBeSame(d2);
            d1.ShouldNotBeSame(d3);
        }


        [Fact]
        public void PinnedDispatchers_must_return_new_instance_each_time()
        {
            var d1 = Lookup("myapp.my-pinned-dispatcher");
            var d2 = Lookup("myapp.my-pinned-dispatcher");
            d1.ShouldNotBeSame(d2);
        }

        #endregion

        #region Support methods and classes

        class DispatcherNameEcho : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Sender.Tell(Context.Props.Dispatcher);
            }
        }

        MessageDispatcher Lookup(string dispatcherId)
        {
            return Sys.Dispatchers.Lookup(dispatcherId);
        }

        MessageDispatcher From(Config config)
        {
            return Sys.Dispatchers.From(config);
        }

        #endregion
    }
}

