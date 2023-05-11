// //-----------------------------------------------------------------------
// // <copyright file="Producers.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Cluster.Sharding;
using Akka.Cluster.Sharding.Delivery;
using Akka.Util;

namespace ShoppingCart;

#region MessageProducer

/// <summary>
/// Actor is responsible for producing messages
/// </summary>
public sealed class Producer : ReceiveActor, IWithTimers
{
    private static readonly string[] Customers = new[]
    {
        "Yoda", "Obi-Wan", "Darth Vader", "Princess Leia", 
        "Luke Skywalker", "R2D2", "Han Solo", "Chewbacca", "Jabba"
    };
    
    private static readonly string [] Items = new[]
    {
        "Yoghurt", "Fruits", "Lightsaber", "Fluffy toy", "Dreamcatcher", 
        "Candies", "Cigars", "Chicken nuggets", "French fries"
    };
    
    private sealed class Produce
    {
        public static readonly Produce Instance = new Produce();
        private Produce() {}
    }

    public ITimerScheduler Timers { get; set; }

    public IActorRef SendNext { get; set; } = ActorRefs.Nobody;

    public Producer()
    {
        Idle();
    }

    protected override void PreStart()
    {
        // produce messages 1 per second
        Timers.StartPeriodicTimer("produce", Produce.Instance, TimeSpan.FromSeconds(1));
    }

    private void Active()
    {
        Receive<Produce>(_ =>
        {
            var customer = PickRandom(Customers);
            var item = PickRandom(Items);
            var msg = new Customer.PurchaseItem(item);
            SendNext.Tell(new ShardingEnvelope(customer, msg));
            Become(Idle); // wait for demand
        });

        Receive<ShardingProducerController.RequestNext<Customer.ICustomerCommand>>(next =>
        {
            // no work to do yet, but update SendNext
            SendNext = next.SendNextTo;
        });
    }

    /// <summary>
    /// Waiting for demand for messages to come from sharding system
    /// </summary>
    private void Idle()
    {
        Receive<Produce>(_ =>
        {
            // ignore
        });
        
        Receive<ShardingProducerController.RequestNext<Customer.ICustomerCommand>>(next =>
        {
            Become(Active);
            SendNext = next.SendNextTo;
        });
    }
    
    private static T PickRandom<T>(IReadOnlyList<T> items) => items[ThreadLocalRandom.Current.Next(items.Count)];
}

#endregion