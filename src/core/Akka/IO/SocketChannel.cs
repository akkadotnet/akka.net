//-----------------------------------------------------------------------
// <copyright file="ActorBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
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
        private IAsyncResult _connectResult;
            
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

        public bool Connect(EndPoint address)
        {
            _connectResult = _socket.BeginConnect(address, ar => { }, null);
            if (_connectResult.CompletedSynchronously)
            {
                _socket.EndConnect(_connectResult);
                _connected = true;
                return true;
            }
            return false;
        }
        public bool FinishConnect()
        {
            if (_connectResult.CompletedSynchronously)
                return true;
            if (_connectResult.IsCompleted)
            {
                _socket.EndConnect(_connectResult);
                _connected = true;
            }
            return _connected;
        }

        public SocketChannel Accept()
        {
            //TODO: Investigate. If we dont wait 1ms we get intermittent test failure in TcpListnerSpec.  
            return _socket.Poll(1, SelectMode.SelectRead)
                ? new SocketChannel(_socket.Accept()) {_connected = true}
                : null;
        }

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

        public int Write(ByteBuffer buffer)
        {
            if (!_socket.Poll(0, SelectMode.SelectWrite))
                return 0;
            var data = new byte[buffer.Remaining];
            buffer.Get(data);
            return _socket.Send(data);
        }

        public void Close()
        {
            _connected = false;
            _socket.Close();
        }
        internal IActorRef Connection { get { return _connection; } }
    }
}