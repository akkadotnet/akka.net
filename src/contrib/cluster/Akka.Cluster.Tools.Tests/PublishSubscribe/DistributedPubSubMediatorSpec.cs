//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubMediatorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tests.MultiNode;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.TestKit;
using Xunit;

namespace Akka.Cluster.Tools.Tests.PublishSubscribe
{
    public class DistributedPubSubMediatorSpecConfig : MultiNodeConfig
    {
        public readonly RoleName First;
        public readonly RoleName Second;
        public readonly RoleName Third;

        public DistributedPubSubMediatorSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.auto-down-unreachable-after = 0s
                akka.cluster.pub-sub.max-delta-elements = 500
            ").WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class DistributedPubSubMediatorNode1 : DistributedPubSubMediatorSpec { }
    public class DistributedPubSubMediatorNode2 : DistributedPubSubMediatorSpec { }
    public class DistributedPubSubMediatorNode3 : DistributedPubSubMediatorSpec { }

    public abstract class DistributedPubSubMediatorSpec : MultiNodeClusterSpec
    {
        #region setup 

        [Serializable]
        public sealed class Whisper
        {
            public readonly string Path;
            public readonly object Message;

            public Whisper(string path, object message)
            {
                Path = path;
                Message = message;
            }
        }

        [Serializable]
        public sealed class Talk
        {
            public readonly string Path;
            public readonly object Message;

            public Talk(string path, object message)
            {
                Path = path;
                Message = message;
            }
        }

        [Serializable]
        public sealed class TalkToOthers
        {
            public readonly string Path;
            public readonly object Message;

            public TalkToOthers(string path, object message)
            {
                Path = path;
                Message = message;
            }
        }

        [Serializable]
        public sealed class Shout
        {
            public readonly string Topic;
            public readonly object Message;

            public Shout(string topic, object message)
            {
                Topic = topic;
                Message = message;
            }
        }

        [Serializable]
        public sealed class ShoutToGroup
        {
            public readonly string Topic;
            public readonly object Message;

            public ShoutToGroup(string topic, object message)
            {
                Topic = topic;
                Message = message;
            }
        }

        [Serializable]
        public sealed class JoinGroup
        {
            public readonly string Topic;
            public readonly string Group;

            public JoinGroup(string topic, string @group)
            {
                Topic = topic;
                Group = @group;
            }
        }

        [Serializable]
        public sealed class ExitGroup
        {
            public readonly string Topic;
            public readonly string Group;

            public ExitGroup(string topic, string @group)
            {
                Topic = topic;
                Group = @group;
            }
        }

        public class TestChatUser : ReceiveActor
        {
            public TestChatUser(IActorRef mediator, IActorRef testActorRef)
            {
                Receive<Whisper>(w => mediator.Tell(new Send(w.Path, w.Message, true)));
                Receive<Talk>(t => mediator.Tell(new SendToAll(t.Path, t.Message)));
                Receive<TalkToOthers>(t => mediator.Tell(new SendToAll(t.Path, t.Message, true)));
                Receive<Shout>(s => mediator.Tell(new Publish(s.Topic, s.Message)));
                Receive<ShoutToGroup>(s => mediator.Tell(new Publish(s.Topic, s.Message, true)));
                Receive<JoinGroup>(j => mediator.Tell(new Subscribe(j.Topic, Self, j.Group)));
                Receive<ExitGroup>(j => mediator.Tell(new Unsubscribe(j.Topic, Self, j.Group)));
                ReceiveAny(msg => testActorRef.Tell(msg));
            }
        }

        public class Publisher : ReceiveActor
        {
            public Publisher()
            {
                var mediator = DistributedPubSub.Get(Context.System).Mediator;
                Receive<string>(input => mediator.Tell(new Publish("content", input.ToUpperInvariant())));
            }
        }

        public class Subscriber : UntypedActor
        {
            private readonly IActorRef _mediator;
            private readonly ILoggingAdapter _log;

