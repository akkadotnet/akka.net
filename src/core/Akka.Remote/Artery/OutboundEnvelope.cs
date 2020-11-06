using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;
using Akka.Remote.Artery.Utils;
using Akka.Util;

namespace Akka.Remote.Artery
{
    internal static class OutboundEnvelope
    {
        public static IOutboundEnvelope Create(IOptionVal<RemoteActorRef> recipient, object message, IOptionVal<IActorRef> sender)
            => new ReusableOutboundEnvelope().Init(recipient, message, sender);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal interface IOutboundEnvelope : INoSerializationVerificationNeeded
    {
        IOptionVal<RemoteActorRef> Recipient { get; }
        object Message { get; }
        IOptionVal<IActorRef> Sender { get; }

        IOutboundEnvelope WithMessage(object message);
        IOutboundEnvelope Copy();
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class ReusableOutboundEnvelope : IOutboundEnvelope
    {
        public static ObjectPool<ReusableOutboundEnvelope> CreateObjectPool(int capacity)
            => new ObjectPool<ReusableOutboundEnvelope>(capacity, () => new ReusableOutboundEnvelope(), env => env.Clear());

        internal ReusableOutboundEnvelope() {}

        public IOptionVal<RemoteActorRef> Recipient { get; private set; } = OptionVal.None<RemoteActorRef>();
        public object Message { get; private set; } = null;
        public IOptionVal<IActorRef> Sender { get; private set; } = OptionVal.None<IActorRef>();

        public IOutboundEnvelope WithMessage(object message)
        {
            Message = message;
            return this;
        }

        public IOutboundEnvelope Copy()
            => new ReusableOutboundEnvelope().Init(Recipient, Message, Sender);

        internal void Clear()
        {
            Recipient = OptionVal.None<RemoteActorRef>();
            Message = null;
            Sender = OptionVal.None<IActorRef>();
        }

        public IOutboundEnvelope Init(
            IOptionVal<RemoteActorRef> recipient,
            object message,
            IOptionVal<IActorRef> sender)
        {
            Recipient = recipient;
            Message = message;
            Sender = sender;

            return this;
        }

        public override string ToString()
            => $"OutboundEnvelope({Recipient}, {Message}, {Sender})";
    }
}
