using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Akka.Util;

namespace Akka.Remote.Artery.Interfaces
{
    /// <summary>
    /// INTERNAL API
    /// marker trait for protobuf-serializable artery messages
    /// </summary>
    internal interface IArteryMessage
    { }

    /// <summary>
    /// INTERNAL API
    /// Marker trait for control messages that can be sent via the system message sub-channel
    /// but don't need full reliable delivery. E.g. `HandshakeReq` and `Reply`.
    /// </summary>
    internal interface IControlMessage : IArteryMessage
    { }

    /// <summary>
    /// INTERNAL API
    /// Marker trait for reply messages
    /// </summary>
    internal interface IReply : IControlMessage
    { }
}
