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
        public static void Subscribe<T>(this IActorRef client, IActorRef subscriber, string topic, string group = null)
        {
            client.Tell(new ClusterClient.Subscribe(topic, typeof(T), subscriber, group));
        }

        public static void Publish<T>(this IActorRef client, string topic, T message)
        {
            client.Tell(new ClusterClient.Publish(topic, message));
        }
    }
}
