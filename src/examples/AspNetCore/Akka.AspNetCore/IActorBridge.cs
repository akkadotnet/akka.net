//-----------------------------------------------------------------------
// <copyright file="IActorBridge.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#region actor-bridge
namespace Akka.AspNetCore
{
    public interface IActorBridge
    {
        void Tell(object message);
        Task<T> Ask<T>(object message);
    }
}
#endregion
