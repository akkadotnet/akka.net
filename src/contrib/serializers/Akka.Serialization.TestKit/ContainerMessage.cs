//-----------------------------------------------------------------------
// <copyright file="ContainerMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Tests.Serialization
{
    public class ContainerMessage<T>
    {
        public ContainerMessage(T contents)
        {
            Contents = contents;
        }
        public T Contents { get; private set; }
    }
}
