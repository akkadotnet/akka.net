using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Cluster.Tools.Client
{
    public static class ClusterClientExtensions
    {
        public static Action Subscribe<T>(this IActorRef client, ClusterClient.Subscribe subscribe)
        {
            client.Tell(subscribe);
            return () => { client.Unsubscribe<T>(subscribe); };
        }
        public static Action Subscribe<T>(this IActorRef client, IActorRef subscriber, string topic, string group = null)
        {
            return Subscribe<T>(client, new ClusterClient.Subscribe(topic, typeof(T), subscriber, group));
        }
        public static void Unsubscribe<T>(this IActorRef client, ClusterClient.Subscribe subscribe)
        {
            client.Tell(new ClusterClient.Unsubscribe(subscribe));
        }
        public static void Unsubscribe<T>(this IActorRef client, IActorRef subscriber, string topic, string group = null)
        {
            Unsubscribe<T>(client, new ClusterClient.Subscribe(topic, typeof(T), subscriber, group));
        }

        public static void Publish<T>(this IActorRef client, string topic, T message)
        {
            client.Tell(new ClusterClient.Publish(topic, message));
        }
    }
}
