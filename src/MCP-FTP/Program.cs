using MCP_FTP.Core;
using MCP_FTP.Ftp;
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
                if (cmd == "--ui" || cmd == "--create-profile")
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    string editProfile = args.Length > 1 ? args[1] : null;
                    using (var form = new ProfileEditorForm(editProfile))
                    {
                        form.ShowDialog();
                    }
                    return 0;
                }

                if (cmd == "--sync" && args.Length > 1)
                {
                    string profileName = args[1];
                    bool forceAll = args.Length > 2 && args[2].Equals("--force", StringComparison.OrdinalIgnoreCase);
                    var report = FtpSyncEngine.SyncProfile(profileName, forceAll);
                    Console.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
                    return report.Errors.Count == 0 ? 0 : 1;
                }

                if (cmd == "--list-remote" && args.Length > 1)
                {
                    string profileName = args[1];
                    var files = FtpSyncEngine.ListRemoteFiles(profileName);
                    Console.WriteLine(JsonConvert.SerializeObject(files, Formatting.Indented));
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
    }
}
