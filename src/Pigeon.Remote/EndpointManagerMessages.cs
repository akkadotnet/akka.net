using Pigeon.Actor;
using Pigeon.Remote.Transport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Remote
{
    public interface RemotingCommand : NoSerializationVerificationNeeded
    {

    }
    public class Listen : RemotingCommand
    {
        public Listen(Task<ProtocolTransportAddressPair[]> addressesPromise)
        {
            this.AddressesPromise = addressesPromise;
        }

        public Task<ProtocolTransportAddressPair[]> AddressesPromise { get;private set; }
    }
/*
 sealed trait RemotingCommand extends NoSerializationVerificationNeeded
  case class Listen(addressesPromise: Promise[Seq[(AkkaProtocolTransport, Address)]]) extends RemotingCommand
  case object StartupFinished extends RemotingCommand
  case object ShutdownAndFlush extends RemotingCommand
  case class Send(message: Any, senderOption: Option[ActorRef], recipient: RemoteActorRef, seqOpt: Option[SeqNo] = None)
    extends RemotingCommand with HasSequenceNumber {
    override def toString = s"Remote message $senderOption -> $recipient"

    // This MUST throw an exception to indicate that we attempted to put a nonsequenced message in one of the
    // acknowledged delivery buffers
    def seq = seqOpt.get
  }
  case class Quarantine(remoteAddress: Address, uid: Option[Int]) extends RemotingCommand
  case class ManagementCommand(cmd: Any) extends RemotingCommand
  case class ManagementCommandAck(status: Boolean)

  // Messages internal to EndpointManager
  case object Prune extends NoSerializationVerificationNeeded
  case class ListensResult(addressesPromise: Promise[Seq[(AkkaProtocolTransport, Address)]],
                           results: Seq[(AkkaProtocolTransport, Address, Promise[AssociationEventListener])])
    extends NoSerializationVerificationNeeded
  case class ListensFailure(addressesPromise: Promise[Seq[(AkkaProtocolTransport, Address)]], cause: Throwable)
    extends NoSerializationVerificationNeeded

  // Helper class to store address pairs
  case class Link(localAddress: Address, remoteAddress: Address)

  case class ResendState(uid: Int, buffer: AckedReceiveBuffer[Message])

  sealed trait EndpointPolicy {

 
     * Indicates that the policy does not contain an active endpoint, but it is a tombstone of a previous failure
    
    def isTombstone: Boolean
  }
  case class Pass(endpoint: ActorRef) extends EndpointPolicy {
    override def isTombstone: Boolean = false
  }
  case class Gated(timeOfRelease: Deadline) extends EndpointPolicy {
    override def isTombstone: Boolean = true
  }
  case class Quarantined(uid: Int, timeOfRelease: Deadline) extends EndpointPolicy {
    override def isTombstone: Boolean = true
  }
*/
}
