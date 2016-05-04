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
    abstract class WithUdpSend : ActorBase
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private Udp.Send _pendingSend;
        private IActorRef _pendingCommander;
        private bool _retriedSend;

        private bool HasWritePending
        {
            get { return _pendingSend != null; }
        }

        protected abstract DatagramChannel Channel { get; }
        protected abstract UdpExt Udp { get; }

        public Receive SendHandlers(ChannelRegistration registration)
        {
            return message =>
            {
                var send = message as Udp.Send;
                if (send != null && HasWritePending)
                {
                    if (Udp.Setting.TraceLogging) _log.Debug("Dropping write because queue is full");
                    Sender.Tell(new Udp.CommandFailed(send));
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
                                _pendingSend = new Udp.Send(_pendingSend.Payload, new IPEndPoint(resolved.Addr, dns.Port), _pendingSend.Ack);
                                DoSend(registration);
                            }
                            catch (Exception ex)
                            {
                                Sender.Tell(new Udp.CommandFailed(send));
                                _log.Debug("Failure while sending UDP datagram to remote address [{0}]: {1}", send.Target, ex);
                                _retriedSend = false;
                                _pendingSend = null;
                                _pendingCommander = null;
                            }
                        }
                    }
                    else
                    {
                        DoSend(registration);
                    }
                    return true;
                }
                if (message is SelectionHandler.ChannelWritable && HasWritePending)
                {
                    DoSend(registration);
                    return true;
                }
                return false;
            };
        }

        private void DoSend(ChannelRegistration registration)
        {
            var buffer = Udp.BufferPool.Acquire();
            try
            {
                buffer.Clear();
                _pendingSend.Payload.CopyToBuffer(buffer);
                buffer.Flip();
                var writtenBytes = Channel.Send(buffer, _pendingSend.Target);
                if (Udp.Setting.TraceLogging) _log.Debug("Wrote [{0}] bytes to channel", writtenBytes);

                // Datagram channel either sends the whole message, or nothing
                if (writtenBytes == 0)
                {
                    if (_retriedSend)
                    {
                        _pendingCommander.Tell(new Udp.CommandFailed(_pendingSend));
                        _retriedSend = false;
                        _pendingSend = null;
                        _pendingCommander = null;
                    }
                    else
                    {
                        registration.EnableInterest(SocketAsyncOperation.Send);
                        _retriedSend = true;
                    }
                }
                else
                {
                    if (_pendingSend.WantsAck) _pendingCommander.Tell(_pendingSend.Ack);
                    _retriedSend = false;
                    _pendingSend = null;
                    _pendingCommander = null;
                }
            }
            finally
            {
                Udp.BufferPool.Release(buffer);
            }
        }
    }
}
