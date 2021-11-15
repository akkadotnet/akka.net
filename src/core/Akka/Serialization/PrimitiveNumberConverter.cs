//-----------------------------------------------------------------------
// <copyright file="NewtonSoftJsonSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Akka.Serialization
{

    public partial class NewtonSoftJsonSerializer
    {
        internal class PrimitiveNumberConverter : JsonConverter, IObjectConverter
        {
            private const string PropertyName = "$";
            public override bool CanConvert(Type objectType)
            {
                return objectType == typeof(int)
                       || objectType == typeof(float)
                       || objectType == typeof(decimal);
            }

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
                if (deserializedValue is JObject jObject
                    && jObject.TryGetValue(PropertyName, out var jToken)
                    && jToken is JValue jValue
                    && jValue.Value is string encodedNumberString)
                {
                    var primitiveType = encodedNumberString[0];
                    var numberString = encodedNumberString.Substring(1);
                    switch (primitiveType)
                    {
                        case 'I':
                            convertedValue = int.Parse(numberString, NumberFormatInfo.InvariantInfo);
                            return true;
                        case 'F':
                            convertedValue = float.Parse(numberString, NumberFormatInfo.InvariantInfo);
                            return true;
                        case 'M':
                            convertedValue = decimal.Parse(numberString, NumberFormatInfo.InvariantInfo);
                            return true;
                        default:
                            throw new NotSupportedException($"unsupported primitive type {primitiveType}");
                    }
                }

                convertedValue = default;
                return false;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                string numberString;
                switch (value)
                {
                    case int i:
                        numberString = $"I{i.ToString(NumberFormatInfo.InvariantInfo)}";
                        break;
                    case float f:
                        numberString = $"F{f.ToString(NumberFormatInfo.InvariantInfo)}";
                        break;
                    case decimal m:
                        numberString = $"M{m.ToString(NumberFormatInfo.InvariantInfo)}";
                        break;
                    default:
                        throw new NotSupportedException();
                }

                writer.WriteStartObject();
                writer.WritePropertyName(PropertyName);
                writer.WriteValue(numberString);
                writer.WriteEndObject();
            }
        }
    }
}
