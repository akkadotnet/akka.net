using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;

namespace ClusterClientExample.Shared
{
    public abstract class TextMessage
    {
        public string Body { get; }
        public IActorRef PublisherRef { get; }
        protected TextMessage(string body, IActorRef publisherRef)
        {
            Body = body;
            PublisherRef = publisherRef;
        }
    }
    public class ThankYou : TextMessage
    {
        public ThankYou(string body, IActorRef publisherRef) : base(body, publisherRef) { }
    }
    public class YouAreWelcome : TextMessage
    {
        public YouAreWelcome(string body, IActorRef publisherRef) : base(body, publisherRef) { }
    }
}
