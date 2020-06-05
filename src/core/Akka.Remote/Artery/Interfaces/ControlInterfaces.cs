using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Akka.Util;

namespace Akka.Remote.Artery.Interfaces
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// marker trait for protobuf-serializable artery messages
    /// </summary>
    internal interface IArteryMessage
    {
        // Empty
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Marker trait for reply messages
    /// </summary>
    internal interface IReply : IControlMessage
    {
        // Empty
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Marker trait for control messages that can be sent via the system message sub-channel
    /// but don't need full reliable delivery. E.g. `HandshakeReq` and `Reply`.
    /// </summary>
    internal interface IControlMessage : IArteryMessage
    {
        // Empty
    }

    /// <summary>
    /// Observer subject for inbound control messages.
    /// Interested observers can attach themselves to the
    /// subject to get notification of incoming control
    /// messages.
    /// </summary>
    internal interface IControlMessageSubject
    {
        Task<Done> Attach(IControlMessageObserver observer);
        void Detach(IControlMessageObserver observer);
    }

    internal interface IControlMessageObserver
    {
        /// <summary>
        /// Notification of incoming control message. The message
        /// of the envelope is always a `ControlMessage`.
        /// </summary>
        /// <param name="inboundEnvelope"></param>
        void Notify(IInboundEnvelope inboundEnvelope);
        void ControlSubjectCompleted(Try<Done> signal);
    }

}
