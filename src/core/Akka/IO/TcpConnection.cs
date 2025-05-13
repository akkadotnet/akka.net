//-----------------------------------------------------------------------
// <copyright file="TcpConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Pattern;
using Akka.Util;
using Akka.Util.Internal;

#nullable enable

namespace Akka.IO
{
    using static Akka.IO.Tcp;
    using ByteBuffer = ArraySegment<byte>;

    /// <summary>
    /// INTERNAL API: Base class for TcpIncomingConnection and TcpOutgoingConnection.
    /// 
    /// TcpConnection is an actor abstraction over single connection between TCP server and client. 
    /// Since actors are processing messages in synchronous fashion, they are way to provide thread 
    /// safety over sockets and <see cref="SocketAsyncEventArgs"/>.
    /// 
    /// Every TcpConnection gets assigned a single socket fields and pair of <see cref="SocketAsyncEventArgs"/>,
    /// allocated once per lifetime of the connection actor:
    /// 
    /// - <see cref="ReceiveArgs"/> used only for receiving data. It has assigned buffer, rent from 
    ///   <see cref="TcpExt"/> once and recycled back upon actor termination. Once data has been received, it's 
    ///   copied to a separate <see cref="ByteString"/> object (so it's NOT a zero-copy operation).
    /// - <see cref="SendArgs"/> used only for sending data. Unlike receive args, it doesn't have any buffer 
    ///   assigned. Instead it uses treats incoming data as a buffer (it's safe due to immutable nature of
    ///   <see cref="ByteString"/> object). Therefore writes don't allocate any byte buffers.
    /// 
    /// Similar approach can be found on other networking libraries (i.e. System.IO.Pipelines and EventStore).
    /// Both buffers and <see cref="SocketAsyncEventArgs"/> are pooled to reduce GC pressure.
    /// </summary>
    internal abstract class TcpConnection : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        #region Ack‑aware SAEA

        private sealed class AckSocketAsyncEventArgs : SocketAsyncEventArgs
        {
            public readonly List<(IActorRef Commander, object Ack)> PendingAcks = new(8);
            public void ClearAcks() => PendingAcks.Clear();
        }

        #endregion

        #region completion msgs

        private sealed class SocketReceiveCompleted(int bytes, SocketError error) : INoSerializationVerificationNeeded
        {
            public int Bytes { get; } = bytes;
            public SocketError Error { get; } = error;
        }

        private sealed class SocketSendCompleted(int bytes, SocketError error) : INoSerializationVerificationNeeded
        {
            public int Bytes { get; } = bytes;
            public SocketError Error { get; } = error;
        }

        #endregion

        protected readonly TcpExt Tcp;
        protected readonly Socket Socket;
        protected ILoggingAdapter Log { get; } = Context.GetLogger();

        private readonly ArrayPool<byte> _bufferPool = ArrayPool<byte>.Shared;

        private readonly Queue<(WriteCommand Cmd, IActorRef Sender)> _pendingWrites;

        private readonly byte[] _receiveBuffer;
        private SocketAsyncEventArgs _receiveArgs;
        private AckSocketAsyncEventArgs _sendArgs;
        
        private IActorRef _watchedActor = Context.System.DeadLetters;
        private readonly int _maxWriteCapacity;

        private volatile bool _sending;
        private volatile bool _closingRequested;
        private volatile bool _peerClosed;

        private readonly bool _traceLogging;
        
        // so we don't try to close the socket a second time during PostStop
        private bool _socketAlreadyClosed;

        private CloseInformation? _closedMessage; // for ConnectionClosed message in postStop
        
        private static readonly IOException DroppingWriteBecauseClosingException =
            new("Dropping write because the connection is closing");

        private static readonly IOException DroppingWriteBecauseWritingIsSuspendedException =
            new("Dropping write because writing is suspended");

        private static readonly IOException DroppingWriteBecauseQueueIsFullException =
            new("Dropping write because queue is full");

