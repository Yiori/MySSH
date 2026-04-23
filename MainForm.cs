using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;

namespace MySSH
{
    public partial class MainForm : Form
    {
        private TabControl _tabControl;
        
        // Config Tab
        private TabPage _tabConfig;
        private TextBox _txtHost;
        private TextBox _txtUsername;
        private TextBox _txtPassword;
        private Button _btnConnect;
        
        // Terminal Tab
        private TabPage _tabTerminal;
        private Microsoft.Web.WebView2.WinForms.WebView2 _webView;
        
        // SFTP Tab
        private TabPage _tabSftp;
        private TextBox _txtLocalPath;
        private ListBox _lstLocal;
        private TextBox _txtRemotePath;
        private ListBox _lstRemote;
        private Button _btnUpload;
        private Button _btnDownload;

        // Actions Tab
        private TabPage _tabActions;
        private DataGridView _dgvActions;

        private AppConfig _config;
        private SshManager _sshManager;

        public MainForm()
        {
            InitializeComponent();
            _config = ConfigManager.Load();
            _sshManager = new SshManager();
            _sshManager.OnDataReceived += OnTerminalDataReceived;
            _sshManager.OnDisconnected += OnDisconnected;
        }

        private void InitializeComponent()
        {
            this.Text = "MySSH - Customizable SSH Client";
            this.Width = 800;
            this.Height = 600;

            _tabControl = new TabControl { Dock = DockStyle.Fill };

            // Initialize Tabs
            _tabConfig = new TabPage("Configurações");
            _tabTerminal = new TabPage("Terminal");
            _tabSftp = new TabPage("SFTP");
            _tabActions = new TabPage("Ações Rápidas");

            InitializeConfigTab();
            InitializeTerminalTab();
            InitializeSftpTab();
            InitializeActionsTab();

            _tabControl.TabPages.Add(_tabConfig);
            _tabControl.TabPages.Add(_tabTerminal);
            _tabControl.TabPages.Add(_tabSftp);
            _tabControl.TabPages.Add(_tabActions);
            this.Controls.Add(_tabControl);

            this.Load += MainForm_Load;
            this.FormClosing += MainForm_FormClosing;
        }

        private void InitializeConfigTab()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5, Padding = new Padding(20) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            panel.Controls.Add(new Label { Text = "Host / IP:" }, 0, 0);
            _txtHost = new TextBox { Dock = DockStyle.Fill };
            panel.Controls.Add(_txtHost, 1, 0);

            panel.Controls.Add(new Label { Text = "Usuário:" }, 0, 1);
            _txtUsername = new TextBox { Dock = DockStyle.Fill };
            panel.Controls.Add(_txtUsername, 1, 1);

            panel.Controls.Add(new Label { Text = "Senha:" }, 0, 2);
            _txtPassword = new TextBox { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
            panel.Controls.Add(_txtPassword, 1, 2);

            _btnConnect = new Button { Text = "Conectar", Height = 40, Dock = DockStyle.Fill };
            _btnConnect.Click += BtnConnect_Click;
            panel.Controls.Add(_btnConnect, 1, 3);

            _tabConfig.Controls.Add(panel);
        }

        private async void InitializeTerminalTab()
        {
            _webView = new Microsoft.Web.WebView2.WinForms.WebView2 { Dock = DockStyle.Fill };
            _tabTerminal.Controls.Add(_webView);
            
            var userDataFolder = Path.Combine(Path.GetTempPath(), "MySSH_WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);

            string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "terminal.html");
            _webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
            
            _webView.CoreWebView2.WebMessageReceived += WebView_WebMessageReceived;
        }

