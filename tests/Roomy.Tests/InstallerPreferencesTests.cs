using Roomy.Core;

namespace Roomy.Tests;

public sealed class InstallerPreferencesTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, true, true)]
    public void InstallationChoicesEnableBackgroundOnlyWhenNeeded(bool startup, bool lowSpace, bool weekly, bool background)
    {
        var result = new Preferences { LowSpaceGB = 23, CheckEveryMinutes = 60 }.WithInstallerOptions(startup, lowSpace, weekly);
        Assert.Equal(background, result.RunInBackground);
        Assert.Equal(lowSpace, result.LowSpaceAlerts);
        Assert.Equal(weekly, result.WeeklyChecks);
        Assert.Equal(23, result.LowSpaceGB);
        Assert.Equal(60, result.CheckEveryMinutes);
    }

    [Fact]
    public void EnablingChecksMakesFirstIdleScanDueWithoutResettingAnExistingSchedule()
    {
        var now = DateTimeOffset.UtcNow;
        var existing = new Preferences { LastWeeklyCheck = now };
        var enabled = existing.WithInstallerOptions(false, false, true);
        Assert.True(ReminderPolicy.WeeklyCheckDue(enabled, now));
        Assert.Equal(now, (existing with { WeeklyChecks = true }).WithInstallerOptions(false, false, true).LastWeeklyCheck);
    }
}
