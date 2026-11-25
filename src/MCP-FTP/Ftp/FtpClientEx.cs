using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;

namespace MCP_FTP.Ftp
{
    public class FtpClientEx
    {
        private readonly string _host;
        private readonly int _port;
        private readonly NetworkCredential _cred;
        private readonly string _baseRemotePath;
        private readonly HashSet<string> _knownDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool UseSsl { get; private set; }
        public string EffectiveSecurityMode => UseSsl ? "ExplicitFTPS" : "PlainFTP";

        public FtpClientEx(FtpProfileConfig cfg)
        {
            _host = (cfg.Host ?? "").Trim();
            _port = cfg.Port > 0 ? cfg.Port : 21;
            _cred = new NetworkCredential(cfg.Username ?? "", cfg.GetPlainPassword());
            _baseRemotePath = NormalizeRemoteBase(cfg.RemoteFolder);

            string mode = (cfg.SecurityMode ?? "Auto").Trim();
            if (mode.Equals("PlainFTP", StringComparison.OrdinalIgnoreCase))
            {
                UseSsl = false;
            }
            else if (mode.Equals("ExplicitFTPS", StringComparison.OrdinalIgnoreCase))
            {
                UseSsl = true;
            }
            else
            {
                // Auto: try Explicit FTPS first, fall back to Plain FTP
                UseSsl = true;
                try
                {
                    ProbeConnection();
                }
                catch
                {
                    UseSsl = false;
                    ProbeConnection();
                }
            }
        }

        private static string NormalizeRemoteBase(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Trim() == "/")
                return "";
            return "/" + path.Replace('\\', '/').Trim('/');
        }

        private string BuildUri(string relativePath, bool isDir = false)
        {
            string rel = (relativePath ?? "").Replace('\\', '/').Trim('/');
            string fullPath = _baseRemotePath;
            if (!string.IsNullOrEmpty(rel))
                fullPath += "/" + rel;
            if (string.IsNullOrEmpty(fullPath))
                fullPath = "/";
            else if (isDir && !fullPath.EndsWith("/"))
                fullPath += "/";

            return $"ftp://{_host}:{_port}{fullPath}";
        }

        private FtpWebRequest CreateRequest(string relativePath, string method, bool isDir = false)
        {
            var req = (FtpWebRequest)WebRequest.Create(BuildUri(relativePath, isDir));
            req.Method = method;
            req.Credentials = _cred;
            req.UseBinary = true;
            req.UsePassive = true;
            req.KeepAlive = true;
            req.EnableSsl = UseSsl;
            req.Timeout = 20000;
            req.ReadWriteTimeout = 30000;
            return req;
        }

        public void ProbeConnection()
        {
            var req = CreateRequest("", WebRequestMethods.Ftp.PrintWorkingDirectory, true);
            using (var resp = (FtpWebResponse)req.GetResponse())
            {
                _ = resp.StatusCode;
            }
        }

