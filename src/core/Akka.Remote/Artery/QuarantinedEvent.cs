namespace Akka.Remote.Artery
{
    internal sealed class QuarantinedEvent
    {
        public UniqueAddress UniqueAddress { get; }

        public QuarantinedEvent(UniqueAddress uniqueAddress)
        {
            UniqueAddress = uniqueAddress;
        }

        public override string ToString()
            => $"QuarantinedEvent: Association to [{UniqueAddress.Address}] having UID [{UniqueAddress.Uid}] is" +
               "irrecoverably failed. UID is now quarantined and all messages to this UID will be delivered to dead letters. " +
               "Remote ActorSystem must be restarted to recover from this situation.";
    }

    internal sealed class GracefulShutdownQuarantinedEvent
    {
        public UniqueAddress UniqueAddress { get; }
        public string Reason { get; }

        public GracefulShutdownQuarantinedEvent(UniqueAddress uniqueAddress, string reason)
        {
            UniqueAddress = uniqueAddress;
            Reason = reason;
        }

        public override string ToString()
            => $"GracefulShutdownQuarantinedEvent: Association to [{UniqueAddress.Address}] having UID [{UniqueAddress.Uid}] " +
               $"has been stopped. All messages to this UID will be delivered to dead letters. Reason: {Reason}";
    }

    internal sealed class ThisActorSystemQuarantinedEvent
    {
        public UniqueAddress LocalAddress { get; }
        public UniqueAddress RemoteAddress { get; }

        public ThisActorSystemQuarantinedEvent(UniqueAddress localAddress, UniqueAddress remoteAddress)
        {
            LocalAddress = localAddress;
            RemoteAddress = remoteAddress;
        }

        public override string ToString()
            => $"ThisActorSystemQuarantinedEvent: The remote system [{RemoteAddress}] has quarantined this system [{LocalAddress}].";
    }
}
