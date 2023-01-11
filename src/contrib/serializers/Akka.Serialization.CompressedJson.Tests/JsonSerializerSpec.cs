// //-----------------------------------------------------------------------
// // <copyright file="JsonSerializerSpec.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using Akka.Tests.Serialization;
using Xunit;

namespace Akka.Serialization.CompressedJson.Tests
{
    public class JsonSerializerSpec: AkkaSerializationSpec
    {
        public JsonSerializerSpec() : base(typeof(NewtonSoftJsonSerializer))
        {
        }

        [Fact(Skip = "JSON serializer can not serialize Config")]
        public override void CanSerializeConfig()
        {
            base.CanSerializeConfig();
        }
    }
}