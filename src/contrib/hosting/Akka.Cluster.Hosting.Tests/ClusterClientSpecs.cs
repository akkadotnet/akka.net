using System.Collections.Generic;
using Akka.Actor;
using Akka.Cluster.Tools.Client;
using Xunit;

namespace Akka.Cluster.Hosting.Tests;

public class ClusterClientSpecs
{
    [Fact(DisplayName = "ClusterClientReceptionistSettings should be set correctly")]
    public void ClusterClientReceptionistSettingsSpec()
    {
        var config = AkkaClusterHostingExtensions.CreateReceptionistConfig("customName", "customRole")
            .GetConfig("akka.cluster.client.receptionist");
        var settings = ClusterReceptionistSettings.Create(config);

        Assert.Equal("customName", config.GetString("name"));
        Assert.Equal("customRole", settings.Role);
    }

    [Fact(DisplayName = "ClusterClientSettings should be set correctly")]
    public void ClusterClientSettingsSpec()
    {
        var contacts = new List<ActorPath>
        {
            ActorPath.Parse("akka.tcp://one@localhost:1111/system/receptionist"),
            ActorPath.Parse("akka.tcp://two@localhost:1111/system/receptionist"),
            ActorPath.Parse("akka.tcp://three@localhost:1111/system/receptionist"),
        };

        var settings = AkkaClusterHostingExtensions.CreateClusterClientSettings(
            ClusterClientReceptionist.DefaultConfig(),
            contacts);

        contacts.CollectionEquals(settings.InitialContacts);
    }
}