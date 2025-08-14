//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubMediatorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Xunit;
using FluentAssertions;
using System.Collections.Immutable;
using Akka.MultiNode.TestAdapter;

namespace Akka.Cluster.Tools.Tests.MultiNode.PublishSubscribe;

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
                akka.actor.serialize-messages = off
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.auto-down-unreachable-after = 0s
                akka.cluster.pub-sub.max-delta-elements = 500
                akka.testconductor.query-timeout = 1m # we were having timeouts shutting down nodes with 5s default
            ").WithFallback(DistributedPubSub.DefaultConfig());
    }
}

public class DistributedPubSubMediatorSpec : MultiNodeClusterSpec
{
    #region setup

    [Serializable]
    public sealed record Whisper(string Path, object Message);

    [Serializable]
    public sealed record Talk(string Path, object Message);

    [Serializable]
    public sealed record TalkToOthers(string Path, object Message);

    [Serializable]
    public sealed record Shout(string Topic, object Message);

    [Serializable]
    public sealed record ShoutToGroup(string Topic, object Message);

    [Serializable]
    public sealed record JoinGroup(string Topic, string Group);

    [Serializable]
    public sealed record ExitGroup(string Topic, string Group);

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

    public class Sender : UntypedActor
    {
        private readonly IActorRef _mediator;

        public Sender()
        {
            _mediator = DistributedPubSub.Get(Context.System).Mediator;
        }

        protected override void OnReceive(object message)
        {
            var str = message as string;
            if (str != null)
            {
                _mediator.Tell(new Send("/user/destination", str.ToUpperInvariant(), true));
            }
        }
    }

    public class Destination : UntypedActor
    {
        private readonly IActorRef _mediator;
        private readonly ILoggingAdapter _log;

        public Destination()
        {
            _log = Context.GetLogger();
            _mediator = DistributedPubSub.Get(Context.System).Mediator;
            _mediator.Tell(new Put(Self));
        }

        protected override void OnReceive(object message)
        {
            if (message is string)
            {
                _log.Info("Got {0}", message);
            }
        }
    }

    private readonly RoleName _first;
    private readonly RoleName _second;
    private readonly RoleName _third;

    private readonly ConcurrentDictionary<string, IActorRef> _chatUsers = new();

    public DistributedPubSubMediatorSpec() : this(new DistributedPubSubMediatorSpecConfig())
    {
    }

