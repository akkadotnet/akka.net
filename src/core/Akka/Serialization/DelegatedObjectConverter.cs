//-----------------------------------------------------------------------
// <copyright file="NewtonSoftJsonSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Akka.Serialization
{

    public partial class NewtonSoftJsonSerializer
    {
        internal class DelegatedObjectConverter : JsonConverter
        {
            private readonly IObjectConverter[] objectConverters;

            public DelegatedObjectConverter(params IObjectConverter[] objectConverters)
            {
                this.objectConverters = objectConverters;
            }

            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(object);
            }

            public override bool CanWrite => false;

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                var deserializedValue = serializer.Deserialize(reader);

                foreach (var objectConverter in objectConverters)
                {
                    if (objectConverter.TryConvert(deserializedValue, out var convertedValue))
                    {
                        return convertedValue;
                    }
                }

                return deserializedValue;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                throw new NotSupportedException();
            }
        }
    }
}
