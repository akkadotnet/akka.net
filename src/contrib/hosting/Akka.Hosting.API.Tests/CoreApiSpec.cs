using System.Threading.Tasks;
using Akka.Cluster.Hosting.SBR;
using Akka.Persistence.Hosting;
using Akka.Remote.Hosting;
using VerifyTests;
using VerifyXunit;
using Xunit;
using static PublicApiGenerator.ApiGenerator;
using static VerifyXunit.Verifier;

namespace Akka.Hosting.API.Tests;

public class CoreApiSpec
{
    static CoreApiSpec()
    {
        VerifierSettings.ScrubLinesContaining("\"RepositoryUrl\"");
        VerifierSettings.ScrubLinesContaining("Versioning.TargetFramework");
        VerifyDiffPlex.Initialize();
    }

    private static Task VerifyAssembly<T>()
    {
        var settings = new VerifySettings();
        settings.UseDirectory("verify");
        return Verify(typeof(T).Assembly.GeneratePublicApi(), settings);
    }

    [Fact]
    public Task ApproveCore()
    {
        return VerifyAssembly<ActorRegistry>();
    }

    [Fact]
    public Task ApproveTestKit()
    {
        return VerifyAssembly<TestKit.TestKit>();
    }
    
    [Fact]
    public Task ApproveCluster()
    {
        return VerifyAssembly<SplitBrainResolverOption>();
    }

    [Fact]
    public Task ApproveRemoting()
    {
        return VerifyAssembly<RemoteOptions>();
    }

    [Fact]
    public Task ApprovePersistence()
    {
        return VerifyAssembly<JournalOptions>();
    }
}