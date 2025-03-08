namespace Onyx.App.Shared.Services.Usage;

public class Stats
{
    public required string Name { get; set; }
    public required TimeSpan TimeInForeground { get; set; }
    public TimeSpan TimeVisible { get; set; }
    public string Category { get; set; }
}