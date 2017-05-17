//-----------------------------------------------------------------------
// <copyright file="ContainerMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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