            public Subscriber()
            {
                _log = Context.GetLogger();
                _mediator = DistributedPubSub.Get(Context.System).Mediator;
                _mediator.Tell(new Subscribe("content", Self));
            }

            protected override void OnReceive(object message)
            {
                var ack = message as SubscribeAck;
                if (ack != null && ack.Subscribe.Topic == "content" && ack.Subscribe.Ref.Equals(Self))
                {
                    Context.Become(Ready);
                }
            }

            private void Ready(object message)
            {
                if (message is string) _log.Info("Got {0}", message);
            }
        }

        private readonly RoleName _first;
        private readonly RoleName _second;
        private readonly RoleName _third;

        private readonly ConcurrentDictionary<string, IActorRef> _chatUsers = new ConcurrentDictionary<string, IActorRef>();

        protected DistributedPubSubMediatorSpec() : this(new DistributedPubSubMediatorSpecConfig())
        {
        }

        protected DistributedPubSubMediatorSpec(DistributedPubSubMediatorSpecConfig config) : base(config)
        {
            _first = config.First;
            _second = config.Second;
            _third = config.Third;
        }

        public IActorRef Mediator { get { return DistributedPubSub.Get(Sys).Mediator; } }

        private IActorRef CreateChatUser(string name)
        {
            var a = Sys.ActorOf(Props.Create(() => new TestChatUser(Mediator, TestActor)), name);
            _chatUsers.TryAdd(name, a);
            return a;
        }

