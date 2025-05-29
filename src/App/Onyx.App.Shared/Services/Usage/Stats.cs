namespace Onyx.App.Shared.Services.Usage;

public class Stats
{
    public required string Name { get; set; }
    public required TimeSpan TimeInForeground { get; set; }
    public TimeSpan? TimeVisible { get; set; }
    public DateTime? IntervalStart { get; set; }
    public DateTime? IntervalEnd { get; set; }
    public DateTime? LastTimeUsed { get; set; }
    public required string Category { get; set; }
    public required byte[] Icon { get; set; }
}