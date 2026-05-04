using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Renci.SshNet;

namespace MySSH
{
    public class SshManager : IDisposable
    {
        private SshClient _sshClient;
        private SftpClient _sftpClient;
        private ShellStream _shellStream;

        public event Action<string> OnDataReceived;
        public event Action OnDisconnected;

        public bool IsConnected => _sshClient?.IsConnected == true;

        private uint _cols = 80;
        private uint _rows = 24;

        public void Connect(string host, string username, string password, uint cols = 80, uint rows = 24)
        {
            Disconnect();

            _cols = cols > 0 ? cols : 80;
            _rows = rows > 0 ? rows : 24;

            _sshClient = new SshClient(host, username, password);
            _sshClient.Connect();

            _sftpClient = new SftpClient(host, username, password);
            _sftpClient.Connect();

            // Create a shell stream with the actual xterm.js dimensions
            _shellStream = _sshClient.CreateShellStream("xterm-256color", _cols, _rows, _cols * 8, _rows * 16, 65536);
            _shellStream.DataReceived += (s, e) =>
            {
                var data = Encoding.UTF8.GetString(e.Data);
                OnDataReceived?.Invoke(data);
            };

            _sshClient.ErrorOccurred += (s, e) => OnDisconnected?.Invoke();
        }

        public void ResizeTerminal(uint cols, uint rows)
        {
            if (_shellStream == null || cols == 0 || rows == 0) return;

            _cols = cols;
            _rows = rows;

            try
            {
                // SSH.NET 2025.x does not expose SendWindowChangeRequest on ShellStream directly.
                // We access the internal IChannelSession via reflection.
                var channelField = typeof(ShellStream)
                    .GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (channelField == null) return;

                var channel = channelField.GetValue(_shellStream);
                if (channel == null) return;

                var resizeMethod = channel.GetType()
                    .GetMethod("SendWindowChangeRequest",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (resizeMethod == null) return;

                resizeMethod.Invoke(channel, new object[] { cols, rows, cols * 8u, rows * 16u });
            }
            catch { /* best-effort — non-critical */ }
        }

        public void WriteToTerminal(string data)
        {
            if (_shellStream != null && _sshClient.IsConnected)
            {
                _shellStream.Write(data);
                _shellStream.Flush();
            }
        }

        public void Disconnect()
        {
            _shellStream?.Dispose();
            _shellStream = null;

            if (_sshClient?.IsConnected == true) _sshClient.Disconnect();
            _sshClient?.Dispose();
            _sshClient = null;

            if (_sftpClient?.IsConnected == true) _sftpClient.Disconnect();
            _sftpClient?.Dispose();
            _sftpClient = null;
        }

        // SFTP Methods
        public IEnumerable<Renci.SshNet.Sftp.ISftpFile> ListDirectory(string path)
        {
            if (_sftpClient == null || !_sftpClient.IsConnected)
                throw new InvalidOperationException("SFTP is not connected.");

            if (string.IsNullOrEmpty(path)) path = ".";
            return _sftpClient.ListDirectory(path);
        }

        public string GetRemoteWorkingDirectory()
        {
            return _sftpClient?.WorkingDirectory ?? "/";
        }

        public long GetRemoteFileSize(string path)
        {
            if (_sftpClient == null || !_sftpClient.IsConnected) return 0;
            try { return _sftpClient.GetAttributes(path).Size; }
            catch { return 0; }
        }

        public void UploadFile(string localFilePath, string remoteFilePath, Action<ulong> progressCallback = null)
        {
            if (_sftpClient == null || !_sftpClient.IsConnected) return;
            using var fileStream = File.OpenRead(localFilePath);
            _sftpClient.UploadFile(fileStream, remoteFilePath, progressCallback);
        }

        public void DownloadFile(string remoteFilePath, string localFilePath, Action<ulong> progressCallback = null)
        {
            if (_sftpClient == null || !_sftpClient.IsConnected) return;
            using var fileStream = File.OpenWrite(localFilePath);
            _sftpClient.DownloadFile(remoteFilePath, fileStream, progressCallback);
        }

        public void DeleteRemoteFile(string path)
        {
            if (_sftpClient == null || !_sftpClient.IsConnected) return;
            _sftpClient.DeleteFile(path);
        }

        public void DeleteRemoteDirectory(string path)
        {
            if (_sftpClient == null || !_sftpClient.IsConnected) return;
            _sftpClient.DeleteDirectory(path);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
