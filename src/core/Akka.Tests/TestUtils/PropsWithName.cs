//-----------------------------------------------------------------------
// <copyright file="PropsWithName.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Tests.TestUtils
{
    public class PropsWithName
    {
        private readonly Props _props;
        private readonly string _name;

        public PropsWithName(Props props, string name)
        {
            _props = props;
            _name = name;
        }

        public Props Props { get { return _props; } }

        public string Name { get { return _name; } }
    }
}

