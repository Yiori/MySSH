using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MySSH
{
    public class LocalTerminalManager : IDisposable
    {
        private Process _process;
        private CancellationTokenSource _cts;
        private StringBuilder _inputBuffer = new StringBuilder();
        private bool _lastWasCr = false;

        public event Action<string> OnDataReceived;
        public event Action OnDisconnected;

        public void Start()
        {
            if (_process != null) return;

            _process = new Process();
            _process.StartInfo.FileName = "cmd.exe";
            _process.StartInfo.UseShellExecute = false;
            _process.StartInfo.RedirectStandardInput = true;
            _process.StartInfo.RedirectStandardOutput = true;
            _process.StartInfo.RedirectStandardError = true;
            _process.StartInfo.CreateNoWindow = true;
            _process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            _process.StartInfo.StandardErrorEncoding = Encoding.UTF8;

            _process.EnableRaisingEvents = true;
            _process.Exited += (s, e) => OnDisconnected?.Invoke();

            try
            {
                _process.Start();

                _cts = new CancellationTokenSource();
                _ = ReadOutputAsync(_process.StandardOutput, _cts.Token);
                _ = ReadOutputAsync(_process.StandardError, _cts.Token);
            }
            catch (Exception)
            {
                OnDisconnected?.Invoke();
            }
        }

        private async Task ReadOutputAsync(StreamReader reader, CancellationToken token)
        {
            char[] buffer = new char[4096];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    int read = await reader.ReadAsync(buffer, 0, buffer.Length);
                    if (read > 0)
                    {
                        string text = new string(buffer, 0, read);
                        
                        // xterm.js expects \r\n for newlines, cmd usually outputs \r\n.
                        // We replace pure \n that are not preceded by \r just in case.
                        text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");

                        OnDataReceived?.Invoke(text);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch { }
        }

        public void WriteToTerminal(string data)
        {
            if (string.IsNullOrEmpty(data)) return;

            if (_process != null && !_process.HasExited)
            {
                try
                {
                    foreach (char c in data)
                    {
                        if (c == '\x1b')
                        {
                            // Skip the ESC char itself
                            continue;
                        }
                        else if (c == '\x7F' || c == '\b') // Backspace
                        {
                            if (_inputBuffer.Length > 0)
                            {
                                _inputBuffer.Length--;
                                OnDataReceived?.Invoke("\b \b");
                            }
                            _lastWasCr = false;
                        }
                        else if (c == '\r' || c == '\n') // Enter
                        {
                            if (c == '\n' && _lastWasCr)
                            {
                                _lastWasCr = false;
                                continue;
                            }

                            string command = _inputBuffer.ToString() + "\r\n";
                            _inputBuffer.Clear();
                            
                            OnDataReceived?.Invoke("\r\n");
                            
                            _process.StandardInput.Write(command);
                            _process.StandardInput.Flush();

                            _lastWasCr = (c == '\r');
                        }
                        else
                        {
                            _inputBuffer.Append(c);
                            OnDataReceived?.Invoke(c.ToString());
                            _lastWasCr = false;
                        }
                    }
                }
                catch { }
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            if (_process != null)
            {
                try { if (!_process.HasExited) _process.Kill(); } catch { }
                _process.Dispose();
                _process = null;
            }
        }
    }
}