        protected TcpConnection(TcpExt tcp, Socket socket, Option<int> writeCommandsBufferMaxSize)
        {
            _maxWriteCapacity = writeCommandsBufferMaxSize.GetOrElse(tcp.Settings.WriteCommandsQueueMaxSize);
            _pendingWrites = _maxWriteCapacity > 0
                ? new Queue<(WriteCommand Cmd, IActorRef Sender)>(_maxWriteCapacity)
                : new Queue<(WriteCommand Cmd, IActorRef Sender)>(); // unbounded
;            _traceLogging = tcp.Settings.TraceLogging;

            Tcp = tcp;
            Socket = socket ?? throw new ArgumentNullException(nameof(socket));
            const int DefaultBufferSize = 64 * 1024; // 64 KiB – matches legacy DirectBufferSize
            _receiveBuffer = _bufferPool.Rent(DefaultBufferSize);
            InitSocketEventArgs();
        }

        private void InitSocketEventArgs()
        {
            _receiveArgs = new SocketAsyncEventArgs();
            _receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
            _receiveArgs.UserToken = Self;
            _receiveArgs.Completed += OnCompleted;

            _sendArgs = new AckSocketAsyncEventArgs();
            _sendArgs.UserToken = Self;
            _sendArgs.Completed += OnCompleted;
        }

        private static void OnCompleted(object? sender, SocketAsyncEventArgs e)
        {
            if (e.UserToken is not IActorRef self) return;
            switch (e.LastOperation)
            {
                case SocketAsyncOperation.Receive:
                    self.Tell(new SocketReceiveCompleted(e.BytesTransferred, e.SocketError));
                    break;
                case SocketAsyncOperation.Send:
                    self.Tell(new SocketSendCompleted(e.BytesTransferred, e.SocketError));
                    break;
                default:
                    self.Tell(new ErrorClosed($"Unexpected socket op {e.LastOperation}"));
                    break;
            }
        }
        
        /// <summary>
        /// Returns <c>true</c> if a write is in-progress over the wire or if we have writes pending in the queue.
        /// </summary>
        public bool IsWritePending => _sending || _pendingWrites.Count > 0;

        protected void SignDeathPact(IActorRef actor)
        {
            UnsignDeathPact();
            _watchedActor = actor;
            Context.Watch(actor);
        }

        protected void UnsignDeathPact()
        {
            if (!ReferenceEquals(_watchedActor, Context.System.DeadLetters)) Context.Unwatch(_watchedActor);
        }

        private void IssueReceive()
        {
            if(_peerClosed) return; // can't read if peer is closed
            if (!Socket.ReceiveAsync(_receiveArgs))
                Self.Tell(new SocketReceiveCompleted(_receiveArgs.BytesTransferred, _receiveArgs.SocketError));
        }
        
        private void HandleRead(IActorRef handler, SocketReceiveCompleted rc)
        {
            if(_traceLogging)
                Log.Debug("Received {0} bytes from {1}", rc.Bytes, Socket.RemoteEndPoint);
            
            if (rc.Error != SocketError.Success)
            {
                Log.Error("Closing connection due to IO error {0}", rc.Error);
                Self.Tell(new ErrorClosed(rc.Error.ToString()));
                return;
            }

            if (rc.Bytes == 0)
            {
                _peerClosed = true;
                
                // signal to the handler that the peer has closed the connection
                Self.Tell(PeerClosed.Instance);
                return;
            }

            var bs = ByteString.CopyFrom(_receiveBuffer, 0, rc.Bytes);
            handler.Tell(new Received(bs));
            IssueReceive();
        }

        private void IssueSend(IList<ArraySegment<byte>> buffers)
        {
            _sendArgs.BufferList = buffers;
            if (!Socket.SendAsync(_sendArgs))
                Self.Tell(new SocketSendCompleted(_sendArgs.BytesTransferred, _sendArgs.SocketError));
        }

