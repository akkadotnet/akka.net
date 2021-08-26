//-----------------------------------------------------------------------
// <copyright file="BugFix4399Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Serialization;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Serialization
{
    public class BugFix4399Spec : AkkaSpec
    {
        [Fact]
        public void JsonSerializer_should_be_able_to_serialize_Ack()
        {
            var serializer = new NewtonSoftJsonSerializer((ExtendedActorSystem) Sys);

            var nacks = new List<SeqNo>();
            for (var i = 100; i < 200; ++i)
            {
                nacks.Add(new SeqNo(i));
            }

            var message = new Ack(new SeqNo(666), nacks);
            var serializedBytes = serializer.ToBinary(message);
            var deserialized = (Ack)serializer.FromBinary(serializedBytes, typeof(Ack));

            deserialized.CumulativeAck.RawValue.ShouldBe(666);
            deserialized.Nacks.Count.ShouldBe(100);
            var seqNos = deserialized.Nacks.Select(ack => (int)ack.RawValue);
            seqNos.ShouldOnlyContainInOrder(Enumerable.Range(100, 100).ToArray());
        }
    }
}
