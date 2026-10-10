using static ToolFleet.Domain.Tests.Builders;

namespace ToolFleet.Domain.Tests;

public class ToolCalibrationTests
{
    [Theory]
    [InlineData(-10, CalibrationStatus.Ok)]        // calibrated 10 days ago, due in 170
    [InlineData(-150, CalibrationStatus.DueSoon)]  // due in exactly 30 days
    [InlineData(-180, CalibrationStatus.DueSoon)]  // due today
    [InlineData(-181, CalibrationStatus.Overdue)]  // due yesterday
    public void Status_depends_on_days_until_due(int calibratedDaysAgo, CalibrationStatus expected)
    {
        var tool = Tool(lastCalibrated: Today.AddDays(calibratedDaysAgo), intervalDays: 180);

        Assert.Equal(expected, tool.GetCalibrationStatus(Today));
    }

    [Fact]
    public void Recording_a_calibration_resets_the_due_date()
    {
        var tool = Tool(lastCalibrated: Today.AddDays(-200), intervalDays: 180);

        tool.RecordCalibration(Today, Today);

        Assert.Equal(Today.AddDays(180), tool.CalibrationDueOn);
        Assert.Equal(CalibrationStatus.Ok, tool.GetCalibrationStatus(Today));
    }

    [Fact]
    public void Calibration_in_the_future_is_rejected()
    {
        var tool = Tool();

        Assert.Throws<DomainException>(() => tool.RecordCalibration(Today.AddDays(1), Today));
    }

    [Fact]
    public void Calibration_older_than_the_last_one_is_rejected()
    {
        var tool = Tool(lastCalibrated: Today.AddDays(-5));

        Assert.Throws<DomainException>(() => tool.RecordCalibration(Today.AddDays(-6), Today));
    }
}
