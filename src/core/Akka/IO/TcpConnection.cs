//-----------------------------------------------------------------------
// <copyright file="TcpConnection.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Pattern;
using Akka.Util.Internal;

namespace Akka.IO
{
    using static Tcp;

    /**
    *  Base class for TcpIncomingConnection and TcpOutgoingConnection.
    *
    *  INTERNAL API
    */
    internal abstract class TcpConnection : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly TcpExt _tcp;
        private readonly Socket _socket;
        private readonly bool _pullMode;
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private bool _isOutputShutdown;

        protected TcpExt Tcp
        {
            get { return _tcp; }
        }

        protected ILoggingAdapter Log
        {
            get { return _log; }
        }

        internal Socket Socket
        {
            get { return _socket; }
        }

        protected TcpConnection(TcpExt tcp, Socket socket, bool pullMode)
        {
            _tcp = tcp;
            _socket = socket;
            _pullMode = pullMode;
            _readingSuspended = pullMode;
        }

        private PendingWrite _pendingWrite = EmptyPendingWrite.Instance;
        private Tuple<IActorRef, object> _pendingAck = null;
        private bool _peerClosed;
        private bool _writingSuspended;
        private bool _readingSuspended;
        private IActorRef _interestedInResume;
        private CloseInformation _closedMessage;  // for ConnectionClosed message in postStop

        private bool WritePending()
        {
            return _pendingWrite != EmptyPendingWrite.Instance;
        }

        // STATES

        /** connection established, waiting for registration from user handler */
        private Receive WaitingForRegistration(IActorRef commander)
        {
            return message =>
            {
                var register = message as Register;
                if (register != null)
                {
                    // up to this point we've been watching the commander,
                    // but since registration is now complete we only need to watch the handler from here on
                    if (register.Handler != commander)
                    {
                        Context.Unwatch(commander);
                        Context.Watch(register.Handler);
                    }
                    if (_tcp.Settings.TraceLogging) _log.Debug("[{0}] registered as connection handler", register.Handler);

                    var info = new ConnectionInfo(register.Handler, register.KeepOpenonPeerClosed, register.UseResumeWriting);

                    // if we have resumed reading from pullMode while waiting for Register then read
                    if (_pullMode && !_readingSuspended)
                        ResumeReading(info);
                    else if (!_pullMode)
                        ReceiveAsync();

                    Context.SetReceiveTimeout(null);
                    Context.Become(Connected(info));
                    return true;
                }
                if (message is ResumeReading)
                {
                    _readingSuspended = false;
                    return true;
                }
                if (message is SuspendReading)
                {
                    _readingSuspended = true;
                    return false;
                }
                var cmd = message as CloseCommand;
                if (cmd != null)
                {
                    var info = new ConnectionInfo(commander, keepOpenOnPeerClosed: false, useResumeWriting: false);
                    HandleClose(info, Sender, cmd.Event);
                    return true;
                }
                if (message is ReceiveTimeout)
                {
                    // after sending `Register` user should watch this actor to make sure
                    // it didn't die because of the timeout
                    _log.Debug("Configured registration timeout of [{0}] expired, stopping", _tcp.Settings.RegisterTimeout);
                    Context.Stop(Self);
                    return true;
                }
                return false;
            };
        }

        /** normal connected state */
        private Receive Connected(ConnectionInfo info)
        {
            return message =>
            {
                if (HandleWriteMessages(info)(message))
                    return true;
                if (message is SuspendReading)
                {
                    SuspendReading(info);
                    return true;
                }
                if (message is ResumeReading)
                {
                    ResumeReading(info);
                    return true;
                }
                if (message is SocketReceived)
                {
                    DoRead(info, (SocketReceived)message, null);
                    return true;
                }
                var cmd = message as CloseCommand;
                if (cmd != null)
                {
                    HandleClose(info, Sender, cmd.Event);
                }
                return false;
            };
        }

