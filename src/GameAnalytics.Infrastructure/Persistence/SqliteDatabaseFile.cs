using System.Text;
using Microsoft.Data.Sqlite;

namespace GameAnalytics.Infrastructure.Persistence;

/// <summary>
/// Replaces the live SQLite file (e.g. with a multi-GB VPS snapshot) without writing over
/// a file that pooled connections still hold open, and without leaving the old -wal/-shm behind.
/// </summary>
public static class SqliteDatabaseFile
{
    /// <summary>Sidecar files SQLite keeps next to the database; they belong to the old file.</summary>
    public static readonly IReadOnlyList<string> SidecarSuffixes = new[] { "-wal", "-shm", "-journal" };

    private static readonly byte[] SqliteHeader = Encoding.ASCII.GetBytes("SQLite format 3\0");

    /// <summary>Temp download path in the target's directory, so the final swap is a same-volume rename.</summary>
    public static string CreateDownloadTempPath(string targetPath) =>
        $"{targetPath}.download_{Guid.NewGuid():N}";

    /// <summary>True when the file starts with the SQLite 3 magic header (guards against HTML error pages).</summary>
    public static bool HasSqliteHeader(string path)
    {
        if (!File.Exists(path))
            return false;

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[SqliteHeader.Length];
        var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        return read == buffer.Length && buffer.AsSpan().SequenceEqual(SqliteHeader);
    }

    /// <summary>
    /// Closes pooled connections, removes the old sidecars and renames <paramref name="sourcePath"/>
    /// over <paramref name="targetPath"/>. Retries briefly while a connection is still being released.
    /// </summary>
    public static async Task ReplaceAsync(string sourcePath, string targetPath, int maxAttempts = 5, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Downloaded database file is missing.", sourcePath);

        for (var attempt = 1; ; attempt++)
        {
            // Idle pooled connections keep the file handle open on Windows; closing the last one also checkpoints the WAL.
            SqliteConnection.ClearAllPools();
            try
            {
                // A leftover WAL would be replayed onto the new file and corrupt it.
                DeleteSidecars(targetPath);
                File.Move(sourcePath, targetPath, overwrite: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < maxAttempts)
            {
                await Task.Delay(200 * attempt, ct);
            }
        }
    }

    public static void DeleteSidecars(string databasePath)
    {
        foreach (var suffix in SidecarSuffixes)
        {
            var sidecar = databasePath + suffix;
            if (File.Exists(sidecar))
                File.Delete(sidecar);
        }
    }
}
