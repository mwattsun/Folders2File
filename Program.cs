using System.Globalization;
using System.Text;

const string Usage = """
    Turn a folder tree of Internet Shortcut (.URL) files into a Netscape-format
    bookmarks file that Chrome, Edge or Firefox can import.

    Usage: Folders2File folder [options]

    The bookmarks file is written next to the folder and named after it
    (Magazines\ -> Magazines.html). The folder itself becomes the top bookmark
    folder, its subfolders become bookmark folders and each .URL file becomes a
    link titled with the file name. Other files are ignored. Each link and folder
    gets LAST_MODIFIED from its modified time and ADD_DATE from its creation time
    (or its modified time when that's earlier, as it is for copied files).

    Options:
      -o, --output file   write to this file instead (- writes to the console)
      --no-dates          leave out ADD_DATE and LAST_MODIFIED
      -h, --help          show this help

    """;

Console.OutputEncoding = Encoding.UTF8;
return Run(args);

int Run(string[] args)
{
    bool setDates = true;
    string? folder = null, output = null;

    for (int i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        switch (arg)
        {
            case "-h" or "--help":
                Console.Write(Usage);
                return 0;
            case "--no-dates": setDates = false; break;
            case "-o" or "--output":
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine($"{arg} needs a file name\n");
                    Console.Error.Write(Usage);
                    return 1;
                }
                output = args[++i];
                break;
            default:
                if (arg.StartsWith('-') || folder != null)
                {
                    Console.Error.WriteLine($"Unexpected argument: {arg}\n");
                    Console.Error.Write(Usage);
                    return 1;
                }
                folder = arg;
                break;
        }
    }

    if (folder == null)
    {
        Console.Error.Write(Usage);
        return 1;
    }
    folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
    if (!Directory.Exists(folder))
    {
        Console.Error.WriteLine($"Folder not found: {folder}");
        return 1;
    }

    bool toConsole = output == "-";
    if (output == null)
    {
        if (Path.GetFileName(folder).Length == 0)
        {
            Console.Error.WriteLine($"{folder} has no name to call the bookmarks file; use -o file");
            return 1;
        }
        output = folder + ".html";
    }
    else if (!toConsole)
        output = Path.GetFullPath(output);

    // .URL files are usually ANSI, so allow for that code page as well as UTF-8.
    Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    var ansi = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
    var strictUtf8 = new UTF8Encoding(false, true);

    int folderCount = 0, linkCount = 0, problems = 0;
    TextWriter writer;
    try
    {
        writer = toConsole ? Console.Out : new StreamWriter(output!, false, new UTF8Encoding(false));
    }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
    {
        Console.Error.WriteLine($"Can't write {output}: {e.Message}");
        return 1;
    }

    using (writer)
    {
        writer.WriteLine("""
            <!DOCTYPE NETSCAPE-Bookmark-file-1>
            <!-- This is an automatically generated file.
                 It will be read and overwritten.
                 DO NOT EDIT! -->
            <META HTTP-EQUIV="Content-Type" CONTENT="text/html; charset=UTF-8">
            <TITLE>Bookmarks</TITLE>
            <H1>Bookmarks</H1>
            <DL><p>
            """);
        WriteFolder(new DirectoryInfo(folder), 1);
        writer.WriteLine("</DL><p>");
    }

    // With the bookmarks on the console the summary goes to stderr so it can be redirected away from them.
    (toConsole ? Console.Error : Console.Out).WriteLine(
        $"{folderCount} folders and {linkCount} links written to {(toConsole ? "the console" : output)}"
        + (problems > 0 ? $" ({problems} problems, see above)" : ""));
    return problems > 0 ? 2 : 0;

    void WriteFolder(DirectoryInfo dir, int depth)
    {
        var indent = new string(' ', depth * 4);
        writer.WriteLine($"{indent}<DT><H3{Dates(dir)}>{Escape(dir.Name)}</H3>");
        writer.WriteLine($"{indent}<DL><p>");
        folderCount++;

        FileSystemInfo[] entries;
        try
        {
            entries = dir.GetFileSystemInfos();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Report($"Can't read the folder {dir.FullName}: {e.Message}");
            entries = [];
        }
        Array.Sort(entries, (a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name));

        foreach (var entry in entries)
        {
            if (entry is DirectoryInfo sub)
                WriteFolder(sub, depth + 1);
            else if (entry.Extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                var url = ReadUrl(entry.FullName);
                if (url == null)
                    continue;
                var title = Path.GetFileNameWithoutExtension(entry.Name);
                writer.WriteLine($"{indent}    <DT><A HREF=\"{Escape(url)}\"{Dates(entry)}>{Escape(title)}</A>");
                linkCount++;
            }
        }

        writer.WriteLine($"{indent}</DL><p>");
    }

    // The URL= line of the [InternetShortcut] section, or null (with a message) if there isn't one.
    string? ReadUrl(string path)
    {
        string text;
        try
        {
            var bytes = File.ReadAllBytes(path);
            try
            {
                text = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) || bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble)
                    ? new StreamReader(new MemoryStream(bytes), detectEncodingFromByteOrderMarks: true).ReadToEnd()
                    : strictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                text = ansi.GetString(bytes);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Report($"Can't read {path}: {e.Message}");
            return null;
        }

        bool inSection = false;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('['))
                inSection = line.Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase);
            else if (inSection && line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
            {
                var url = line[4..].Trim();
                if (url.Length > 0)
                    return url;
            }
        }
        Report($"URL not found: {path}");
        return null;
    }

    string Dates(FileSystemInfo info)
    {
        if (!setDates)
            return "";
        try
        {
            // Copying a file gives it a new creation time later than its modified time,
            // so take the earlier of the two as the date it was added.
            long modified = ToUnix(info.LastWriteTimeUtc), added = Math.Min(ToUnix(info.CreationTimeUtc), modified);
            if (added <= 0)
                return "";
            return modified != added
                ? $" ADD_DATE=\"{added}\" LAST_MODIFIED=\"{modified}\""
                : $" ADD_DATE=\"{added}\"";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    void Report(string message)
    {
        // Keep problem messages out of the bookmarks when they're going to the console.
        (toConsole ? Console.Error : Console.Out).WriteLine(message);
        problems++;
    }
}

long ToUnix(DateTime utc) => new DateTimeOffset(utc).ToUnixTimeSeconds();

// Only the characters that would break the HTML; everything else is kept as UTF-8.
string Escape(string text) => text
    .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