        /** the peer sent EOF first, but we may still want to send */
        private Receive PeerSentEOF(ConnectionInfo info)
        {
            return message =>
            {
                if (HandleWriteMessages(info)(message))
                    return true;
                var cmd = message as CloseCommand;
                if (cmd != null)
                {
                    HandleClose(info, Sender, cmd.Event);
                    return true;
                }
                return false;
            };
        }

        /** connection is closing but a write has to be finished first */
        private Receive ClosingWithPendingWrite(ConnectionInfo info, IActorRef closeCommandor,
            ConnectionClosed closedEvent)
        {
            return message =>
            {
                if (message is SuspendReading)
                {
                    SuspendReading(info);
                    return true;
                }
                if (message is ResumeReading)
                {
                    ResumeReading(info);
                    return true;
                }
                if (message is SocketReceived)
                {
                    DoRead(info, (SocketReceived)message, closeCommandor);
                    return true;
                }
                if (message is SocketSent)
                {
                    var sent = (SocketSent)message;
                    if(_pendingAck != null)
                    {
                        _pendingAck.Item1.Tell(_pendingAck.Item2);
                        _pendingAck = null;
                    }
                    if (WritePending())    // writing is now finished
                        DoWrite(info);
                    else
                        HandleClose(info, closeCommandor, closedEvent);
                    sent.Release();
                    return true;
                }
                var updatePendingWrite = message as UpdatePendingWriteAndThen;
                if (updatePendingWrite != null)
                {
                    _pendingWrite = updatePendingWrite.RemainingWrite;
                    updatePendingWrite.Work();
                    if (WritePending())
                        DoWrite(info);
                    else
                        HandleClose(info, closeCommandor, closedEvent);
                    return true;
                }
                var writeFailed = message as WriteFileFailed;
                if (writeFailed != null)
                {
                    HandleError(info.Handler, writeFailed.E);  // rethrow exception from dispatcher task
                    return true;
                }
                if (message is Abort)
                {
                    HandleClose(info, Sender, Aborted.Instance);
                    return true;
                }
                return false;
            };
        }

        /** connection is closed on our side and we're waiting from confirmation from the other side */
        private Receive Closing(ConnectionInfo info, IActorRef closeCommandor)
        {
            return message =>
            {
                if (message is SuspendReading)
                {
                    SuspendReading(info);
                    return true;
                }
                if (message is ResumeReading)
                {
                    ResumeReading(info);
                    return true;
                }
                if (message is SocketReceived)
                {
                    DoRead(info, (SocketReceived)message, closeCommandor);
                    return true;
                }
                if (message is Abort)
                {
                    HandleClose(info, Sender, Aborted.Instance);
                    return true;
                }
                return false;
            };
        }

        private Receive HandleWriteMessages(ConnectionInfo info)
        {
            return message =>
            {
                if(message is SocketSent)
                {
                    var sent = (SocketSent)message;
                    if(_pendingAck != null)
                    {
                        _pendingAck.Item1.Tell(_pendingAck.Item2);
                        _pendingAck = null;
                    }
                    if (WritePending())
                    {
                        DoWrite(info);
                        if (!WritePending() && _interestedInResume != null)
                        {
                            _interestedInResume.Tell(WritingResumed.Instance);
                            _interestedInResume = null;
                        }
                    }
                    sent.Release();
                    return true;
                }
                var write = message as WriteCommand;
                if (write != null)
                {
                    if (_writingSuspended)
                    {
                        if (_tcp.Settings.TraceLogging) _log.Debug("Dropping write because writing is suspended");
                        Sender.Tell(write.FailureMessage);
                    }
                    else if (WritePending())
                    {
                        if (_tcp.Settings.TraceLogging) _log.Debug("Dropping write because queue is full");
                        Sender.Tell(write.FailureMessage);
                        if (info.UseResumeWriting) _writingSuspended = true;
                    }
                    else
                    {
                        _pendingWrite = CreatePendingWrite(Sender, write);
                        if (WritePending())
                            DoWrite(info);
                    }
                    return true;
                }
                if (message is ResumeWriting)
                {
                    /*
                     * If more than one actor sends Writes then the first to send this
                     * message might resume too early for the second, leading to a Write of
                     * the second to go through although it has not been resumed yet; there
                     * is nothing we can do about this apart from all actors needing to
                     * register themselves and us keeping track of them, which sounds bad.
                     *
                     * Thus it is documented that useResumeWriting is incompatible with
                     * multiple writers. But we fail as gracefully as we can.
                     */
                    _writingSuspended = false;
                    if (WritePending())
                    {
                        if (_interestedInResume == null) _interestedInResume = Sender;
                        else Sender.Tell(new CommandFailed(ResumeWriting.Instance));
                    }
                    else Sender.Tell(WritingResumed.Instance);
                    return true;
                }
                var updatePendingWrite = message as UpdatePendingWriteAndThen;
                if (updatePendingWrite != null)
                {
                    _pendingWrite = updatePendingWrite.RemainingWrite;
                    updatePendingWrite.Work();
                    if (WritePending())
                        DoWrite(info);
                    return true;
                }
                //TODO: File IO
                return false;
            };
        }

