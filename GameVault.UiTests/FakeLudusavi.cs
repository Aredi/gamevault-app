using System.IO.Compression;

namespace GameVault.UiTests
{
    /// <summary>
    /// A stand-in for Ludusavi (cloud saves): each game has one save file, ~/saves/&lt;title&gt;/save.dat.
    /// It answers find, backup (--preview) and restore like Ludusavi's API does.
    /// </summary>
    internal static class FakeLudusavi
    {
        public static string SaveFile(string title) => Path.Combine(IsolatedHome.Root, "saves", title, "save.dat");

        public static void Install()
        {
            string bin = Path.Combine(IsolatedHome.Root, ".local", "bin");
            Directory.CreateDirectory(bin);
            string script = Path.Combine(bin, "ludusavi");
            File.WriteAllText(script, """
                #!/bin/sh
                # ludusavi --config DIR <command> ...
                shift 2
                command="$1"; shift
                saves="$HOME/saves"
                case "$command" in
                  find)
                    printf '{"games":{"%s":{"score":1.0}}}' "$1" ;;
                  backup)
                    if [ "$1" = "--preview" ]; then
                      title="$3"
                      printf '{"overall":{},"games":{"%s":{"files":{"%s":{"bytes":1}}}}}' "$title" "$saves/$title/save.dat"
                    else
                      # backup --force --format zip --path DIR TITLE
                      target="$5"; title="$6"
                      mkdir -p "$target/$title"
                      echo "name: $title" > "$target/$title/mapping.yaml"
                      cp "$saves/$title/save.dat" "$target/$title/save.dat"
                    fi ;;
                  restore)
                    # restore --force --path DIR
                    mapping=$(find "$3" -name mapping.yaml | head -1)
                    title=$(sed -n 's/^name: //p' "$mapping")
                    mkdir -p "$saves/$title"
                    cp "$(dirname "$mapping")/save.dat" "$saves/$title/save.dat" ;;
                esac
                """.Replace("\r\n", "\n"));
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        /// <summary>A server save as Ludusavi would back it up: "&lt;dir&gt;/&lt;title&gt;/mapping.yaml" next to the save.</summary>
        public static byte[] ServerSaveArchive(string title, string content)
        {
            using var output = new MemoryStream();
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var writer = new StreamWriter(zip.CreateEntry($"backup/{title}/mapping.yaml").Open()))
                    writer.Write($"name: {title}\n");
                using (var writer = new StreamWriter(zip.CreateEntry($"backup/{title}/save.dat").Open()))
                    writer.Write(content);
            }
            return output.ToArray();
        }
    }
}