        private void TrySendNext()
        {
            if (IsWritePending) return;

            var maxBytes = _receiveBuffer.Length;
            var accumulated = 0;
            var batch = new List<ByteString>();
            _sendArgs.ClearAcks();

            while (_pendingWrites.Count > 0 && accumulated < maxBytes)
            {
                var (cmd, snd) = _pendingWrites.Peek();
                switch (cmd)
                {
                    case Write w when !w.Data.IsEmpty:
                        int wouldBe = accumulated + w.Data.Count;
                        if (wouldBe > maxBytes && batch.Count > 0) goto done;
                        _pendingWrites.Dequeue();
                        batch.Add(w.Data);
                        accumulated = wouldBe;
                        if (!Equals(w.Ack, NoAck.Instance))
                            _sendArgs.PendingAcks.Add((snd, w.Ack));
                        break;
                    case Write w:
                        _pendingWrites.Dequeue();
                        if (w.WantsAck) snd.Tell(w.Ack);
                        break;
                    default:
                        _pendingWrites.Dequeue();
                        snd.Tell(new CommandFailed(cmd));
                        break;
                }
            }

            done:
            if (batch.Count == 0)
            {
                return;
            }

            _sending = true;
            var payload = FlattenByteStrings(batch);
            IssueSend(payload);
        }
        
        /// <summary>
        /// Called when the socket closes before we have processed all pending writes.
        /// </summary>
        private void FailUnprocessedPendingWrites(Exception cause)
        {
            foreach (var (cmd, ack) in _pendingWrites)
            {
                var failure = cmd.FailureMessage.WithCause(cause);
                ack.Tell(failure);
            }
            _pendingWrites.Clear();
        }

        private void HandleSendCompleted(SocketSendCompleted socketSendCompleted)
        {
            _sending = false;
            
            if(_traceLogging)
                Log.Debug("Sent {0} bytes to {1}", socketSendCompleted.Bytes, Socket.RemoteEndPoint);
            
            // check for errors
            if (socketSendCompleted.Error != SocketError.Success)
            {
                Log.Error("Closing connection due to IO error {0} received during send", socketSendCompleted.Error);
                Self.Tell(new ErrorClosed(socketSendCompleted.Error.ToString()));
                return;
            }
            
            foreach (var (c, ack) in _sendArgs.PendingAcks)
                c.Tell(ack);
            _sendArgs.ClearAcks();
            _sendArgs.BufferList.Clear();
            
            TrySendNext();
            TryCloseIfDone();
        }
        
        private void DeliverCloseMessages()
        {
            if (_closedMessage == null) return;
            foreach (var handler in _closedMessage.NotificationsTo)
            {
                handler.Tell(_closedMessage.ClosedEvent);
            }
        }

        private void TryCloseIfDone()
        {
            if (!_closingRequested) return;
            if (_sending || _pendingWrites.Count > 0) return;
            if (!_peerClosed) return;
            Context.Stop(Self);
        }

        private static IList<ArraySegment<byte>> FlattenByteStrings(List<ByteString> parts)
        {
            if (parts.Count == 1)
                return parts[0].Buffers;

            return parts.SelectMany(c => c.Buffers).ToArray();
        }

        // STATES

        private bool TryBuffer(WriteCommand cmd, IActorRef sender)
        {
            // buffer is unlimited OR we're below the max write capacity
            if (_maxWriteCapacity < 0 || _pendingWrites.Count < _maxWriteCapacity)
            {
                _pendingWrites.Enqueue((cmd, sender));
                return true;
            }
            
            // buffer is full
            return false;
        }

