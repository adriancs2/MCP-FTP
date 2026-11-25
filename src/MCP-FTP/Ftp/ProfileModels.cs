using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace MCP_FTP.Ftp
{
    public class FtpProfileConfig
    {
        [JsonProperty("profile_name")]
        public string ProfileName { get; set; } = "";

        [JsonProperty("host")]
        public string Host { get; set; } = "";

        [JsonProperty("port")]
        public int Port { get; set; } = 21;

        [JsonProperty("username")]
        public string Username { get; set; } = "";

        /// <summary>DPAPI-encrypted Base64 password (CurrentUser scope). Never exposed to AI.</summary>
        [JsonProperty("password_protected")]
        public string PasswordProtected { get; set; } = "";

        /// <summary>Auto | ExplicitFTPS | PlainFTP</summary>
        [JsonProperty("security_mode")]
        public string SecurityMode { get; set; } = "Auto";

        [JsonProperty("local_folder")]
        public string LocalFolder { get; set; } = "";

        [JsonProperty("remote_folder")]
        public string RemoteFolder { get; set; } = "/";

        [JsonProperty("website_url")]
        public string WebsiteUrl { get; set; } = "";

        public void SetPlainPassword(string plainPassword)
        {
            if (string.IsNullOrEmpty(plainPassword))
            {
                PasswordProtected = "";
                return;
            }
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainPassword);
            byte[] protectedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            PasswordProtected = Convert.ToBase64String(protectedBytes);
        }

        public string GetPlainPassword()
        {
            if (string.IsNullOrEmpty(PasswordProtected))
                return "";
            byte[] protectedBytes = Convert.FromBase64String(PasswordProtected);
            byte[] plainBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }

        public static string NormalizeSecurityMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
                return "Auto";

            string clean = mode.Trim().Replace(" ", "").Replace("-", "").Replace("_", "");
            if (clean.Equals("ExplicitFTPS", StringComparison.OrdinalIgnoreCase)
                || clean.Equals("FTPS", StringComparison.OrdinalIgnoreCase)
                || clean.Equals("TLS", StringComparison.OrdinalIgnoreCase)
                || clean.Equals("SSL", StringComparison.OrdinalIgnoreCase))
            {
                return "ExplicitFTPS";
            }

            if (clean.Equals("PlainFTP", StringComparison.OrdinalIgnoreCase)
                || clean.Equals("Plain", StringComparison.OrdinalIgnoreCase)
                || clean.Equals("FTP", StringComparison.OrdinalIgnoreCase))
            {
                return "PlainFTP";
            }

            return "Auto";
        }
    }

    public class ProfileDraft
    {
        public string ProfileName { get; set; }
        public string Host { get; set; }
        public int? Port { get; set; }
        public string SecurityMode { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string LocalFolder { get; set; }
        public string RemoteFolder { get; set; }
        public string WebsiteUrl { get; set; }
        public string Excludes { get; set; }

        public bool HasAnyEditField =>
            Host != null
            || Port.HasValue
            || SecurityMode != null
            || Username != null
            || Password != null
            || LocalFolder != null
            || RemoteFolder != null
            || WebsiteUrl != null
            || Excludes != null;
    }

    public class PublicProfileSummary
    {
        [JsonProperty("profile_name")]
        public string ProfileName { get; set; }

        [JsonProperty("local_folder")]
        public string LocalFolder { get; set; }

        [JsonProperty("remote_folder")]
        public string RemoteFolder { get; set; }

        [JsonProperty("website_url")]
        public string WebsiteUrl { get; set; }

        [JsonProperty("security_mode")]
        public string SecurityMode { get; set; }

        [JsonProperty("exclude_config_path")]
        public string ExcludeConfigPath { get; set; }

        [JsonProperty("delete_file_path")]
        public string DeleteFilePath { get; set; }

        [JsonProperty("snapshot_path")]
        public string SnapshotPath { get; set; }
    }

    public struct LocalFileSnap
    {
        public long Size;
        public long LastWriteUtcTicks;

        public LocalFileSnap(long size, long lastWriteUtcTicks)
        {
            Size = size;
            LastWriteUtcTicks = lastWriteUtcTicks;
        }
    }

    public class RemoteFileInfo
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("modified_utc")]
        public DateTime ModifiedUtc { get; set; }

        [JsonProperty("is_dir")]
        public bool IsDir { get; set; }
    }

    public class SyncReport
    {
        [JsonProperty("profile")]
        public string Profile { get; set; }

        [JsonProperty("sync_mode")]
        public string SyncMode { get; set; }

        [JsonProperty("security_used")]
        public string SecurityUsed { get; set; }

        [JsonProperty("uploaded")]
        public List<string> Uploaded { get; set; } = new List<string>();

        [JsonProperty("skipped_unchanged")]
        public int SkippedUnchanged { get; set; }

        [JsonProperty("skipped_excluded")]
        public int SkippedExcluded { get; set; }

        [JsonProperty("skipped_remote_excluded_dirs")]
        public int SkippedRemoteExcludedDirs { get; set; }

        [JsonProperty("deleted")]
        public List<string> Deleted { get; set; } = new List<string>();

        [JsonProperty("delete_file_consumed")]
        public bool DeleteFileConsumed { get; set; }

        [JsonProperty("errors")]
        public List<string> Errors { get; set; } = new List<string>();

        [JsonProperty("duration_ms")]
        public long DurationMs { get; set; }
    }
}
