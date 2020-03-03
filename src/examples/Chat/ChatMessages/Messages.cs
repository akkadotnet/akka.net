//-----------------------------------------------------------------------
// <copyright file="Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace ChatMessages
{
    public class ConnectRequest
    {
        public string Username { get; set; }
    }

    public class ConnectResponse
    {
        public string Message { get; set; }
    }

    public class NickRequest
    {
        public string OldUsername { get; set; }
        public string NewUsername { get; set; }
    }

    public class NickResponse
    {
        public string OldUsername { get; set; }
        public string NewUsername { get; set; }
    }

    public class SayRequest
    {
        public string Username { get; set; }
        public string Text { get; set; }
    }

    public class SayResponse
    {
        public string Username { get; set; }
        public string Text { get; set; }
    }
}
