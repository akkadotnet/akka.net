// //-----------------------------------------------------------------------
// // <copyright file="TestSocketOption.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Net.Sockets;
using Akka.IO;

namespace Akka.Tests.IO
{
    internal class TestSocketOption : Inet.SocketOptionV2
    {
        private readonly Action<Socket> _callback;

        public TestSocketOption(Action<Socket> callback)
        {
            _callback = callback;
        }

        public override void AfterBind(Socket s)
            => _callback(s);

        public override void AfterConnect(Socket s)
            => _callback(s);
    }
}