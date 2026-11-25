using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MCP_FTP.Ftp
{
    public static class ProfileStore
    {
        public static string ProfilesRoot
        {
            get
            {
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string SanitizeProfileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Profile name is required.");

            string trimmed = name.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                trimmed = trimmed.Replace(c, '_');

            if (trimmed.Contains("..") || trimmed.Contains("/") || trimmed.Contains("\\"))
                throw new ArgumentException("Invalid profile name.");

            return trimmed;
        }

        public static string GetProfileDir(string profileName)
        {
            return Path.Combine(ProfilesRoot, SanitizeProfileName(profileName));
        }

        public static string GetConfigPath(string profileName)
        {
            return Path.Combine(GetProfileDir(profileName), "ftp_config.json");
        }

        public static string GetExcludeConfigPath(string profileName)
        {
            return Path.Combine(GetProfileDir(profileName), "exclude_config.txt");
        }

        public static string GetDeleteFilePath(string profileName)
        {
            return Path.Combine(GetProfileDir(profileName), "delete-file.txt");
        }

        public static string GetSnapshotPath(string profileName)
        {
            return Path.Combine(GetProfileDir(profileName), "sync_snapshot.txt");
        }

        public static Dictionary<string, LocalFileSnap> LoadSnapshot(string profileName)
        {
            string path = GetSnapshotPath(profileName);
            if (!File.Exists(path))
                return null;

            var map = new Dictionary<string, LocalFileSnap>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#"))
                    continue;

                string[] parts = raw.Split('|');
                if (parts.Length < 3)
                    continue;

                string rel = parts[0].Trim().Replace('\\', '/').TrimStart('/');
                if (string.IsNullOrEmpty(rel))
                    continue;

                if (long.TryParse(parts[1], out long size) && long.TryParse(parts[2], out long ticks))
                {
                    map[rel] = new LocalFileSnap(size, ticks);
                }
            }
            return map;
        }

        public static void SaveSnapshot(string profileName, Dictionary<string, LocalFileSnap> snapshot)
        {
            if (snapshot == null)
                return;

            string dir = GetProfileDir(profileName);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string path = GetSnapshotPath(profileName);
            string tmpPath = path + ".tmp";

            var sb = new StringBuilder(snapshot.Count * 64 + 64);
            sb.AppendLine("# relative_path|size_bytes|last_write_utc_ticks");
            foreach (var kv in snapshot.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                sb.Append(kv.Key).Append('|').Append(kv.Value.Size).Append('|').Append(kv.Value.LastWriteUtcTicks).AppendLine();
            }

            File.WriteAllText(tmpPath, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Copy(tmpPath, path, overwrite: true);
                File.Delete(tmpPath);
            }
            else
            {
                File.Move(tmpPath, path);
            }
        }

        public static bool Exists(string profileName)
        {
            return File.Exists(GetConfigPath(profileName));
        }

        public static FtpProfileConfig Load(string profileName)
        {
            string path = GetConfigPath(profileName);
            if (!File.Exists(path))
                throw new FileNotFoundException($"Profile '{profileName}' not found.");

            string json = File.ReadAllText(path, Encoding.UTF8);
            var cfg = JsonConvert.DeserializeObject<FtpProfileConfig>(json);
            if (cfg == null)
                throw new InvalidDataException($"Profile '{profileName}' config is invalid.");

            if (string.IsNullOrWhiteSpace(cfg.ProfileName))
                cfg.ProfileName = profileName;

            return cfg;
        }

        public static void Save(FtpProfileConfig config, string excludeRulesText = null)
        {
            string safeName = SanitizeProfileName(config.ProfileName);
            config.ProfileName = safeName;

            string dir = GetProfileDir(safeName);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var utf8NoBom = new UTF8Encoding(false);
            string json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(GetConfigPath(safeName), json, utf8NoBom);

            string excludePath = GetExcludeConfigPath(safeName);
            if (excludeRulesText != null)
            {
                File.WriteAllText(excludePath, excludeRulesText, utf8NoBom);
            }
            else if (!File.Exists(excludePath))
            {
                File.WriteAllText(excludePath, "# One relative path or folder per line\n# Example:\n# App_Data/local.db\n# temp/\n", utf8NoBom);
            }
        }

        public static string ReadExcludeText(string profileName)
        {
            string path = GetExcludeConfigPath(profileName);
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
        }

        public static void WriteExcludeText(string profileName, string content)
        {
            if (!Exists(profileName))
                throw new FileNotFoundException($"Profile '{profileName}' not found.");

            File.WriteAllText(GetExcludeConfigPath(profileName), content ?? "", new UTF8Encoding(false));
        }

        public static List<PublicProfileSummary> ListPublicSummaries()
        {
            var list = new List<PublicProfileSummary>();
            if (!Directory.Exists(ProfilesRoot))
                return list;

            foreach (string dir in Directory.GetDirectories(ProfilesRoot).OrderBy(d => d))
            {
                string cfgPath = Path.Combine(dir, "ftp_config.json");
                if (!File.Exists(cfgPath)) continue;

                try
                {
                    var cfg = JsonConvert.DeserializeObject<FtpProfileConfig>(File.ReadAllText(cfgPath, Encoding.UTF8));
                    if (cfg == null) continue;
                    string name = string.IsNullOrWhiteSpace(cfg.ProfileName) ? Path.GetFileName(dir) : cfg.ProfileName;
                    list.Add(new PublicProfileSummary
                    {
                        ProfileName = name,
                        LocalFolder = cfg.LocalFolder,
                        RemoteFolder = cfg.RemoteFolder,
                        WebsiteUrl = cfg.WebsiteUrl,
                        SecurityMode = cfg.SecurityMode,
                        ExcludeConfigPath = GetExcludeConfigPath(name),
                        DeleteFilePath = GetDeleteFilePath(name),
                        SnapshotPath = GetSnapshotPath(name)
                    });
                }
                catch { }
            }

            return list;
        }
    }
}
