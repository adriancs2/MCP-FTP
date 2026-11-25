using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MCP_FTP.Ftp
{
    public static class FtpSyncEngine
    {
        public static List<RemoteFileInfo> ListRemoteFiles(string profileName, string subPath = "")
        {
            var cfg = ProfileStore.Load(profileName);
            var client = new FtpClientEx(cfg);
            var map = client.ListAllFilesRecursive(subPath ?? "");
            return map.Values.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static SyncReport DeleteRemoteFiles(string profileName, IEnumerable<string> relativePaths)
        {
            var sw = Stopwatch.StartNew();
            var cfg = ProfileStore.Load(profileName);
            var client = new FtpClientEx(cfg);
            var report = new SyncReport { Profile = cfg.ProfileName, SecurityUsed = client.EffectiveSecurityMode };

            foreach (string raw in relativePaths ?? Enumerable.Empty<string>())
            {
                string rel = (raw ?? "").Trim().Replace('\\', '/').TrimStart('/');
                if (string.IsNullOrEmpty(rel) || rel.StartsWith("#")) continue;

                try
                {
                    client.DeleteFile(rel);
                    report.Deleted.Add(rel);
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"Delete '{rel}': {ex.Message}");
                }
            }

            sw.Stop();
            report.DurationMs = sw.ElapsedMilliseconds;
            return report;
        }

        public static SyncReport SyncProfile(string profileName, bool forceUploadAll = false)
        {
            var sw = Stopwatch.StartNew();
            var cfg = ProfileStore.Load(profileName);

            if (string.IsNullOrWhiteSpace(cfg.LocalFolder) || !Directory.Exists(cfg.LocalFolder))
                throw new DirectoryNotFoundException($"Local folder not found: {cfg.LocalFolder}");

            var client = new FtpClientEx(cfg);
            var report = new SyncReport { Profile = cfg.ProfileName, SecurityUsed = client.EffectiveSecurityMode };

            string excludeText = ProfileStore.ReadExcludeText(cfg.ProfileName);
            var matcher = new ExcludeMatcher(excludeText, cfg.LocalFolder);

            // 1. Scan remote files for datetime + size matching (unless forceUploadAll)
            Dictionary<string, RemoteFileInfo> remoteFiles = forceUploadAll
                ? new Dictionary<string, RemoteFileInfo>(StringComparer.OrdinalIgnoreCase)
                : client.ListAllFilesRecursive("");

            // 2. Recursively scan local folder and upload new/modified files
            string rootFull = Path.GetFullPath(cfg.LocalFolder).TrimEnd('\\', '/');
            foreach (string localFile in Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories))
            {
                string rel = localFile.Substring(rootFull.Length + 1).Replace('\\', '/');
                if (matcher.IsExcluded(rel))
                {
                    report.SkippedExcluded++;
                    continue;
                }

                var fi = new FileInfo(localFile);
                if (!forceUploadAll && remoteFiles.TryGetValue(rel, out RemoteFileInfo remote))
                {
                    bool sizeMatch = remote.Size == fi.Length;
                    bool timeMatch = remote.ModifiedUtc != DateTime.MinValue
                        && remote.ModifiedUtc >= fi.LastWriteTimeUtc.AddSeconds(-2);

                    if (sizeMatch && timeMatch)
                    {
                        report.SkippedUnchanged++;
                        continue;
                    }
                }

                try
                {
                    client.UploadFile(localFile, rel);
                    report.Uploaded.Add(rel);
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"Upload '{rel}': {ex.Message}");
                }
            }

            // 3. Consume delete-file.txt if present, then delete delete-file.txt immediately
            string deleteFilePath = ProfileStore.GetDeleteFilePath(cfg.ProfileName);
            if (File.Exists(deleteFilePath))
            {
                string[] lines = File.ReadAllLines(deleteFilePath);
                foreach (string raw in lines)
                {
                    string rel = (raw ?? "").Trim().Replace('\\', '/').TrimStart('/');
                    if (string.IsNullOrEmpty(rel) || rel.StartsWith("#")) continue;

                    try
                    {
                        client.DeleteFile(rel);
                        report.Deleted.Add(rel);
                    }
                    catch (Exception ex)
                    {
                        report.Errors.Add($"Delete '{rel}': {ex.Message}");
                    }
                }

                try
                {
                    File.Delete(deleteFilePath);
                    report.DeleteFileConsumed = true;
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"Remove delete-file.txt: {ex.Message}");
                }
            }

            sw.Stop();
            report.DurationMs = sw.ElapsedMilliseconds;
            return report;
        }
    }
}
