//-----------------------------------------------------------------------
// <copyright file="NewtonSoftJsonSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;
using Newtonsoft.Json.Serialization;

namespace Akka.Serialization
{

    public partial class NewtonSoftJsonSerializer
    {
        internal class ReferenceResolver : IReferenceResolver
        {
            private readonly ObjectIDGenerator objectIdGenerator = new ObjectIDGenerator();
            private readonly Dictionary<string, WeakReference> objectsByReference = new Dictionary<string, WeakReference>();

            public void AddReference(object context, string reference, object value)
            {
                objectsByReference[reference] = new WeakReference(value, false);
            }

            public string GetReference(object context, object value)
            {
                var id = objectIdGenerator.GetId(value, out var firstTime);
                var reference = id.ToString(NumberFormatInfo.InvariantInfo);
                if (firstTime)
                {
                    AddReference(context, reference, value);
                }
                return reference;
            }

            public bool IsReferenced(object context, object value)
            {
                var id = objectIdGenerator.HasId(value, out var firstTime);
                if (firstTime) return false;

                var reference = id.ToString(NumberFormatInfo.InvariantInfo);
                return objectsByReference.ContainsKey(reference);
            }

            public object ResolveReference(object context, string reference)
            {
                return objectsByReference[reference].Target;
            }
        }
    }
}
