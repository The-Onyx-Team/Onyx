namespace Onyx.App.AndroidData;

public class CleanUsageData
{
    public required string Name { get; set; }
    public required TimeSpan TimeInForeground { get; set; }
}