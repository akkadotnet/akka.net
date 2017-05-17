//-----------------------------------------------------------------------
// <copyright file="WithUdpSend.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Sockets;
using Akka.Actor;
using Akka.Event;

namespace Akka.IO
{
    using static Udp;
    using ByteBuffer = ArraySegment<byte>;
    
    abstract class WithUdpSend : ActorBase
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private Send _pendingSend;
        private IActorRef _pendingCommander;
        private bool _retriedSend;

        private bool HasWritePending => _pendingSend != null;
        
        protected abstract Socket Socket { get; }
        protected abstract UdpExt Udp { get; }

        public bool SendHandlers(object message)
        {
            var send = message as Send;
            if (send != null && HasWritePending)
            {
                if (Udp.Setting.TraceLogging) _log.Debug("Dropping write because queue is full");
                Sender.Tell(new CommandFailed(send));
                return true;
            }
            if (send != null)
            {
                if (send.HasData)
                {
                    _pendingSend = send;
                    _pendingCommander = Sender;

                    var e = Udp.SocketEventArgsPool.Acquire(Self);
                    var dns = send.Target as DnsEndPoint;
                    if (dns != null)
                    {
                        var resolved = Dns.ResolveName(dns.Host, Context.System, Self);
                        if (resolved != null)
                        {
                            try
                            {
                                _pendingSend = new Send(_pendingSend.Payload, new IPEndPoint(resolved.Addr, dns.Port), _pendingSend.Ack);
                                DoSend(e);
                            }
                            catch (Exception ex)
                            {
                                Sender.Tell(new CommandFailed(send));
                                _log.Debug("Failure while sending UDP datagram to remote address [{0}]: {1}",
                                    send.Target, ex);
                                _retriedSend = false;
                                _pendingSend = null;
                                _pendingCommander = null;
                            }
                        }
                    }
                    else
                    {
                        DoSend(e);
                    }
                }
                else
                {
                    if (send.WantsAck)
                        Sender.Tell(send.Ack);
                }

                return true;
            }
            if (message is SocketSent)
            {
                var sent = (SocketSent) message;
                if (sent.EventArgs.SocketError == SocketError.Success)
                {
                    if (Udp.Setting.TraceLogging)
                        _log.Debug("Wrote [{0}] bytes to channel", sent.EventArgs.BytesTransferred);

                    var nextSend = _pendingSend.Advance();
                    if (nextSend.HasData)
                    {
                        Self.Tell(nextSend);
                    }
                    else
                    {
                        if (_pendingSend.WantsAck) _pendingCommander.Tell(_pendingSend.Ack);
                        
                        _retriedSend = false;
                        _pendingSend = null;
                        _pendingCommander = null;
                    }
                }
                else
                {
                    if (_retriedSend)
                    {
                        _pendingCommander.Tell(new CommandFailed(_pendingSend));
                        _retriedSend = false;
                        _pendingSend = null;
                        _pendingCommander = null;
                    }
                    else
                    {
                        DoSend(sent.EventArgs);
                        _retriedSend = true;
                    }
                }
                return true;
            }
            return false;
        }

        private void DoSend(SocketAsyncEventArgs e)
        {
            var buffer = _pendingSend.Payload.Current;
            e.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);
            e.RemoteEndPoint = _pendingSend.Target;
            if (!Socket.SendToAsync(e))
                Self.Tell(new SocketSent(e));
        }
    }
}
