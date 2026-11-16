using System;
using System.IO;

namespace lcdb.IO;

/// <summary>Complete and flush a sibling temporary file before replacing an existing document.</summary>
public static class AtomicFile
{
    public static void Write(string path, Action<Stream> write)
    {
        string destination = Path.GetFullPath(path);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                write(stream);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(destination))
                File.Replace(temporary, destination, destination + ".bak");
            else
                File.Move(temporary, destination);
        }
        finally
        {
            // A failed write or replace leaves the previous destination intact.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
