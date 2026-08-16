using System;
using System.IO;
using System.Linq;

namespace StockTracker.Services
{
    /// <summary>Keeps downloaded history outside build output so Debug and Release share it.</summary>
    public static class AppDataPathService
    {
        private const long MinimumUsefulDatabaseBytes = 100 * 1024;

        public static string GetT86HistoryDirectory()
        {
            var destinationDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StockTracker", "T86_History");
            Directory.CreateDirectory(destinationDirectory);
            MigrateLegacyHistoryIfNeeded(destinationDirectory);
            return destinationDirectory;
        }

        private static void MigrateLegacyHistoryIfNeeded(string destinationDirectory)
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var sourceDirectories = new[]
            {
                Path.Combine(baseDirectory, "T86_History"),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "Debug", "T86_History")),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "..", "Debug", "T86_History"))
            }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var fileName in new[]
            {
                "twse_t86.db", "twse_margin.db", "twse_margin_metric.db", "daily_price.db",
                "Ranking.db", "RankingEmailList.txt"
            })
            {
                var destination = Path.Combine(destinationDirectory, fileName);
                var destinationLength = File.Exists(destination) ? new FileInfo(destination).Length : 0L;
                var source = sourceDirectories
                    .Select(directory => Path.Combine(directory, fileName))
                    .Where(File.Exists)
                    .Select(path => new FileInfo(path))
                    .Where(file => file.Length > Math.Max(destinationLength, MinimumUsefulDatabaseBytes))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (source != null)
                    File.Copy(source.FullName, destination, true);
            }
        }
    }
}
