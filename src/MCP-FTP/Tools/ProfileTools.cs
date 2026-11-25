using MCP_FTP.Core;
using MCP_FTP.Ftp;
using MCP_FTP.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace MCP_FTP.Tools
{
    internal static class UiRunner
    {
        public static (DialogResult Result, string SavedProfile) ShowProfileDialog(string existingProfile = null, ProfileDraft prefill = null)
        {
            DialogResult result = DialogResult.Cancel;
            string savedName = null;
            Exception threadEx = null;

            var t = new Thread(() =>
            {
                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (var form = new ProfileEditorForm(existingProfile, prefill))
                    {
                        result = form.ShowDialog();
                        savedName = form.SavedProfileName;
                    }
                }
                catch (Exception ex)
                {
                    threadEx = ex;
                }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();

            if (threadEx != null) throw threadEx;
            return (result, savedName);
        }
    }

    public static class ProfileManager
    {
        public static ProfileDraft ParseDraft(JObject args)
        {
            if (args == null) return new ProfileDraft();
            return new ProfileDraft
            {
                ProfileName = args["profile"]?.Value<string>(),
                Host = args["host"]?.Value<string>(),
                Port = args["port"]?.Value<int?>(),
                SecurityMode = args["security_mode"]?.Value<string>(),
                Username = args["username"]?.Value<string>(),
                Password = args["password"]?.Value<string>(),
                LocalFolder = args["local_folder"]?.Value<string>(),
                RemoteFolder = args["remote_folder"]?.Value<string>(),
                WebsiteUrl = args["website_url"]?.Value<string>(),
                Excludes = args["excludes"]?.Value<string>()
            };
        }

        public static object CreateHeadless(ProfileDraft draft, bool overwrite = false, bool testConnection = true)
        {
            if (draft == null)
                throw new ArgumentException("Profile configuration is required.");
            if (string.IsNullOrWhiteSpace(draft.ProfileName))
                throw new ArgumentException("'profile' is required when show_ui is false.");
            if (string.IsNullOrWhiteSpace(draft.Host))
                throw new ArgumentException("'host' is required when show_ui is false.");
            if (string.IsNullOrWhiteSpace(draft.Username))
                throw new ArgumentException("'username' is required when show_ui is false.");
            if (string.IsNullOrEmpty(draft.Password))
                throw new ArgumentException("'password' is required when show_ui is false (or set show_ui=true to enter the password in the UI dialog).");

            string safeName = ProfileStore.SanitizeProfileName(draft.ProfileName);
            if (!overwrite && ProfileStore.Exists(safeName))
                throw new InvalidOperationException($"Profile '{safeName}' already exists. Use ftp_edit_profile to update it, or pass overwrite=true.");

            var cfg = new FtpProfileConfig
            {
                ProfileName = safeName,
                Host = draft.Host.Trim(),
                Port = draft.Port.HasValue && draft.Port.Value > 0 ? draft.Port.Value : 21,
                SecurityMode = FtpProfileConfig.NormalizeSecurityMode(draft.SecurityMode),
                Username = draft.Username.Trim(),
                LocalFolder = (draft.LocalFolder ?? "").Trim(),
                RemoteFolder = string.IsNullOrWhiteSpace(draft.RemoteFolder) ? "/" : draft.RemoteFolder.Trim(),
                WebsiteUrl = (draft.WebsiteUrl ?? "").Trim()
            };
            cfg.SetPlainPassword(draft.Password);

            string effectiveSecurity = null;
            if (testConnection)
            {
                var client = new FtpClientEx(cfg);
                client.ProbeConnection();
                effectiveSecurity = client.EffectiveSecurityMode;
            }

            ProfileStore.Save(cfg, draft.Excludes);

            // Clear any stale snapshot if overwriting an existing profile
            string snapPath = ProfileStore.GetSnapshotPath(safeName);
            if (overwrite && File.Exists(snapPath))
                File.Delete(snapPath);

            return BuildSavedSummary(safeName, "headless", effectiveSecurity);
        }

        public static object EditHeadless(string profileName, ProfileDraft draft, bool testConnection = true, bool explicitTestFlag = false)
        {
            if (string.IsNullOrWhiteSpace(profileName))
                throw new ArgumentException("'profile' is required.");

            string safeName = ProfileStore.SanitizeProfileName(profileName);
            if (!ProfileStore.Exists(safeName))
                throw new FileNotFoundException($"Profile '{safeName}' does not exist.");

            var cfg = ProfileStore.Load(safeName);
            string prevLocal = cfg.LocalFolder ?? "";
            string prevRemote = cfg.RemoteFolder ?? "/";
            bool connChanged = false;

            if (draft != null)
            {
                if (!string.IsNullOrWhiteSpace(draft.Host))
                {
                    string h = draft.Host.Trim();
                    if (!string.Equals(cfg.Host, h, StringComparison.OrdinalIgnoreCase)) connChanged = true;
                    cfg.Host = h;
                }
                if (draft.Port.HasValue && draft.Port.Value > 0)
                {
                    if (cfg.Port != draft.Port.Value) connChanged = true;
                    cfg.Port = draft.Port.Value;
                }
                if (!string.IsNullOrWhiteSpace(draft.SecurityMode))
                {
                    string sec = FtpProfileConfig.NormalizeSecurityMode(draft.SecurityMode);
                    if (!string.Equals(cfg.SecurityMode, sec, StringComparison.OrdinalIgnoreCase)) connChanged = true;
                    cfg.SecurityMode = sec;
                }
                if (!string.IsNullOrWhiteSpace(draft.Username))
                {
                    string u = draft.Username.Trim();
                    if (!string.Equals(cfg.Username, u, StringComparison.Ordinal)) connChanged = true;
                    cfg.Username = u;
                }
                if (!string.IsNullOrEmpty(draft.Password))
                {
                    cfg.SetPlainPassword(draft.Password);
                    connChanged = true;
                }
                if (draft.LocalFolder != null)
                {
                    cfg.LocalFolder = draft.LocalFolder.Trim();
                }
                if (!string.IsNullOrWhiteSpace(draft.RemoteFolder))
                {
                    string r = draft.RemoteFolder.Trim();
                    if (!string.Equals(cfg.RemoteFolder, r, StringComparison.Ordinal)) connChanged = true;
                    cfg.RemoteFolder = r;
                }
                if (draft.WebsiteUrl != null)
                {
                    cfg.WebsiteUrl = draft.WebsiteUrl.Trim();
                }
            }

            string effectiveSecurity = null;
            if (testConnection && (connChanged || explicitTestFlag))
            {
                var client = new FtpClientEx(cfg);
                client.ProbeConnection();
                effectiveSecurity = client.EffectiveSecurityMode;
            }

            ProfileStore.Save(cfg, draft?.Excludes);

            // Reset local snapshot if local_folder or remote_folder changed
            bool folderChanged = !string.Equals(prevLocal, cfg.LocalFolder ?? "", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(prevRemote, cfg.RemoteFolder ?? "/", StringComparison.Ordinal);
            if (folderChanged)
            {
                string snapPath = ProfileStore.GetSnapshotPath(safeName);
                if (File.Exists(snapPath))
                    File.Delete(snapPath);
            }

            return BuildSavedSummary(safeName, "headless", effectiveSecurity);
        }

        public static object BuildSavedSummary(string profileName, string mode, string verifiedSecurity = null)
        {
            var cfg = ProfileStore.Load(profileName);
            return new
            {
                status = "saved",
                mode,
                profile = cfg.ProfileName,
                security_mode = cfg.SecurityMode,
                verified_security = verifiedSecurity,
                local_folder = cfg.LocalFolder,
                local_folder_exists = !string.IsNullOrWhiteSpace(cfg.LocalFolder) && Directory.Exists(cfg.LocalFolder),
                remote_folder = cfg.RemoteFolder,
                website_url = cfg.WebsiteUrl,
                exclude_config_path = ProfileStore.GetExcludeConfigPath(cfg.ProfileName),
                delete_file_path = ProfileStore.GetDeleteFilePath(cfg.ProfileName),
                snapshot_path = ProfileStore.GetSnapshotPath(cfg.ProfileName)
            };
        }
    }

    public class ListProfilesTool : ITool
    {
        public string Name => "ftp_list_profiles";
        public string Description => "List all configured FTP profiles (local folder, remote folder, website URL, and paths to exclude_config.txt / delete-file.txt / sync_snapshot.txt). Credentials are never exposed.";
        public ToolParamList Params => new ToolParamList();

        public ToolResult Execute(JObject args)
        {
            var list = ProfileStore.ListPublicSummaries();
            return ToolResult.Success(JsonConvert.SerializeObject(list, Formatting.Indented));
        }
    }

    public class CreateProfileTool : ITool
    {
        public string Name => "ftp_create_profile";
        public string Description => "Create a new FTP profile (DPAPI-encrypted on disk). Supports both UI mode (show_ui=true: opens WinForms dialog, optionally pre-filled with provided fields) and headless CLI/API mode (show_ui=false: validates connection, encrypts password via Windows DPAPI, and saves directly without opening a dialog).";
        public ToolParamList Params => new ToolParamList()
            .Bool("show_ui", "If true, open the WinForms dialog (pre-filled with any supplied parameters). If false, save headlessly without any UI popup. If omitted, defaults to false when profile, host, username, and password are all provided, otherwise true.", required: false)
            .String("profile", "Profile name (e.g. 'myshop'). Required when show_ui=false.", required: false)
            .String("host", "FTP host (e.g. 'ftp.example.com'). Required when show_ui=false.", required: false)
            .Int("port", "FTP port (default: 21).", required: false, defaultValue: 21)
            .StringEnum("security_mode", "FTP security mode: 'Auto' (tries ExplicitFTPS then PlainFTP), 'ExplicitFTPS', or 'PlainFTP'.", new[] { "Auto", "ExplicitFTPS", "PlainFTP" }, required: false, defaultValue: "Auto")
            .String("username", "FTP username. Required when show_ui=false.", required: false)
            .String("password", "FTP password (encrypted immediately with Windows DPAPI and never echoed back). Required when show_ui=false; omit when show_ui=true if the user prefers typing it in the UI.", required: false)
            .String("local_folder", "Local workspace directory path (e.g. 'C:\\www\\myshop').", required: false)
            .String("remote_folder", "Remote base folder on the FTP server (default: '/').", required: false, defaultValue: "/")
            .String("website_url", "Public HTTP/HTTPS URL of the website (e.g. 'https://myshop.com').", required: false)
            .String("excludes", "Optional multi-line exclude rules for exclude_config.txt (one relative path or folder per line).", required: false)
            .Bool("test_connection", "When show_ui=false, probe the FTP connection before saving to verify credentials (default: true).", required: false, defaultValue: true)
            .Bool("overwrite", "When show_ui=false, allow overwriting an existing profile of the same name (default: false).", required: false, defaultValue: false);

        public ToolResult Execute(JObject args)
        {
            var draft = ProfileManager.ParseDraft(args);
            bool hasFullHeadlessCreds = !string.IsNullOrWhiteSpace(draft.ProfileName)
                && !string.IsNullOrWhiteSpace(draft.Host)
                && !string.IsNullOrWhiteSpace(draft.Username)
                && !string.IsNullOrEmpty(draft.Password);

            bool showUi = args?["show_ui"] != null
                ? args.Value<bool>("show_ui")
                : !hasFullHeadlessCreds;

            if (showUi)
            {
                var (result, savedName) = UiRunner.ShowProfileDialog(null, draft);
                if (result == DialogResult.OK && !string.IsNullOrEmpty(savedName))
                {
                    var summary = ProfileManager.BuildSavedSummary(savedName, "ui");
                    return ToolResult.Success(JsonConvert.SerializeObject(summary, Formatting.Indented));
                }
                return ToolResult.Success(JsonConvert.SerializeObject(new { status = "cancelled", mode = "ui" }, Formatting.Indented));
            }

            bool testConn = args?["test_connection"]?.Value<bool?>() ?? true;
            bool overwrite = args?["overwrite"]?.Value<bool?>() ?? false;
            var saved = ProfileManager.CreateHeadless(draft, overwrite, testConn);
            return ToolResult.Success(JsonConvert.SerializeObject(saved, Formatting.Indented));
        }
    }

    public class EditProfileTool : ITool
    {
        public string Name => "ftp_edit_profile";
        public string Description => "Edit an existing FTP profile's settings or credentials. Supports both UI mode (show_ui=true: opens WinForms dialog loaded with current profile plus any supplied overrides) and headless mode (show_ui=false: updates only the supplied fields while preserving existing DPAPI-encrypted credentials if password is omitted).";
        public ToolParamList Params => new ToolParamList()
            .String("profile", "Name of the existing profile to edit.", required: true)
            .Bool("show_ui", "If true, open the WinForms dialog. If false, apply updates headlessly without UI. If omitted, defaults to false when at least one setting override is provided, otherwise true.", required: false)
            .String("host", "New FTP host (omit to keep current).", required: false)
            .Int("port", "New FTP port (omit to keep current).", required: false)
            .StringEnum("security_mode", "New security mode: 'Auto', 'ExplicitFTPS', or 'PlainFTP' (omit to keep current).", new[] { "Auto", "ExplicitFTPS", "PlainFTP" }, required: false)
            .String("username", "New FTP username (omit to keep current).", required: false)
            .String("password", "New FTP password to encrypt via DPAPI (omit to keep current encrypted password).", required: false)
            .String("local_folder", "New local workspace folder path (omit to keep current).", required: false)
            .String("remote_folder", "New remote base folder path (omit to keep current).", required: false)
            .String("website_url", "New public website URL (omit to keep current).", required: false)
            .String("excludes", "New full text content for exclude_config.txt (omit to keep current).", required: false)
            .Bool("test_connection", "When show_ui=false and connection/login settings changed, test FTP connection before saving (default: true).", required: false, defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            string profile = args?.Value<string>("profile");
            if (string.IsNullOrWhiteSpace(profile))
                throw new ArgumentException("profile is required.");

            string safeName = ProfileStore.SanitizeProfileName(profile);
            if (!ProfileStore.Exists(safeName))
                return ToolResult.Error("NOT_FOUND", $"Profile '{safeName}' does not exist.");

            var draft = ProfileManager.ParseDraft(args);
            bool showUi = args?["show_ui"] != null
                ? args.Value<bool>("show_ui")
                : !draft.HasAnyEditField;

            if (showUi)
            {
                var (result, savedName) = UiRunner.ShowProfileDialog(safeName, draft);
                if (result == DialogResult.OK && !string.IsNullOrEmpty(savedName))
                {
                    var summary = ProfileManager.BuildSavedSummary(savedName, "ui");
                    return ToolResult.Success(JsonConvert.SerializeObject(summary, Formatting.Indented));
                }
                return ToolResult.Success(JsonConvert.SerializeObject(new { status = "cancelled", mode = "ui", profile = safeName }, Formatting.Indented));
            }

            bool explicitTest = args?["test_connection"] != null;
            bool testConn = args?["test_connection"]?.Value<bool?>() ?? true;
            var updated = ProfileManager.EditHeadless(safeName, draft, testConn, explicitTest);
            return ToolResult.Success(JsonConvert.SerializeObject(updated, Formatting.Indented));
        }
    }
}
