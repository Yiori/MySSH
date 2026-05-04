using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MySSH
{
    public class LocalTerminalManager : IDisposable
    {
        public event Action<string>? OnDataReceived;
        public event Action? OnDisconnected;

        private IntPtr _hPC = IntPtr.Zero;
        private IntPtr _hProcess = IntPtr.Zero;
        private IntPtr _hThread = IntPtr.Zero;
        private FileStream? _inPipe;
        private FileStream? _outPipe;
        private CancellationTokenSource? _cts;
        private bool _isDisposed = false;

        public void Start(int cols = 80, int rows = 24)
        {
            if (_hPC != IntPtr.Zero) return; // Already started

            // 1. Create pipes
            CreatePipe(out IntPtr hStdInRead, out IntPtr hStdInWrite, IntPtr.Zero, 0);
            CreatePipe(out IntPtr hStdOutRead, out IntPtr hStdOutWrite, IntPtr.Zero, 0);

            // 2. Create Pseudo Console
            var size = new COORD { X = (short)cols, Y = (short)rows };
            int hr = CreatePseudoConsole(size, hStdInRead, hStdOutWrite, 0, out _hPC);
            if (hr != 0)
            {
                CloseHandle(hStdInRead);
                CloseHandle(hStdInWrite);
                CloseHandle(hStdOutRead);
                CloseHandle(hStdOutWrite);
                OnDisconnected?.Invoke();
                return;
            }

            // We can close the read end of input and write end of output because ConPTY owns them now
            CloseHandle(hStdInRead);
            CloseHandle(hStdOutWrite);

            // 3. Prepare Process Attributes
            IntPtr attrList = IntPtr.Zero;
            long attrSize = 0;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
            if (attrSize > 0)
            {
                attrList = Marshal.AllocHGlobal((int)attrSize);
                InitializeProcThreadAttributeList(attrList, 1, 0, ref attrSize);
                UpdateProcThreadAttribute(attrList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, _hPC, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero);
            }

            // 4. Create Process
            var siEx = new STARTUPINFOEX();
            siEx.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            siEx.lpAttributeList = attrList;

            bool created = CreateProcess(
                null,
                "cmd.exe",
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                EXTENDED_STARTUPINFO_PRESENT,
                IntPtr.Zero,
                null,
                ref siEx,
                out PROCESS_INFORMATION pInfo);

            if (attrList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attrList);
                Marshal.FreeHGlobal(attrList);
            }

            if (!created)
            {
                CloseHandle(hStdInWrite);
                CloseHandle(hStdOutRead);
                ClosePseudoConsole(_hPC);
                _hPC = IntPtr.Zero;
                OnDisconnected?.Invoke();
                return;
            }

            _hProcess = pInfo.hProcess;
            _hThread = pInfo.hThread;

            // 5. Wrap pipes in FileStream
            _inPipe = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(hStdInWrite, true), FileAccess.Write);
            _outPipe = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(hStdOutRead, true), FileAccess.Read);

            // 6. Start reading loop and exit monitor
            _cts = new CancellationTokenSource();
            _ = ReadOutputAsync(_outPipe, _cts.Token);
            _ = MonitorProcessAsync(_hProcess, _cts.Token);
        }

        public void ResizeTerminal(int cols, int rows)
        {
            if (_hPC != IntPtr.Zero)
            {
                var size = new COORD { X = (short)cols, Y = (short)rows };
                ResizePseudoConsole(_hPC, size);
            }
        }

        public void WriteToTerminal(string data)
        {
            if (_inPipe != null && _hPC != IntPtr.Zero && !_isDisposed)
            {
                try
                {
                    // ConPTY processes all echoing and vt parsing natively!
                    // So we just send raw bytes.
                    byte[] bytes = Encoding.UTF8.GetBytes(data);
                    _inPipe.Write(bytes, 0, bytes.Length);
                    _inPipe.Flush();
                }
                catch { }
            }
        }

        private async Task ReadOutputAsync(FileStream stream, CancellationToken token)
        {
            byte[] buffer = new byte[8192];
            try
            {
                while (!token.IsCancellationRequested)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, token);
                    if (read > 0)
                    {
                        string text = Encoding.UTF8.GetString(buffer, 0, read);
                        OnDataReceived?.Invoke(text);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            catch { }
            
            if (!_isDisposed) OnDisconnected?.Invoke();
        }

        private async Task MonitorProcessAsync(IntPtr hProcess, CancellationToken token)
        {
            await Task.Run(() =>
            {
                WaitForSingleObject(hProcess, 0xFFFFFFFF); // INFINITE
            }, token);
            
            if (!_isDisposed) OnDisconnected?.Invoke();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _cts?.Cancel();
            
            if (_hProcess != IntPtr.Zero)
            {
                TerminateProcess(_hProcess, 0);
                CloseHandle(_hProcess);
                _hProcess = IntPtr.Zero;
            }
            if (_hThread != IntPtr.Zero)
            {
                CloseHandle(_hThread);
                _hThread = IntPtr.Zero;
            }
            if (_hPC != IntPtr.Zero)
            {
                ClosePseudoConsole(_hPC);
                _hPC = IntPtr.Zero;
            }
            
            _inPipe?.Dispose();
            _outPipe?.Dispose();
        }

        // --- P/Invoke Definitions ---

        [StructLayout(LayoutKind.Sequential)]
        private struct COORD
        {
            public short X;
            public short Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct STARTUPINFOEX
        {
            public STARTUPINFO StartupInfo;
            public IntPtr lpAttributeList;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
        private const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int CreatePseudoConsole(COORD size, IntPtr hConsoleInput, IntPtr hConsoleOutput, uint dwFlags, out IntPtr phPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern void ClosePseudoConsole(IntPtr hPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CreatePipe(out IntPtr hReadPipe, out IntPtr hWritePipe, IntPtr lpPipeAttributes, uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref long lpSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr Attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcess(
            string? lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string? lpCurrentDirectory,
            [In] ref STARTUPINFOEX lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);
    }
}
