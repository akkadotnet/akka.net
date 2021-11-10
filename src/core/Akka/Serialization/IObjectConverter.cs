//-----------------------------------------------------------------------
// <copyright file="NewtonSoftJsonSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Serialization
{

    public partial class NewtonSoftJsonSerializer
    {
        internal interface IObjectConverter
        {
            bool TryConvert(object deserializedValue, out object convertedValue);
        }
    }
}
