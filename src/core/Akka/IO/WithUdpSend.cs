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
            if (send != null && send.Payload.IsEmpty)
            {
                if (send.WantsAck)
                    Sender.Tell(send.Ack);
                return true;
            }
            if (send != null)
            {
                _pendingSend = send;
                _pendingCommander = Sender;

                var dns = send.Target as DnsEndPoint;
                if (dns != null)
                {
                    var resolved = Dns.ResolveName(dns.Host, Context.System, Self);
                    if (resolved != null)
                    {
                        try
                        {
                            _pendingSend = new Send(_pendingSend.Payload, new IPEndPoint(resolved.Addr, dns.Port), _pendingSend.Ack);
                            DoSend();
                        }
                        catch (Exception ex)
                        {
                            Sender.Tell(new CommandFailed(send));
                            _log.Debug("Failure while sending UDP datagram to remote address [{0}]: {1}", send.Target, ex);
                            _retriedSend = false;
                            _pendingSend = null;
                            _pendingCommander = null;
                        }
                    }
                }
                else
                {
                    DoSend();
                }
                return true;
            }
            if (message is SocketSent)
            {
                var sent = (SocketSent)message;


                if (sent.EventArgs.SocketError == SocketError.Success)
                {
                    if (Udp.Setting.TraceLogging) _log.Debug("Wrote [{0}] bytes to channel", sent.EventArgs.BytesTransferred);

                    if (_pendingSend.WantsAck) _pendingCommander.Tell(_pendingSend.Ack);
                    _retriedSend = false;
                    _pendingSend = null;
                    _pendingCommander = null;
                }
                else
                {
                    if(_retriedSend)
                    {
                        _pendingCommander.Tell(new CommandFailed(_pendingSend));
                        _retriedSend = false;
                        _pendingSend = null;
                        _pendingCommander = null;
                    }
                    else
                    {
                        DoSend();
                        _retriedSend = true;
                    }
                }
                Udp.SocketEventArgsPool.Release(sent.EventArgs);
                return true;
            }
            return false;
        }

        private void DoSend()
        {
            var saea = Udp.SocketEventArgsPool.Acquire(Self);
            var len = _pendingSend.Payload.CopyTo(saea.Buffer, saea.Offset, _pendingSend.Payload.Count);
            saea.SetBuffer(saea.Offset, len);
            saea.RemoteEndPoint = _pendingSend.Target;
            if (!Socket.SendToAsync(saea))
                Self.Tell(new SocketSent(saea, Udp.SocketEventArgsPool));
        }
    }
}
