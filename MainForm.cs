using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MySSH
{
    public static class IconHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        };

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DestroyIcon(IntPtr hIcon);

        public const uint SHGFI_ICON = 0x100;
        public const uint SHGFI_SMALLICON = 0x1;
        public const uint SHGFI_USEFILEATTRIBUTES = 0x10;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

        public static System.Drawing.Icon GetIcon(string extension, bool isDirectory)
        {
            SHFILEINFO shinfo = new SHFILEINFO();
            uint flags = SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES;
            uint attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            
            string path = isDirectory ? "folder" : ("file" + extension);

            SHGetFileInfo(path, attributes, ref shinfo, (uint)Marshal.SizeOf(shinfo), flags);
            if (shinfo.hIcon != IntPtr.Zero)
            {
                System.Drawing.Icon icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(shinfo.hIcon).Clone();
                DestroyIcon(shinfo.hIcon);
                return icon;
            }
            return System.Drawing.SystemIcons.WinLogo;
        }
    }

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
        private ListView _lvwLocal;
        private TextBox _txtRemotePath;
        private ListView _lvwRemote;
        private Button _btnUpload;
        private Button _btnDownload;
        private ImageList _sftpImageList;
        private Dictionary<string, int> _iconCache;
        private ProgressBar _progressBar;
        private TextBox _txtSftpLog;

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
            _sftpImageList = new ImageList { ImageSize = new System.Drawing.Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
            _iconCache = new Dictionary<string, int>();

            var mainPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));

            var splitContainer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };

            // Local Panel
            var localPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
            localPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            localPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            localPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            
            _txtLocalPath = new TextBox { Dock = DockStyle.Fill };
            _txtLocalPath.KeyDown += TxtLocalPath_KeyDown;
            localPanel.Controls.Add(_txtLocalPath, 0, 0);

            _lvwLocal = new ListView 
            { 
                Dock = DockStyle.Fill, 
                View = View.Details, 
                FullRowSelect = true,
                HideSelection = false,
                SmallImageList = _sftpImageList
            };
            _lvwLocal.Columns.Add("Nome", 200);
            _lvwLocal.Columns.Add("Tamanho", 80);
            _lvwLocal.Columns.Add("Tipo", 80);
            _lvwLocal.Columns.Add("Modificado", 120);
            _lvwLocal.DoubleClick += LvwLocal_DoubleClick;
            _lvwLocal.KeyDown += LvwLocal_KeyDown;
            
            var localMenu = new ContextMenuStrip();
            var localDelete = new ToolStripMenuItem("Excluir");
            localDelete.Click += LocalDelete_Click;
            localMenu.Items.Add(localDelete);
            _lvwLocal.ContextMenuStrip = localMenu;
            
            localPanel.Controls.Add(_lvwLocal, 0, 1);

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

            _lvwRemote = new ListView 
            { 
                Dock = DockStyle.Fill, 
                View = View.Details, 
                FullRowSelect = true,
                HideSelection = false,
                SmallImageList = _sftpImageList
            };
            _lvwRemote.Columns.Add("Nome", 200);
            _lvwRemote.Columns.Add("Tamanho", 80);
            _lvwRemote.Columns.Add("Tipo", 80);
            _lvwRemote.Columns.Add("Modificado", 120);
            _lvwRemote.DoubleClick += LvwRemote_DoubleClick;
            _lvwRemote.KeyDown += LvwRemote_KeyDown;
            
            var remoteMenu = new ContextMenuStrip();
            var remoteDelete = new ToolStripMenuItem("Excluir");
            remoteDelete.Click += RemoteDelete_Click;
            remoteMenu.Items.Add(remoteDelete);
            _lvwRemote.ContextMenuStrip = remoteMenu;
            
            remotePanel.Controls.Add(_lvwRemote, 0, 1);

            _btnDownload = new Button { Text = "<< Download", Dock = DockStyle.Fill };
            _btnDownload.Click += BtnDownload_Click;
            remotePanel.Controls.Add(_btnDownload, 0, 2);

            splitContainer.Panel1.Controls.Add(localPanel);
            splitContainer.Panel2.Controls.Add(remotePanel);

            _progressBar = new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Continuous };
            _txtSftpLog = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };

            mainPanel.Controls.Add(splitContainer, 0, 0);
            mainPanel.Controls.Add(_progressBar, 0, 1);
            mainPanel.Controls.Add(_txtSftpLog, 0, 2);

            _tabSftp.Controls.Add(mainPanel);
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

        private void LogSftp(string message)
        {
            if (InvokeRequired) { Invoke(new Action<string>(LogSftp), message); return; }
            _txtSftpLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
        }

        private void UpdateProgress(int percentage)
        {
            if (InvokeRequired) { Invoke(new Action<int>(UpdateProgress), percentage); return; }
            if (percentage < 0) percentage = 0;
            if (percentage > 100) percentage = 100;
            _progressBar.Value = percentage;
        }

        private int GetIconIndex(string extension, bool isDirectory)
        {
            string key = isDirectory ? "DIR" : extension.ToLower();
            if (_iconCache.ContainsKey(key))
                return _iconCache[key];

            var icon = IconHelper.GetIcon(extension, isDirectory);
            _sftpImageList.Images.Add(key, icon);
            int index = _sftpImageList.Images.Count - 1;
            _iconCache[key] = index;
            return index;
        }

        private string FormatSize(long bytes)
        {
            string[] suf = { "B", "KB", "MB", "GB", "TB" };
            if (bytes == 0) return "0 B";
            int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
            double num = Math.Round(bytes / Math.Pow(1024, place), 1);
            return $"{num} {suf[place]}";
        }

        private void LoadLocalDirectory(string path)
        {
            try
            {
                _lvwLocal.Items.Clear();
                int upImgIndex = GetIconIndex("", true);
                _lvwLocal.Items.Add(new ListViewItem(new[] { "..", "", "", "" }, upImgIndex) { Tag = true });
                var dirInfo = new DirectoryInfo(path);

                foreach (var d in dirInfo.GetDirectories())
                {
                    int imgIndex = GetIconIndex("", true);
                    _lvwLocal.Items.Add(new ListViewItem(new[] { d.Name, "", "Pasta", d.LastWriteTime.ToString("g") }, imgIndex) { Tag = true });
                }
                foreach (var f in dirInfo.GetFiles())
                {
                    int imgIndex = GetIconIndex(f.Extension, false);
                    _lvwLocal.Items.Add(new ListViewItem(new[] { f.Name, FormatSize(f.Length), f.Extension, f.LastWriteTime.ToString("g") }, imgIndex) { Tag = false });
                }

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
                _lvwRemote.Items.Clear();
                var items = _sshManager.ListDirectory(path);

                foreach (var item in items)
                {
                    if (item.Name == ".") continue;
                    if (item.Name == "..")
                    {
                        int upImgIndex = GetIconIndex("", true);
                        _lvwRemote.Items.Add(new ListViewItem(new[] { "..", "", "", "" }, upImgIndex) { Tag = true });
                        continue;
                    }

                    if (item.IsDirectory)
                    {
                        int imgIndex = GetIconIndex("", true);
                        _lvwRemote.Items.Add(new ListViewItem(new[] { item.Name, "", "Pasta", item.LastWriteTime.ToString("g") }, imgIndex) { Tag = true });
                    }
                    else
                    {
                        string ext = Path.GetExtension(item.Name);
                        int imgIndex = GetIconIndex(ext, false);
                        _lvwRemote.Items.Add(new ListViewItem(new[] { item.Name, FormatSize(item.Attributes.Size), "Arquivo", item.LastWriteTime.ToString("g") }, imgIndex) { Tag = false });
                    }
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

        private void LvwLocal_DoubleClick(object sender, EventArgs e)
        {
            if (_lvwLocal.SelectedItems.Count == 0) return;
            var item = _lvwLocal.SelectedItems[0];
            string selected = item.Text;
            bool isDir = item.Tag is bool b && b;
            
            try
            {
                if (selected == "..")
                {
                    var parent = Directory.GetParent(_txtLocalPath.Text);
                    if (parent != null) LoadLocalDirectory(parent.FullName);
                }
                else if (isDir)
                {
                    LoadLocalDirectory(Path.Combine(_txtLocalPath.Text, selected));
                }
            }
            catch { }
        }

        private void LvwRemote_DoubleClick(object sender, EventArgs e)
        {
            if (_lvwRemote.SelectedItems.Count == 0 || !_sshManager.IsConnected) return;
            var item = _lvwRemote.SelectedItems[0];
            string selected = item.Text;
            bool isDir = item.Tag is bool b && b;

            try
            {
                if (selected == "..")
                {
                    LoadRemoteDirectory($"{_txtRemotePath.Text}/..");
                }
                else if (isDir)
                {
                    string newPath = _txtRemotePath.Text.EndsWith("/") ? $"{_txtRemotePath.Text}{selected}" : $"{_txtRemotePath.Text}/{selected}";
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

        private async void BtnUpload_Click(object sender, EventArgs e)
        {
            if (!_sshManager.IsConnected || _lvwLocal.SelectedItems.Count == 0) return;
            var item = _lvwLocal.SelectedItems[0];
            string selected = item.Text;
            bool isDir = item.Tag is bool b && b;
            if (isDir || selected == "..") return;

            string localPath = Path.Combine(_txtLocalPath.Text, selected);
            string remotePath = _txtRemotePath.Text.EndsWith("/") ? $"{_txtRemotePath.Text}{selected}" : $"{_txtRemotePath.Text}/{selected}";

            _btnUpload.Enabled = false;
            _btnDownload.Enabled = false;
            _progressBar.Value = 0;
            LogSftp($"Iniciando upload: {selected}");

            try
            {
                long totalSize = new FileInfo(localPath).Length;
                
                await Task.Run(() =>
                {
                    _sshManager.UploadFile(localPath, remotePath, (uploaded) => 
                    {
                        if (totalSize > 0)
                        {
                            int percentage = (int)((uploaded * 100) / (ulong)totalSize);
                            UpdateProgress(percentage);
                        }
                    });
                });
                
                LogSftp($"Upload concluído: {selected}");
                LoadRemoteDirectory(_txtRemotePath.Text);
            }
            catch (Exception ex)
            {
                LogSftp($"Erro no upload: {ex.Message}");
                MessageBox.Show($"Erro no upload: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnUpload.Enabled = true;
                _btnDownload.Enabled = true;
                _progressBar.Value = 0;
            }
        }

        private async void BtnDownload_Click(object sender, EventArgs e)
        {
            if (!_sshManager.IsConnected || _lvwRemote.SelectedItems.Count == 0) return;
            var item = _lvwRemote.SelectedItems[0];
            string selected = item.Text;
            bool isDir = item.Tag is bool b && b;
            if (isDir || selected == "..") return;

            string remotePath = _txtRemotePath.Text.EndsWith("/") ? $"{_txtRemotePath.Text}{selected}" : $"{_txtRemotePath.Text}/{selected}";
            string localPath = Path.Combine(_txtLocalPath.Text, selected);

            _btnUpload.Enabled = false;
            _btnDownload.Enabled = false;
            _progressBar.Value = 0;
            LogSftp($"Iniciando download: {selected}");

            try
            {
                long totalSize = _sshManager.GetRemoteFileSize(remotePath);

                await Task.Run(() =>
                {
                    _sshManager.DownloadFile(remotePath, localPath, (downloaded) => 
                    {
                        if (totalSize > 0)
                        {
                            int percentage = (int)((downloaded * 100) / (ulong)totalSize);
                            UpdateProgress(percentage);
                        }
                    });
                });

                LogSftp($"Download concluído: {selected}");
                LoadLocalDirectory(_txtLocalPath.Text);
            }
            catch (Exception ex)
            {
                LogSftp($"Erro no download: {ex.Message}");
                MessageBox.Show($"Erro no download: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _btnUpload.Enabled = true;
                _btnDownload.Enabled = true;
                _progressBar.Value = 0;
            }
        }

        private void LocalDelete_Click(object sender, EventArgs e) => DeleteSelectedLocal();
        private void LvwLocal_KeyDown(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) DeleteSelectedLocal(); }

        private void RemoteDelete_Click(object sender, EventArgs e) => DeleteSelectedRemote();
        private void LvwRemote_KeyDown(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Delete) DeleteSelectedRemote(); }

        private void DeleteSelectedLocal()
        {
            if (_lvwLocal.SelectedItems.Count == 0) return;
            var item = _lvwLocal.SelectedItems[0];
            if (item.Text == "..") return;

            bool isDir = item.Tag is bool b && b;
            string targetPath = Path.Combine(_txtLocalPath.Text, item.Text);

            if (MessageBox.Show($"Tem certeza que deseja excluir '{item.Text}'?", "Confirmar Exclusão", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                try
                {
                    if (isDir) Directory.Delete(targetPath, true);
                    else File.Delete(targetPath);
                    LoadLocalDirectory(_txtLocalPath.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao excluir: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void DeleteSelectedRemote()
        {
            if (_lvwRemote.SelectedItems.Count == 0 || !_sshManager.IsConnected) return;
            var item = _lvwRemote.SelectedItems[0];
            if (item.Text == "..") return;

            bool isDir = item.Tag is bool b && b;
            string targetPath = _txtRemotePath.Text.EndsWith("/") ? $"{_txtRemotePath.Text}{item.Text}" : $"{_txtRemotePath.Text}/{item.Text}";

            if (MessageBox.Show($"Tem certeza que deseja excluir '{item.Text}' do servidor?", "Confirmar Exclusão", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                try
                {
                    if (isDir) _sshManager.DeleteRemoteDirectory(targetPath);
                    else _sshManager.DeleteRemoteFile(targetPath);
                    LoadRemoteDirectory(_txtRemotePath.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erro ao excluir remoto: {ex.Message}", "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
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
