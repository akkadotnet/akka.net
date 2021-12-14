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
        internal class SurrogateConverter : JsonConverter, IObjectConverter
        {
            private readonly ExtendedActorSystem system;

            public SurrogateConverter(ExtendedActorSystem system)
            {
                this.system = system;
            }

            public override bool CanConvert(Type objectType)
            {
                return typeof(ISurrogate).IsAssignableFrom(objectType);
            }

            public override bool CanWrite => false;

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var deserializedValue = serializer.Deserialize(reader);

                if (TryConvert(deserializedValue, out var convertedValue))
                {
                    return convertedValue;
                }

                return deserializedValue;
            }

            public bool TryConvert(object deserializedValue, out object convertedValue)
            {
                if (deserializedValue is ISurrogate surrogate)
                {
                    convertedValue = surrogate.FromSurrogate(system);
                    return true;
                }

                convertedValue = default;
                return false;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                throw new NotSupportedException();
            }
        }
    }
}
