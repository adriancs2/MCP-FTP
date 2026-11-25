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
        public string Description => "Recursively list files on the remote FTP server for a profile, including file sizes and UTC modified timestamps.";
        public ToolParamList Params => new ToolParamList()
            .String("profile", "Profile name.", required: true)
            .String("path", "Optional relative subfolder on the FTP server.", required: false, defaultValue: "");

        public ToolResult Execute(JObject args)
        {
            string profile = args.Value<string>("profile");
            string path = args.Value<string>("path") ?? "";
            var files = FtpSyncEngine.ListRemoteFiles(profile, path);
            return ToolResult.Success(JsonConvert.SerializeObject(files, Formatting.Indented));
        }
    }

    public class SyncTool : ITool
    {
        public string Name => "ftp_sync";
        public string Description => "Recursively sync local folder to FTP using datetime+size matching (MLSD), respecting profiles/{profile}/exclude_config.txt. Also consumes and immediately deletes profiles/{profile}/delete-file.txt if present.";
        public ToolParamList Params => new ToolParamList()
            .String("profile", "Profile name.", required: true)
            .Bool("force_all", "If true, upload all non-excluded files regardless of remote timestamp.", required: false, defaultValue: false);

        public ToolResult Execute(JObject args)
        {
            string profile = args.Value<string>("profile");
            bool forceAll = args.Value<bool?>("force_all") ?? false;
            var report = FtpSyncEngine.SyncProfile(profile, forceAll);
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
}
