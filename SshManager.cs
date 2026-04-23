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

        public void Connect(string host, string username, string password)
        {
            Disconnect();

            _sshClient = new SshClient(host, username, password);
            _sshClient.Connect();

            _sftpClient = new SftpClient(host, username, password);
            _sftpClient.Connect();

            // Create a shell stream with standard VT100 dimensions
            _shellStream = _sshClient.CreateShellStream("xterm", 80, 24, 800, 600, 1024);
            _shellStream.DataReceived += (s, e) =>
            {
                var data = Encoding.UTF8.GetString(e.Data);
                OnDataReceived?.Invoke(data);
            };

            _sshClient.ErrorOccurred += (s, e) => OnDisconnected?.Invoke();
        }

        public void ResizeTerminal(uint cols, uint rows)
        {
            if (_shellStream != null && cols > 0 && rows > 0)
            {
                // Terminal width and height
                // _shellStream.SendWindowChangeRequest(cols, rows, cols * 10, rows * 10);
            }
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

        public void UploadFile(string localFilePath, string remoteFilePath)
        {
            if (_sftpClient == null || !_sftpClient.IsConnected) return;
            using var fileStream = File.OpenRead(localFilePath);
            _sftpClient.UploadFile(fileStream, remoteFilePath);
        }

        public void DownloadFile(string remoteFilePath, string localFilePath)
        {
            if (_sftpClient == null || !_sftpClient.IsConnected) return;
            using var fileStream = File.OpenWrite(localFilePath);
            _sftpClient.DownloadFile(remoteFilePath, fileStream);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
