using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Util;
using FSharp.PubSub.Messages;

namespace PubSub.Client
{
    public class RandomUser : ReceiveActor
    {
        private class Tick
        {
            public static Tick Instance { get; } = new ();
            private Tick() { }
        }

        private static readonly List<string> Phrases = new ()
        {
            "Creativity is allowing yourself to make mistakes. Art is knowing which ones to keep.",
            "The best way to compile inaccurate information that no one wants is to make it up.",
            "Decisions are made by people who have time, not people who have talent.",
            "Frankly, I'm suspicious of anyone who has a strong opinion on a complicated issue.",
            "Nothing inspires forgiveness quite like revenge.",
            "Free will is an illusion. People always choose the perceived path of greatest pleasure.",
            "The best things in life are silly.",
            "Remind people that profit is the difference between revenue and expense. This makes you look smart.",
            "Engineers like to solve problems. If there are no problems handily available, they will create their own problems."
        };

        private readonly IScheduler _scheduler;
        private readonly Random _rnd = ThreadLocalRandom.Current;
        
        public RandomUser()
        {
            var client = Context.ActorOf(ChatClient.Props(Self.Path.Name), "client");
            _scheduler = Context.System.Scheduler;

            var id = 0;
            Receive<Tick>(_ =>
            {
                _scheduler.ScheduleTellOnce(TimeSpan.FromSeconds(_rnd.Next(5, 20)), Self, Tick.Instance, ActorRefs.NoSender);
                var msg = Phrases[_rnd.Next(Phrases.Count)];
                client.Tell(new Publish(0L, msg, id));
                id++;
            });
        }

        protected override void PreStart()
        {
            base.PreStart();
            _scheduler.ScheduleTellOnce(TimeSpan.FromSeconds(5), Self, Tick.Instance, ActorRefs.NoSender);
        }

        // override postRestart so we don't call preStart and schedule a new Tick
        protected override void PostRestart(Exception reason)
        {
        }
    }
}