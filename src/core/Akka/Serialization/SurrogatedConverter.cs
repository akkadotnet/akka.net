//-----------------------------------------------------------------------
// <copyright file="NewtonSoftJsonSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Util;
using Newtonsoft.Json;

namespace Akka.Serialization
{
    public partial class NewtonSoftJsonSerializer
    {
        internal class SurrogatedConverter : JsonConverter
        {
            private readonly ExtendedActorSystem system;

            public SurrogatedConverter(ExtendedActorSystem system)
            {
                this.system = system;
            }

            public override bool CanConvert(Type objectType)
            {
                return typeof(ISurrogated).IsAssignableFrom(objectType);
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var deserializedValue = serializer.Deserialize(reader);
                if (deserializedValue is ISurrogate surrogate)
                {
                    return surrogate.FromSurrogate(system);
                }

                return deserializedValue;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                if (value is ISurrogated surrogated)
                {
                    var surrogate = surrogated.ToSurrogate(system);
                    serializer.Serialize(writer, surrogate);
                    return;
                }

                throw new NotSupportedException();
            }
        }
    }
}