        // AUXILIARIES and IMPLEMENTATION

        /** used in subclasses to start the common machinery above once a channel is connected */
        protected void CompleteConnect(IActorRef commander,
                                       IEnumerable<Inet.SocketOption> options)
        {
            // Turn off Nagle's algorithm by default
            try
            {
                _socket.NoDelay = true;
            }
            catch (SocketException e)
            {
                _log.Debug("Could not enable TcpNoDelay: {0}", e.Message);
            }
            options.ForEach(x => x.AfterConnect(_socket));

            commander.Tell(new Connected(
                _socket.RemoteEndPoint,
                _socket.LocalEndPoint));

            Context.SetReceiveTimeout(_tcp.Settings.RegisterTimeout);
            Context.Become(WaitingForRegistration(commander));
        }

        private void SuspendReading(ConnectionInfo info)
        {
            _readingSuspended = true;
        }

        private void ResumeReading(ConnectionInfo info)
        {
            _readingSuspended = false;

            ReceiveAsync();
        }

        private void DoRead(ConnectionInfo info, SocketReceived received, IActorRef closeCommander)
        {
            //TODO: What should we do if reading is suspended with an oustanding read - this will discard the read
            //      Should probably have an 'oustanding read'
            var saea = received.EventArgs;
            if (!_readingSuspended)
            {
                Func<SocketAsyncEventArgs, int, ReadResult> innerRead = null;
                innerRead = (ea, remainingLimit) =>
                {
                    if (remainingLimit > 0)
                    {
                        var maxBufferSpace = Math.Min(_tcp.Settings.DirectBufferSize, remainingLimit);
                        var readBytes = ea.BytesTransferred;

                        if (_tcp.Settings.TraceLogging) _log.Debug("Read [{0}] bytes.", readBytes);
                        if (ea.SocketError == SocketError.Success && readBytes > 0)
                            info.Handler.Tell(new Received(ByteString.Create(ea.Buffer, ea.Offset, ea.BytesTransferred)));

                        //if (ea.SocketError == SocketError.ConnectionReset)
                        //    return EndOfStream.Instance;
                        if (ea.SocketError != SocketError.Success)
                            return new ReadError(ea.SocketError);
                        if (readBytes > 0)
                            return AllRead.Instance;
                        if (readBytes == 0)
                            return EndOfStream.Instance;

                        throw new IllegalStateException($"Unexpected value returned from read: {readBytes}");
                    }
                    return AllRead.Instance;
                };
                try
                {
                    var read = innerRead(saea, _tcp.Settings.ReceivedMessageSizeLimit);
                    if (read is AllRead)
                    {
                        if (!_pullMode)
                            ReceiveAsync();
                    }
                    else if(read is EndOfStream && _isOutputShutdown)
                    {
                        if (_tcp.Settings.TraceLogging) _log.Debug("Read returned end-of-stream, our side already closed");
                        DoCloseConnection(info.Handler, closeCommander, ConfirmedClosed.Instance);
                    }
                    else if (read is EndOfStream)
                    {
                        if (_tcp.Settings.TraceLogging) _log.Debug("Read returned end-of-stream, our side not yet closed");
                        HandleClose(info, closeCommander, PeerClosed.Instance);
                    }
                    else if(read is ReadError)
                    {
                        var error = read as ReadError;
                        HandleError(info.Handler, new SocketException((int)error.Error));
                    }
                }
                catch (SocketException e)
                {
                    HandleError(info.Handler, e);
                }
                finally
                {
                    received.Release();
                }
            }
        }