        public List<RemoteFileInfo> ListDirectory(string relativeDir)
        {
            var results = new List<RemoteFileInfo>();
            string normDir = (relativeDir ?? "").Replace('\\', '/').Trim('/');

            try
            {
                var req = CreateRequest(normDir, "MLSD", true);
                using (var resp = (FtpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream()))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var item = ParseMlsdLine(line, normDir);
                        if (item != null) results.Add(item);
                    }
                }
                return results;
            }
            catch (Exception)
            {
                // Fallback to LIST if server or .NET FtpWebRequest doesn't support MLSD
                var req = CreateRequest(normDir, WebRequestMethods.Ftp.ListDirectoryDetails, true);
                using (var resp = (FtpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream()))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var item = ParseUnixListLine(line, normDir);
                        if (item != null) results.Add(item);
                    }
                }
                return results;
            }
        }

        public Dictionary<string, RemoteFileInfo> ListAllFilesRecursive(string startDir = "", ExcludeMatcher matcher = null)
        {
            int ignored = 0;
            return ListAllFilesRecursive(startDir, matcher, out ignored);
        }

        public Dictionary<string, RemoteFileInfo> ListAllFilesRecursive(string startDir, ExcludeMatcher matcher, out int skippedExcludedDirs)
        {
            skippedExcludedDirs = 0;
            var map = new Dictionary<string, RemoteFileInfo>(StringComparer.OrdinalIgnoreCase);
            string normStart = (startDir ?? "").Replace('\\', '/').Trim('/');
            if (!string.IsNullOrEmpty(normStart) && matcher != null && matcher.IsExcluded(normStart, isDirectory: true))
            {
                skippedExcludedDirs++;
                return map;
            }
            WalkRemote(normStart, map, matcher, ref skippedExcludedDirs);
            return map;
        }

        private void WalkRemote(string dir, Dictionary<string, RemoteFileInfo> map, ExcludeMatcher matcher, ref int skippedExcludedDirs)
        {
            List<RemoteFileInfo> entries;
            try
            {
                entries = ListDirectory(dir);
            }
            catch
            {
                return;
            }

            foreach (var entry in entries)
            {
                if (entry.IsDir)
                {
                    _knownDirs.Add(entry.Path);
                    if (matcher != null && matcher.IsExcluded(entry.Path, isDirectory: true))
                    {
                        skippedExcludedDirs++;
                        continue;
                    }
                    WalkRemote(entry.Path, map, matcher, ref skippedExcludedDirs);
                }
                else
                {
                    if (matcher != null && matcher.IsExcluded(entry.Path, isDirectory: false))
                        continue;
                    map[entry.Path] = entry;
                }
            }
        }

        public void SeedKnownDirectories(IEnumerable<string> relativeFilePaths)
        {
            if (relativeFilePaths == null) return;
            foreach (string raw in relativeFilePaths)
            {
                string norm = (raw ?? "").Replace('\\', '/').Trim('/');
                int slash = norm.LastIndexOf('/');
                while (slash > 0)
                {
                    string parent = norm.Substring(0, slash);
                    if (!_knownDirs.Add(parent))
                        break;
                    slash = parent.LastIndexOf('/');
                }
            }
        }

        private void ForgetDirectoryChain(string relativeDir)
        {
            string norm = (relativeDir ?? "").Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(norm)) return;
            string[] parts = norm.Split('/');
            string current = "";
            foreach (string part in parts)
            {
                current = string.IsNullOrEmpty(current) ? part : current + "/" + part;
                _knownDirs.Remove(current);
            }
        }

        public void EnsureDirectoryExists(string relativeDir)
        {
            string norm = (relativeDir ?? "").Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(norm) || _knownDirs.Contains(norm))
                return;

            string[] parts = norm.Split('/');
            string current = "";
            foreach (string part in parts)
            {
                current = string.IsNullOrEmpty(current) ? part : current + "/" + part;
                if (_knownDirs.Contains(current)) continue;

                try
                {
                    var req = CreateRequest(current, WebRequestMethods.Ftp.MakeDirectory, false);
                    using (var resp = (FtpWebResponse)req.GetResponse()) { }
                }
                catch (WebException ex)
                {
                    // 550 = directory already exists
                    if (ex.Response is FtpWebResponse ftpResp && ftpResp.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
                    {
                        // Ignore already exists
                    }
                }
                _knownDirs.Add(current);
            }
        }

        public void UploadFile(string localPath, string relativeRemotePath)
        {
            string norm = relativeRemotePath.Replace('\\', '/').Trim('/');
            int lastSlash = norm.LastIndexOf('/');
            string parentDir = lastSlash > 0 ? norm.Substring(0, lastSlash) : null;
            if (parentDir != null)
            {
                EnsureDirectoryExists(parentDir);
            }

            try
            {
                UploadFileCore(localPath, norm);
            }
            catch (WebException ex) when (parentDir != null && ex.Response is FtpWebResponse ftpResp && ftpResp.StatusCode == FtpStatusCode.ActionNotTakenFileUnavailable)
            {
                // Cached parent directory was missing on server; recreate directory chain and retry once
                ForgetDirectoryChain(parentDir);
                EnsureDirectoryExists(parentDir);
                UploadFileCore(localPath, norm);
            }
        }

        private void UploadFileCore(string localPath, string normRemotePath)
        {
            var req = CreateRequest(normRemotePath, WebRequestMethods.Ftp.UploadFile, false);
            using (var fileStream = File.OpenRead(localPath))
            using (var reqStream = req.GetRequestStream())
            {
                fileStream.CopyTo(reqStream);
            }

            using (var resp = (FtpWebResponse)req.GetResponse())
            {
                _ = resp.StatusCode;
            }
        }

        public void DeleteFile(string relativeRemotePath)
        {
            string norm = relativeRemotePath.Replace('\\', '/').Trim('/');
            var req = CreateRequest(norm, WebRequestMethods.Ftp.DeleteFile, false);
            using (var resp = (FtpWebResponse)req.GetResponse())
            {
                _ = resp.StatusCode;
            }
        }

        public long DownloadFile(string relativeRemotePath, string localDestinationPath)
        {
            string norm = (relativeRemotePath ?? "").Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(norm))
                throw new ArgumentException("Remote file path is required.");

            string dir = Path.GetDirectoryName(localDestinationPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            long bytesWritten = 0;
            var req = CreateRequest(norm, WebRequestMethods.Ftp.DownloadFile, false);
            using (var resp = (FtpWebResponse)req.GetResponse())
            using (var respStream = resp.GetResponseStream())
            using (var fileStream = new FileStream(localDestinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[65536];
                int read;
                while (respStream != null && (read = respStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    fileStream.Write(buffer, 0, read);
                    bytesWritten += read;
                }
            }
            return bytesWritten;
        }

        public byte[] DownloadBytes(string relativeRemotePath, int maxBytes, out bool truncated)
        {
            truncated = false;
            string norm = (relativeRemotePath ?? "").Replace('\\', '/').Trim('/');
            if (string.IsNullOrEmpty(norm))
                throw new ArgumentException("Remote file path is required.");

            var req = CreateRequest(norm, WebRequestMethods.Ftp.DownloadFile, false);
            using (var ms = new MemoryStream())
            {
                try
                {
                    using (var resp = (FtpWebResponse)req.GetResponse())
                    using (var respStream = resp.GetResponseStream())
                    {
                        byte[] buffer = new byte[16384];
                        int read;
                        while (respStream != null && (read = respStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (maxBytes > 0 && ms.Length + read > maxBytes)
                            {
                                int toKeep = maxBytes - (int)ms.Length;
                                if (toKeep > 0)
                                    ms.Write(buffer, 0, toKeep);
                                truncated = true;
                                req.Abort();
                                break;
                            }
                            ms.Write(buffer, 0, read);
                        }
                    }
                }
                catch (WebException) when (truncated)
                {
                    // Expected when aborting stream early after reaching maxBytes
                }

                return ms.ToArray();
            }
        }

        private static RemoteFileInfo ParseMlsdLine(string line, string parentDir)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            int spaceIdx = line.IndexOf(' ');
            if (spaceIdx <= 0) return null;

            string factsPart = line.Substring(0, spaceIdx);
            string name = line.Substring(spaceIdx + 1).Trim();
            if (string.IsNullOrEmpty(name) || name == "." || name == ".." || name == ".ftpquota")
                return null;

            bool isDir = false;
            long size = 0;
            DateTime modifiedUtc = DateTime.MinValue;

            foreach (string fact in factsPart.Split(';'))
            {
                int eq = fact.IndexOf('=');
                if (eq <= 0) continue;
                string k = fact.Substring(0, eq).Trim().ToLowerInvariant();
                string v = fact.Substring(eq + 1).Trim();

                if (k == "type")
                {
                    if (v.Equals("cdir", StringComparison.OrdinalIgnoreCase) || v.Equals("pdir", StringComparison.OrdinalIgnoreCase))
                        return null;
                    isDir = v.Equals("dir", StringComparison.OrdinalIgnoreCase);
                }
                else if (k == "size")
                {
                    long.TryParse(v, out size);
                }
                else if (k == "modify")
                {
                    if (v.Length >= 14 && DateTime.TryParseExact(v.Substring(0, 14), "yyyyMMddHHmmss",
                        CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime dt))
                    {
                        modifiedUtc = dt;
                    }
                }
            }

            string fullRel = string.IsNullOrEmpty(parentDir) ? name : parentDir + "/" + name;
            return new RemoteFileInfo { Path = fullRel, Size = size, ModifiedUtc = modifiedUtc, IsDir = isDir };
        }

        private static RemoteFileInfo ParseUnixListLine(string line, string parentDir)
        {
            if (string.IsNullOrWhiteSpace(line) || line.Length < 10) return null;
            bool isDir = line[0] == 'd';
            string[] parts = line.Split(new[] { ' ' }, 9, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 9) return null;

            string name = parts[8].Trim();
            if (name == "." || name == ".." || name == ".ftpquota") return null;

            long.TryParse(parts[4], out long size);
            DateTime modifiedUtc = ParseUnixListDate(parts[5], parts[6], parts[7]);
            string fullRel = string.IsNullOrEmpty(parentDir) ? name : parentDir + "/" + name;
            return new RemoteFileInfo { Path = fullRel, Size = size, ModifiedUtc = modifiedUtc, IsDir = isDir };
        }

        private static DateTime ParseUnixListDate(string month, string day, string yearOrTime)
        {
            string combined = $"{month} {day} {yearOrTime}";
            if (yearOrTime.Contains(":"))
            {
                string withYear = $"{DateTime.UtcNow.Year} {month} {day} {yearOrTime}";
                if (DateTime.TryParseExact(withYear, new[] { "yyyy MMM d HH:mm", "yyyy MMM dd HH:mm" },
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime dt))
                {
                    if (dt > DateTime.Now.AddDays(2)) dt = dt.AddYears(-1);
                    return dt.ToUniversalTime();
                }
            }
            else
            {
                if (DateTime.TryParseExact(combined, new[] { "MMM d yyyy", "MMM dd yyyy" },
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime dt))
                {
                    return dt.ToUniversalTime();
                }
            }
            return DateTime.MinValue;
        }
    }
}
