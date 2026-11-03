using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TwinLink {
    public class FileTransferForm : Form {
        readonly TextBox target=new TextBox(),log=new TextBox();
        readonly ListBox files=new ListBox();
        readonly Label state=new Label();
        readonly ProgressBar progress=new ProgressBar();
        readonly Button send=new Button(),cancel=new Button();
        readonly FlowLayoutPanel edit=new FlowLayoutPanel();
        CancellationTokenSource cancellation;
        bool active;
        public FileTransferForm(string host) {
            Text="TwinLink · 文件互传";Font=new Font("Microsoft YaHei UI",10);
            ClientSize=new Size(850,700);MinimumSize=new Size(790,640);StartPosition=FormStartPosition.CenterParent;BackColor=Color.FromArgb(243,246,251);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=9};Controls.Add(layout);
            foreach(int height in new[]{65,42,44})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,55));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,25));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));layout.RowStyles.Add(new RowStyle(SizeType.Percent,45));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
            layout.Controls.Add(new Label{Dock=DockStyle.Fill,Text="两台电脑登录同一 Tailscale 账户并保持在线，即可互相发送文件。\n收到的文件由 Tailscale 自动保存到接收电脑的“下载”文件夹。无需先连接远程桌面。"},0,0);
            var row=new FlowLayoutPanel{Dock=DockStyle.Fill};row.Controls.Add(new Label{Text="接收电脑 IP / 设备名",AutoSize=true,Padding=new Padding(0,5,0,0)});
            target.Width=340;target.Text=host??"";row.Controls.Add(target);row.Controls.Add(AsyncButton("查看可接收设备",async()=>await Targets()));layout.Controls.Add(row,0,1);
            edit.Dock=DockStyle.Fill;edit.Controls.Add(MakeButton("添加文件",Add));edit.Controls.Add(MakeButton("移除所选",()=>{foreach(var item in files.SelectedItems.Cast<string>().ToArray())files.Items.Remove(item);}));edit.Controls.Add(MakeButton("清空",()=>files.Items.Clear()));edit.Controls.Add(new Label{Text="也可以把多个文件拖入下方列表",AutoSize=true,Padding=new Padding(0,5,0,0)});layout.Controls.Add(edit,0,2);
            files.Dock=DockStyle.Fill;files.HorizontalScrollbar=true;files.SelectionMode=SelectionMode.MultiExtended;files.AllowDrop=true;
            files.DragEnter+=(s,e)=>{e.Effect=!active&&e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;};
            files.DragDrop+=(s,e)=>{if(!active)AddPaths((string[])e.Data.GetData(DataFormats.FileDrop));};layout.Controls.Add(files,0,3);
            row=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,6,0,0)};send.Text="发送文件";send.AutoSize=true;send.Click+=async(s,e)=>await Send();row.Controls.Add(send);
            cancel.Text="取消传输";cancel.Enabled=false;cancel.AutoSize=true;cancel.Click+=(s,e)=>{if(cancellation!=null)cancellation.Cancel();};row.Controls.Add(cancel);
            row.Controls.Add(MakeButton("打开本机接收文件夹",()=>{string folder=FileTransfer.DownloadDirectory();Directory.CreateDirectory(folder);SystemTools.Open(folder);}));layout.Controls.Add(row,0,4);
            progress.Dock=DockStyle.Fill;layout.Controls.Add(progress,0,5);state.Text="尚未发送。进度按已完成文件数显示。";state.Dock=DockStyle.Fill;layout.Controls.Add(state,0,6);
            log.Dock=DockStyle.Fill;log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Vertical;log.BackColor=Color.White;layout.Controls.Add(log,0,7);
            row=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,6,0,0)};row.Controls.Add(MakeButton("启用文件互传",()=>SystemTools.Open("https://login.tailscale.com/admin/settings/general")));row.Controls.Add(MakeButton("复制传输日志",()=>Clipboard.SetText(log.Text.Length==0?state.Text:log.Text)));row.Controls.Add(MakeButton("关闭",()=>Close()));layout.Controls.Add(row,0,8);
            Append("首次使用请在 Tailscale 管理页面启用 Send Files；此功能适用于同一账户的个人设备，不支持标记设备或其他用户的设备。");
            Append("发送文件夹时请先压缩为 ZIP。传输与远程画面共用带宽，大文件可能影响远控流畅度。");
            FormClosing+=(s,e)=>{if(active){e.Cancel=true;if(cancellation!=null)cancellation.Cancel();state.Text="正在取消，传输停止后可关闭窗口。";}};
        }
        Button MakeButton(string text,Action action){var b=new Button{Text=text,AutoSize=true,Height=32,Margin=new Padding(0,0,8,0)};b.Click+=(s,e)=>{try{action();}catch(Exception ex){Append(ex.Message);}};return b;}
        Button AsyncButton(string text,Func<Task> action){return MakeButton(text,(Action)(async()=>{try{await action();}catch(Exception ex){Append(ex.Message);}}));}
        void Append(string message){if(!IsDisposed)log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine);}
        void Add(){using(var dialog=new OpenFileDialog{Multiselect=true,Title="选择要发送的文件"}){if(dialog.ShowDialog(this)==DialogResult.OK)AddPaths(dialog.FileNames);}}
        void AddPaths(string[] paths){foreach(string path in paths){if(!File.Exists(path)){Append("未添加："+path+"。仅支持文件，文件夹请先压缩。");continue;}string full=Path.GetFullPath(path);if(!files.Items.Cast<string>().Contains(full,StringComparer.OrdinalIgnoreCase))files.Items.Add(full);}}
        string Tailscale(){string exe=Core.Find("Tailscale",null);if(exe==null)throw new FileNotFoundException("未找到 Tailscale，请先在主窗口安装必要组件并登录。");return exe;}
        async Task Targets(){string exe=Tailscale();Append("正在查询可接收设备…");string result=await SystemTools.Run(exe,"file cp --targets",10000);Append(String.IsNullOrWhiteSpace(result)?"没有可接收设备。请检查对方在线、同一账户以及 Send Files 设置。":result);}
        async Task Send(){
            if(active)return;
            int completed=0,total=files.Items.Count;
            try {
                string exe=Tailscale(),host=Core.ValidateHost(target.Text);string[] queue=files.Items.Cast<string>().ToArray();
                if(queue.Length==0)throw new ArgumentException("请先添加文件，也可以直接拖入文件列表。");
                active=true;edit.Enabled=false;target.ReadOnly=true;send.Enabled=false;cancel.Enabled=true;
                cancellation=new CancellationTokenSource();progress.Maximum=total;progress.Value=0;
                var updates=new Progress<TransferProgress>(p=>{completed=p.Completed;progress.Value=p.Completed;state.Text=p.Message+"："+p.File+"；已完成 "+p.Completed+" / "+p.Total+" 个文件";if(p.Message=="发送完成")Append(p.File+"：发送完成。");});
                Append("开始向 "+host+" 发送 "+total+" 个文件。对方请在“下载”文件夹中查看。");
                await FileTransfer.Send(exe,host,queue,updates,cancellation.Token);
                state.Text="全部发送完成："+total+" 个文件。";Append(state.Text);
            } catch(OperationCanceledException){state.Text="已取消；此前已完成 "+completed+" / "+total+" 个文件。";Append(state.Text+" 对方已收到的文件会保留，未完成的文件可重试。");}
            catch(Exception ex){state.Text="发送未完成；此前已完成 "+completed+" / "+total+" 个文件。";Append(ex.Message);}
            finally{active=false;edit.Enabled=true;target.ReadOnly=false;send.Enabled=true;cancel.Enabled=false;if(cancellation!=null){cancellation.Dispose();cancellation=null;}}
        }
    }
}
