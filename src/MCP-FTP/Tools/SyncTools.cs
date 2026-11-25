using MCP_FTP.Core;
using MCP_FTP.Ftp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MCP_FTP.Tools
{
    public class ListRemoteFilesTool : ITool
    {
        public string Name => "ftp_list_remote_files";
        public string Description => "Recursively list files on the remote FTP server for a profile, including file sizes and UTC modified timestamps. Respects exclude_config.txt by default so large excluded folders (like uploads/ or vendor/) are skipped unless include_excluded is true.";
        public ToolParamList Params => new ToolParamList()
            .String("profile", "Profile name.", required: true)
            .String("path", "Optional relative subfolder on the FTP server.", required: false, defaultValue: "")
            .Bool("include_excluded", "If true, also scan and return files inside directories matched by exclude_config.txt.", required: false, defaultValue: false);

        public ToolResult Execute(JObject args)
        {
            string profile = args.Value<string>("profile");
            string path = args.Value<string>("path") ?? "";
            bool includeExcluded = args.Value<bool?>("include_excluded") ?? false;
            var files = FtpSyncEngine.ListRemoteFiles(profile, path, includeExcluded);
            return ToolResult.Success(JsonConvert.SerializeObject(files, Formatting.Indented));
        }
    }

    public class SyncTool : ITool
    {
        public string Name => "ftp_sync";
        public string Description => "Sync local folder to FTP using fast local snapshot tracking (profiles/{profile}/sync_snapshot.txt) with zero remote scanning after the initial sync. Respects exclude_config.txt and consumes delete-file.txt if present. Set verify_remote=true to reconcile against the live FTP server via MLSD.";
        public ToolParamList Params => new ToolParamList()
            .String("profile", "Profile name.", required: true)
            .Bool("force_all", "If true, upload all non-excluded local files regardless of snapshot or remote timestamp.", required: false, defaultValue: false)
            .Bool("verify_remote", "If true, scan the remote FTP server via MLSD instead of trusting the local snapshot (useful if files were modified directly on the server).", required: false, defaultValue: false);

        public ToolResult Execute(JObject args)
        {
            string profile = args.Value<string>("profile");
            bool forceAll = args.Value<bool?>("force_all") ?? false;
            bool verifyRemote = args.Value<bool?>("verify_remote") ?? false;
            var report = FtpSyncEngine.SyncProfile(profile, forceAll, verifyRemote);
            return ToolResult.Success(JsonConvert.SerializeObject(report, Formatting.Indented));
        }
    }

    public class GetExcludesTool : ITool
    {
        public string Name => "ftp_get_excludes";
        public string Description => "Read profiles/{profile}/exclude_config.txt for a profile.";
        public ToolParamList Params => new ToolParamList()
            .String("profile", "Profile name.", required: true);

        public ToolResult Execute(JObject args)
        {
            string profile = args.Value<string>("profile");
            string content = ProfileStore.ReadExcludeText(profile);
            return ToolResult.Success(content);
        }
    }

    public class SetExcludesTool : ITool
    {
        public string Name => "ftp_set_excludes";
        public string Description => "Write profiles/{profile}/exclude_config.txt (one relative file or folder path per line, # for comments).";
        public ToolParamList Params => new ToolParamList()
            .String("profile", "Profile name.", required: true)
            .String("content", "Full text content for exclude_config.txt.", required: true);

        public ToolResult Execute(JObject args)
        {
            string profile = args.Value<string>("profile");
            string content = args.Value<string>("content") ?? "";
            ProfileStore.WriteExcludeText(profile, content);
            return ToolResult.Success($"Updated {ProfileStore.GetExcludeConfigPath(profile)}");
        }
    }

    public class DeleteRemoteFilesTool : ITool
    {
        public string Name => "ftp_delete_remote_files";
        public string Description => "Explicitly delete one or more remote files from the FTP server.";
        public ToolParamList Params => new ToolParamList()
            .String("profile", "Profile name.", required: true)
            .Array("files", "Relative remote file paths to delete.", required: true, itemsType: "string");

        public ToolResult Execute(JObject args)
        {
            string profile = args.Value<string>("profile");
            var arr = args["files"] as JArray;
            if (arr == null || arr.Count == 0)
                throw new ArgumentException("files array is required and must not be empty.");

            List<string> files = arr.Select(t => t.Value<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            var report = FtpSyncEngine.DeleteRemoteFiles(profile, files);
            return ToolResult.Success(JsonConvert.SerializeObject(report, Formatting.Indented));
        }
    }

    public class ReadRemoteFileTool : ITool
    {
        public string Name => "ftp_read_remote_file";
        public string Description => "Read a UTF-8 text file directly from the remote FTP server into memory without saving it locally (ideal for inspecting PHP error_log, .htaccess, or remote config files). Supports tail_lines and max_bytes.";
        public ToolParamList Params => new ToolParamList()
            .String("profile", "Profile name.", required: true)
            .String("path", "Relative remote file path (e.g. 'error_log' or 'logs/app.log').", required: true)
            .Int("tail_lines", "Optional. If greater than 0, return only the last N lines of the file (useful for PHP error_log).", required: false, defaultValue: 0)
            .Int("max_bytes", "Maximum bytes to return (default: 262144 = 256 KB).", required: false, defaultValue: 262144);

        public ToolResult Execute(JObject args)
        {
            string profile = args.Value<string>("profile");
            string path = args.Value<string>("path");
            int tailLines = args.Value<int?>("tail_lines") ?? 0;
            int maxBytes = args.Value<int?>("max_bytes") ?? 262144;

            var result = FtpSyncEngine.ReadRemoteTextFile(profile, path, tailLines, maxBytes);
            return ToolResult.Success(JsonConvert.SerializeObject(result, Formatting.Indented));
        }
    }

    public class DownloadRemoteFilesTool : ITool
    {
        public string Name => "ftp_download";
        public string Description => "Download one or more files from the remote FTP server to the profile's local_folder (or an optional custom local_dir), preserving relative subfolder structure.";
        public ToolParamList Params => new ToolParamList()
            .String("profile", "Profile name.", required: true)
            .Array("files", "Relative remote file paths to download (e.g. ['index.php', 'error_log']).", required: true, itemsType: "string")
            .String("local_dir", "Optional local destination root directory. Defaults to the profile's configured local_folder.", required: false)
            .Bool("overwrite", "If true (default), overwrite existing local files.", required: false, defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            string profile = args.Value<string>("profile");
            var arr = args["files"] as JArray;
            if (arr == null || arr.Count == 0)
                throw new ArgumentException("files array is required and must not be empty.");

            string localDir = args.Value<string>("local_dir");
            bool overwrite = args.Value<bool?>("overwrite") ?? true;
            List<string> files = arr.Select(t => t.Value<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

            var report = FtpSyncEngine.DownloadRemoteFiles(profile, files, localDir, overwrite);
            return ToolResult.Success(JsonConvert.SerializeObject(report, Formatting.Indented));
        }
    }

    public class DownloadAllRemoteFilesTool : ITool
    {
        public string Name => "ftp_download_all";
        public string Description => "Recursively download the full source code (or a subfolder) from the FTP server to local_folder while automatically skipping runtime user uploads and paths listed in exclude_config.txt. Updates sync_snapshot.txt so subsequent ftp_sync calls on the new machine are instant.";
        public ToolParamList Params => new ToolParamList()
            .String("profile", "Profile name.", required: true)
            .String("path", "Optional remote subfolder to download (default empty = entire workspace).", required: false, defaultValue: "")
            .String("local_dir", "Optional custom local destination root. Defaults to the profile's configured local_folder.", required: false)
            .Bool("overwrite", "If true (default), overwrite existing local files. If false, only download missing files.", required: false, defaultValue: true)
            .Bool("include_excluded", "If true, also download files matched by exclude_config.txt. Keep false (default) to skip runtime user-uploaded media/databases.", required: false, defaultValue: false);

        public ToolResult Execute(JObject args)
        {
            string profile = args.Value<string>("profile");
            string path = args.Value<string>("path") ?? "";
            string localDir = args.Value<string>("local_dir");
            bool overwrite = args.Value<bool?>("overwrite") ?? true;
            bool includeExcluded = args.Value<bool?>("include_excluded") ?? false;

            var report = FtpSyncEngine.DownloadAllRemoteFiles(profile, path, localDir, overwrite, includeExcluded);
            return ToolResult.Success(JsonConvert.SerializeObject(report, Formatting.Indented));
        }
    }
}