        /// <summary>
        /// Connection established, waiting for registration from user handler.
        /// </summary>
        private Receive WaitingForRegistration(IActorRef commander)
        {
            return message =>
            {
                switch (message)
                {
                    case Register register:
                        // up to this point we've been watching the commander,
                        // but since registration is now complete we only need to watch the handler from here on
                        if (!Equals(register.Handler, commander))
                            SignDeathPact(register.Handler); // will unsign death pact with commander automatically

                        if (_traceLogging) Log.Debug("[{0}] registered as connection handler", register.Handler);

                        var registerInfo = new ConnectionInfo(register.Handler, register.KeepOpenOnPeerClosed,
                            register.UseResumeWriting);
                        
                        // we set a default close message here in case the actor dies before we get a close message
                        // this will prevent close messages from going missing
                        _closedMessage =
                            new CloseInformation(new HashSet<IActorRef>([register.Handler]), Aborted.Instance);

                        Context.SetReceiveTimeout(null);
                        Context.Become(Connected(registerInfo));
                        // If there is something buffered before we got Register message - put it all to the socket
                        TrySendNext();
                        // start reading
                        IssueReceive();
                        return true;
                    case CloseCommand cmd:
                        var info = new ConnectionInfo(commander, keepOpenOnPeerClosed: false, useResumeWriting: false);
                        HandleCloseCommand(info, Sender, cmd);
                        return true;
                    case ReceiveTimeout:
                        // after sending `Register` user should watch this actor to make sure
                        // it didn't die because of the timeout
                        Log.Debug("Configured registration timeout of [{0}] expired, stopping",
                            Tcp.Settings.RegisterTimeout);
                        Context.Stop(Self);
                        return true;
                    case WriteCommand write:
                        // Have to buffer writes until registration
                        var buffered = TryBuffer(write, Sender);
                        if (!buffered)
                        {
                            DropWrite(write);
                        }
                        else
                        {
                            Log.Warning("Received Write command before Register command. " +
                                        "It will be buffered until Register will be received (buffered write size is {0} bytes)",
                                write.Bytes);
                        }

                        return true;
                    case Terminated t:
                    {
                        // if the handler dies before registration, we need to stop
                        if (t.ActorRef.Equals(commander))
                        {
                            Log.Debug("Handler [{0}] died before registration, stopping", t.ActorRef);
                            Context.Stop(Self);
                        }
                        else
                        {
                            // ignore
                            Log.Debug("Handler [{0}] died before registration, ignoring", t.ActorRef);
                        }

                        return true;
                    }
                    default: return false;
                }
            };
        }

        /// <summary>
        /// Normal connected state.
        /// </summary>
        private Receive Connected(ConnectionInfo info)
        {
            return message =>
            {
                switch (message)
                {
                    case SocketReceiveCompleted r:
                        HandleRead(info.Handler, r);
                        return true;
                    case WriteCommand write:
                        var buffered = TryBuffer(write, Sender);
                        if (!buffered)
                        {
                            DropWrite(write);
                        }
                        else
                        {
                            Log.Warning("Received Write command before Register command. " +
                                        "It will be buffered until Register will be received (buffered write size is {0} bytes)",
                                write.Bytes);
                        }
                        return true;
                    case SocketSendCompleted sendCompleted:
                        HandleSendCompleted(sendCompleted);
                        return true;
                    case CloseCommand cmd: // we are trying to close the socket first
                        HandleCloseCommand(info, Sender, cmd);
                        return true;
                    case ConnectionClosed closed: // peer is closing the socket
                        HandleCloseEvent(info, Sender, closed);
                        return true;
                    case SuspendReading:
                    case ResumeReading:
                        // no-ops
                        return true;
                    case Terminated t: // handler died
                    {
                        Log.Debug("Handler [{0}] died, stopping", t.ActorRef);
                        Context.Stop(Self);
                        return true;
                    }
                    default: return false;
                }
            };
        }

