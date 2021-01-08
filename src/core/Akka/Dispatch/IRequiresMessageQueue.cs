//-----------------------------------------------------------------------
// <copyright file="IRequiresMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Dispatch.MessageQueues;

namespace Akka.Dispatch
{
    /// <summary>
    /// Used to help give hints to the <see cref="ActorSystem"/> as to what types of <see cref="IMessageQueue"/> this
    /// actor requires. Used mostly for system actors.
    /// </summary>
    /// <typeparam name="T">The type of <see cref="ISemantics"/> required</typeparam>
    public interface IRequiresMessageQueue<T> where T:ISemantics
    {
    }
}

