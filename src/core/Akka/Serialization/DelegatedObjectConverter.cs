//-----------------------------------------------------------------------
// <copyright file="NewtonSoftJsonSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Newtonsoft.Json;

namespace Akka.Serialization
{
    public partial class NewtonSoftJsonSerializer
    {
        /// <summary>
        /// Handles converting <see cref="object"/> values using configured <see cref="IObjectConverter"/>s
        /// </summary>
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
                return Convert(deserializedValue);
            }

            /// <summary>
            /// Attempt to convert <paramref name="deserializedValue"/> using the configured delegates
            /// </summary>
            /// <param name="deserializedValue"></param>
            /// <returns>Converted object if any delegate applies; otherwise <paramref name="deserializedValue"/></returns>
            public object Convert(object deserializedValue)
            {
                for (var i = 0; i < objectConverters.Length; i++)
                {
                    var objectConverter = objectConverters[i];
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
