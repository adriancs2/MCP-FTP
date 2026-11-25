using MCP_FTP.Ftp;
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MCP_FTP.UI
{
    public class ProfileEditorForm : Form
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private TextBox _txtProfile;
        private TextBox _txtHost;
        private NumericUpDown _numPort;
        private TextBox _txtUser;
        private TextBox _txtPass;
        private ComboBox _cboSecurity;
        private TextBox _txtLocal;
        private TextBox _txtRemote;
        private TextBox _txtWebsite;
        private TextBox _txtExcludes;
        private Label _lblStatus;

        public string SavedProfileName { get; private set; }

        public ProfileEditorForm(string existingProfileName = null)
        {
            Text = string.IsNullOrWhiteSpace(existingProfileName) ? "MCP-FTP - Create Profile" : $"MCP-FTP - Edit Profile ({existingProfileName})";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            ClientSize = new Size(520, 520);
            Font = new Font("Segoe UI", 9.5f);

            BuildLayout();

            if (!string.IsNullOrWhiteSpace(existingProfileName) && ProfileStore.Exists(existingProfileName))
            {
                LoadProfile(existingProfileName);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            TopMost = true;
            BringToFront();
            Activate();
            SetForegroundWindow(Handle);
        }

        private void BuildLayout()
        {
            int y = 14;
            int labelW = 120;
            int inputX = 140;
            int inputW = 355;

            _txtProfile = AddRow("Profile Name:", ref y, labelW, inputX, inputW);
            _txtHost = AddRow("FTP Host:", ref y, labelW, inputX, inputW);

            Controls.Add(new Label { Text = "Port / Security:", Location = new Point(16, y + 3), Size = new Size(labelW, 22) });
            _numPort = new NumericUpDown { Location = new Point(inputX, y), Size = new Size(80, 24), Minimum = 1, Maximum = 65535, Value = 21 };
            _cboSecurity = new ComboBox { Location = new Point(inputX + 90, y), Size = new Size(inputW - 90, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            _cboSecurity.Items.AddRange(new object[] { "Auto", "ExplicitFTPS", "PlainFTP" });
            _cboSecurity.SelectedIndex = 0;
            Controls.Add(_numPort);
            Controls.Add(_cboSecurity);
            y += 32;

            _txtUser = AddRow("FTP Username:", ref y, labelW, inputX, inputW);
            _txtPass = AddRow("FTP Password:", ref y, labelW, inputX, inputW);
            _txtPass.UseSystemPasswordChar = true;

            Controls.Add(new Label { Text = "Local Folder:", Location = new Point(16, y + 3), Size = new Size(labelW, 22) });
            _txtLocal = new TextBox { Location = new Point(inputX, y), Size = new Size(inputW - 82, 24) };
            var btnBrowse = new Button { Text = "Browse...", Location = new Point(inputX + inputW - 76, y - 1), Size = new Size(76, 26) };
            btnBrowse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                        _txtLocal.Text = dlg.SelectedPath;
                }
            };
            Controls.Add(_txtLocal);
            Controls.Add(btnBrowse);
            y += 32;

            _txtRemote = AddRow("Remote Folder:", ref y, labelW, inputX, inputW);
            _txtRemote.Text = "/";
            _txtWebsite = AddRow("Website URL:", ref y, labelW, inputX, inputW);

            Controls.Add(new Label { Text = "Exclude Rules:\n(exclude_config.txt)", Location = new Point(16, y + 3), Size = new Size(labelW, 38) });
            _txtExcludes = new TextBox
            {
                Location = new Point(inputX, y),
                Size = new Size(inputW, 130),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9f)
            };
            Controls.Add(_txtExcludes);
            y += 140;

            _lblStatus = new Label { Location = new Point(16, y), Size = new Size(480, 22), ForeColor = Color.DarkBlue };
            Controls.Add(_lblStatus);
            y += 28;

            var btnTest = new Button { Text = "Test Connection", Location = new Point(16, y), Size = new Size(125, 32) };
            btnTest.Click += OnTestConnection;

            var btnSave = new Button { Text = "Save Profile", Location = new Point(305, y), Size = new Size(100, 32) };
            btnSave.Click += OnSave;

            var btnCancel = new Button { Text = "Cancel", Location = new Point(415, y), Size = new Size(80, 32), DialogResult = DialogResult.Cancel };

            Controls.Add(btnTest);
            Controls.Add(btnSave);
            Controls.Add(btnCancel);
            AcceptButton = btnSave;
            CancelButton = btnCancel;
        }

        private TextBox AddRow(string label, ref int y, int labelW, int inputX, int inputW)
        {
            Controls.Add(new Label { Text = label, Location = new Point(16, y + 3), Size = new Size(labelW, 22) });
            var tb = new TextBox { Location = new Point(inputX, y), Size = new Size(inputW, 24) };
            Controls.Add(tb);
            y += 32;
            return tb;
        }

        private void LoadProfile(string profileName)
        {
            var cfg = ProfileStore.Load(profileName);
            _txtProfile.Text = cfg.ProfileName;
            _txtProfile.ReadOnly = true;
            _txtHost.Text = cfg.Host;
            _numPort.Value = cfg.Port > 0 ? cfg.Port : 21;
            _txtUser.Text = cfg.Username;
            _txtPass.Text = cfg.GetPlainPassword();
            _cboSecurity.SelectedItem = cfg.SecurityMode ?? "Auto";
            _txtLocal.Text = cfg.LocalFolder;
            _txtRemote.Text = string.IsNullOrWhiteSpace(cfg.RemoteFolder) ? "/" : cfg.RemoteFolder;
            _txtWebsite.Text = cfg.WebsiteUrl;
            _txtExcludes.Text = ProfileStore.ReadExcludeText(profileName);
        }

        private FtpProfileConfig BuildConfigFromForm()
        {
            var cfg = new FtpProfileConfig
            {
                ProfileName = _txtProfile.Text.Trim(),
                Host = _txtHost.Text.Trim(),
                Port = (int)_numPort.Value,
                Username = _txtUser.Text.Trim(),
                SecurityMode = _cboSecurity.SelectedItem?.ToString() ?? "Auto",
                LocalFolder = _txtLocal.Text.Trim(),
                RemoteFolder = string.IsNullOrWhiteSpace(_txtRemote.Text) ? "/" : _txtRemote.Text.Trim(),
                WebsiteUrl = _txtWebsite.Text.Trim()
            };
            cfg.SetPlainPassword(_txtPass.Text);
            return cfg;
        }

        private void OnTestConnection(object sender, EventArgs e)
        {
            _lblStatus.ForeColor = Color.DarkBlue;
            _lblStatus.Text = "Testing FTP connection...";
            Application.DoEvents();

            try
            {
                var cfg = BuildConfigFromForm();
                var client = new FtpClientEx(cfg);
                client.ProbeConnection();
                _lblStatus.ForeColor = Color.DarkGreen;
                _lblStatus.Text = $"Connected successfully ({client.EffectiveSecurityMode})!";
            }
            catch (Exception ex)
            {
                _lblStatus.ForeColor = Color.DarkRed;
                _lblStatus.Text = "Connection failed: " + ex.Message;
            }
        }

        private void OnSave(object sender, EventArgs e)
        {
            try
            {
                var cfg = BuildConfigFromForm();
                if (string.IsNullOrWhiteSpace(cfg.ProfileName))
                    throw new ArgumentException("Profile Name is required.");
                if (string.IsNullOrWhiteSpace(cfg.Host))
                    throw new ArgumentException("FTP Host is required.");

                ProfileStore.Save(cfg, _txtExcludes.Text);
                SavedProfileName = cfg.ProfileName;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
