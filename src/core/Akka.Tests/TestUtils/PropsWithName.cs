//-----------------------------------------------------------------------
// <copyright file="PropsWithName.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
