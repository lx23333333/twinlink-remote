using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TwinLink {
    public class TransferProgress {
        public int Completed;
        public int Total;
        public string File;
        public string Message;
    }
    public class TransferResult {
        public int ExitCode;
        public string Output;
    }
    public static class FileTransfer {
        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath(ref Guid id,uint flags,IntPtr token,out IntPtr path);
        public static string DownloadDirectory() {
            Guid downloads=new Guid("374DE290-123F-4565-9164-39C4925E467B");IntPtr path;
            int result=SHGetKnownFolderPath(ref downloads,0,IntPtr.Zero,out path);
            if(result!=0)Marshal.ThrowExceptionForHR(result);
            try{return Marshal.PtrToStringUni(path);}finally{Marshal.FreeCoTaskMem(path);}
        }
        public static string Target(string host) {
            host=Core.ValidateHost(host);IPAddress ip;
            if(IPAddress.TryParse(host,out ip)&&ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetworkV6)host="["+host+"]";
            return host+":";
        }
        public static string SendArgs(string host,string path) {
            string target=Target(host);
            string full=Path.GetFullPath(path);
            if(!System.IO.File.Exists(full))throw new FileNotFoundException("文件不存在或不是普通文件："+full);
            return "file cp --verbose --update-interval=0s "+Core.Quote(full)+" "+Core.Quote(target);
        }
        public static async Task<TransferResult> Run(string executable,string args,CancellationToken cancellation) {
            cancellation.ThrowIfCancellationRequested();
            using(var process=new Process()) {
                process.StartInfo=new ProcessStartInfo(executable,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
                process.Start();
                using(cancellation.Register(()=>{try{if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}})) {
                    var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
                    await Task.Run(()=>process.WaitForExit()).ConfigureAwait(false);
                    string text=(await stdout.ConfigureAwait(false))+(await stderr.ConfigureAwait(false));
                    cancellation.ThrowIfCancellationRequested();
                    return new TransferResult{ExitCode=process.ExitCode,Output=text.Trim()};
                }
            }
        }
        public static async Task Send(string executable,string host,string[] paths,IProgress<TransferProgress> progress,CancellationToken cancellation,
            Func<string,string,CancellationToken,Task<TransferResult>> runner=null) {
            if(paths==null||paths.Length==0)throw new ArgumentException("请先添加要发送的文件。");
            // Validate the entire queue before sending anything. Full paths cannot become CLI flags.
            var commands=paths.Select(p=>SendArgs(host,p)).ToArray();
            if(runner==null)runner=Run;
            for(int index=0;index<paths.Length;index++) {
                cancellation.ThrowIfCancellationRequested();
                progress.Report(new TransferProgress{Completed=index,Total=paths.Length,File=Path.GetFileName(paths[index]),Message="正在发送"});
                var result=await runner(executable,commands[index],cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                if(result.ExitCode!=0)throw new IOException("发送 "+Path.GetFileName(paths[index])+" 未完成（代码 "+result.ExitCode+"）。\n"+result.Output+"\n请确认两端登录同一 Tailscale 账户、对方在线，且已启用 Send Files。");
                progress.Report(new TransferProgress{Completed=index+1,Total=paths.Length,File=Path.GetFileName(paths[index]),Message="发送完成"});
            }
        }
    }
}
