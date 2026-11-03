using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TwinLink {
    public class InstallerForm : Form {
        readonly TextBox output = new TextBox();
        readonly Label state = new Label();
        readonly Button install = new Button();
        readonly ProgressBar progress = new ProgressBar();
        bool installing;

        public InstallerForm() {
            Text = "TwinLink · 安装必要组件";
            Font = new Font("Microsoft YaHei UI", 10);
            ClientSize = new Size(790, 570);
            MinimumSize = new Size(700, 500);
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(243,246,251);
            var layout = new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(20), RowCount=6, ColumnCount=1 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,65));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,54));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,26));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,50));
            Controls.Add(layout);
            layout.Controls.Add(new Label { Text="安装 Sunshine、Moonlight 和 Tailscale\n点击下方“开始安装”，过程中可能弹出管理员确认。", Dock=DockStyle.Fill },0,0);
            var links = new FlowLayoutPanel { Dock=DockStyle.Fill };
            AddLink(links,"Sunshine 官方下载","https://github.com/LizardByte/Sunshine/releases/latest");
            AddLink(links,"Moonlight 官方下载","https://github.com/moonlight-stream/moonlight-qt/releases/latest");
            AddLink(links,"Tailscale 官方下载","https://tailscale.com/download/windows");
            AddLink(links,"安装 winget","https://apps.microsoft.com/detail/9nblggh4nns1");
            layout.Controls.Add(links,0,1);
            state.Text="准备就绪，尚未开始安装。";state.Dock=DockStyle.Fill;
            layout.Controls.Add(state,0,2);
            output.Multiline=true;output.ReadOnly=true;output.ScrollBars=ScrollBars.Vertical;output.Dock=DockStyle.Fill;output.BackColor=Color.White;
            layout.Controls.Add(output,0,3);
            progress.Dock=DockStyle.Fill;progress.Style=ProgressBarStyle.Blocks;
            layout.Controls.Add(progress,0,4);
            var buttons = new FlowLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(0,8,0,0) };
            install.Text="开始安装";install.AutoSize=true;install.Height=34;
            install.Click += async (s,e) => await InstallAll();
            buttons.Controls.Add(install);
            var copy=new Button { Text="复制安装日志", AutoSize=true,Height=34 };
            copy.Click+=(s,e)=>{try{Clipboard.SetText(output.Text.Length==0?state.Text:output.Text);}catch(Exception ex){Append(ex.Message);}};
            buttons.Controls.Add(copy);
            var close=new Button { Text="关闭", AutoSize=true,Height=34 };close.Click+=(s,e)=>Close();buttons.Controls.Add(close);
            layout.Controls.Add(buttons,0,5);
            Append("点击“开始安装”后，安装进度与错误信息会显示在这里。");
            Append("自动安装失败时，可以点击上方官方下载链接手动安装；完成后回主窗口刷新状态。");
            FormClosing+=(s,e)=>{if(installing){e.Cancel=true;state.Text="安装正在运行，请等待当前安装流程结束后关闭。";}};
        }

        void AddLink(Control parent, string name, string url) {
            var link=new LinkLabel { Text=name,AutoSize=true,Margin=new Padding(0,5,18,5) };
            link.LinkClicked+=(s,e)=>{try{SystemTools.Open(url);}catch(Exception ex){Append("无法打开下载页："+ex.Message);}};
            parent.Controls.Add(link);
        }
        void Append(string text) {
            if(IsDisposed)return;
            output.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);
        }
        public static string FindWinget() {
            var roots = new List<string>();
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Microsoft","WindowsApps"));
            roots.AddRange((Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator));
            foreach(string root in roots) {
                try { string path=Path.Combine(root.Trim().Trim('"'),"winget.exe");if(File.Exists(path))return path; } catch(ArgumentException) {} catch(NotSupportedException) {}
            }
            throw new FileNotFoundException("未找到 winget。点击上方“安装 winget”安装 Microsoft 应用安装程序，或直接从三个官方下载页安装组件。");
        }
        async Task InstallAll() {
            if(installing)return;
            installing=true;install.Enabled=false;progress.Style=ProgressBarStyle.Marquee;
            int failed=0;
            try {
                string winget=FindWinget();
                Append("安装工具："+winget);
                foreach(var component in new[]{new[]{"Sunshine","LizardByte.Sunshine"},new[]{"Moonlight","MoonlightGameStreamingProject.Moonlight"},new[]{"Tailscale","Tailscale.Tailscale"}}) {
                    state.Text="正在安装 "+component[0]+"，请留意系统管理员确认…";
                    Append(state.Text);
                    // Only fixed package identifiers reach this process; no shell is involved.
                    int code=await RunInstaller(winget,"install --id "+component[1]+" --exact --source winget --accept-source-agreements --accept-package-agreements --disable-interactivity",Append);
                    if(code==0)Append(component[0]+"：安装完成。");
                    else {failed++;Append(component[0]+" 未能确认安装成功，退出代码 "+code+" (0x"+unchecked((uint)code).ToString("X8")+")。请查看上方错误或使用官方下载链接。");}
                }
                Append("正在配置本程序的文件连接权限（仅允许 Tailscale 地址），请留意 Windows 管理员确认。");
                try{int code=await NetworkPermissions.InstallRule();if(code!=0){failed++;Append("文件连接权限未配置成功。可重新运行此安装流程，或在 Windows 防火墙允许本程序。");}else Append("文件连接权限已配置完成。");}catch(Exception ex){failed++;Append("文件连接权限未配置："+ex.Message);}
                state.Text=failed==0?"安装流程完成，请回主窗口查看准备状态。":"安装流程结束，有 "+failed+" 项需要检查上面的错误信息。";
                Append(state.Text);
            } catch(Exception ex) {
                state.Text="安装未完成，请查看下方原因或使用官方下载。";
                Append(ex.Message);
            } finally {installing=false;install.Enabled=true;install.Text="重新检查 / 安装";progress.Style=ProgressBarStyle.Blocks;}
        }
        public static async Task<int> RunInstaller(string file,string args,Action<string> report) {
            using(var process=new Process()) {
                process.StartInfo=new ProcessStartInfo(file,args) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
                process.Start();
                Task stdout=Pump(process.StandardOutput,report),stderr=Pump(process.StandardError,report);
                await Task.Run(()=>process.WaitForExit());
                await Task.WhenAll(stdout,stderr);
                return process.ExitCode;
            }
        }
        static async Task Pump(StreamReader reader,Action<string> report) {
            // Read blocks rather than lines so download progress without newlines is visible.
            char[] buffer=new char[2048];int length;
            while((length=await reader.ReadAsync(buffer,0,buffer.Length))!=0) {
                string message=new string(buffer,0,length).Trim('\r','\n');
                if(message.Length!=0)report(message);
            }
        }
    }
}
