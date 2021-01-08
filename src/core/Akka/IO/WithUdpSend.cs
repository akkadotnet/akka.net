//-----------------------------------------------------------------------
// <copyright file="WithUdpSend.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

        private bool HasWritePending => !ReferenceEquals(_pendingSend, null);

        protected abstract Socket Socket { get; }
        protected abstract UdpExt Udp { get; }

        public bool SendHandlers(object message)
        {
            switch (message)
            {
                case Send send when HasWritePending:
                    {
                        if (Udp.Setting.TraceLogging) _log.Debug("Dropping write because queue is full");
                        Sender.Tell(new CommandFailed(send));
                        return true;
                    }
                case Send send:
                    {
                        if (send.HasData)
                        {
                            _pendingSend = send;
                            _pendingCommander = Sender;

                            //var e = Udp.SocketEventArgsPool.Acquire(Self);
                            if (send.Target is DnsEndPoint dns)
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
                                DoSend();
                            }
                        }
                        else
                        {
                            if (send.WantsAck)
                                Sender.Tell(send.Ack);
                        }

                        return true;
                    }
                case SocketSent sent:
                    {
                        if (sent.EventArgs.SocketError == SocketError.Success)
                        {
                            if (Udp.Setting.TraceLogging)
                                _log.Debug("Wrote [{0}] bytes to channel", sent.EventArgs.BytesTransferred);

                            if (_pendingSend.WantsAck) _pendingCommander.Tell(_pendingSend.Ack);

                            _retriedSend = false;
                            _pendingSend = null;
                            _pendingCommander = null;
                        }
                        else if (_retriedSend)
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
                        return true;
                    }
                default: return false;
            }
        }

        private void DoSend()
        {
            try
            {
                var data = _pendingSend.Payload;

                var bytesWritten = Socket.SendTo(data.ToArray(), _pendingSend.Target);
                if (Udp.Setting.TraceLogging)
                    _log.Debug("Wrote [{0}] bytes to socket", bytesWritten);

                // Datagram channel either sends the whole message or nothing
                if (bytesWritten == 0) _pendingCommander.Tell(new CommandFailed(_pendingSend));
                else if (_pendingSend.WantsAck) _pendingCommander.Tell(_pendingSend.Ack);
            }
            finally
            {
                _pendingSend = null;
            }
        }
    }
}
