namespace RyanMusicStudio.Core.Persistence;

public static class AtomicFile
{
    public static void WriteAllText(string destination, string contents)
    {
        var dir = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var backup = destination + ".bak";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(true);
            }

            if (File.Exists(destination))
            {
                File.Replace(temp, destination, backup, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, destination);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { /* leftover temp is harmless */ }
            }
        }
    }

    public static bool LooksLikeValidProjectJson(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var text = File.ReadAllText(path);
            return text.Contains("\"formatVersion\"", StringComparison.OrdinalIgnoreCase)
                   && text.Contains("\"tracks\"", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
