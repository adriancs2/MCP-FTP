using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace MCP_FTP.Ftp
{
    public static class FtpSyncEngine
    {
        public static List<RemoteFileInfo> ListRemoteFiles(string profileName, string subPath = "", bool includeExcluded = false)
        {
            var cfg = ProfileStore.Load(profileName);
            var client = new FtpClientEx(cfg);
            ExcludeMatcher matcher = includeExcluded
                ? null
                : new ExcludeMatcher(ProfileStore.ReadExcludeText(cfg.ProfileName), cfg.LocalFolder);
            var map = client.ListAllFilesRecursive(subPath ?? "", matcher);
            return map.Values.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static object ReadRemoteTextFile(string profileName, string relativeRemotePath, int tailLines = 0, int maxBytes = 262144)
        {
            var sw = Stopwatch.StartNew();
            var cfg = ProfileStore.Load(profileName);
            var client = new FtpClientEx(cfg);
            string norm = (relativeRemotePath ?? "").Trim().Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrEmpty(norm))
                throw new ArgumentException("Remote file path is required.");

            // If tailLines > 0, allow reading up to 4 MB (unless caller passed a larger maxBytes) so we get the real tail of PHP error_log files
            int fetchCap = maxBytes > 0 ? maxBytes : 262144;
            if (tailLines > 0 && fetchCap < 4 * 1024 * 1024)
                fetchCap = 4 * 1024 * 1024;

            byte[] raw = client.DownloadBytes(norm, fetchCap, out bool byteTruncated);
            string text = DecodeUtf8Text(raw);
            bool lineTruncated = false;

            if (tailLines > 0 && !string.IsNullOrEmpty(text))
            {
                string[] lines = text.Replace("\r\n", "\n").Split('\n');
                // Drop trailing empty element after final newline when counting lines
                int effectiveCount = lines.Length;
                bool endsWithNewline = effectiveCount > 0 && lines[effectiveCount - 1].Length == 0;
                int logicalLines = endsWithNewline ? effectiveCount - 1 : effectiveCount;

                if (logicalLines > tailLines)
                {
                    int start = logicalLines - tailLines;
                    text = string.Join("\n", lines.Skip(start).Take(tailLines));
                    lineTruncated = true;
                }
            }

            if (maxBytes > 0 && Encoding.UTF8.GetByteCount(text) > maxBytes)
            {
                byte[] utf8Bytes = Encoding.UTF8.GetBytes(text);
                if (tailLines > 0)
                {
                    // Keep the latest bytes from the end
                    text = Encoding.UTF8.GetString(utf8Bytes, utf8Bytes.Length - maxBytes, maxBytes);
                }
                else
                {
                    text = Encoding.UTF8.GetString(utf8Bytes, 0, maxBytes);
                }
                byteTruncated = true;
            }

            sw.Stop();
            return new
            {
                profile = cfg.ProfileName,
                remote_path = norm,
                bytes_fetched = raw.Length,
                truncated = byteTruncated || lineTruncated,
                tail_lines = tailLines > 0 ? (int?)tailLines : null,
                duration_ms = sw.ElapsedMilliseconds,
                content = text
            };
        }

        public static object DownloadRemoteFiles(string profileName, IEnumerable<string> relativeRemotePaths, string customLocalDir = null, bool overwrite = true)
        {
            var sw = Stopwatch.StartNew();
            var cfg = ProfileStore.Load(profileName);
            string baseLocal = string.IsNullOrWhiteSpace(customLocalDir) ? cfg.LocalFolder : customLocalDir.Trim();
            if (string.IsNullOrWhiteSpace(baseLocal))
                throw new ArgumentException("No local destination folder specified and profile has no local_folder.");

            string rootFull = Path.GetFullPath(baseLocal).TrimEnd('\\', '/');
            bool isProfileRoot = !string.IsNullOrWhiteSpace(cfg.LocalFolder)
                && rootFull.Equals(Path.GetFullPath(cfg.LocalFolder).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

            var snapshot = isProfileRoot ? ProfileStore.LoadSnapshot(cfg.ProfileName) : null;
            bool snapshotDirty = false;
            var client = new FtpClientEx(cfg);

            var downloaded = new List<object>();
            var skipped = new List<string>();
            var errors = new List<string>();

            foreach (string raw in relativeRemotePaths ?? Enumerable.Empty<string>())
            {
                string rel = (raw ?? "").Trim().Replace('\\', '/').TrimStart('/');
                if (string.IsNullOrEmpty(rel) || rel.StartsWith("#")) continue;

                // Prevent path traversal outside rootFull
                string candidate = Path.GetFullPath(Path.Combine(rootFull, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!candidate.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !candidate.Equals(rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Download '{rel}': Invalid relative path outside destination root.");
                    continue;
                }

                if (!overwrite && File.Exists(candidate))
                {
                    skipped.Add(rel);
                    continue;
                }

                try
                {
                    long bytes = client.DownloadFile(rel, candidate);
                    downloaded.Add(new { remote_path = rel, local_path = candidate, bytes });
                    if (snapshot != null)
                    {
                        var fi = new FileInfo(candidate);
                        snapshot[rel] = new LocalFileSnap(fi.Length, fi.LastWriteTimeUtc.Ticks);
                        snapshotDirty = true;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Download '{rel}': {ex.Message}");
                }
            }

            if (snapshot != null && snapshotDirty)
            {
                ProfileStore.SaveSnapshot(cfg.ProfileName, snapshot);
            }

            sw.Stop();
            return new
            {
                profile = cfg.ProfileName,
                security_used = client.EffectiveSecurityMode,
                destination_root = rootFull,
                downloaded,
                skipped_existing = skipped,
                errors,
                duration_ms = sw.ElapsedMilliseconds
            };
        }

        public static object DownloadAllRemoteFiles(string profileName, string subPath = "", string customLocalDir = null, bool overwrite = true, bool includeExcluded = false)
        {
            var sw = Stopwatch.StartNew();
            var cfg = ProfileStore.Load(profileName);
            string baseLocal = string.IsNullOrWhiteSpace(customLocalDir) ? cfg.LocalFolder : customLocalDir.Trim();
            if (string.IsNullOrWhiteSpace(baseLocal))
                throw new ArgumentException("No local destination folder specified and profile has no local_folder.");

            string rootFull = Path.GetFullPath(baseLocal).TrimEnd('\\', '/');
            if (!Directory.Exists(rootFull))
                Directory.CreateDirectory(rootFull);

            bool isProfileRoot = !string.IsNullOrWhiteSpace(cfg.LocalFolder)
                && rootFull.Equals(Path.GetFullPath(cfg.LocalFolder).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

            string normSub = (subPath ?? "").Replace('\\', '/').Trim('/');
            var matcher = includeExcluded
                ? null
                : new ExcludeMatcher(ProfileStore.ReadExcludeText(cfg.ProfileName), cfg.LocalFolder);

            var client = new FtpClientEx(cfg);
            int skippedRemoteDirs = 0;
            var remoteMap = client.ListAllFilesRecursive(normSub, matcher, out skippedRemoteDirs);

            // If pulling into the profile's own local_folder, update (or initialize) sync_snapshot.txt
            Dictionary<string, LocalFileSnap> snapshot = null;
            if (isProfileRoot)
            {
                snapshot = ProfileStore.LoadSnapshot(cfg.ProfileName)
                    ?? new Dictionary<string, LocalFileSnap>(StringComparer.OrdinalIgnoreCase);
            }

            var downloaded = new List<string>();
            var skippedExisting = new List<string>();
            var errors = new List<string>();
            long totalBytes = 0;

            foreach (var entry in remoteMap.Values.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase))
            {
                string rel = entry.Path;
                string candidate = Path.GetFullPath(Path.Combine(rootFull, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!candidate.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !candidate.Equals(rootFull, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Download '{rel}': Invalid relative path outside destination root.");
                    continue;
                }

                if (!overwrite && File.Exists(candidate))
                {
                    skippedExisting.Add(rel);
                    if (snapshot != null)
                    {
                        var existingFi = new FileInfo(candidate);
                        snapshot[rel] = new LocalFileSnap(existingFi.Length, existingFi.LastWriteTimeUtc.Ticks);
                    }
                    continue;
                }

                try
                {
                    long bytes = client.DownloadFile(rel, candidate);
                    downloaded.Add(rel);
                    totalBytes += bytes;
                    if (snapshot != null)
                    {
                        var fi = new FileInfo(candidate);
                        snapshot[rel] = new LocalFileSnap(fi.Length, fi.LastWriteTimeUtc.Ticks);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Download '{rel}': {ex.Message}");
                }
            }

            if (snapshot != null)
            {
                ProfileStore.SaveSnapshot(cfg.ProfileName, snapshot);
            }

            sw.Stop();
            return new
            {
                profile = cfg.ProfileName,
                security_used = client.EffectiveSecurityMode,
                destination_root = rootFull,
                downloaded_count = downloaded.Count,
                total_bytes = totalBytes,
                skipped_existing_count = skippedExisting.Count,
                skipped_remote_excluded_dirs = skippedRemoteDirs,
                snapshot_updated = snapshot != null,
                downloaded,
                errors,
                duration_ms = sw.ElapsedMilliseconds
            };
        }

        private static string DecodeUtf8Text(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return "";

            // Strip UTF-8 BOM if present
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

            return Encoding.UTF8.GetString(bytes);
        }

        public static SyncReport DeleteRemoteFiles(string profileName, IEnumerable<string> relativePaths)
        {
            var sw = Stopwatch.StartNew();
            var cfg = ProfileStore.Load(profileName);
            var client = new FtpClientEx(cfg);
            var report = new SyncReport
            {
                Profile = cfg.ProfileName,
                SyncMode = "delete_only",
                SecurityUsed = client.EffectiveSecurityMode
            };

            var snapshot = ProfileStore.LoadSnapshot(cfg.ProfileName);
            bool snapshotChanged = false;

            foreach (string raw in relativePaths ?? Enumerable.Empty<string>())
            {
                string rel = (raw ?? "").Trim().Replace('\\', '/').TrimStart('/');
                if (string.IsNullOrEmpty(rel) || rel.StartsWith("#")) continue;

                try
                {
                    client.DeleteFile(rel);
                    report.Deleted.Add(rel);
                    if (snapshot != null && snapshot.Remove(rel))
                        snapshotChanged = true;
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"Delete '{rel}': {ex.Message}");
                }
            }

            if (snapshot != null && snapshotChanged)
            {
                ProfileStore.SaveSnapshot(cfg.ProfileName, snapshot);
            }

            sw.Stop();
            report.DurationMs = sw.ElapsedMilliseconds;
            return report;
        }

        private struct PendingUploadItem
        {
            public string LocalFullPath;
            public string RelativePath;
            public LocalFileSnap Snap;
        }

        public static SyncReport SyncProfile(string profileName, bool forceUploadAll = false, bool verifyRemote = false)
        {
            var sw = Stopwatch.StartNew();
            var cfg = ProfileStore.Load(profileName);

            if (string.IsNullOrWhiteSpace(cfg.LocalFolder) || !Directory.Exists(cfg.LocalFolder))
                throw new DirectoryNotFoundException($"Local folder not found: {cfg.LocalFolder}");

            string excludeText = ProfileStore.ReadExcludeText(cfg.ProfileName);
            var matcher = new ExcludeMatcher(excludeText, cfg.LocalFolder);
            string rootFull = Path.GetFullPath(cfg.LocalFolder).TrimEnd('\\', '/');
            string deleteFilePath = ProfileStore.GetDeleteFilePath(cfg.ProfileName);
            bool hasDeleteFile = File.Exists(deleteFilePath);

            var existingSnapshot = ProfileStore.LoadSnapshot(cfg.ProfileName);
            bool useLocalSnapshot = !forceUploadAll && !verifyRemote && existingSnapshot != null;

            var report = new SyncReport
            {
                Profile = cfg.ProfileName,
                SyncMode = forceUploadAll
                    ? "force_all"
                    : (useLocalSnapshot ? "local_snapshot" : (existingSnapshot == null ? "initial_remote_scan" : "verify_remote"))
            };

            var nextSnapshot = new Dictionary<string, LocalFileSnap>(StringComparer.OrdinalIgnoreCase);

            // FAST PATH: Local Snapshot Delta Detection (zero remote MLSD queries!)
            if (useLocalSnapshot)
            {
                var pendingUploads = new List<PendingUploadItem>();

                foreach (string localFile in Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories))
                {
                    string rel = localFile.Substring(rootFull.Length + 1).Replace('\\', '/');
                    if (matcher.IsExcluded(rel))
                    {
                        report.SkippedExcluded++;
                        continue;
                    }

                    var fi = new FileInfo(localFile);
                    var currentSnap = new LocalFileSnap(fi.Length, fi.LastWriteTimeUtc.Ticks);

                    if (existingSnapshot.TryGetValue(rel, out LocalFileSnap recordedSnap)
                        && recordedSnap.Size == currentSnap.Size
                        && recordedSnap.LastWriteUtcTicks == currentSnap.LastWriteUtcTicks)
                    {
                        report.SkippedUnchanged++;
                        nextSnapshot[rel] = recordedSnap;
                    }
                    else
                    {
                        pendingUploads.Add(new PendingUploadItem
                        {
                            LocalFullPath = localFile,
                            RelativePath = rel,
                            Snap = currentSnap
                        });
                    }
                }

                // Instant exit without opening any FTP connection if nothing changed and no delete-file.txt
                if (pendingUploads.Count == 0 && !hasDeleteFile)
                {
                    if (nextSnapshot.Count != existingSnapshot.Count)
                        ProfileStore.SaveSnapshot(cfg.ProfileName, nextSnapshot);

                    report.SecurityUsed = "None (No Changes)";
                    sw.Stop();
                    report.DurationMs = sw.ElapsedMilliseconds;
                    return report;
                }

                var fastClient = new FtpClientEx(cfg);
                report.SecurityUsed = fastClient.EffectiveSecurityMode;
                fastClient.SeedKnownDirectories(existingSnapshot.Keys);

                foreach (var item in pendingUploads)
                {
                    try
                    {
                        fastClient.UploadFile(item.LocalFullPath, item.RelativePath);
                        report.Uploaded.Add(item.RelativePath);
                        nextSnapshot[item.RelativePath] = item.Snap;
                    }
                    catch (Exception ex)
                    {
                        report.Errors.Add($"Upload '{item.RelativePath}': {ex.Message}");
                        // Preserve prior snapshot entry (if any) so next sync retries the failed upload
                        if (existingSnapshot.TryGetValue(item.RelativePath, out LocalFileSnap oldSnap))
                            nextSnapshot[item.RelativePath] = oldSnap;
                    }
                }

                if (hasDeleteFile)
                {
                    ConsumeDeleteFile(fastClient, deleteFilePath, nextSnapshot, report);
                }

                ProfileStore.SaveSnapshot(cfg.ProfileName, nextSnapshot);
                sw.Stop();
                report.DurationMs = sw.ElapsedMilliseconds;
                return report;
            }

            // FULL RECONCILE PATH: initial_remote_scan, verify_remote, or force_all
            var client = new FtpClientEx(cfg);
            report.SecurityUsed = client.EffectiveSecurityMode;

            int skippedRemoteDirs = 0;
            Dictionary<string, RemoteFileInfo> remoteFiles = forceUploadAll
                ? new Dictionary<string, RemoteFileInfo>(StringComparer.OrdinalIgnoreCase)
                : client.ListAllFilesRecursive("", matcher, out skippedRemoteDirs);
            report.SkippedRemoteExcludedDirs = skippedRemoteDirs;

            foreach (string localFile in Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories))
            {
                string rel = localFile.Substring(rootFull.Length + 1).Replace('\\', '/');
                if (matcher.IsExcluded(rel))
                {
                    report.SkippedExcluded++;
                    continue;
                }

                var fi = new FileInfo(localFile);
                var currentSnap = new LocalFileSnap(fi.Length, fi.LastWriteTimeUtc.Ticks);

                if (!forceUploadAll && remoteFiles.TryGetValue(rel, out RemoteFileInfo remote))
                {
                    bool sizeMatch = remote.Size == fi.Length;
                    bool timeMatch = remote.ModifiedUtc != DateTime.MinValue
                        && remote.ModifiedUtc >= fi.LastWriteTimeUtc.AddSeconds(-2);

                    if (sizeMatch && timeMatch)
                    {
                        report.SkippedUnchanged++;
                        nextSnapshot[rel] = currentSnap;
                        continue;
                    }
                }

                try
                {
                    client.UploadFile(localFile, rel);
                    report.Uploaded.Add(rel);
                    nextSnapshot[rel] = currentSnap;
                }
                catch (Exception ex)
                {
                    report.Errors.Add($"Upload '{rel}': {ex.Message}");
                }
            }

            if (hasDeleteFile)
            {
                ConsumeDeleteFile(client, deleteFilePath, nextSnapshot, report);
            }

            ProfileStore.SaveSnapshot(cfg.ProfileName, nextSnapshot);
            sw.Stop();
            report.DurationMs = sw.ElapsedMilliseconds;
            return report;
        }

        private static void ConsumeDeleteFile(FtpClientEx client, string deleteFilePath, Dictionary<string, LocalFileSnap> snapshot, SyncReport report)
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
                    snapshot?.Remove(rel);
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
    }
}
