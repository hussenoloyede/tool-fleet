namespace ToolFleet.Domain;

public enum CalibrationStatus
{
    Ok,
    DueSoon,
    Overdue,
}

/// <summary>A physical downhole tool, identified by serial number.</summary>
public class Tool
{
    public const int DueSoonWindowDays = 30;

    private Tool() { } // for EF Core

    public Tool(
        string serialNumber,
        string model,
        Guid locationId,
        string firmwareVersion,
        DateOnly lastCalibratedOn,
        int calibrationIntervalDays)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) throw new DomainException("Serial number is required.");
        if (string.IsNullOrWhiteSpace(model)) throw new DomainException("Model is required.");
        if (string.IsNullOrWhiteSpace(firmwareVersion)) throw new DomainException("Firmware version is required.");
        if (calibrationIntervalDays <= 0) throw new DomainException("Calibration interval must be at least one day.");

        Id = Guid.NewGuid();
        SerialNumber = serialNumber.Trim();
        Model = model.Trim();
        LocationId = locationId;
        FirmwareVersion = firmwareVersion.Trim();
        LastCalibratedOn = lastCalibratedOn;
        CalibrationIntervalDays = calibrationIntervalDays;
    }

    public Guid Id { get; private set; }
    public string SerialNumber { get; private set; } = "";
    public string Model { get; private set; } = "";
    public Guid LocationId { get; private set; }
    public FieldLocation? Location { get; private set; }
    public string FirmwareVersion { get; private set; } = "";
    public DateOnly LastCalibratedOn { get; private set; }
    public int CalibrationIntervalDays { get; private set; }

    public DateOnly CalibrationDueOn => LastCalibratedOn.AddDays(CalibrationIntervalDays);

    public CalibrationStatus GetCalibrationStatus(DateOnly today)
    {
        if (today > CalibrationDueOn) return CalibrationStatus.Overdue;
        if (today >= CalibrationDueOn.AddDays(-DueSoonWindowDays)) return CalibrationStatus.DueSoon;
        return CalibrationStatus.Ok;
    }

    public void RecordCalibration(DateOnly calibratedOn, DateOnly today)
    {
        if (calibratedOn > today) throw new DomainException("Calibration date cannot be in the future.");
        if (calibratedOn < LastCalibratedOn) throw new DomainException("Calibration date is earlier than the last recorded calibration.");
        LastCalibratedOn = calibratedOn;
    }

    /// <summary>Only a rollout should change firmware, which is why this is internal.</summary>
    internal void ApplyFirmware(string version) => FirmwareVersion = version;
}
