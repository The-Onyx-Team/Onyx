namespace Onyx.Collector.Linux.Sources;

internal static class ProcessInfo
{
    /// <summary>Parent of a process, from /proc/pid/stat (the field after the parenthesized command name).</summary>
    public static int GetParentProcessId(int pid)
    {
        try
        {
            var stat = File.ReadAllText($"/proc/{pid}/stat");
            var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            return int.Parse(fields[1]);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException
                                      or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return 0;
        }
    }

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
