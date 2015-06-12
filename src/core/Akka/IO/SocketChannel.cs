//-----------------------------------------------------------------------
// <copyright file="ActorBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Akka.Actor;

namespace Akka.IO
{
    /* 
     * SocketChannel does not exists in the .NET BCL - This class is an adapter to hide the diffirences in CLR & JVM IO.
     * This implimentation uses blocking IO calls, and then catch SocketExceptions if the socket is set to non bloking. 
     * This might introduce performance issues, with lots of thrown exceptions
     * TODO: Impliments this class with .NET Async calls
     */
    public class SocketChannel 
    {
        private readonly Socket _socket;
        private IActorRef _connection;
        private bool _connected;
            
        public SocketChannel(Socket socket) 
        {
            _socket = socket;
        }

        public static SocketChannel Open()
        {
            return new SocketChannel(new Socket(SocketType.Stream, ProtocolType.Tcp));
        }

        public SocketChannel ConfigureBlocking(bool block)
        {
            _socket.Blocking = block;
            return this;
        }

        public Socket Socket
        {
            get { return _socket; }
        }

        public void Register(IActorRef connection, SocketAsyncOperation? initialOps)
        {
            _connection = connection;
        }


        public virtual bool IsOpen()
        {
            return _connected;
        }

        public bool Connect(IPEndPoint address)
        {
            try
            {
                _socket.Connect(address);
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.WouldBlock)
                    return false;
                throw;
            }
            return _socket.Connected;
        }
        public bool FinishConnect()
        {
            _connected = _socket.Connected;
            return _socket.Connected;
        }

        public SocketChannel Accept()
        {
            _socket.Blocking = false;
            try
            {
                var s = _socket.Accept();
                return new SocketChannel(s) {_connected = true};
            }
            catch (Exception)
            {
                return null;
            }
        }

        public int Read(ByteBuffer buffer)
        {
            try
            {
                var data = new byte[buffer.Remaining];
                var length = _socket.Receive(data);
                if (length == 0)
                    return -1;
                buffer.Put(data, 0, length);
                return length;
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.WouldBlock)
                    return 0;
                throw new IOException(ex.Message, ex);
            }
        }

        public int Write(ByteBuffer buffer)
        {
            try
            {
                var data = new byte[buffer.Remaining];
                buffer.Get(data);
                return _socket.Send(data);
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.WouldBlock)
                {
                    buffer.Flip();
                    return 0;
                }
                throw new IOException(ex.Message, ex);
            }
        }

        public void Close()
        {
            _connected = false;
            _socket.Close();
        }
        internal IActorRef Connection { get { return _connection; } }
    }
}