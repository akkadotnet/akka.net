//-----------------------------------------------------------------------
// <copyright file="PropsWithName.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Tests.TestUtils
{
    public class PropsWithName
    {
        public PropsWithName(Props props, string name)
        {
            Props = props;
            Name = name;
        }

        public Props Props { get; }

        public string Name { get; }
    }
}