        private IActorRef ChatUser(string name)
        {
            IActorRef a;
            return _chatUsers.TryGetValue(name, out a) ? a : ActorRefs.Nobody;
        }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Join(Node(to).Address);
                CreateMediator();
            }, from);
            EnterBarrier(from.Name + "-joined");
        }

        private void CreateMediator()
        {
            var m = DistributedPubSub.Get(Sys).Mediator;
        }

        private void AwaitCount(int expected)
        {
            AwaitAssert(() =>
            {
                Mediator.Tell(Count.Instance);
                Assert.Equal(expected, ExpectMsg<int>());
            });
        }

        #endregion

        //[MultiNodeFact(Skip = "TODO")]
        public void DistributedPubSubMediator_should_startup_2_nodes_cluster()
        {
            Within(TimeSpan.FromSeconds(15), () =>
            {
                Join(_first, _first);
                Join(_second, _first);
                EnterBarrier("after-1");
            });
        }

        //[MultiNodeFact(Skip = "TODO")]
        public void DistributedPubSubMediator_should_keep_track_of_added_users()
        {
            DistributedPubSubMediator_should_startup_2_nodes_cluster();

            Within(TimeSpan.FromSeconds(15), () =>
            {
                RunOn(() =>
                {
                    var u1 = CreateChatUser("u1");
                    Mediator.Tell(new Put(u1));
                    var u2 = CreateChatUser("u2");
                    Mediator.Tell(new Put(u2));

                    AwaitCount(2);

                    // send to actor at the same node
                    u1.Tell(new Whisper("/user/u2", "hello"));
                    ExpectMsg("hello");
                    Assert.Equal(u2, LastSender);
                }, _first);

                RunOn(() =>
                {
                    var u3 = CreateChatUser("u3");
                    Mediator.Tell(new Put(u3));
                }, _second);

                RunOn(() =>
                {
                    AwaitCount(3);
                }, _first, _second);
                EnterBarrier("3-registered");

                RunOn(() =>
                {
                    var u4 = CreateChatUser("u4");
                    Mediator.Tell(new Put(u4));
                }, _second);

                RunOn(() =>
                {
                    AwaitCount(4);
                }, _first, _second);
                EnterBarrier("4-registered");

                RunOn(() =>
                {
                    // send to an actor on another node
                    ChatUser("u1").Tell(new Whisper("/user/u4", "hi there"));
                }, _first);

                RunOn(() =>
                {
                    ExpectMsg("hi there");
                    Assert.Equal("u4", LastSender.Path.Name);
                }, _second);
                EnterBarrier("after-2");
            });
        }

        //[MultiNodeFact(Skip = "TODO")]
        public void DistributedPubSubMediator_should_replicate_users_to_new_node()
        {
            DistributedPubSubMediator_should_keep_track_of_added_users();

            Within(TimeSpan.FromSeconds(20), () =>
            {
                Join(_third, _first);
                RunOn(() =>
                {
                    var u5 = CreateChatUser("u5");
                    Mediator.Tell(new Put(u5));
                }, _third);

                AwaitCount(5);
                EnterBarrier("5-registered");

                RunOn(() =>
                {
                    ChatUser("u5").Tell(new Whisper("/user/u4", "go"));
                }, _third);

                RunOn(() =>
                {
                    ExpectMsg("go");
                    Assert.Equal("u4", LastSender.Path.Name);
                }, _second);
                EnterBarrier("after-3");
            });
        }

        //[MultiNodeFact(Skip = "TODO")]
        public void DistributedPubSubMediator_should_keep_track_of_removed_users()
        {
            DistributedPubSubMediator_should_replicate_users_to_new_node();

            Within(TimeSpan.FromSeconds(15), () =>
            {
                var u6 = CreateChatUser("u6");
                Mediator.Tell(new Put(u6));
            });
            AwaitCount(6);
            EnterBarrier("6-registered");

            RunOn(() =>
            {
                Mediator.Tell(new Remove("/user/u6"));
            }, _first);
            AwaitCount(5);
            EnterBarrier("after-4");
        }

        //[MultiNodeFact(Skip = "TODO")]
        public void DistributedPubSubMediator_should_remove_terminated_users()
        {
            DistributedPubSubMediator_should_keep_track_of_removed_users();

            Within(TimeSpan.FromSeconds(5), () =>
            {
                RunOn(() =>
                {
                    ChatUser("u3").Tell(PoisonPill.Instance);
                }, _second);

                AwaitCount(4);
                EnterBarrier("after-5");
            });
        }

        //[MultiNodeFact(Skip = "TODO")]
        public void DistributedPubSubMediator_should_publish()
        {
            DistributedPubSubMediator_should_remove_terminated_users();

            Within(TimeSpan.FromSeconds(15), () =>
            {
                RunOn(() =>
                {
                    var u7 = CreateChatUser("u7");
                    Mediator.Tell(new Put(u7));
                }, _first, _second);
                AwaitCount(6);
                EnterBarrier("7-registered");

                RunOn(() =>
                {
                    ChatUser("u5").Tell(new Talk("/user/u7", "hi"));
                }, _third);

                RunOn(() =>
                {
                    ExpectMsg("hi");
                    Assert.Equal("u7", LastSender.Path.Name);
                }, _first, _second);

                RunOn(() =>
                {
                    ExpectNoMsg(TimeSpan.FromSeconds(3));
                }, _third);
                EnterBarrier("after-6");
            });
        }

        //[MultiNodeFact(Skip = "TODO")]
        public void DistributedPubSubMediator_should_publish_to_topic()
        {
            DistributedPubSubMediator_should_publish();

            Within(TimeSpan.FromSeconds(15), () =>
            {
                RunOn(() =>
                {
                    var s8 = new Subscribe("topic1", CreateChatUser("u8"));
                    Mediator.Tell(s8);
                    ExpectMsg<SubscribeAck>(x => x.Subscribe.Equals(s8));
                    var s9 = new Subscribe("topic1", CreateChatUser("u9"));
                    Mediator.Tell(s9);
                    ExpectMsg<SubscribeAck>(x => x.Subscribe.Equals(s9));
                }, _first);

                RunOn(() =>
                {
                    var s10 = new Subscribe("topic1", CreateChatUser("u10"));
                    Mediator.Tell(s10);
                    ExpectMsg<SubscribeAck>(x => x.Subscribe.Equals(s10));
                }, _second);

                // one topic on two nodes
                AwaitCount(8);
                EnterBarrier("topic1-registered");

                RunOn(() =>
                {
                    ChatUser("u5").Tell(new Shout("topic1", "hello all"));
                }, _third);

                RunOn(() =>
                {
                    var names = ReceiveWhile(x => "hello all".Equals(x) ? LastSender.Path.Name : null, msgs: 2);
                    Assert.True(names.All(x => x == "u8" || x == "u9"));
                }, _first);

                RunOn(() =>
                {
                    ExpectMsg("hello all");
                    Assert.Equal("u10", LastSender.Path.Name);
                }, _second);

                RunOn(() =>
                {
                    ExpectNoMsg(TimeSpan.FromSeconds(2));
                }, _third);
                EnterBarrier("after-7");
            });
        }

        //[MultiNodeFact(Skip = "TODO")]
        public void DistributedPubSubMediator_should_demonstrate_usage()
        {
            DistributedPubSubMediator_should_publish_to_topic();

            Within(TimeSpan.FromSeconds(15), () =>
            {
                RunOn(() =>
                {
                    Sys.ActorOf(Props.Create<Subscriber>(), "subscriber1");
                }, _first);

                RunOn(() =>
                {
                    Sys.ActorOf(Props.Create<Subscriber>(), "subscriber2");
                    Sys.ActorOf(Props.Create<Subscriber>(), "subscriber3");
                }, _second);

                RunOn(() =>
                {
                    var publisher = Sys.ActorOf(Props.Create<Publisher>(), "publisher");
                    AwaitCount(10);
                    // after a while the subscriptions are replicated
                    publisher.Tell("hello");
                }, _third);
                EnterBarrier("after-8");
            });
        }

        //[MultiNodeFact(Skip = "TODO")]
        public void DistributedPubSubMediator_should_SendAll_to_all_other_nodes()
        {
            DistributedPubSubMediator_should_demonstrate_usage();

            Within(TimeSpan.FromSeconds(15), () =>
            {
                RunOn(() =>
                {
                    var u11 = CreateChatUser("u11");
                    Mediator.Tell(new Put(u11));
                }, _first, _second, _third);
                AwaitCount(13);
                EnterBarrier("11-registered");

                RunOn(() =>
                {
                    ChatUser("u5").Tell(new TalkToOthers("/user/u11", "hi"));
                }, _third);

                RunOn(() =>
                {
                    ExpectMsg("hi");
                    Assert.Equal("u1", LastSender.Path.Name);
                }, _first, _second);

                RunOn(() =>
                {
                    ExpectNoMsg(TimeSpan.FromSeconds(2));
                }, _third);
                EnterBarrier("after-11");
            });
        }

        //[MultiNodeFact(Skip = "TODO")]
        public void DistributedPubSubMediator_should_send_one_message_to_each_group()
        {
            DistributedPubSubMediator_should_SendAll_to_all_other_nodes();

            Within(TimeSpan.FromSeconds(20), () =>
            {
                RunOn(() =>
                {
                    var u12 = CreateChatUser("u12");
                    u12.Tell(new JoinGroup("topic2", "group1"));
                    ExpectMsg<SubscribeAck>(s => s.Subscribe.Topic == "topic2"
                                                                           && s.Subscribe.Group == "group1"
                                                                           && s.Subscribe.Ref.Equals(u12));
                }, _first);

                RunOn(() =>
                {
                    var u12 = CreateChatUser("u12");
                    u12.Tell(new JoinGroup("topic2", "group2"));
                    ExpectMsg<SubscribeAck>(s => s.Subscribe.Topic == "topic2"
                                                                           && s.Subscribe.Group == "group2"
                                                                           && s.Subscribe.Ref.Equals(u12));

                    var u13 = CreateChatUser("u13");
                    u12.Tell(new JoinGroup("topic2", "group2"));
                    ExpectMsg<SubscribeAck>(s => s.Subscribe.Topic == "topic2"
                                                                           && s.Subscribe.Group == "group2"
                                                                           && s.Subscribe.Ref.Equals(u13));
                }, _second);

                AwaitCount(17);
                EnterBarrier("12-registered");

                RunOn(() =>
                {
                    ChatUser("u12").Tell(new ShoutToGroup("topic12", "hi"));
                }, _first);

                RunOn(() =>
                {
                    ExpectMsg("hi");
                    ExpectNoMsg(TimeSpan.FromSeconds(2));   // each group receive only one message
                }, _first, _second);
                EnterBarrier("12-published");

                RunOn(() =>
                {
                    var u12 = ChatUser("u12");
                    u12.Tell(new ExitGroup("topic2", "group1"));
                    ExpectMsg<UnsubscribeAck>(s => s.Unsubscribe.Topic == "topic2"
                                                                           && s.Unsubscribe.Group == "group1"
                                                                           && s.Unsubscribe.Ref.Equals(u12));
                }, _first);

                RunOn(() =>
                {
                    var u12 = ChatUser("u12");
                    u12.Tell(new ExitGroup("topic2", "group2"));
                    ExpectMsg<UnsubscribeAck>(s => s.Unsubscribe.Topic == "topic2"
                                                                           && s.Unsubscribe.Group == "group2"
                                                                           && s.Unsubscribe.Ref.Equals(u12));
                    var u13 = ChatUser("u13");
                    u12.Tell(new ExitGroup("topic2", "group2"));
                    ExpectMsg<UnsubscribeAck>(s => s.Unsubscribe.Topic == "topic2"
                                                                           && s.Unsubscribe.Group == "group2"
                                                                           && s.Unsubscribe.Ref.Equals(u13));
                }, _second);
                EnterBarrier("after-12");
            });
        }

        //[MultiNodeFact(Skip = "TODO")]
        public void DistributedPubSubMediator_should_transfer_delta_correctly()
        {
            DistributedPubSubMediator_should_send_one_message_to_each_group();

            var firstAddress = Node(_first).Address;
            var secondAddress = Node(_second).Address;
            var thirdAddress = Node(_third).Address;

            RunOn(() =>
            {
                Mediator.Tell(new Tools.PublishSubscribe.Internal.Status(new Dictionary<Address, long>()));
                var deltaBuckets = ExpectMsg<Delta>().Buckets;
                Assert.Equal(3, deltaBuckets.Count());
                Assert.Equal(9, deltaBuckets.First(x => x.Owner == firstAddress).Content.Count);
                Assert.Equal(8, deltaBuckets.First(x => x.Owner == secondAddress).Content.Count);
                Assert.Equal(2, deltaBuckets.First(x => x.Owner == thirdAddress).Content.Count);
            }, _first);
            EnterBarrier("verified-initial-delta");

            // this test is configured with max-delta-elements = 500
            var many = 1010;
            RunOn(() =>
            {
                for (int i = 1; i <= many; i++)
                {
                    Mediator.Tell(new Put(CreateChatUser("u" + (i + 1000))));
                }

                Mediator.Tell(new Tools.PublishSubscribe.Internal.Status(new Dictionary<Address, long>()));
                var deltaBuckets1 = ExpectMsg<Delta>().Buckets;
                Assert.Equal(500, deltaBuckets1.Sum(x => x.Content.Count));

                Mediator.Tell(new Tools.PublishSubscribe.Internal.Status(deltaBuckets1.ToDictionary(b => b.Owner, b => b.Version)));
                var deltaBuckets2 = ExpectMsg<Delta>().Buckets;
                Assert.Equal(500, deltaBuckets2.Sum(x => x.Content.Count));

                Mediator.Tell(new Tools.PublishSubscribe.Internal.Status(deltaBuckets2.ToDictionary(b => b.Owner, b => b.Version)));
                var deltaBuckets3 = ExpectMsg<Delta>().Buckets;
                Assert.Equal(9 + 8 + 2 + many - 500 - 500, deltaBuckets3.Sum(x => x.Content.Count));

            }, _first);
            EnterBarrier("verified-delta-with-many");

            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitCount(17 + many);
            });
            EnterBarrier("after-13");
        }

        //[MultiNodeFact(Skip = "TODO")]
        public void DistributedPubSubMediator_should_remove_entries_when_node_is_removed()
        {
            DistributedPubSubMediator_should_transfer_delta_correctly();

            Within(TimeSpan.FromSeconds(30), () =>
            {
                Mediator.Tell(Count.Instance);
                var countBefore = ExpectMsg<int>();

                RunOn(() =>
                {
                    TestConductor.Exit(_third, 0).Wait();
                }, _first);
                EnterBarrier("third-shutdown");

                // third had 2 entries u5 and u11, and those should be removed everywhere
                RunOn(() =>
                {
                    AwaitCount(countBefore - 2);
                }, _first, _second);
                EnterBarrier("after-14");
            });
        }

        //[MultiNodeFact(Skip = "TODO")]
        public void DistributedPubSubMediator_should_receive_proper_UnsubscribeAck_message()
        {
            DistributedPubSubMediator_should_remove_entries_when_node_is_removed();

            Within(TimeSpan.FromSeconds(15), () =>
            {
                RunOn(() =>
                {
                    var user = CreateChatUser("u111");
                    var topic = "sample-topic-14";
                    var s1 = new Subscribe(topic, user);
                    Mediator.Tell(s1);
                    ExpectMsg<SubscribeAck>(x => x.Subscribe.Equals(s1));
                    var uns = new Unsubscribe(topic, user);
                    Mediator.Tell(uns);
                    ExpectMsg<UnsubscribeAck>(x => x.Unsubscribe.Equals(uns));
                }, _first);
                EnterBarrier("after-15");
            });
        }

        [MultiNodeFact(Skip = "TODO")]
        public void DistributedPubSubMediator_should_get_topics_after_simple_publish()
        {
            DistributedPubSubMediator_should_receive_proper_UnsubscribeAck_message();

            Within(TimeSpan.FromSeconds(15), () =>
            {
                RunOn(() =>
                {
                    var s1 = new Subscribe("topic_a1", CreateChatUser("u14"));
                    Mediator.Tell(s1);
                    ExpectMsg<SubscribeAck>(x => x.Subscribe.Equals(s1));

                    var s2 = new Subscribe("topic_a1", CreateChatUser("u15"));
                    Mediator.Tell(s2);
                    ExpectMsg<SubscribeAck>(x => x.Subscribe.Equals(s2));

                    var s3 = new Subscribe("topic_a2", CreateChatUser("u16"));
                    Mediator.Tell(s3);
                    ExpectMsg<SubscribeAck>(x => x.Subscribe.Equals(s3));

                }, _first);

                RunOn(() =>
                {
                    var s3 = new Subscribe("topic_a1", CreateChatUser("u17"));
                    Mediator.Tell(s3);
                    ExpectMsg<SubscribeAck>(x => x.Subscribe.Equals(s3));

                }, _second);
                EnterBarrier("topics-registered");

                RunOn(() =>
                {
                    Mediator.Tell(GetTopics.Instance);
                    ExpectMsg<CurrentTopics>(
                        x => x.Topics.Contains("topic_a1") && x.Topics.Contains("topic_a2"));
                }, _first);

                RunOn(() =>
                {
                    // topics will eventually be replicated
                    AwaitAssert(() =>
                    {
                        Mediator.Tell(GetTopics.Instance);
                        var topics = ExpectMsg<CurrentTopics>().Topics;

                        Assert.True(topics.Contains("topic_a1"));
                        Assert.True(topics.Contains("topic_a2"));
                    });
                }, _second);
                EnterBarrier("after-get-topics");
            });
        }
    }
}