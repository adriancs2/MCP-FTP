using MCP_FTP.Core;
using MCP_FTP.Ftp;
using MCP_FTP.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Threading;
using System.Windows.Forms;

namespace MCP_FTP.Tools
{
    internal static class UiRunner
    {
        public static (DialogResult Result, string SavedProfile) ShowProfileDialog(string existingProfile = null)
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
                    using (var form = new ProfileEditorForm(existingProfile))
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

    public class ListProfilesTool : ITool
    {
        public string Name => "ftp_list_profiles";
        public string Description => "List all configured FTP profiles (local folder, remote folder, website URL, and paths to exclude_config.txt / delete-file.txt). Credentials are never exposed.";
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
        public string Description => "Open an empty WinForms dialog for the user to enter FTP credentials and save a new profile securely (DPAPI-encrypted). No parameters required.";
        public ToolParamList Params => new ToolParamList();

        public ToolResult Execute(JObject args)
        {
            var (result, savedName) = UiRunner.ShowProfileDialog(null);
            if (result == DialogResult.OK && !string.IsNullOrEmpty(savedName))
            {
                return ToolResult.Success(JsonConvert.SerializeObject(new
                {
                    status = "saved",
                    profile = savedName,
                    exclude_config_path = ProfileStore.GetExcludeConfigPath(savedName),
                    delete_file_path = ProfileStore.GetDeleteFilePath(savedName)
                }, Formatting.Indented));
            }
            return ToolResult.Success(JsonConvert.SerializeObject(new { status = "cancelled" }));
        }
    }

    public class EditProfileTool : ITool
    {
        public string Name => "ftp_edit_profile";
        public string Description => "Open the WinForms dialog to edit an existing FTP profile's credentials or settings.";
        public ToolParamList Params => new ToolParamList()
            .String("profile", "Name of the existing profile to edit.", required: true);

        public ToolResult Execute(JObject args)
        {
            string profile = args.Value<string>("profile");
            if (string.IsNullOrWhiteSpace(profile))
                throw new ArgumentException("profile is required.");
            if (!ProfileStore.Exists(profile))
                return ToolResult.Error("NOT_FOUND", $"Profile '{profile}' does not exist.");

            var (result, savedName) = UiRunner.ShowProfileDialog(profile);
            if (result == DialogResult.OK)
                return ToolResult.Success(JsonConvert.SerializeObject(new { status = "saved", profile = savedName }));

            return ToolResult.Success(JsonConvert.SerializeObject(new { status = "cancelled", profile }));
        }
    }
}
