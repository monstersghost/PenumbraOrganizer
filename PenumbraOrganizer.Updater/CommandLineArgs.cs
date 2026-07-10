namespace PenumbraOrganizer.Updater;

public sealed record CommandLineArgs(int ProcessId, string SourceDirectory, string DestinationDirectory)
{
    public static CommandLineArgs? Parse(string[] args)
    {
        int? pid = null;
        string? source = null;
        string? dest = null;

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--pid" when int.TryParse(args[i + 1], out var parsedPid):
                    pid = parsedPid;
                    break;
                case "--source":
                    source = args[i + 1];
                    break;
                case "--dest":
                    dest = args[i + 1];
                    break;
            }
        }

        return pid is null || source is null || dest is null
            ? null
            : new CommandLineArgs(pid.Value, source, dest);
    }
}
