using Microsoft.Extensions.Configuration;

static class Settings
{
    public static string Host { get; private set; } = "localhost";
    public static int Port { get; private set; } = 21;
    public static string User { get; private set; } = "";
    public static string Password { get; private set; } = "";
    public static string RootDir { get; private set; } = "/";

    public static string DbFile { get; private set; } = "monitor.db";

    public static int MinInterval { get; private set; } = 15;
    public static int MaxInterval { get; private set; } = 300;
    public static int IntervalStep { get; private set; } = 15;
    public static int MaxDepth { get; private set; } = 10;

    public static bool WaitForStableSize { get; private set; } = true;
    public const long UnknownSize = -1;

    public static Rule[] Rules { get; private set; } = Array.Empty<Rule>();

    // Reads appsettings.json, then lets environment variables override it.
    // Example: FTPMONITOR_Ftp__Password=secret overrides Ftp:Password.
    public static void Load()
    {
        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables("FTPMONITOR_")
            .Build();

        Host = config["Ftp:Host"] ?? Host;
        Port = config.GetValue("Ftp:Port", Port);
        User = config["Ftp:User"] ?? User;
        Password = config["Ftp:Password"] ?? Password;
        RootDir = config["Ftp:RootDir"] ?? RootDir;

        if (string.IsNullOrWhiteSpace(User))
            throw new InvalidOperationException(
                "FTP credentials are not set. Provide them via FTPMONITOR_Ftp__User and FTPMONITOR_Ftp__Password environment variables.");

        DbFile = config["Monitor:DbFile"] ?? DbFile;
        MinInterval = config.GetValue("Monitor:MinIntervalSeconds", MinInterval);
        MaxInterval = config.GetValue("Monitor:MaxIntervalSeconds", MaxInterval);
        IntervalStep = config.GetValue("Monitor:IntervalStepSeconds", IntervalStep);
        MaxDepth = config.GetValue("Monitor:MaxDepth", MaxDepth);
        WaitForStableSize = config.GetValue("Monitor:WaitForStableSize", WaitForStableSize);

        List<Rule> rules = new List<Rule>();

        foreach (IConfigurationSection section in config.GetSection("Rules").GetChildren())
        {
            string? name = section["Name"];
            string? pattern = section["Pattern"];

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(pattern))
                throw new InvalidOperationException("Every rule needs a Name and a Pattern.");

            rules.Add(new Rule(name, pattern, section.GetValue("IsRegex", false)));
        }

        if (rules.Count == 0)
            throw new InvalidOperationException("No rules configured. Add at least one entry under \"Rules\" in appsettings.json.");

        Rules = rules.ToArray();
    }
}