        private void DoWrite(ConnectionInfo info)
        {
            _pendingWrite = _pendingWrite.DoWrite(info);
        }

        private ConnectionClosed CloseReason()
        {
            return _isOutputShutdown ? ConfirmedClosed.Instance as ConnectionClosed 
                                     : PeerClosed.Instance;
        }

        private void HandleClose(ConnectionInfo info, IActorRef closeCommander, ConnectionClosed closedEvent)
        {
            if (closedEvent is Aborted)
            {
                if (_tcp.Settings.TraceLogging) _log.Debug("Got Abort command. RESETing connection.");
                DoCloseConnection(info.Handler, closeCommander, closedEvent);
                return;
            }
            if (closedEvent is PeerClosed && info.KeepOpenOnPeerClosed)
            {
                // report that peer closed the connection
                info.Handler.Tell(PeerClosed.Instance);
                // used to check if peer already closed its side later
                _peerClosed = true;
                Context.Become(PeerSentEOF(info));
                return;
            }
            if (WritePending())   // finish writing first
            {
                if (_tcp.Settings.TraceLogging) _log.Debug("Got Close command but write is still pending.");
                Context.Become(ClosingWithPendingWrite(info, closeCommander, closedEvent));
                return;
            }
            if (closedEvent is ConfirmedClosed) // shutdown output and wait for confirmation
            {
                if (_tcp.Settings.TraceLogging) _log.Debug("Got ConfirmedClose command, sending FIN.");

                // If peer closed first, the socket is now fully closed.
                // Also, if shutdownOutput threw an exception we expect this to be an indication
                // that the peer closed first or concurrently with this code running.
                if (_peerClosed || !SafeShutdownOutput())
                    DoCloseConnection(info.Handler, closeCommander, closedEvent);
                else Context.Become(Closing(info, closeCommander));
                return;
            }
            // close now
            if (_tcp.Settings.TraceLogging) _log.Debug("Got Close command, closing connection.");
            DoCloseConnection(info.Handler, closeCommander, closedEvent);
        }

        private void DoCloseConnection(IActorRef handler, IActorRef closeCommander, ConnectionClosed closedEvent)
        {
            if (closedEvent is Aborted) Abort();
            else
            {
                //if (!closedEvent.IsPeerClosed || closedEvent.IsConfirmed)
                //    _socket.Shutdown(SocketShutdown.Send);
                //_socket.Close();
                _socket.Shutdown(SocketShutdown.Both);
            }
            StopWith(new CloseInformation(new HashSet<IActorRef>(new[] { handler, closeCommander }.Where(x => x != null)), closedEvent));
        }

        private void HandleError(IActorRef handler, SocketException exception)
        {
            _log.Debug("Closing connection due to IO error {0}", exception);
            StopWith(new CloseInformation(new HashSet<IActorRef>(new[] {handler}), new ErrorClosed(exception.Message)));
        }

