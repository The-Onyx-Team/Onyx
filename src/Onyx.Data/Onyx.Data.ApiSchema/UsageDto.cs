namespace Onyx.Data.ApiSchema;

public class UsageDto
{
    public required string Action { get; set; }
    public string? IconUrl { get; set; }
    public TimeSpan TimeSpan { get; set; }
    public DateTime Date { get; set; }
    public int DeviceId { get; set; }
    public required string DeviceName { get; set; }
    public required string UserId { get; set; }
    public required string UserName { get; set; }
}