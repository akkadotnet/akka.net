using Akka.Configuration;
using Xunit;

namespace Akka.Cluster.Hosting.Tests;

public static class ConfigAssertionHelper
{
    public static void AssertSameString(this Config first, Config second, string key)
        => Assert.Equal(second.GetString(key), first.GetString(key));

    public static void AssertSameInt(this Config first, Config second, string key)
        => Assert.Equal(second.GetInt(key), first.GetInt(key));
    
    public static void AssertSameTimeSpan(this Config first, Config second, string key)
        => Assert.Equal(second.GetTimeSpan(key), first.GetTimeSpan(key));
}