        private void InitializeActionsTab()
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 1, Padding = new Padding(10) };
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _dgvActions = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = true,
                AllowUserToDeleteRows = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = System.Drawing.SystemColors.Window,
                BorderStyle = BorderStyle.Fixed3D
            };

            _dgvActions.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colName",
                HeaderText = "Nome da Ação",
                FillWeight = 35
            });
            _dgvActions.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "colCommand",
                HeaderText = "Comando SSH",
                FillWeight = 65
            });

            // Auto-save whenever the user finishes editing a cell or deletes a row
            _dgvActions.CellValueChanged += (s, e) => SaveActions();
            _dgvActions.UserDeletedRow  += (s, e) => SaveActions();

            panel.Controls.Add(_dgvActions, 0, 0);
            _tabActions.Controls.Add(panel);
        }

        private void InitializeSftpTab()
        {
            var splitContainer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };

            // Local Panel
            var localPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            localPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            localPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            localPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            
            _txtLocalPath = new TextBox { Dock = DockStyle.Fill };
            _txtLocalPath.KeyDown += TxtLocalPath_KeyDown;
            localPanel.Controls.Add(_txtLocalPath, 0, 0);

            _lstLocal = new ListBox { Dock = DockStyle.Fill };
            _lstLocal.DoubleClick += LstLocal_DoubleClick;
            localPanel.Controls.Add(_lstLocal, 0, 1);

            _btnUpload = new Button { Text = "Upload >>", Dock = DockStyle.Fill };
            _btnUpload.Click += BtnUpload_Click;
            localPanel.Controls.Add(_btnUpload, 0, 2);

            // Remote Panel
            var remotePanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            remotePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            remotePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            remotePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _txtRemotePath = new TextBox { Dock = DockStyle.Fill };
            _txtRemotePath.KeyDown += TxtRemotePath_KeyDown;
            remotePanel.Controls.Add(_txtRemotePath, 0, 0);

            _lstRemote = new ListBox { Dock = DockStyle.Fill };
            _lstRemote.DoubleClick += LstRemote_DoubleClick;
            remotePanel.Controls.Add(_lstRemote, 0, 1);

            _btnDownload = new Button { Text = "<< Download", Dock = DockStyle.Fill };
            _btnDownload.Click += BtnDownload_Click;
            remotePanel.Controls.Add(_btnDownload, 0, 2);

            splitContainer.Panel1.Controls.Add(localPanel);
            splitContainer.Panel2.Controls.Add(remotePanel);

            _tabSftp.Controls.Add(splitContainer);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            _txtHost.Text = _config.Host;
            _txtUsername.Text = _config.Username;
            _txtPassword.Text = _config.Password;

            // Populate actions DataGridView
            foreach (var action in _config.CustomActions)
                _dgvActions.Rows.Add(action.Name, action.Command);
            
            if (string.IsNullOrEmpty(_config.LastLocalPath))
                _config.LastLocalPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                
            LoadLocalDirectory(_config.LastLocalPath);
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _config.Host = _txtHost.Text;
            _config.Username = _txtUsername.Text;
            _config.Password = _txtPassword.Text;
            ConfigManager.Save(_config);
            
            _sshManager?.Dispose();
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            if (_sshManager.IsConnected)
            {
                _sshManager.Disconnect();
                _btnConnect.Text = "Conectar";
                return;
            }

            try
            {
                _btnConnect.Enabled = false;
                _sshManager.Connect(_txtHost.Text, _txtUsername.Text, _txtPassword.Text);
                
                // Save config on successful connect
                _config.Host = _txtHost.Text;
                _config.Username = _txtUsername.Text;
                _config.Password = _txtPassword.Text;
                ConfigManager.Save(_config);

                _btnConnect.Text = "Desconectar";
                
                // Switch to terminal tab
                _tabControl.SelectedTab = _tabTerminal;

                // Load initial remote directory
                LoadRemoteDirectory(string.IsNullOrEmpty(_config.LastRemotePath) ? "." : _config.LastRemotePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao conectar: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _btnConnect.Text = "Conectar";
            }
            finally
            {
                _btnConnect.Enabled = true;
            }
        }

        private void OnDisconnected()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(OnDisconnected));
                return;
            }
            _btnConnect.Text = "Conectar";
        }

        #region Terminal Logic

        private void WebView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var message = e.TryGetWebMessageAsString();
            var data = JsonConvert.DeserializeObject<dynamic>(message);

            if (data.type == "input" && _sshManager.IsConnected)
            {
                string input = data.data;
                _sshManager.WriteToTerminal(input);
            }
            else if (data.type == "action" && _sshManager.IsConnected)
            {
                string command = (string)data.command;
                // Send the command followed by Enter (\r is what SSH shells expect)
                _sshManager.WriteToTerminal(command + "\r");
            }
            else if (data.type == "resize" || data.type == "ready")
            {
                if (_sshManager.IsConnected)
                    _sshManager.ResizeTerminal((uint)data.cols, (uint)data.rows);

                // Always push the current actions list when the terminal (re)loads
                PushActionsToTerminal();
            }
        }

        private void PushActionsToTerminal()
        {
            if (_webView?.CoreWebView2 == null) return;
            var json = JsonConvert.SerializeObject(_config.CustomActions);
            var escaped = JsonConvert.SerializeObject(json); // produces a JSON-string literal
            _webView.CoreWebView2.ExecuteScriptAsync($"loadActions({escaped});");
        }

        private void OnTerminalDataReceived(string data)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(OnTerminalDataReceived), data);
                return;
            }

            if (_webView != null && _webView.CoreWebView2 != null)
            {
                var encodedData = JsonConvert.SerializeObject(data);
                _webView.CoreWebView2.ExecuteScriptAsync($"writeToTerminal({encodedData});");
            }
        }

        #endregion

        #region SFTP Logic

        private void LoadLocalDirectory(string path)
        {
            try
            {
                _lstLocal.Items.Clear();
                _lstLocal.Items.Add("..");
                var dirs = Directory.GetDirectories(path);
                var files = Directory.GetFiles(path);

                foreach (var d in dirs) _lstLocal.Items.Add($"[DIR] {Path.GetFileName(d)}");
                foreach (var f in files) _lstLocal.Items.Add(Path.GetFileName(f));

                _txtLocalPath.Text = path;
                _config.LastLocalPath = path;
                ConfigManager.Save(_config);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar diretório local: {ex.Message}");
            }
        }

        private void LoadRemoteDirectory(string path)
        {
            if (!_sshManager.IsConnected) return;

            try
            {
                _lstRemote.Items.Clear();
                var items = _sshManager.ListDirectory(path);

                foreach (var item in items)
                {
                    if (item.Name == ".") continue;
                    if (item.IsDirectory)
                        _lstRemote.Items.Add($"[DIR] {item.Name}");
                    else
                        _lstRemote.Items.Add(item.Name);
                }

                _txtRemotePath.Text = path;
                _config.LastRemotePath = path;
                ConfigManager.Save(_config);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro ao carregar diretório remoto: {ex.Message}");
            }
        }

        private void LstLocal_DoubleClick(object sender, EventArgs e)
        {
            if (_lstLocal.SelectedItem == null) return;
            string selected = _lstLocal.SelectedItem.ToString();
            
            try
            {
                if (selected == "..")
                {
                    var parent = Directory.GetParent(_txtLocalPath.Text);
                    if (parent != null) LoadLocalDirectory(parent.FullName);
                }
                else if (selected.StartsWith("[DIR] "))
                {
                    string dirName = selected.Substring(6);
                    LoadLocalDirectory(Path.Combine(_txtLocalPath.Text, dirName));
                }
            }
            catch { }
        }

        private void LstRemote_DoubleClick(object sender, EventArgs e)
        {
            if (_lstRemote.SelectedItem == null || !_sshManager.IsConnected) return;
            string selected = _lstRemote.SelectedItem.ToString();

            try
            {
                if (selected == "..")
                {
                    // Basic remote parent directory logic
                    LoadRemoteDirectory($"{_txtRemotePath.Text}/..");
                }
                else if (selected.StartsWith("[DIR] "))
                {
                    string dirName = selected.Substring(6);
                    string newPath = _txtRemotePath.Text.EndsWith("/") ? $"{_txtRemotePath.Text}{dirName}" : $"{_txtRemotePath.Text}/{dirName}";
                    LoadRemoteDirectory(newPath);
                }
            }
            catch { }
        }

        private void TxtLocalPath_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                LoadLocalDirectory(_txtLocalPath.Text);
            }
        }

        private void TxtRemotePath_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                LoadRemoteDirectory(_txtRemotePath.Text);
            }
        }

        private void BtnUpload_Click(object sender, EventArgs e)
        {
            if (!_sshManager.IsConnected || _lstLocal.SelectedItem == null) return;
            string selected = _lstLocal.SelectedItem.ToString();
            if (selected.StartsWith("[DIR] ") || selected == "..") return;

            string localPath = Path.Combine(_txtLocalPath.Text, selected);
            string remotePath = _txtRemotePath.Text.EndsWith("/") ? $"{_txtRemotePath.Text}{selected}" : $"{_txtRemotePath.Text}/{selected}";

            try
            {
                _sshManager.UploadFile(localPath, remotePath);
                MessageBox.Show("Upload concluído!");
                LoadRemoteDirectory(_txtRemotePath.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro no upload: {ex.Message}");
            }
        }

        private void BtnDownload_Click(object sender, EventArgs e)
        {
            if (!_sshManager.IsConnected || _lstRemote.SelectedItem == null) return;
            string selected = _lstRemote.SelectedItem.ToString();
            if (selected.StartsWith("[DIR] ") || selected == "..") return;

            string remotePath = _txtRemotePath.Text.EndsWith("/") ? $"{_txtRemotePath.Text}{selected}" : $"{_txtRemotePath.Text}/{selected}";
            string localPath = Path.Combine(_txtLocalPath.Text, selected);

            try
            {
                _sshManager.DownloadFile(remotePath, localPath);
                MessageBox.Show("Download concluído!");
                LoadLocalDirectory(_txtLocalPath.Text);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erro no download: {ex.Message}");
            }
        }

        #endregion

        #region Actions Logic

        private void BtnSaveActions_Click(object sender, EventArgs e) => SaveActions();

        private void SaveActions()
        {
            _config.CustomActions.Clear();

            foreach (DataGridViewRow row in _dgvActions.Rows)
            {
                if (row.IsNewRow) continue;
                var name = row.Cells["colName"].Value?.ToString() ?? "";
                var cmd  = row.Cells["colCommand"].Value?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(name))
                    _config.CustomActions.Add(new CustomAction { Name = name, Command = cmd });
            }

            ConfigManager.Save(_config);
            PushActionsToTerminal();
        }

        #endregion
    }
}
