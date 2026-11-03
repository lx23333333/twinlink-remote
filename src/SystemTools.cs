using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TwinLink {
    public static class SystemTools {
        public static Process Launch(string file, string args) {
            return Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(file) });
        }
        public static void Open(string target) { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        public static async Task<string> Run(string file, string args, int timeout) {
            using (var p = new Process()) {
                p.StartInfo = new ProcessStartInfo(file, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
                p.Start();
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                var done = Task.Run(() => p.WaitForExit(timeout));
                if (!await done) { try { p.Kill(); } catch {} throw new TimeoutException("检测超时，请检查服务是否运行。"); }
                string output = (await stdout) + (await stderr);
                if (p.ExitCode != 0) throw new InvalidOperationException(output.Length == 0 ? "组件退出，代码 " + p.ExitCode : output);
                return output.Trim();
            }
        }
        public static async Task<bool> Port(string host, int port) {
            using (var tcp = new TcpClient()) {
                try {
                    var connect = tcp.ConnectAsync(host, port);
                    if (await Task.WhenAny(connect, Task.Delay(2500)) != connect) return false;
                    await connect; return tcp.Connected;
                } catch { return false; }
            }
        }
    }
}
