//-----------------------------------------------------------------------
// <copyright file="Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace ChatMessages
{
    /// <summary>
    /// Marker interface for all chat messages exchanged between client and server
    /// </summary>
    /// <remarks>
    /// Currently not used for anything - can be used in the future for serialization
    /// or routing purposes.
    /// </remarks>
    public interface IChatProtocol { }
    
    public record ConnectRequest(string Username) : IChatProtocol;

    public record ConnectResponse(string Message) : IChatProtocol;

    public record NickRequest(string OldUsername, string NewUsername) : IChatProtocol;
    public record NickResponse(string OldUsername, string NewUsername) : IChatProtocol;
    public record SayRequest(string Username, string Text) : IChatProtocol;

    public record SayResponse(string Username, string Text) : IChatProtocol;
}
