# Folders2File

A Windows command-line tool that turns a folder tree of Internet Shortcut (`.URL`) files into a Netscape-format bookmarks file that Chrome, Edge or Firefox can import. C# port of `folders2file.py`, and the reverse of [File2Folders](https://github.com/mwattsun/File2Folders).

- **Folders become bookmark folders, `.URL` files become links.** The folder you give becomes the top bookmark folder. Each link is titled with its file name minus `.URL`. Other files are ignored.
- **The bookmarks file goes next to the folder** and is named after it (`Magazines\` -> `Magazines.html`), unless you use `-o`. With `-o -` the bookmarks are written to the console, and messages go to stderr so they stay out of a redirect.
- **Dates are kept.** `LAST_MODIFIED` comes from the file or folder's modified time. `ADD_DATE` comes from its creation time, or from the modified time when that's earlier (as it is for copied files). Use `--no-dates` to leave both out. File2Folders reads these dates back, so a round trip keeps them.
- **`.URL` files can be UTF-8 or ANSI.** The URL is taken from the `URL=` line of the `[InternetShortcut]` section. A file with no URL is skipped and listed.

## Usage

```
Folders2File folder [options]
```

| Option | Effect |
|---|---|
| `-o`, `--output file` | Write to this file instead (`-` writes to the console) |
| `--no-dates` | Leave out `ADD_DATE` and `LAST_MODIFIED` |
| `-h`, `--help` | Show help |

Exit code is 0 on success, 1 if the folder isn't found or the output can't be written, and 2 if some files or folders had problems (listed in the output).

## Publish

Settings are in `Properties\PublishProfiles\FolderProfile.pubxml` (self-contained, single file, trimmed, to `D:\Tools\bin`):

```
dotnet publish Folders2File.csproj -p:PublishProfile=FolderProfile
```
