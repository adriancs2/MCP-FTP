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
                        DeleteFilePath = GetDeleteFilePath(name)
                    });
                }
                catch { }
            }

            return list;
        }
    }
}
