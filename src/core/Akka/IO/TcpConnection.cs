//-----------------------------------------------------------------------
// <copyright file="ActorBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Event;
using Akka.Pattern;
using Akka.Util.Internal;

namespace Akka.IO
{
    /**
    *  Base class for TcpIncomingConnection and TcpOutgoingConnection.
    *
    *  INTERNAL API
    */
    internal abstract class TcpConnection : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {
        private readonly TcpExt _tcp;
        private readonly SocketChannel _channel;
        private readonly bool _pullMode;
        private readonly ILoggingAdapter _log = Context.GetLogger();

        protected TcpExt Tcp
        {
            get { return _tcp; }
        }

        protected ILoggingAdapter Log
        {
            get { return _log; }
        }

        internal SocketChannel Channel
        {
            get { return _channel; }
        }

        protected TcpConnection(TcpExt tcp, SocketChannel channel, bool pullMode)
        {
            _tcp = tcp;
            _channel = channel;
            _pullMode = pullMode;
            _readingSuspended = pullMode;
        }

        private PendingWrite _pendingWrite = EmptyPendingWrite.Instance;
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
        private Receive WaitingForRegistration(ChannelRegistration registration, IActorRef commander)
        {
            return message =>
            {
                var register = message as Tcp.Register;
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

                    var info = new ConnectionInfo(registration, register.Handler, register.KeepOpenonPeerClosed, register.UseResumeWriting);
                    
                    // if we have resumed reading from pullMode while waiting for Register then register OP_READ interest
                    if (_pullMode && !_readingSuspended) ResumeReading(info);
                    DoRead(info, null); // immediately try reading, pullMode is handled by readingSuspended
                    Context.SetReceiveTimeout(null);
                    Context.Become(Connected(info));
                    return true;
                }
                if (message is Tcp.ResumeReading)
                {
                    _readingSuspended = false;
                    return true;
                }
                if (message is Tcp.SuspendReading)
                {
                    _readingSuspended = true;
                    return false;
                }
                var cmd = message as Tcp.CloseCommand;
                if (cmd != null)
                {
                    var info = new ConnectionInfo(registration, commander, keepOpenOnPeerClosed: false, useResumeWriting: false);
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
                if (message is Tcp.SuspendReading)
                {
                    SuspendReading(info);
                    return true;
                }
                if (message is Tcp.ResumeReading)
                {
                    ResumeReading(info);
                    return true;
                }
                if (message is SelectionHandler.ChannelReadable)
                {
                    DoRead(info, null);
                    return true;
                }
                var cmd = message as Tcp.CloseCommand;
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
                var cmd = message as Tcp.CloseCommand;
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
            Tcp.ConnectionClosed closedEvent)
        {
            return message =>
            {
                if (message is Tcp.SuspendReading)
                {
                    SuspendReading(info);
                    return true;
                }
                if (message is Tcp.ResumeReading)
                {
                    ResumeReading(info);
                    return true;
                }
                if (message is SelectionHandler.ChannelReadable)
                {
                    DoRead(info, closeCommandor);
                    return true;
                }
                if (message is SelectionHandler.ChannelWritable)
                {
                    DoWrite(info);
                    if (!WritePending())    // writing is now finished
                        HandleClose(info, closeCommandor, closedEvent);
                    return true;
                }
                var updatePendingWrite = message as UpdatePendingWriteAndThen;
                if (updatePendingWrite != null)
                {
                    _pendingWrite = updatePendingWrite.RemainingWrite;
                    updatePendingWrite.Work();
                    if (WritePending())
                        info.Registration.EnableInterest(SocketAsyncOperation.Send);
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
                if (message is Tcp.Abort)
                {
                    HandleClose(info, Sender, IO.Tcp.Aborted.Instance);
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
                if (message is Tcp.SuspendReading)
                {
                    SuspendReading(info);
                    return true;
                }
                if (message is Tcp.ResumeReading)
                {
                    ResumeReading(info);
                    return true;
                }
                if (message is SelectionHandler.ChannelReadable)
                {
                    DoRead(info, closeCommandor);
                    return true;
                }
                if (message is Tcp.Abort)
                {
                    HandleClose(info, Sender, IO.Tcp.Aborted.Instance);
                    return true;
                }
                return false;
            };
        }

        private Receive HandleWriteMessages(ConnectionInfo info)
        {
            return message =>
            {
                if (message is SelectionHandler.ChannelWritable)
                {
                    if (WritePending())
                    {
                        DoWrite(info);
                        if (WritePending() && _interestedInResume != null)
                        {
                            _interestedInResume.Tell(IO.Tcp.WritingResumed.Instance);
                            _interestedInResume = null;
                        }
                    }
                    return true;
                }
                var write = message as Tcp.WriteCommand;
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
                if (message is Tcp.ResumeWriting)
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
                        else Sender.Tell(new Tcp.CommandFailed(IO.Tcp.ResumeWriting.Instance));
                    }
                    else Sender.Tell(IO.Tcp.WritingResumed.Instance);
                    return true;
                }
                var updatePendingWrite = message as UpdatePendingWriteAndThen;
                if (updatePendingWrite != null)
                {
                    _pendingWrite = updatePendingWrite.RemainingWrite;
                    updatePendingWrite.Work();
                    if (WritePending())
                        info.Registration.EnableInterest(SocketAsyncOperation.Send);
                    return true;
                }
                //TODO: File IO
                return false;
            };
        }

        // AUXILIARIES and IMPLEMENTATION

        /** used in subclasses to start the common machinery above once a channel is connected */
        protected void CompleteConnect(ChannelRegistration registration, IActorRef commander,
                                       IEnumerable<Inet.SocketOption> options)
        {
            // Turn off Nagle's algorithm by default
            try
            {
                Channel.Socket.NoDelay = true;
            }
            catch (SocketException e)
            {
                _log.Debug("Could not enable TcpNoDelay: {0}", e.Message);
            }
            options.ForEach(x => x.AfterConnect(Channel.Socket));

            commander.Tell(new Tcp.Connected(
                Channel.Socket.RemoteEndPoint,
                Channel.Socket.LocalEndPoint));

            Context.SetReceiveTimeout(_tcp.Settings.RegisterTimeout);

            // TODO: Not ported. The following need to be investigated before porting
            //if (WindowsConnectionAbortWorkaroundEnabled) 
            //    registration.EnableInterest(SocketAsyncOperation.Connect);

            Context.Become(WaitingForRegistration(registration, commander));
        }

        private void SuspendReading(ConnectionInfo info)
        {
            _readingSuspended = true;
            info.Registration.DisableInterest(SocketAsyncOperation.Receive);
        }

        private void ResumeReading(ConnectionInfo info)
        {
            _readingSuspended = false;
            info.Registration.EnableInterest(SocketAsyncOperation.Receive);
        }

        private void DoRead(ConnectionInfo info, IActorRef closeCommander)
        {
            if (!_readingSuspended)
            {
                Func<ByteBuffer, int, ReadResult> innerRead = null;
                innerRead = (buffer, remainingLimit) =>
                {
                    if (remainingLimit > 0)
                    {
                        buffer.Clear();
                        var maxBufferSpace = Math.Min(_tcp.Settings.DirectBufferSize, remainingLimit);
                        buffer.Limit(maxBufferSpace);
                        var readBytes = Channel.Read(buffer);
                        buffer.Flip();

                        if (_tcp.Settings.TraceLogging) _log.Debug("Read [{0}] bytes.", readBytes);
                        if (readBytes > 0)
                            info.Handler.Tell(new Tcp.Received(ByteString.Create(buffer)));

                        if (readBytes == maxBufferSpace)
                        {
                            return _pullMode
                                ? MoreDataWaiting.Instance
                                : innerRead(buffer, remainingLimit - maxBufferSpace);
                        }
                        if (readBytes >= 0)
                            return AllRead.Instance;
                        if (readBytes == -1)
                            return EndOfStream.Instance;

                        throw new IllegalStateException("Unexpected value returned from read: " + readBytes);
                    }
                    return MoreDataWaiting.Instance;
                };
                var buffr = _tcp.BufferPool.Acquire();
                try
                {
                    var read = innerRead(buffr, _tcp.Settings.ReceivedMessageSizeLimit);
                    if (read is AllRead)
                    {
                        if (!_pullMode)
                            info.Registration.EnableInterest(SocketAsyncOperation.Receive);
                    }
                    if (read is MoreDataWaiting)
                    {
                        if (!_pullMode)
                            Self.Tell(SelectionHandler.ChannelReadable.Instance);
                    }

                    // TODO: Port. Socket does not seem to expose (isOutputShutdown). It is passed as 'how' argument to Socket.Shutdown, but not exposed. 
                    // case EndOfStream if channel.socket.isOutputShutdown ⇒
                    //    if (TraceLogging) log.debug("Read returned end-of-stream, our side already closed")
                    //    doCloseConnection(info.handler, closeCommander, ConfirmedClosed)

                    if (read is EndOfStream)
                    {
                        if (_tcp.Settings.TraceLogging) _log.Debug("Read returned end-of-stream, our side not yet closed");
                        HandleClose(info, closeCommander, IO.Tcp.PeerClosed.Instance);
                    }
                }
                catch (IOException e)
                {
                    HandleError(info.Handler, e);
                }
                finally
                {
                    Tcp.BufferPool.Release(buffr);
                }
            }
        }

        private void DoWrite(ConnectionInfo info)
        {
            _pendingWrite = _pendingWrite.DoWrite(info);
        }

        private Tcp.ConnectionClosed CloseReason()
        {
            // TODO: Port. Socket does not seem to expose (isOutputShutdown). It is passed as 'how' argument to Socket.Shutdown, but not exposed. 
            // if (channel.socket.isOutputShutdown) ConfirmedClosed
            return IO.Tcp.PeerClosed.Instance;
        }

        private void HandleClose(ConnectionInfo info, IActorRef closeCommander, Tcp.ConnectionClosed closedEvent)
        {
            if (closedEvent is Tcp.Aborted)
            {
                if (_tcp.Settings.TraceLogging) _log.Debug("Got Abort command. RESETing connection.");
                DoCloseConnection(info.Handler, closeCommander, closedEvent);
                return;
            }
            if (closedEvent is Tcp.PeerClosed && info.KeepOpenOnPeerClosed)
            {
                // report that peer closed the connection
                info.Handler.Tell(IO.Tcp.PeerClosed.Instance);
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
            if (closedEvent is Tcp.ConfirmedClosed) // shutdown output and wait for confirmation
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

        private void DoCloseConnection(IActorRef handler, IActorRef closeCommander, Tcp.ConnectionClosed closedEvent)
        {
            if (closedEvent is Tcp.Aborted) Abort();
            else _channel.Close();
            StopWith(new CloseInformation(new HashSet<IActorRef>(new[] { handler, closeCommander }.Where(x => x != null)), closedEvent));
        }

        private void HandleError(IActorRef handler, IOException exception)
        {
            _log.Debug("Closing connection due to IO error {0}", exception);
            StopWith(new CloseInformation(new HashSet<IActorRef>(new[] {handler}), new Tcp.ErrorClosed(exception.Message)));
        }

        private bool SafeShutdownOutput()
        {
            try
            {
                Channel.Socket.Shutdown(SocketShutdown.Send);
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
                Channel.Socket.LingerState = new LingerOption(true, 0);  // causes the following close() to send TCP RST
            }
            catch (Exception e)
            {
                if (_tcp.Settings.TraceLogging) _log.Debug("setSoLinger(true, 0) failed with [{0}]", e);
            }
            Channel.Close();
        }

        protected void StopWith(CloseInformation closeInfo)
        {
            _closedMessage = closeInfo;
            Context.Stop(Self);
        }

        protected override void PostStop()
        {
            if (Channel.IsOpen())
                Abort();
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

        private PendingWrite CreatePendingWrite(IActorRef commander, Tcp.WriteCommand write)
        {
            Func<Tcp.WriteCommand, Tcp.WriteCommand, PendingWrite> create = null;
            create = (head, tail) =>
            {
                if (head == IO.Tcp.Write.Empty)
                    return tail == IO.Tcp.Write.Empty
                        ? EmptyPendingWrite.Instance
                        : create(tail, IO.Tcp.Write.Empty);
                var w = head as Tcp.Write;
                if (w != null && w.Data.NonEmpty)
                {
                    return CreatePendingBufferWrite(commander, w.Data, w.Ack, tail);
                }
                //TODO: Port file IO
                var cwrite = head as Tcp.CompoundWrite;
                if (cwrite != null)
                {
                    return create(cwrite.Head, cwrite.TailCommand);
                }
                if (w != null)  // empty write with either an ACK or a non-standard NoACK
                {
                    if (w.WantsAck) commander.Tell(w.Ack);
                    create(tail, IO.Tcp.Write.Empty);
                }
                throw new Exception("Non reachable code");
            };
            return create(write, IO.Tcp.Write.Empty);
        }

        private PendingWrite CreatePendingBufferWrite(IActorRef commander, ByteString data, Tcp.Event ack, Tcp.WriteCommand tail)
        {
            var buffer = _tcp.BufferPool.Acquire();
            try
            {
                var copied = data.CopyToBuffer(buffer);
                buffer.Flip();
                return new PendingBufferWrite(this, commander, data.Drop(copied), ack, buffer, tail);
            }
            catch (Exception)
            {
                _tcp.BufferPool.Release(buffer);
                throw;
            }
        }

        private class PendingBufferWrite : PendingWrite
        {
            private readonly TcpConnection _connection;
            private readonly IActorRef _commander;
            private readonly ByteString _remainingData;
            private readonly object _ack;
            private readonly ByteBuffer _buffer;
            private readonly Tcp.WriteCommand _tail;

            public PendingBufferWrite(
                TcpConnection connection,
                IActorRef commander,
                ByteString remainingData,
                object ack,
                ByteBuffer buffer,
                Tcp.WriteCommand tail)
            {
                _connection = connection;
                _commander = commander;
                _remainingData = remainingData;
                _ack = ack;
                _buffer = buffer;
                _tail = tail;
            }

            public override IActorRef Commander
            {
                get { return _commander; }
            }

            public override PendingWrite DoWrite(ConnectionInfo info)
            {
                Func<ByteString, PendingWrite> writeToChannel = null;
                writeToChannel = data =>
                {
                    var writtenBytes = _connection.Channel.Write(_buffer); // at first we try to drain the remaining bytes from the buffer
                    if (_connection._tcp.Settings.TraceLogging) _connection._log.Debug("Wrote [{0}] bytes to channel", writtenBytes);
                    if (_buffer.HasRemaining)
                    {
                        // we weren't able to write all bytes from the buffer, so we need to try again later
                        return data == _remainingData
                            ? this
                            : new PendingBufferWrite(_connection, _commander, data, _ack, _buffer, _tail); // copy with updated remainingData
                    }
                    if (data.NonEmpty)
                    {
                        _buffer.Clear();
                        var copied = data.CopyToBuffer(_buffer);
                        _buffer.Flip();
                        return writeToChannel(data.Drop(copied));
                    }
                    if (!(_ack is Tcp.NoAck)) _commander.Tell(_ack);
                    Release();
                    return _connection.CreatePendingWrite(_commander, _tail);
                };
                try
                {
                    var next = writeToChannel(_remainingData);
                    if (next != EmptyPendingWrite.Instance)
                        info.Registration.EnableInterest(SocketAsyncOperation.Send);
                    return next;
                }
                catch (IOException e)
                {
                    _connection.HandleError(info.Handler, e);
                    return this;
                }
            }

            public override void Release()
            {
                _connection.Tcp.BufferPool.Release(_buffer);
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
        private class MoreDataWaiting : ReadResult
        {
            public static readonly ReadResult Instance = new MoreDataWaiting();

            private MoreDataWaiting()
            { }
        }

        /**
        * Used to transport information to the postStop method to notify
        * interested party about a connection close.
        */
        protected class CloseInformation
        {
            public ISet<IActorRef> NotificationsTo { get; private set; }
            public Tcp.Event ClosedEvent { get; private set; }

            public CloseInformation(ISet<IActorRef> notificationsTo, Tcp.Event closedEvent)
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
            public ChannelRegistration Registration { get; private set; }
            public IActorRef Handler { get; private set; }
            public bool KeepOpenOnPeerClosed { get; private set; }
            public bool UseResumeWriting { get; private set; }

            public ConnectionInfo(ChannelRegistration registration, IActorRef handler, bool keepOpenOnPeerClosed, bool useResumeWriting)
            {
                Registration = registration;
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
            public IOException E { get; private set; }

            public WriteFileFailed(IOException e)
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
    }
}