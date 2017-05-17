﻿//-----------------------------------------------------------------------
// <copyright file="SerializationChecksSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.TestKit;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class SerializationChecksSpec : ClusterSpecBase
    {
        [Fact]
        public void Settings_serializemessages_and_serializecreators_must_be_on_for_tests()
        {
            Sys.Settings.SerializeAllCreators.ShouldBeTrue();
            Sys.Settings.SerializeAllMessages.ShouldBeTrue();
        }
    }
}

