using Xunit;
using Xunit.Runner.VisualStudio;

public class RunSettingsTests
{
    void AssertDefaultValues(RunSettings runSettings)
    {
        Assert.False(runSettings.DisableAppDomain);
        Assert.False(runSettings.DisableParallelization);
        Assert.False(runSettings.NoAutoReporters);
        Assert.True(runSettings.DesignMode);
        Assert.True(runSettings.CollectSourceInformation);
        Assert.Null(runSettings.ReporterSwitch);
        Assert.Null(runSettings.TargetFrameworkVersion);
    }

    [Fact]
    public void RunSettingsHelperShouldNotThrowExceptionOnBadXml()
    {
        string settingsXml =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
            <RunSettings";

        var runSettings = RunSettings.Parse(settingsXml);

        AssertDefaultValues(runSettings);
    }

    [Fact]
    public void RunSettingsHelperShouldNotThrowExceptionOnInvalidValuesForElements()
    {
        string settingsXml =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
                <RunSettings>
                    <RunConfiguration>
                        <DisableAppDomain>1234</DisableAppDomain>
                        <DisableParallelization>smfhekhgekr</DisableParallelization>
                        <DesignMode>3245sax</DesignMode>
                        <CollectSourceInformation>1234blah</CollectSourceInformation>
                        <NoAutoReporters>1x3_5f8g0</NoAutoReporters>
                    </RunConfiguration>
                </RunSettings>";

        var runSettings = RunSettings.Parse(settingsXml);

        AssertDefaultValues(runSettings);
    }

    [Fact]
    public void RunSettingsHelperShouldUseDefaultValuesInCaseOfIncorrectSchemaAndIgnoreAttributes()
    {
        string settingsXml =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
                <RunSettings>
                    <RunConfiguration>
                        <OuterElement>
                            <DisableParallelization>true</DisableParallelization>
                        </OuterElement>
                        <DisableAppDomain value=""false"">true</DisableAppDomain>
                    </RunConfiguration>
                </RunSettings>";

        var runSettings = RunSettings.Parse(settingsXml);

        // Use element value, not attribute value
        Assert.True(runSettings.DisableAppDomain);
        // Ignore value that isn't at the right level
        Assert.False(runSettings.DisableParallelization);
    }

    [Fact]
    public void RunSettingsHelperShouldUseDefaultValuesInCaseOfBadXml()
    {
        string settingsXml =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
                <RunSettings>
                    <RunConfiguration>
                        Random Text
                        <DisableParallelization>true</DisableParallelization>
                    </RunConfiguration>
                </RunSettings>";

        var runSettings = RunSettings.Parse(settingsXml);

        // Allow value to be read even after unexpected element body
        Assert.True(runSettings.DisableParallelization);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RunSettingsHelperShouldReadValuesCorrectly(bool testValue)
    {
        string settingsXml =
            $@"<?xml version=""1.0"" encoding=""utf-8""?>
                <RunSettings>
                    <RunConfiguration>
                        <CollectSourceInformation>{testValue.ToString().ToLowerInvariant()}</CollectSourceInformation>
                        <DesignMode>{testValue.ToString().ToLowerInvariant()}</DesignMode>
                        <DisableAppDomain>{testValue.ToString().ToLowerInvariant()}</DisableAppDomain>
                        <DisableParallelization>{testValue.ToString().ToLowerInvariant()}</DisableParallelization>
                        <NoAutoReporters>{testValue.ToString().ToLowerInvariant()}</NoAutoReporters>
                    </RunConfiguration>
                </RunSettings>";

        var runSettings = RunSettings.Parse(settingsXml);

        Assert.Equal(testValue, runSettings.CollectSourceInformation);
        Assert.Equal(testValue, runSettings.DesignMode);
        Assert.Equal(testValue, runSettings.DisableAppDomain);
        Assert.Equal(testValue, runSettings.DisableParallelization);
        Assert.Equal(testValue, runSettings.NoAutoReporters);
    }

    [Fact]
    public void RunSettingsHelperShouldIgnoreEvenIfAdditionalElementsExist()
    {
        string settingsXml =
            @"<?xml version=""1.0"" encoding=""utf-8""?>
                <RunSettings>
                        <RunConfiguration>
                            <TargetPlatform>x64</TargetPlatform>
                            <TargetFrameworkVersion>FrameworkCore10</TargetFrameworkVersion>
                            <SolutionDirectory>%temp%</SolutionDirectory>
                            <DisableAppDomain>true</DisableAppDomain>
                            <DisableParallelization>true</DisableParallelization>
                            <MaxCpuCount>4</MaxCpuCount>
                            <NoAutoReporters>true</NoAutoReporters>
                            <ReporterSwitch>foo</ReporterSwitch>
                        </RunConfiguration>
                </RunSettings>";

        var runSettings = RunSettings.Parse(settingsXml);

        Assert.True(runSettings.DisableAppDomain);
        Assert.True(runSettings.DisableParallelization);
        Assert.True(runSettings.NoAutoReporters);
        Assert.Equal("FrameworkCore10", runSettings.TargetFrameworkVersion);
        Assert.Equal("foo", runSettings.ReporterSwitch);
    }
}
