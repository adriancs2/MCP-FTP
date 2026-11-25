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

        [JsonProperty("security_used")]
        public string SecurityUsed { get; set; }

        [JsonProperty("uploaded")]
        public List<string> Uploaded { get; set; } = new List<string>();

        [JsonProperty("skipped_unchanged")]
        public int SkippedUnchanged { get; set; }

        [JsonProperty("skipped_excluded")]
        public int SkippedExcluded { get; set; }

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