        /// <summary>
        /// Connection is closing but a write has to be finished first
        /// </summary>
        private Receive Closing(ConnectionInfo info, bool confirmClose)
        {
            return message =>
            {
                switch (message)
                {
                    case SocketReceiveCompleted r:
                        HandleRead(info.Handler, r);
                        return true;
                    case SocketSendCompleted s:
                        HandleSendCompleted(s);
                        if (confirmClose && !IsWritePending)
                        {
                            // done writing, so we can now half-close the socket
                            if (_traceLogging) Log.Debug("Running in close-confirm mode, half-closing socket for writes");
                            
                            // We will need to get an EOF
                            Socket.Shutdown(SocketShutdown.Send);
                        }
                        return true;
                    case WriteCommand write:
                        DropWrite(write);
                        return true;
                    case SuspendReading:
                    case ResumeReading:
                        // no-ops
                        return true;
                    case Abort a:
                        HandleCloseCommand(info, Sender, a);
                        return true;
                    case ConnectionClosed:
                        _peerClosed = true;
                        if (!IsWritePending)
                        {
                            Log.Debug("Peer closed connection, stopping");;
                            Context.Stop(Self);
                        }
                        return true;
                    case Terminated t: // handler died
                    {
                        Log.Debug("Handler [{0}] died, stopping", t.ActorRef);
                        Context.Stop(Self);
                        return true;
                    }
                    default: 
                        return false;
                }
            };
        }

        private enum DropReason
        {
            QueueFull = 1,
            Closing = 2,
            WritingSuspended = 3
        }
        
        private static string GetDropReasonMessage(DropReason reason)
        {
            return reason switch
            {
                DropReason.QueueFull => "queue is full",
                DropReason.Closing => "connection is closing",
                DropReason.WritingSuspended => "writing is suspended",
                _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
            };
        }
        
        private static IOException GetDropMessageException(DropReason reason)
        {
            return reason switch
            {
                DropReason.QueueFull => DroppingWriteBecauseQueueIsFullException,
                DropReason.Closing => DroppingWriteBecauseClosingException,
                DropReason.WritingSuspended => DroppingWriteBecauseWritingIsSuspendedException,
                _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
            };
        }

        private void DropWrite(WriteCommand write, DropReason reason = DropReason.QueueFull)
        {
            if (_traceLogging) Log.Debug("Dropping write because {0}", GetDropReasonMessage(reason));
            Sender.Tell(write.FailureMessage.WithCause(GetDropMessageException(reason)));
        }

        // AUXILIARIES and IMPLEMENTATION

        /// <summary>
        /// Used in subclasses to start the common machinery above once a channel is connected
        /// </summary>
        protected void CompleteConnect(IActorRef commander, IEnumerable<Inet.SocketOption> options)
        {
            // Turn off Nagle's algorithm by default
            try
            {
                Socket.NoDelay = true;
            }
            catch (SocketException e)
            {
                Log.Debug(e, "Could not enable TcpNoDelay: {0}", e.Message);
            }

            foreach (var option in options)
            {
                option.AfterConnect(Socket);
            }

            commander.Tell(new Connected(Socket.RemoteEndPoint, Socket.LocalEndPoint));

            Context.SetReceiveTimeout(Tcp.Settings.RegisterTimeout);
            Context.Become(WaitingForRegistration(commander));
        }
        
        /// <summary>
        /// We are in the driver's seat and want to close the connection.
        /// </summary>
        private void HandleCloseCommand(ConnectionInfo info, IActorRef sender, CloseCommand cmd)
        {
            // we are closing the connection, so set the hook now.
            _closedMessage  = new CloseInformation(new HashSet<IActorRef> { info.Handler, sender }, cmd.Event);
            
            switch (cmd)
            {
                case Abort _:
                {
                    if (_traceLogging) Log.Debug("Got Abort command. RESETing connection.");
                    Abort();
                    break;
                }
                case Close:
                {
                    _closingRequested = true;
                    if (IsWritePending) // if we have writes pending, we need to send them
                    {
                        if(_traceLogging) Log.Debug("Got Close command but writes are still pending.");
                        Become(Closing(info, false));
                    }
                    else
                    {
                        // if we are not writing, we can close the socket right away
                        if (_traceLogging) Log.Debug("Got Close command, closing connection.");
                        CloseSocket();
                        Context.Stop(Self);
                    }
                    break;
                }
                case ConfirmedClose:
                {
                    _closingRequested = true;
                    if(_traceLogging) Log.Debug("Got ConfirmedClose command - waiting for peer to terminate.");
                    Become(Closing(info, true));
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(cmd), cmd, "Unknown close command");
            }
        }

