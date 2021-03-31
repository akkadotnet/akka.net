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
        public static NewtonSoftJsonSerializerSetup Create(Func<JsonSerializerSettings> settingsFactory)
            => new NewtonSoftJsonSerializerSetup(settingsFactory);

        public Func<JsonSerializerSettings>  CreateSettings { get; }

        private NewtonSoftJsonSerializerSetup(Func<JsonSerializerSettings> settingsFactory)
        {
            CreateSettings = settingsFactory;
        }
    }
}
