//-----------------------------------------------------------------------
// <copyright file="SocketChannel.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#if AKKAIO
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Util;

namespace Akka.IO
{
    /* 
     * SocketChannel does not exists in the .NET BCL - This class is an adapter to hide the differences in CLR & JVM IO.
     * This implementation uses blocking IO calls, and then catch SocketExceptions if the socket is set to non blocking. 
     * This might introduce performance issues, with lots of thrown exceptions
     * TODO: Implements this class with .NET Async calls
     */
    /// <summary>
    /// TBD
    /// </summary>
    public class SocketChannel 
    {
        private readonly Socket _socket;
        private IActorRef _connection;
        private bool _connected;
        private IAsyncResult _connectResult;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="socket">TBD</param>
        public SocketChannel(Socket socket) 
        {
            _socket = socket;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static SocketChannel Open()
        {
            // TODO: Mono does not support IPV6 Uris correctly https://bugzilla.xamarin.com/show_bug.cgi?id=43649 (Aaronontheweb 9/13/2016)
            if (RuntimeDetector.IsMono)
                return new SocketChannel(new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp));
            return new SocketChannel(new Socket(SocketType.Stream, ProtocolType.Tcp));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="block">TBD</param>
        /// <returns>TBD</returns>
        public SocketChannel ConfigureBlocking(bool block)
        {
            _socket.Blocking = block;
            return this;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Socket Socket
        {
            get { return _socket; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="initialOps">TBD</param>
        public void Register(IActorRef connection, SocketAsyncOperation? initialOps)
        {
            _connection = connection;
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public virtual bool IsOpen()
        {
            return _connected;
        }

#if CORECLR
        public IAsyncResult BeginSocketConnect(Socket socket, EndPoint address, AsyncCallback callback, object state)
        {
            return socket.ConnectAsync(address).AsApm(callback, state);
        }

        public void EndSocketConnect(IAsyncResult asyncResult)
        {
            ((Task)asyncResult).Wait();
        }
#endif

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="address">TBD</param>
        /// <returns>TBD</returns>
        public bool Connect(EndPoint address)
        {
#if CORECLR
            _connectResult = BeginSocketConnect(_socket, address, ar => { }, null);
#else
            _connectResult = _socket.BeginConnect(address, ar => { }, null);
#endif
            if (_connectResult.CompletedSynchronously)
            {
#if CORECLR
                EndSocketConnect(_connectResult);
#else
                _socket.EndConnect(_connectResult);
#endif
                _connected = true;
                return true;
            }
            return false;
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public bool FinishConnect()
        {
            if (_connectResult.CompletedSynchronously)
                return true;
            if (_connectResult.IsCompleted)
            {
#if CORECLR
                EndSocketConnect(_connectResult);
#else
                _socket.EndConnect(_connectResult);
#endif
                _connected = true;
            }
            return _connected;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public SocketChannel Accept()
        {
            //TODO: Investigate. If we don't wait 1ms we get intermittent test failure in TcpListenerSpec.  
            return _socket.Poll(1, SelectMode.SelectRead)
                ? new SocketChannel(_socket.Accept()) {_connected = true}
                : null;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="buffer">TBD</param>
        /// <returns>TBD</returns>
        public int Read(ByteBuffer buffer)
        {
            if (!_socket.Poll(0, SelectMode.SelectRead))
                return 0;
            var data = new byte[buffer.Remaining];
            var length = _socket.Receive(data);
            if (length == 0)
                return -1;
            buffer.Put(data, 0, length);
            return length;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="buffer">TBD</param>
        /// <returns>TBD</returns>
        public int Write(ByteBuffer buffer)
        {
            if (!_socket.Poll(0, SelectMode.SelectWrite))
                return 0;
            var data = new byte[buffer.Remaining];
            buffer.Get(data);
            return _socket.Send(data);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public void Close()
        {
            _connected = false;
            _socket.Dispose();
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal IActorRef Connection { get { return _connection; } }
    }

    internal static class TaskExtension
    {
        public static IAsyncResult AsApm(this Task task,
            AsyncCallback callback,
            object state)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            var tcs = new TaskCompletionSource<object>(state);
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    tcs.TrySetException(t.Exception.InnerExceptions);
                else if (t.IsCanceled)
                    tcs.TrySetCanceled();
                else
                    tcs.TrySetResult(null);

                callback?.Invoke(tcs.Task);
            }, TaskScheduler.Default);
            return tcs.Task;
        }

        public static IAsyncResult AsApm<T>(this Task<T> task,
            AsyncCallback callback,
            object state)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            var tcs = new TaskCompletionSource<T>(state);
            task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    tcs.TrySetException(t.Exception.InnerExceptions);
                else if (t.IsCanceled)
                    tcs.TrySetCanceled();
                else
                    tcs.TrySetResult(t.Result);

                callback?.Invoke(tcs.Task);
            }, TaskScheduler.Default);
            return tcs.Task;
        }
    }
}
#endif