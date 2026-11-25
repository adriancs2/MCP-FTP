using MCP_FTP.Core;
using MCP_FTP.Ftp;
using MCP_FTP.Tools;
using MCP_FTP.UI;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace MCP_FTP
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;

            if (args != null && args.Length > 0)
            {
                string cmd = args[0].ToLowerInvariant();
                if (cmd == "--ui" || cmd == "--create-profile" || cmd == "--edit-profile")
                {
                    return HandleProfileCli(cmd, args);
                }

                if (cmd == "--sync" && args.Length > 1)
                {
                    string profileName = args[1];
                    bool forceAll = args.Length > 2 && args[2].Equals("--force", StringComparison.OrdinalIgnoreCase);
                    bool verifyRemote = args.Length > 2 && args[2].Equals("--verify", StringComparison.OrdinalIgnoreCase);
                    var report = FtpSyncEngine.SyncProfile(profileName, forceAll, verifyRemote);
                    Console.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
                    return report.Errors.Count == 0 ? 0 : 1;
                }

                if (cmd == "--list-remote" && args.Length > 1)
                {
                    string profileName = args[1];
                    bool includeExcluded = args.Length > 2 && args[2].Equals("--all", StringComparison.OrdinalIgnoreCase);
                    var files = FtpSyncEngine.ListRemoteFiles(profileName, "", includeExcluded);
                    Console.WriteLine(JsonConvert.SerializeObject(files, Formatting.Indented));
                    return 0;
                }

                if (cmd == "--read-remote" && args.Length > 2)
                {
                    string profileName = args[1];
                    string remotePath = args[2];
                    int tailLines = (args.Length > 3 && int.TryParse(args[3], out int tl)) ? tl : 0;
                    var result = FtpSyncEngine.ReadRemoteTextFile(profileName, remotePath, tailLines);
                    Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented));
                    return 0;
                }

                if (cmd == "--download" && args.Length > 2)
                {
                    string profileName = args[1];
                    var files = new string[args.Length - 2];
                    Array.Copy(args, 2, files, 0, files.Length);
                    var report = FtpSyncEngine.DownloadRemoteFiles(profileName, files);
                    Console.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
                    return 0;
                }

                if (cmd == "--download-all" && args.Length > 1)
                {
                    string profileName = args[1];
                    string customLocalDir = args.Length > 2 ? args[2] : null;
                    var report = FtpSyncEngine.DownloadAllRemoteFiles(profileName, "", customLocalDir);
                    Console.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
                    return 0;
                }
            }

            // Default: MCP JSON-RPC stdio server
            var utf8 = new UTF8Encoding(false);
            try
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
                Console.SetIn(new StreamReader(Console.OpenStandardInput(), utf8));
            }
            catch { }

            var server = new McpServer();
            server.Run();
            return 0;
        }

        private static int HandleProfileCli(string cmd, string[] args)
        {
            var draft = new ProfileDraft();
            bool? showUiFlag = cmd == "--ui" ? (bool?)true : null;
            bool testConnection = true;
            bool explicitTest = false;
            bool overwrite = false;
            string positionalProfile = null;

            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                string lower = a.ToLowerInvariant();

                if (!a.StartsWith("-") && positionalProfile == null && i == 1)
                {
                    positionalProfile = a;
                    continue;
                }

                if (lower == "--show-ui" || lower == "--ui")
                {
                    if (i + 1 < args.Length && bool.TryParse(args[i + 1], out bool bUi))
                    {
                        showUiFlag = bUi;
                        i++;
                    }
                    else
                    {
                        showUiFlag = true;
                    }
                }
                else if (lower == "--headless" || lower == "--no-ui")
                {
                    showUiFlag = false;
                }
                else if (lower == "--no-test")
                {
                    testConnection = false;
                    explicitTest = true;
                }
                else if (lower == "--overwrite" || lower == "--force")
                {
                    overwrite = true;
                }
                else if (i + 1 < args.Length)
                {
                    string val = args[++i];
                    switch (lower)
                    {
                        case "--profile": draft.ProfileName = val; break;
                        case "--host": draft.Host = val; break;
                        case "--port": if (int.TryParse(val, out int p)) draft.Port = p; break;
                        case "--security": draft.SecurityMode = val; break;
                        case "--user":
                        case "--username": draft.Username = val; break;
                        case "--pass":
                        case "--password": draft.Password = val; break;
                        case "--local": draft.LocalFolder = val; break;
                        case "--remote": draft.RemoteFolder = val; break;
                        case "--url": draft.WebsiteUrl = val; break;
                        case "--excludes": draft.Excludes = val; break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(draft.ProfileName) && !string.IsNullOrWhiteSpace(positionalProfile))
                draft.ProfileName = positionalProfile;

            bool isEdit = cmd == "--edit-profile" || (cmd == "--ui" && !string.IsNullOrWhiteSpace(positionalProfile) && ProfileStore.Exists(positionalProfile));
            bool hasFullHeadless = !string.IsNullOrWhiteSpace(draft.ProfileName)
                && !string.IsNullOrWhiteSpace(draft.Host)
                && !string.IsNullOrWhiteSpace(draft.Username)
                && !string.IsNullOrEmpty(draft.Password);

            bool showUi = showUiFlag ?? (isEdit ? !draft.HasAnyEditField : !hasFullHeadless);

            if (showUi)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var form = new ProfileEditorForm(isEdit ? draft.ProfileName : null, draft))
                {
                    var res = form.ShowDialog();
                    return res == DialogResult.OK ? 0 : 1;
                }
            }

            try
            {
                object summary = isEdit
                    ? ProfileManager.EditHeadless(draft.ProfileName, draft, testConnection, explicitTest)
                    : ProfileManager.CreateHeadless(draft, overwrite, testConnection);
                Console.WriteLine(JsonConvert.SerializeObject(summary, Formatting.Indented));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }
}
