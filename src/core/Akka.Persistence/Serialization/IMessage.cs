﻿//-----------------------------------------------------------------------
// <copyright file="IMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.Serialization
{
    /// <summary>
    /// Marked interface used to identify message types which are used in persistence.
    /// <see cref="IPersistentRepresentation"/>
    /// </summary>
    public interface IMessage { }
}
