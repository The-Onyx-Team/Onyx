namespace Onyx.Collector.Linux.Sources;

internal static class ProcessInfo
{
    /// <summary>Executable name of a process (basename of /proc/pid/exe, falling back to comm).</summary>
    public static string? GetExecutableName(int pid)
    {
        if (pid <= 0)
            return null;

        try
        {
            var target = new FileInfo($"/proc/{pid}/exe").LinkTarget;
            if (!string.IsNullOrEmpty(target))
                return Path.GetFileName(target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        try
        {
            return File.ReadAllText($"/proc/{pid}/comm").Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
