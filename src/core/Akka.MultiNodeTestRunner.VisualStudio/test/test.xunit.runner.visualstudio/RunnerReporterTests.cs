using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;
using Xunit.Runner.VisualStudio;

public class RunnerReporterTests
{
    public class TestRunnerReporterNotEnabled : IRunnerReporter
    {
        string IRunnerReporter.Description
            => throw new NotImplementedException();

        bool IRunnerReporter.IsEnvironmentallyEnabled
            => false;

        string IRunnerReporter.RunnerSwitch
            => "notautoenabled";

        IMessageSink IRunnerReporter.CreateMessageHandler(IRunnerLogger logger)
            => throw new NotImplementedException();
    }

    public class TestRunnerReporter : TestRunnerReporterNotEnabled, IRunnerReporter
    {
        bool IRunnerReporter.IsEnvironmentallyEnabled
            => true;

        string IRunnerReporter.RunnerSwitch
            => null;
    }

    [Fact]
    public void WhenNotUsingAutoReporters_ChoosesDefault()
    {
        var settings = new RunSettings { NoAutoReporters = true };

        var runnerReporter = VsTestRunner.GetRunnerReporter(null, settings, new[] { Assembly.GetExecutingAssembly().Location });

        Assert.Equal(typeof(DefaultRunnerReporterWithTypes).AssemblyQualifiedName, runnerReporter.GetType().AssemblyQualifiedName);
    }

    [Fact]
    public void WhenUsingAutoReporters_DoesNotChooseDefault()
    {
        var settings = new RunSettings { NoAutoReporters = false };

        var runnerReporter = VsTestRunner.GetRunnerReporter(null, settings, new[] { Assembly.GetExecutingAssembly().Location });

        // We just make sure _an_ auto-reporter was chosen, but we can't rely on which one because this code
        // wil run when we're in CI, and therefore will choose the CI reporter sometimes. It's good enough
        // that we've provide an option above so that the default never gets chosen.
        Assert.NotEqual(typeof(DefaultRunnerReporterWithTypes).AssemblyQualifiedName, runnerReporter.GetType().AssemblyQualifiedName);
    }

    [Fact]
    public void WhenUsingReporterSwitch_PicksThatReporter()
    {
        var settings = new RunSettings { NoAutoReporters = true, ReporterSwitch = "notautoenabled" };

        var runnerReporter = VsTestRunner.GetRunnerReporter(null, settings, new[] { Assembly.GetExecutingAssembly().Location });

        Assert.Equal(typeof(TestRunnerReporterNotEnabled).AssemblyQualifiedName, runnerReporter.GetType().AssemblyQualifiedName);
    }

    [Fact]
    public void WhenRequestedReporterDoesntExist_LogsAndFallsBack()
    {
        var settings = new RunSettings { NoAutoReporters = true, ReporterSwitch = "thisnotavalidreporter" };
        var logger = Substitute.For<IMessageLogger>();
        var loggerHelper = new LoggerHelper(logger, new Stopwatch());

        var runnerReporter = VsTestRunner.GetRunnerReporter(loggerHelper, settings, new[] { Assembly.GetExecutingAssembly().Location });

        Assert.Equal(typeof(DefaultRunnerReporterWithTypes).AssemblyQualifiedName, runnerReporter.GetType().AssemblyQualifiedName);
        logger.Received(1).SendMessage(TestMessageLevel.Warning, "[xUnit.net 00:00:00.00] Could not find requested reporter 'thisnotavalidreporter'");
    }

    [Fact]
    public void VSTestRunnerShouldHaveCategoryAttribute_WithValueManaged()
    {
        var attribute = typeof(VsTestRunner).GetCustomAttribute(typeof(CategoryAttribute));
        Assert.NotNull(attribute);
        Assert.Equal("managed", (attribute as CategoryAttribute)?.Category);
    }
}
