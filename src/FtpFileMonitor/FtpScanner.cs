using FluentFTP;

class FileItem
{
    public string Path;
    public long Size;
    public DateTime Modified;
    public string Rule;
    public string DetectedUtc = "";

    public FileItem(string path, long size, DateTime modified, string rule)
    {
        Path = path;
        Size = size;
        Modified = modified;
        Rule = rule;
    }
}

static class FtpScanner
{
    public static async Task<List<FileItem>> Scan(AsyncFtpClient ftp)
    {
        List<FileItem> result = new List<FileItem>();
        await ScanDirectory(ftp, Settings.RootDir, 0, result);

        return result;
    }

    static async Task ScanDirectory(AsyncFtpClient ftp, string dir, int depth, List<FileItem> result)
    {
        if (depth > Settings.MaxDepth)
            throw new IOException("Max depth exceeded at " + dir);

        FtpListItem[] items = await ftp.GetListing(dir);

        if (items == null)
            throw new IOException("Listing failed for " + dir);

        foreach (FtpListItem item in items)
        {
            string path = NormalizePath(item.FullName);

            if (item.Type == FtpObjectType.Directory)
            {
                await ScanDirectory(ftp, path, depth + 1, result);
            }
            else if (item.Type == FtpObjectType.File)
            {
                string? rule = MatchRule(item.Name);

                if (rule != null)
                    result.Add(new FileItem(path, item.Size, item.Modified, rule));
            }
        }
    }

    static string? MatchRule(string fileName)
    {
        foreach (Rule rule in Settings.Rules)
        {
            if (rule.Pattern.IsMatch(fileName))
                return rule.Name;
        }

        return null;
    }

    static string NormalizePath(string path)
    {
        string result = path.Replace('\\', '/');

        while (result.Contains("//"))
            result = result.Replace("//", "/");

        if (!result.StartsWith("/"))
            result = "/" + result;

        if (result.Length > 1 && result.EndsWith("/"))
            result = result.Substring(0, result.Length - 1);

        return result;
    }
}