    protected DistributedPubSubMediatorSpec(DistributedPubSubMediatorSpecConfig config) : base(config, typeof(DistributedPubSubMediatorSpec))
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
        return _chatUsers.TryGetValue(name, out var a) ? a : ActorRefs.Nobody;
    }

    private async Task Join(RoleName from, RoleName to)
    {
        RunOn(() =>
        {
            Cluster.Join(Node(to).Address);
            CreateMediator();
        }, from);
        await EnterBarrierAsync(from.Name + "-joined");
    }

    private void CreateMediator()
    {
        var m = DistributedPubSub.Get(Sys).Mediator;
    }

    private async Task AwaitCount(int expected)
    {
        await AwaitAssertAsync(async () =>
        {
            Mediator.Tell(Count.Instance);
            (await ExpectMsgAsync<int>()).Should().Be(expected);
        });
    }

    private async Task AwaitCountSubscribers(int expected, string topic)
    {
        await AwaitAssertAsync(async () =>
        {
            Mediator.Tell(new CountSubscribers(topic));
            (await ExpectMsgAsync<int>()).Should().Be(expected);
        });
    }

    #endregion

    [MultiNodeFact]
    public async Task DistributedPubSubMediatorSpecs()
    {
        await DistributedPubSubMediator_must_startup_2_nodes_cluster();
        await DistributedPubSubMediator_must_keep_track_of_added_users();
        await DistributedPubSubMediator_must_replicate_users_to_new_node();
        await DistributedPubSubMediator_must_keep_track_of_removed_users();
        await DistributedPubSubMediator_must_remove_terminated_users();
        await DistributedPubSubMediator_must_publish();
        await DistributedPubSubMediator_must_publish_to_topic();
        await DistributedPubSubMediator_must_demonstrate_usage_of_Publish();
        await DistributedPubSubMediator_must_demonstrate_usage_of_Send();
        await DistributedPubSubMediator_must_SendAll_to_all_other_nodes();
        await DistributedPubSubMediator_must_send_one_message_to_each_group();
        await DistributedPubSubMediator_must_transfer_delta_correctly();
        await DistributedPubSubMediator_must_remove_entries_when_node_is_removed();
        await DistributedPubSubMediator_must_receive_proper_UnsubscribeAck_message();
        await DistributedPubSubMediator_must_get_topics_after_simple_publish();
        await DistributedPubSubMediator_must_remove_topic_subscribers_when_they_terminate();
    }

    public async Task DistributedPubSubMediator_must_startup_2_nodes_cluster()
    {
        await WithinAsync(TimeSpan.FromSeconds(15), async () =>
        {
            await Join(_first, _first);
            await Join(_second, _first);
            await EnterBarrierAsync("after-1");
        });
    }

    public async Task DistributedPubSubMediator_must_keep_track_of_added_users()
    {
        await WithinAsync(TimeSpan.FromSeconds(15), async () =>
        {
            await RunOnAsync(async () =>
            {
                var u1 = CreateChatUser("u1");
                Mediator.Tell(new Put(u1));

                var u2 = CreateChatUser("u2");
                Mediator.Tell(new Put(u2));

                await AwaitCount(2);

                // send to actor at the same node
                u1.Tell(new Whisper("/user/u2", "hello"));
                await ExpectMsgAsync("hello");
                LastSender.Should().Be(u2);
            }, _first);

            RunOn(() =>
            {
                var u3 = CreateChatUser("u3");
                Mediator.Tell(new Put(u3));
            }, _second);

            await RunOnAsync(async () =>
            {
                 await AwaitCount(3);
            }, _first, _second);
            await EnterBarrierAsync("3-registered");

            RunOn(() =>
            {
                var u4 = CreateChatUser("u4");
                Mediator.Tell(new Put(u4));
            }, _second);

            await RunOnAsync(async () =>
            {
                await AwaitCount(4);
            }, _first, _second);
            await EnterBarrierAsync("4-registered");

            RunOn(() =>
            {
                // send to an actor on another node
                ChatUser("u1").Tell(new Whisper("/user/u4", "hi there"));
            }, _first);

            await RunOnAsync(async () =>
            {
                await ExpectMsgAsync("hi there");
                LastSender.Path.Name.Should().Be("u4");
            }, _second);
            await EnterBarrierAsync("after-2");
        });
    }

    public async Task DistributedPubSubMediator_must_replicate_users_to_new_node()
    {
        await WithinAsync(TimeSpan.FromSeconds(20), async () =>
        {
            await Join(_third, _first);
            RunOn(() =>
            {
                var u5 = CreateChatUser("u5");
                Mediator.Tell(new Put(u5));
            }, _third);

            await AwaitCount(5);
            await EnterBarrierAsync("5-registered");

            RunOn(() =>
            {
                ChatUser("u5").Tell(new Whisper("/user/u4", "go"));
            }, _third);

            await RunOnAsync(async () =>
            {
                await ExpectMsgAsync("go");
                LastSender.Path.Name.Should().Be("u4");
            }, _second);
            await EnterBarrierAsync("after-3");
        });
    }

    public async Task DistributedPubSubMediator_must_keep_track_of_removed_users()
    {
        await WithinAsync(TimeSpan.FromSeconds(15), async () =>
        {
            RunOn(() =>
            {
                var u6 = CreateChatUser("u6");
                Mediator.Tell(new Put(u6));
            }, _first);
            await AwaitCount(6);
            await EnterBarrierAsync("6-registered");

            RunOn(() =>
            {
                Mediator.Tell(new Remove("/user/u6"));
            }, _first);
            await AwaitCount(5);

            await EnterBarrierAsync("after-4");
        });
    }

    public async Task DistributedPubSubMediator_must_remove_terminated_users()
    {
        await WithinAsync(TimeSpan.FromSeconds(5), async () =>
        {
            RunOn(() =>
            {
                ChatUser("u3").Tell(PoisonPill.Instance);
            }, _second);

            await AwaitCount(4);
            await EnterBarrierAsync("after-5");
        });
    }

    public async Task DistributedPubSubMediator_must_publish()
    {
        await WithinAsync(TimeSpan.FromSeconds(15), async () =>
        {
            RunOn(() =>
            {
                var u7 = CreateChatUser("u7");
                Mediator.Tell(new Put(u7));
            }, _first, _second);
            await AwaitCount(6);
            await EnterBarrierAsync("7-registered");

            RunOn(() =>
            {
                ChatUser("u5").Tell(new Talk("/user/u7", "hi"));
            }, _third);

            RunOn(() =>
            {
                ExpectMsg("hi");
                LastSender.Path.Name.Should().Be("u7");
            }, _first, _second);

            await RunOnAsync(async () =>
            {
                await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
            }, _third);

            await EnterBarrierAsync("after-6");
        });
    }

    public async Task DistributedPubSubMediator_must_publish_to_topic()
    {
        await WithinAsync(TimeSpan.FromSeconds(15), async () =>
        {
            await RunOnAsync(async () =>
            {
                var s8 = new Subscribe("topic1", CreateChatUser("u8"));
                Mediator.Tell(s8);
                await ExpectMsgAsync<SubscribeAck>(x => x.Subscribe.Equals(s8));
                var s9 = new Subscribe("topic1", CreateChatUser("u9"));
                Mediator.Tell(s9);
                await ExpectMsgAsync<SubscribeAck>(x => x.Subscribe.Equals(s9));
            }, _first);

            await RunOnAsync(async () =>
            {
                var s10 = new Subscribe("topic1", CreateChatUser("u10"));
                Mediator.Tell(s10);
                await ExpectMsgAsync<SubscribeAck>(x => x.Subscribe.Equals(s10));
            }, _second);

            // one topic on two nodes
            await AwaitCount(8);
            await EnterBarrierAsync("topic1-registered");

            RunOn(() =>
            {
                ChatUser("u5").Tell(new Shout("topic1", "hello all"));
            }, _third);

            RunOn(() =>
            {
                var names = ReceiveWhile(x => "hello all".Equals(x) ? LastSender.Path.Name : null, msgs: 2);
                names.All(x => x is "u8" or "u9").Should().BeTrue();
            }, _first);

            await RunOnAsync(async () =>
            {
                await ExpectMsgAsync("hello all");
                LastSender.Path.Name.Should().Be("u10");
            }, _second);

            await RunOnAsync(async () =>
            {
                await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
            }, _third);
            await EnterBarrierAsync("after-7");
        });
    }

    public async Task DistributedPubSubMediator_must_demonstrate_usage_of_Publish()
    {
        await WithinAsync(TimeSpan.FromSeconds(15), async () =>
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

            await RunOnAsync(async () =>
            {
                var publisher = Sys.ActorOf(Props.Create<Publisher>(), "publisher");
                await AwaitCount(10);
                // after a while the subscriptions are replicated
                publisher.Tell("hello");
            }, _third);
            await EnterBarrierAsync("after-8");
        });
    }

    public async Task DistributedPubSubMediator_must_demonstrate_usage_of_Send()
    {
        await WithinAsync(TimeSpan.FromSeconds(15), async () =>
        {
            RunOn(() =>
            {
                Sys.ActorOf(Props.Create<Destination>(), "destination");
            }, _first);

            RunOn(() =>
            {
                Sys.ActorOf(Props.Create<Destination>(), "destination");
            }, _second);

            await RunOnAsync(async () =>
            {
                var sender = Sys.ActorOf(Props.Create<Sender>(), "sender");
                await AwaitCount(12);
                // after a while the destinations are replicated
                sender.Tell("hello");
            }, _third);

            await EnterBarrierAsync("after-8");
        });
    }

    public async Task DistributedPubSubMediator_must_SendAll_to_all_other_nodes()
    {
        await WithinAsync(TimeSpan.FromSeconds(15), async () =>
        {
            RunOn(() =>
            {
                var u11 = CreateChatUser("u11");
                Mediator.Tell(new Put(u11));
            }, _first, _second, _third);
            await AwaitCount(15);
            await EnterBarrierAsync("11-registered");

            RunOn(() =>
            {
                ChatUser("u5").Tell(new TalkToOthers("/user/u11", "hi"));
            }, _third);

            await RunOnAsync(async () =>
            {
                await ExpectMsgAsync("hi");
                LastSender.Path.Name.Should().Be("u11");
            }, _first, _second);

            await RunOnAsync(async () =>
            {
                await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));
            }, _third);
            await EnterBarrierAsync("after-11");
        });
    }

    public async Task DistributedPubSubMediator_must_send_one_message_to_each_group()
    {
        await WithinAsync(TimeSpan.FromSeconds(20), async () =>
        {
            await RunOnAsync(async () =>
            {
                var u12 = CreateChatUser("u12");
                u12.Tell(new JoinGroup("topic2", "group1"));
                var message = await ExpectMsgAsync<SubscribeAck>();
                message.Subscribe.Topic.Should().Be("topic2");
                message.Subscribe.Group.Should().Be("group1");
                message.Subscribe.Ref.Should().Be(u12);
            }, _first);

            await RunOnAsync(async () =>
            {
                var u12 = CreateChatUser("u12");
                u12.Tell(new JoinGroup("topic2", "group2"));
                var message1 = await ExpectMsgAsync<SubscribeAck>();
                message1.Subscribe.Topic.Should().Be("topic2");
                message1.Subscribe.Group.Should().Be("group2");
                message1.Subscribe.Ref.Should().Be(u12);

                var u13 = CreateChatUser("u13");
                u13.Tell(new JoinGroup("topic2", "group2"));
                var message2 = await ExpectMsgAsync<SubscribeAck>();
                message2.Subscribe.Topic.Should().Be("topic2");
                message2.Subscribe.Group.Should().Be("group2");
                message2.Subscribe.Ref.Should().Be(u13);
            }, _second);

            await AwaitCount(19);
            await EnterBarrierAsync("12-registered");

            RunOn(() =>
            {
                ChatUser("u12").Tell(new ShoutToGroup("topic2", "hi"));
            }, _first);

            await RunOnAsync(async () =>
            {
                await ExpectMsgAsync("hi");
                await ExpectNoMsgAsync(TimeSpan.FromSeconds(2));   // each group receive only one message
            }, _first, _second);
            await EnterBarrierAsync("12-published");

            await RunOnAsync(async () =>
            {
                var u12 = ChatUser("u12");
                u12.Tell(new ExitGroup("topic2", "group1"));
                await ExpectMsgAsync<UnsubscribeAck>(s => s.Unsubscribe.Topic == "topic2"
                                                          && s.Unsubscribe.Group == "group1"
                                                          && s.Unsubscribe.Ref.Equals(u12));
            }, _first);

            await RunOnAsync(async () =>
            {
                var u12 = ChatUser("u12");
                u12.Tell(new ExitGroup("topic2", "group2"));
                var message1 = await ExpectMsgAsync<UnsubscribeAck>();
                message1.Unsubscribe.Topic.Should().Be("topic2");
                message1.Unsubscribe.Group.Should().Be("group2");
                message1.Unsubscribe.Ref.Should().Be(u12);

                var u13 = ChatUser("u13");
                u13.Tell(new ExitGroup("topic2", "group2"));
                var message2 = await ExpectMsgAsync<UnsubscribeAck>();
                message2.Unsubscribe.Topic.Should().Be("topic2");
                message2.Unsubscribe.Group.Should().Be("group2");
                message2.Unsubscribe.Ref.Should().Be(u13);
            }, _second);
            await EnterBarrierAsync("after-12");
        });
    }

    public async Task DistributedPubSubMediator_must_transfer_delta_correctly()
    {
        var firstAddress = (await NodeAsync(_first)).Address;
        var secondAddress = (await NodeAsync(_second)).Address;
        var thirdAddress = (await NodeAsync(_third)).Address;

        await RunOnAsync(async () =>
        {
            Mediator.Tell(new Tools.PublishSubscribe.Internal.Status(ImmutableDictionary<Address, long>.Empty, isReplyToStatus: false));
            var deltaBuckets = (await ExpectMsgAsync<Delta>()).Buckets;
            deltaBuckets.Count.Should().Be(3);
            deltaBuckets.First(x => x.Owner == firstAddress).Content.Count.Should().Be(10);
            deltaBuckets.First(x => x.Owner == secondAddress).Content.Count.Should().Be(9);
            deltaBuckets.First(x => x.Owner == thirdAddress).Content.Count.Should().Be(2);
        }, _first);
        await EnterBarrierAsync("verified-initial-delta");

        // this test is configured with max-delta-elements = 500
        const int many = 1010;
        await RunOnAsync(async () =>
        {
            for (int i = 1; i <= many; i++)
            {
                Mediator.Tell(new Put(CreateChatUser("u" + (1000 + i))));
            }

            Mediator.Tell(new Tools.PublishSubscribe.Internal.Status(ImmutableDictionary<Address, long>.Empty, isReplyToStatus: false));
            var deltaBuckets1 = (await ExpectMsgAsync<Delta>()).Buckets;
            deltaBuckets1.Sum(x => x.Content.Count).Should().Be(500);
            var versions1 = deltaBuckets1.ToImmutableDictionary(b => b.Owner, b => b.Version);

            Mediator.Tell(new Tools.PublishSubscribe.Internal.Status(versions1, isReplyToStatus: false));
            var deltaBuckets2 = (await ExpectMsgAsync<Delta>()).Buckets;
            deltaBuckets2.Sum(x => x.Content.Count).Should().Be(500);

            Mediator.Tell(new Tools.PublishSubscribe.Internal.Status(versions1.SetItems(deltaBuckets2.ToImmutableDictionary(b => b.Owner, b => b.Version)), isReplyToStatus: false));
            var deltaBuckets3 = (await ExpectMsgAsync<Delta>()).Buckets;
            deltaBuckets3.Sum(x => x.Content.Count).Should().Be(10 + 9 + 2 + many - 500 - 500);
        }, _first);
        await EnterBarrierAsync("verified-delta-with-many");

        await WithinAsync(TimeSpan.FromSeconds(10), async () =>
        {
            await AwaitCount(19 + many);
        });
        await EnterBarrierAsync("after-13");
    }

    public async Task DistributedPubSubMediator_must_remove_entries_when_node_is_removed()
    {
        await WithinAsync(TimeSpan.FromSeconds(30), async () =>
        {
            Mediator.Tell(Count.Instance);
            var countBefore = await ExpectMsgAsync<int>();

            await RunOnAsync(async () =>
            {
                await TestConductor.ExitAsync(_third, 0);
            }, _first);
            await EnterBarrierAsync("third-shutdown");

            // third had 2 entries u5 and u11, and those should be removed everywhere
            await RunOnAsync(async () =>
            {
                await AwaitCount(countBefore - 2);
            }, _first, _second);
            await EnterBarrierAsync("after-14");
        });
    }

    public async Task DistributedPubSubMediator_must_receive_proper_UnsubscribeAck_message()
    {
        await WithinAsync(TimeSpan.FromSeconds(15), async () =>
        {
            await RunOnAsync(async () =>
            {
                var user = CreateChatUser("u111");
                var topic = "sample-topic-14";
                var s1 = new Subscribe(topic, user);
                Mediator.Tell(s1);
                await ExpectMsgAsync<SubscribeAck>(x => x.Subscribe.Equals(s1));
                var uns = new Unsubscribe(topic, user);
                Mediator.Tell(uns);
                await ExpectMsgAsync<UnsubscribeAck>(x => x.Unsubscribe.Equals(uns));
            }, _first);
            await EnterBarrierAsync("after-15");
        });
    }

    public async Task DistributedPubSubMediator_must_get_topics_after_simple_publish()
    {
        await WithinAsync(TimeSpan.FromSeconds(15), async () =>
        {
            await RunOnAsync(async () =>
            {
                var s1 = new Subscribe("topic_a1", CreateChatUser("u14"));
                Mediator.Tell(s1);
                await ExpectMsgAsync<SubscribeAck>(x => x.Subscribe.Equals(s1));

                var s2 = new Subscribe("topic_a1", CreateChatUser("u15"));
                Mediator.Tell(s2);
                await ExpectMsgAsync<SubscribeAck>(x => x.Subscribe.Equals(s2));

                var s3 = new Subscribe("topic_a2", CreateChatUser("u16"));
                Mediator.Tell(s3);
                await ExpectMsgAsync<SubscribeAck>(x => x.Subscribe.Equals(s3));

            }, _first);

            await RunOnAsync(async () =>
            {
                var s3 = new Subscribe("topic_a1", CreateChatUser("u17"));
                Mediator.Tell(s3);
                await ExpectMsgAsync<SubscribeAck>(x => x.Subscribe.Equals(s3));

            }, _second);
            await EnterBarrierAsync("topics-registered");

            await RunOnAsync(async () =>
            {
                Mediator.Tell(GetTopics.Instance);
                await ExpectMsgAsync<CurrentTopics>(
                    x => x.Topics.Contains("topic_a1") && x.Topics.Contains("topic_a2"));
            }, _first);

            await RunOnAsync(async () =>
            {
                // topics will eventually be replicated
                await AwaitAssertAsync(async () =>
                {
                    Mediator.Tell(GetTopics.Instance);
                    var topics = (await ExpectMsgAsync<CurrentTopics>()).Topics;

                    topics.Contains("topic_a1").Should().BeTrue();
                    topics.Contains("topic_a2").Should().BeTrue();
                });
            }, _second);
            await EnterBarrierAsync("after-get-topics");
        });
    }

    public async Task DistributedPubSubMediator_must_remove_topic_subscribers_when_they_terminate()
    {
        await WithinAsync(TimeSpan.FromSeconds(15), async () =>
        {
            await RunOnAsync(async () =>
            {
                var s1 = new Subscribe("topic_b1", CreateChatUser("u18"));
                Mediator.Tell(s1);
                await ExpectMsgAsync<SubscribeAck>(x => x.Subscribe.Equals(s1));

                await AwaitCountSubscribers(1, "topic_b1");
                ChatUser("u18").Tell(PoisonPill.Instance);
                await AwaitCountSubscribers(0, "topic_b1");
            }, _first);
            await EnterBarrierAsync("after-15");
        });
    }
}