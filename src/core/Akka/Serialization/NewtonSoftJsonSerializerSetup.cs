//-----------------------------------------------------------------------
// <copyright file="NewtonSoftJsonSerializerSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor.Setup;
using Newtonsoft.Json;

namespace Akka.Serialization
{
    /// <summary>
    /// Setup for the <see cref="NewtonSoftJsonSerializer"/> serializer.
    ///
    /// Constructor is INTERNAL API. Use the factory method <see cref="Create"/>.
    ///
    /// NOTE:
    ///   - <see cref="JsonSerializerSettings.ObjectCreationHandling"/>  will always be overriden with
    /// <see cref="ObjectCreationHandling.Replace"/>
    ///   - <see cref="JsonSerializerSettings.ContractResolver"/> will always be overriden with the internal
    /// contract resolver <see cref="NewtonSoftJsonSerializer.AkkaContractResolver"/>
    /// </summary>
    public sealed class NewtonSoftJsonSerializerSetup : Setup
    {
        public static NewtonSoftJsonSerializerSetup Create(Action<JsonSerializerSettings> settings)
            => new NewtonSoftJsonSerializerSetup(settings);

        public Action<JsonSerializerSettings>  ApplySettings { get; }

        private NewtonSoftJsonSerializerSetup(Action<JsonSerializerSettings> settings)
        {
            ApplySettings = settings;
        }
    }
}