        /// <summary>
        /// Someone else is closing the connection, so we need to handle it.
        /// </summary>
        private void HandleCloseEvent(ConnectionInfo info, IActorRef closeCommander, ConnectionClosed closedEvent)
        {
            _closedMessage = new CloseInformation(new HashSet<IActorRef> { info.Handler, closeCommander }, closedEvent);
            _closingRequested = true;
            
            switch (closedEvent)
            {
                case Aborted:
                {
                    if (_traceLogging) Log.Debug("Got Aborted event. RESETing connection.");
                    Context.Stop(Self);
                    break;
                }
                case PeerClosed:
                {
                    if (_traceLogging) Log.Debug("Got PeerClosed event. Closing connection.");
                    _peerClosed = true;
                    if (info.KeepOpenOnPeerClosed)
                    {
                        // we are not closing the socket, but we need to stop reading
                        Context.Become(Closing(info, false));
                    }
                    else
                    {
                        // we are closing the socket
                        CloseSocket();
                        Context.Stop(Self);
                    }
                    break;
                }
                default:
                {
                    // log a warning - someone sent us the wrong message type
                    Log.Warning("Received unexpected ConnectionClosed event type [{0}]", closedEvent.GetType());
                    
                    // closing connection anyway, I guess
                    Become(Closing(info, false));
                    break;
                }
            }
        }
        
        /* Mostly called from outside */
        protected void StopWith(CloseInformation closeInfo)
        {
            _closedMessage = closeInfo;
            UnsignDeathPact();
            Context.Stop(Self);
        }
        
        private void Abort()
        {
            try
            {
                Socket.LingerState = new LingerOption(true, 0); // causes the following close() to send TCP RST
            }
            catch (Exception e)
            {
                if (_traceLogging) Log.Debug("setSoLinger(true, 0) failed with [{0}]", e);
            }
            Context.Stop(Self);
        }

        private void CloseSocket()
        {
            if (_socketAlreadyClosed) return;
            _socketAlreadyClosed = true;
            try { Socket.Shutdown(SocketShutdown.Both); } catch { /* ignore */ }
            try{ Socket.Dispose(); } catch { /* ignore */ }
        }

        protected override void PostStop()
        {
            if (_traceLogging) Log.Debug("Stopping connection actor [{0}]", Self);
            CloseSocket(); // just in case we didn't shut ourselves down gracefully first
            _receiveArgs.Dispose();
            _sendArgs.Dispose();
            _bufferPool.Return(_receiveBuffer);
            
            FailUnprocessedPendingWrites(DroppingWriteBecauseClosingException);
            if(_closedMessage != null)
            {
                // if we have a close message, we need to deliver it
                DeliverCloseMessages();
            }
            
            base.PostStop();
        }

        protected override void PostRestart(Exception reason)
        {
            throw new IllegalStateException("Restarting not supported for connection actors.");
        }
        
        /// <summary>
        /// Groups required connection-related data that are only available once the connection has been fully established.
        /// </summary>
        private sealed class ConnectionInfo
        {
            public readonly IActorRef Handler;
            public readonly bool KeepOpenOnPeerClosed;
            public readonly bool UseResumeWriting;

            public ConnectionInfo(IActorRef handler, bool keepOpenOnPeerClosed, bool useResumeWriting)
            {
                Handler = handler;
                KeepOpenOnPeerClosed = keepOpenOnPeerClosed;
                UseResumeWriting = useResumeWriting;
            }
        }

        /// <summary>
        /// Used to transport information to the postStop method to notify
        /// interested party about a connection close.
        /// </summary>
        protected sealed class CloseInformation
        {
            public ISet<IActorRef> NotificationsTo { get; }

            public Event ClosedEvent { get; }

            public CloseInformation(ISet<IActorRef> notificationsTo, Tcp.Event closedEvent)
            {
                NotificationsTo = notificationsTo;
                ClosedEvent = closedEvent;
            }
        }
    }
}