        private bool SafeShutdownOutput()
        {
            try
            {
                _isOutputShutdown = true;
                _socket.Shutdown(SocketShutdown.Send);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        //TODO: Port. Where is this used?
        /*
          @tailrec private[this] def extractMsg(t: Throwable): String =
            if (t == null) "unknown"
            else {
              t.getMessage match {
                case null | "" ⇒ extractMsg(t.getCause)
                case msg       ⇒ msg
              }
            }
        */

        private void Abort()
        {
            try
            {
                _socket.LingerState = new LingerOption(true, 0);  // causes the following close() to send TCP RST
            }
            catch (Exception e)
            {
                if (_tcp.Settings.TraceLogging) _log.Debug("setSoLinger(true, 0) failed with [{0}]", e);
            }
            _socket.Dispose();
            //_socket.Shutdown(SocketShutdown.Both);
        }

        protected void StopWith(CloseInformation closeInfo)
        {
            _closedMessage = closeInfo;
            Context.Stop(Self);
        }

        protected override void PostStop()
        {
            _socket.Dispose();
            //if (_socket.Connected)
            //    Abort();
            if (WritePending())
                _pendingWrite.Release();
            if (_closedMessage != null)
            {
                var interestedInClose = WritePending()
                    ? _closedMessage.NotificationsTo.Union(new[] {_pendingWrite.Commander})
                    : _closedMessage.NotificationsTo;
                interestedInClose.ForEach(x => x.Tell(_closedMessage.ClosedEvent));
            }
        }

        protected override void PostRestart(Exception reason)
        {
            throw new IllegalStateException("Restarting not supported for connection actors.");
        }

        private PendingWrite CreatePendingWrite(IActorRef commander, WriteCommand write)
        {
            Func<WriteCommand, WriteCommand, PendingWrite> create = null;
            create = (head, tail) =>
            {
                if (head == Write.Empty)
                    return tail == Write.Empty
                        ? EmptyPendingWrite.Instance
                        : create(tail, Write.Empty);
                var w = head as Write;
                if (w != null && w.Data.NonEmpty)
                {
                    return CreatePendingBufferWrite(commander, w.Data, w.Ack, tail);
                }
                //TODO: Port file IO
                var cwrite = head as CompoundWrite;
                if (cwrite != null)
                {
                    return create(cwrite.Head, cwrite.TailCommand);
                }
                if (w != null)  // empty write with either an ACK or a non-standard NoACK
                {
                    if (w.WantsAck) commander.Tell(w.Ack);
                    return create(tail, Write.Empty);
                }
                throw new InvalidOperationException("Non reachable code");
            };
            return create(write, Write.Empty);
        }

        private PendingWrite CreatePendingBufferWrite(IActorRef commander, ByteString data, Event ack, WriteCommand tail)
        {
            var saea = _tcp.SocketEventArgsPool.Acquire(Self);
            try
            {
                var copied = data.CopyTo(saea.Buffer, saea.Offset, saea.Count);
                saea.SetBuffer(saea.Offset, copied);
                return new PendingBufferWrite(this, Self, commander, data.Drop(copied), ack, saea, tail);
            }
            catch (Exception)
            {
                _tcp.SocketEventArgsPool.Release(saea);
                throw;
            }
        }

        private class PendingBufferWrite : PendingWrite
        {
            private readonly TcpConnection _connection;
            private readonly IActorRef _self;
            private readonly IActorRef _commander;
            private readonly ByteString _remainingData;
            private readonly object _ack;
            private readonly SocketAsyncEventArgs _saea;
            private readonly WriteCommand _tail;

            public PendingBufferWrite(
                TcpConnection connection,
                IActorRef self,
                IActorRef commander,
                ByteString remainingData,
                object ack,
                SocketAsyncEventArgs saea,
                WriteCommand tail)
            {
                _connection = connection;
                _self = self;
                _commander = commander;
                _remainingData = remainingData;
                _ack = ack;
                _saea = saea;
                _tail = tail;
            }

            public override IActorRef Commander
            {
                get { return _commander; }
            }

            public override PendingWrite DoWrite(ConnectionInfo info)
            {
                try
                {
                    if (!_connection.Socket.SendAsync(_saea))
                        _self.Tell(new SocketSent(_saea, _connection.Tcp.SocketEventArgsPool));


                    if (_connection._tcp.Settings.TraceLogging) _connection._log.Debug("Wrote [{0}] bytes to channel", _saea.Count);

                    if (_remainingData.NonEmpty)
                    {
                        var ea = _connection.Tcp.SocketEventArgsPool.Acquire(_self);
                        var copied = _remainingData.CopyTo(ea.Buffer, ea.Offset, ea.Count);
                        ea.SetBuffer(ea.Offset, copied);
                        return new PendingBufferWrite(_connection, _self, _commander, _remainingData.Drop(copied), _ack, ea, _tail);
                    }
                    _connection._pendingAck = _ack == NoAck.Instance ? null : Tuple.Create(_commander, _ack);
                    return _connection.CreatePendingWrite(_commander, _tail);

                }
                catch (SocketException e)
                {
                    _connection.HandleError(info.Handler, e);
                    return this;
                }
            }

            public override void Release()
            {
                // _connection.Tcp.BufferPool.Release(_buffer);
            }
        }

        //TODO: Port File IO

        // INTERNAL API
        private abstract class ReadResult
        {
        }
        private class EndOfStream : ReadResult
        {
            public static readonly ReadResult Instance = new EndOfStream();

            private EndOfStream()
            { }
        }
        private class AllRead : ReadResult
        {
            public static readonly AllRead Instance = new AllRead();

            private AllRead()
            { }
        }
        private class ReadError : ReadResult
        {
            public SocketError Error { get; }
            public ReadError(SocketError error)
            {
                Error = error;
            }
        }

        /**
        * Used to transport information to the postStop method to notify
        * interested party about a connection close.
        */
        protected class CloseInformation
        {
            public ISet<IActorRef> NotificationsTo { get; private set; }
            public Event ClosedEvent { get; private set; }

            public CloseInformation(ISet<IActorRef> notificationsTo, Event closedEvent)
            {
                NotificationsTo = notificationsTo;
                ClosedEvent = closedEvent;
            }
        }

        /**
        * Groups required connection-related data that are only available once the connection has been fully established.
        */
        private class ConnectionInfo
        {
            public IActorRef Handler { get; private set; }
            public bool KeepOpenOnPeerClosed { get; private set; }
            public bool UseResumeWriting { get; private set; }

            public ConnectionInfo(IActorRef handler, bool keepOpenOnPeerClosed, bool useResumeWriting)
            {
                //Registration = registration;
                Handler = handler;
                KeepOpenOnPeerClosed = keepOpenOnPeerClosed;
                UseResumeWriting = useResumeWriting;
            }
        }

        // INTERNAL MESSAGES
        private class UpdatePendingWriteAndThen : INoSerializationVerificationNeeded
        {
            public PendingWrite RemainingWrite { get; private set; }
            public Action Work { get; private set; }

            public UpdatePendingWriteAndThen(PendingWrite remainingWrite, Action work)
            {
                RemainingWrite = remainingWrite;
                Work = work;
            }
        }

        private class WriteFileFailed
        {
            public SocketException E { get; private set; }

            public WriteFileFailed(SocketException e)
            {
                E = e;
            }
        }

        private abstract class PendingWrite
        {
            public abstract IActorRef Commander { get; }
            public abstract PendingWrite DoWrite(ConnectionInfo info);
            public abstract void Release();
        }

        private class EmptyPendingWrite : PendingWrite
        {
            public static PendingWrite Instance = new EmptyPendingWrite();

            private EmptyPendingWrite()
            { }

            public override IActorRef Commander
            {
                get { throw new IllegalStateException(string.Empty); }
            }

            public override PendingWrite DoWrite(ConnectionInfo info)
            {
                throw new IllegalStateException(string.Empty);
            }

            public override void Release()
            {
                throw new IllegalStateException(string.Empty);
            }
        }



        private void ReceiveAsync()
        {
            var saea = Tcp.SocketEventArgsPool.Acquire(Self);
            if (!_socket.ReceiveAsync(saea))
                Self.Tell(new SocketReceived(saea, Tcp.SocketEventArgsPool));

        }
    }
}