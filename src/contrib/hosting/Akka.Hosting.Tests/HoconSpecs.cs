using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Akka.Hosting.Tests.TestHelpers;

namespace Akka.Hosting.Tests;

public class HoconSpecs
{
    [Fact]
    public async Task Should_load_HOCON_from_file()
    {
        // arrange
        using var host = await StartHost(collection => collection.AddAkka("Test", builder =>
        {
            builder.AddHoconFile("test.hocon", HoconAddMode.Append);
        }));
        
        // act
        var sys = host.Services.GetRequiredService<ActorSystem>();
        var hocon = sys.Settings.Config;
        
        // assert
        hocon.HasPath("petabridge.cmd").Should().BeTrue();
    }
}