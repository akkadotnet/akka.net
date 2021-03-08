﻿//-----------------------------------------------------------------------
// <copyright file="HyperionTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Tests.Serialization;

namespace Akka.Serialization.Hyperion.Tests
{
    public class HyperionTests : AkkaSerializationSpec
    {
        public HyperionTests() : base(typeof(HyperionSerializer))
        {
        }
    }
}
