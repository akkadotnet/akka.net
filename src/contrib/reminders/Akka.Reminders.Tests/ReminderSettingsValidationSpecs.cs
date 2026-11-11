using Akka.Reminders.Storage;

namespace Akka.Reminders.Tests;

public class ReminderSettingsValidationSpecs
{
    [Fact]
    public void Validate_ShouldThrow_WhenMaxBatchSizeIsZero()
    {
        var settings = new ReminderSettings { MaxBatchSize = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_ShouldThrow_WhenMaxBatchSizeIsNegative()
    {
        var settings = new ReminderSettings { MaxBatchSize = -1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_ShouldThrow_WhenDeliveryCommitChunkSizeIsZero()
    {
        var settings = new ReminderSettings { DeliveryCommitChunkSize = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_ShouldThrow_WhenDeliveryCommitChunkSizeIsNegative()
    {
        var settings = new ReminderSettings { DeliveryCommitChunkSize = -5 };
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_ShouldThrow_WhenDefaultMaxDeliveryWindowIsZero()
    {
        var settings = new ReminderSettings { DefaultMaxDeliveryWindow = TimeSpan.Zero };
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
    }

    [Fact]
    public void Validate_ShouldThrow_WhenDefaultMaxDeliveryWindowIsNegative()
    {
        var settings = new ReminderSettings { DefaultMaxDeliveryWindow = TimeSpan.FromMinutes(-1) };
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
    }

    [Fact]
    public void ReminderBatchSize_ShouldThrow_WhenZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReminderBatchSize(0));
    }

    [Fact]
    public void ReminderBatchSize_ShouldStoreValue_WhenPositive()
    {
        var batchSize = new ReminderBatchSize(25);
        Assert.Equal(25, batchSize.Value);
    }

    [Fact]
    public void Defaults_ShouldHaveExpectedValues()
    {
        var settings = new ReminderSettings();
        Assert.Equal(TimeSpan.FromSeconds(10), settings.AckTimeout);
        Assert.Equal(10, settings.MaxDeliveryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(60), settings.RetryBackoffBase);
        Assert.Equal(TimeSpan.FromMinutes(10), settings.MaxRetryBackoff);
        Assert.Equal(1000, settings.MaxBatchSize);
        Assert.Equal(100, settings.DeliveryCommitChunkSize);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.MaxSlippage);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.StorageTimeout);
        Assert.Null(settings.DefaultMaxDeliveryWindow);
    }
}
