using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TwinLink {
    public class SessionFilesForm : Form {
        readonly TextBox peer=new TextBox(),secret=new TextBox(),output=new TextBox();
        readonly Button toggle=new Button();
        readonly Label state=new Label();
        readonly CheckBox copy=new CheckBox(),drag=new CheckBox();
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer{Interval=250};
        readonly System.Windows.Forms.Timer dragTimer=new System.Windows.Forms.Timer{Interval=250};
        readonly NativeFileDrop dropTarget;
        string dropError;
        readonly NotifyIcon notification;
        readonly bool ownsNotification;
        readonly HashSet<string> visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase),seen=new HashSet<string>();
        readonly Dictionary<string,string> dropFolders=new Dictionary<string,string>();
        readonly Dictionary<string,int> retries=new Dictionary<string,int>();
        CancellationTokenSource lifetime;
        FileStream sessionLock;
        byte[] key;
        string self,remote,tailscale,inbox;
        uint clipboardSequence;
        bool enabled,polling,sending,starting,closing;
        bool terminating,outgoing,streamActive;
        public event Action StopRequested;
        string sessionPeer;
        public bool IsEnabled {get{return enabled;}}
        public void SetLive(bool live){if(live&&!streamActive)clipboardSequence=BridgeDesktop.GetClipboardSequenceNumber();streamActive=live;if(!live)dropTarget.Detach();}
        public async Task StartAutomatic(string local,string other,string sessionKey,bool controller){peer.Text=other;secret.Text=sessionKey;outgoing=controller;sessionPeer=other;await Start(local);}
        public void CloseAutomatic(){terminating=true;Stop();Close();}
        public void BindSession(string host){sessionPeer=Core.ValidateHost(host);if(enabled&&sessionPeer!=remote)Log("当前 Moonlight 连接与助手对方不同，暂停本机自动发送；接收仍然有效。");}
        public SessionFilesForm(string host,NotifyIcon sharedNotification=null) {
            ownsNotification=sharedNotification==null;notification=sharedNotification??new NotifyIcon{Icon=SystemIcons.Information,Text="TwinLink 文件同步"};
            dropTarget=new NativeFileDrop(()=>CanReceiveDrop(),paths=>{BeginInvoke((Action)(async()=>{if(CanReceiveDrop())await Send(paths,"drop");}));});
            Text="TwinLink · 文件状态";Font=new Font("Microsoft YaHei UI",10);ClientSize=new Size(900,500);MinimumSize=new Size(860,470);StartPosition=FormStartPosition.CenterParent;BackColor=Color.FromArgb(243,246,251);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=4};Controls.Add(layout);
            foreach(int h in new[]{80,46,42})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.Controls.Add(new Label{Dock=DockStyle.Fill,Text="文件复制与拖拽会随远程连接自动启用，无需口令或单独开启。\nCtrl+C 复制文件，等待就绪通知后在对方 Ctrl+V；也可拖入远程画面。\n本窗口仅查看状态，关闭窗口不会停止远程会话的文件功能。"},0,0);
            var row=new FlowLayoutPanel{Dock=DockStyle.Fill};copy.Text="文件剪贴板";copy.Checked=true;copy.AutoSize=true;drag.Text="拖入远程画面";drag.Checked=true;drag.AutoSize=true;row.Controls.Add(copy);row.Controls.Add(drag);toggle.Text="停止本次文件同步";toggle.AutoSize=true;toggle.Click+=(s,e)=>{Stop();if(StopRequested!=null)StopRequested();};row.Controls.Add(toggle);row.Controls.Add(MakeButton("复制日志",()=>Clipboard.SetText(output.TextLength==0?state.Text:output.Text)));layout.Controls.Add(row,0,1);
            state.Dock=DockStyle.Fill;state.Text="等待远程连接，连接后自动准备文件功能。";layout.Controls.Add(state,0,2);
            output.Multiline=true;output.ReadOnly=true;output.ScrollBars=ScrollBars.Vertical;output.Dock=DockStyle.Fill;output.BackColor=Color.White;layout.Controls.Add(output,0,3);
            timer.Tick+=async(s,e)=>await Tick();
            dragTimer.Tick+=(s,e)=>UpdateDropTarget();
            FormClosing+=(s,e)=>{if(!terminating){e.Cancel=true;Hide();return;}if(sending||polling||starting){e.Cancel=true;closing=true;Stop();state.Text="正在停止，请等待当前文件处理结束…";}else Stop();};
            FormClosed+=(s,e)=>{timer.Dispose();dragTimer.Dispose();dropTarget.Dispose();if(ownsNotification)notification.Dispose();if(lifetime!=null)lifetime.Dispose();};
            Log("双方保持新版 TwinLink 运行即可。一次最多 256 个文件 / 2 GB，文件夹请先压缩。");
            Log("Ctrl+C 后请等待对端日志显示“剪贴板就绪”再 Ctrl+V。仅同步文件，不读取或同步文本；不会执行收到的文件。");
            var ensureHandle=Handle;
        }
        Button MakeButton(string text,Action action){var b=new Button{Text=text,AutoSize=true,Margin=new Padding(6,0,0,0)};b.Click+=(s,e)=>{try{action();}catch(Exception ex){Log(ex.Message);}};return b;}
        void Log(string message){if(!IsDisposed){output.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine);if(notification.Visible&&(message.StartsWith("对端确认：")||message.StartsWith("剪贴板就绪：")))notification.ShowBalloonTip(5000,"TwinLink 文件操作就绪",message,ToolTipIcon.Info);}}
        public static string PeerIp(string input){IPAddress ip;if(!IPAddress.TryParse(input.Trim(),out ip)||ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork||ip.GetAddressBytes()[0]!=100||ip.GetAddressBytes()[1]<64||ip.GetAddressBytes()[1]>127)throw new ArgumentException("请填写对方的 Tailscale IPv4（100.64–100.127 开头），不要填本机地址。");return ip.ToString();}
        async Task Start(string automaticLocal){
            if(sending||polling)throw new InvalidOperationException("请等待上一批文件处理停止后再启用。");
            toggle.Enabled=false;starting=true;
            try {
                remote=PeerIp(peer.Text);key=BridgePacket.Key(secret.Text);tailscale=Core.Find("Tailscale",null);if(tailscale==null)throw new FileNotFoundException("请先安装并登录 Tailscale。");
                string detected=automaticLocal;if(String.IsNullOrEmpty(detected))detected=await SystemTools.Run(tailscale,"ip -4",6000);if(closing)return;
                self=PeerIp(detected.Trim());if(self==remote)throw new ArgumentException("对方地址不能是本机地址。");
                Directory.CreateDirectory(Core.Home);sessionLock=new FileStream(Path.Combine(Core.Home,"session-files.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
                inbox=FileTransfer.DownloadDirectory();Directory.CreateDirectory(inbox);visited.Clear();seen.Clear();dropFolders.Clear();retries.Clear();
                foreach(string path in Directory.GetFiles(inbox,"TwinLink-*.tlbridge"))visited.Add(path);
                clipboardSequence=BridgeDesktop.GetClipboardSequenceNumber();if(lifetime!=null)lifetime.Dispose();lifetime=new CancellationTokenSource();enabled=true;notification.Visible=true;timer.Start();dragTimer.Start();peer.ReadOnly=secret.ReadOnly=true;toggle.Text="停止本次文件同步";
                state.Text="已自动准备："+self+" ↔ "+remote+"；随远程会话开始和结束。";Log(state.Text);
            } finally {starting=false;toggle.Enabled=true;if(closing)BeginInvoke((Action)(()=>Close()));}
        }
        void Stop(){enabled=false;streamActive=false;timer.Stop();dragTimer.Stop();dropTarget.Detach();if(ownsNotification)notification.Visible=false;if(lifetime!=null)lifetime.Cancel();if(sessionLock!=null){sessionLock.Dispose();sessionLock=null;}toggle.Enabled=false;state.Text="本次文件同步已停止，重新连接远程桌面后会自动恢复。";}
        async Task Tick(){
            if(!enabled||!streamActive||polling)return;polling=true;
            try {
                bool bound=!outgoing||(sessionPeer==remote&&BridgeDesktop.HasStreamWindow());
                foreach(string path in Directory.GetFiles(inbox,"TwinLink-*.tlbridge").Where(p=>!visited.Contains(p)).OrderBy(p=>Path.GetFileName(p).Contains("-begin-drop-")?0:1).Take(128)) {
                    if(!enabled)break;if(visited.Contains(path))continue;
                    try{await Receive(path);visited.Add(path);}
                    catch(OperationCanceledException){break;}
                    catch(Exception ex){int n;retries.TryGetValue(path,out n);retries[path]=++n;if(n>=4){visited.Add(path);Log("未处理文件包："+ex.Message);}}
                }
                if(enabled&&copy.Checked&&!sending&&bound) {
                    uint sequence=BridgeDesktop.GetClipboardSequenceNumber();if(sequence!=clipboardSequence){
                        var data=Clipboard.GetDataObject();
                        if(data!=null&&data.GetDataPresent(DataFormats.FileDrop)&&IsCopy(data)) {
                            string[] paths=(data.GetData(DataFormats.FileDrop) as string[])??new string[0];
                            string cache=Path.GetFullPath(Path.Combine(Core.Home,"session-cache"))+Path.DirectorySeparatorChar;
                            clipboardSequence=sequence;
                            if(paths.Length>0&&!paths.Any(p=>Path.GetFullPath(p).StartsWith(cache,StringComparison.OrdinalIgnoreCase)))await Send(paths,"clipboard");
                        }else clipboardSequence=sequence;
                    }
                }
            }catch(Exception ex){Log("助手检测："+ex.Message);}finally{polling=false;if(!enabled&&!sending)toggle.Enabled=true;if(closing&&!sending)Close();}
        }
        public static bool IsCopy(IDataObject data){if(data==null)return false;var effect=data.GetData("Preferred DropEffect") as MemoryStream;if(effect==null)return true;byte[] bytes=effect.ToArray();return bytes.Length<4||(BitConverter.ToInt32(bytes,0)&2)==0;}
        bool CanReceiveDrop(){return enabled&&streamActive&&outgoing&&drag.Checked&&!sending&&sessionPeer==remote;}
        void UpdateDropTarget(){
            try{dropTarget.Attach(CanReceiveDrop()?BridgeDesktop.StreamWindowHandle():IntPtr.Zero);dropError=null;}
            catch(Exception ex){if(dropError!=ex.Message){Log(ex.Message);dropError=ex.Message;}}
        }
        async Task SendPacket(BridgeMessage message,string[] paths,CancellationToken token){
            string dir=Path.Combine(Core.Home,"session-outbox");Directory.CreateDirectory(dir);
            string packet=Path.Combine(dir,"TwinLink-"+message.Id+"-"+message.Action+"-"+Guid.NewGuid().ToString("N")+".tlbridge");
            byte[] activeKey=key;string executable=tailscale,destination=remote;
            try {
                await Task.Run(()=>{token.ThrowIfCancellationRequested();BridgePacket.Write(packet,message,paths,activeKey);token.ThrowIfCancellationRequested();});
                var result=await FileTransfer.Run(executable,FileTransfer.SendArgs(destination,packet),token);if(result.ExitCode!=0)throw new IOException("文件请求发送失败："+result.Output);
            }finally{try{File.Delete(packet);}catch{}}
        }
        async Task Send(string[] paths,string action){
            if(sending){Log("正在传输，请等待完成后重试复制或拖拽。");return;}if(!enabled)return;sending=true;dropTarget.Detach();
            try {
                var token=lifetime.Token;string id=Guid.NewGuid().ToString("N");
                if(paths.Any(p=>!File.Exists(p)))throw new ArgumentException("仅支持文件，文件夹请先压缩。");
                if(action=="drop")await SendPacket(BridgePacket.Message(id,self,remote,"begin-drop"),new string[0],token);
                Log("正在发送 "+paths.Length+" 个文件，等待对端确认…");
                await SendPacket(BridgePacket.Message(id,self,remote,action),paths,token);
                Log("文件包已送达，等待对端准备剪贴板 / 复制结果。");
            }catch(OperationCanceledException){Log("本次文件发送已取消。");}catch(Exception ex){Log("文件发送未完成："+ex.Message);}finally{sending=false;if(!enabled&&!polling)toggle.Enabled=true;if(closing&&!polling)Close();}
        }
        async Task Receive(string packet){
            var token=lifetime.Token;byte[] activeKey=key;string expected=remote,local=self;
            var message=await Task.Run(()=>BridgePacket.Read(packet,expected,local,activeKey,null));token.ThrowIfCancellationRequested();
            string identity=message.Id+"-"+message.Action;if(seen.Contains(identity))return;
            if(message.Action=="ack"||message.Action=="error"){Log((message.Action=="ack"?"对端确认：":"对端未完成：")+message.Detail);seen.Add(identity);return;}
            if(message.Action=="begin-drop") {
                try{dropFolders[message.Id]=BridgeDesktop.CurrentFolder();Log("已锁定拖拽目标文件夹："+dropFolders[message.Id]);}
                catch(Exception ex){dropFolders[message.Id]="";Log(ex.Message);}
                seen.Add(identity);return;
            }
            string cacheRoot=Path.Combine(Core.Home,"session-cache");Directory.CreateDirectory(cacheRoot);
            string done=Path.Combine(cacheRoot,message.Id+".done");if(File.Exists(done))return;
            var reply=BridgePacket.Message(message.Id,self,remote,"ack");
            try {
                string target=null;if(message.Action=="drop"&&(!dropFolders.TryGetValue(message.Id,out target)||String.IsNullOrEmpty(target)))throw new InvalidOperationException("没有有效的拖拽目标，请先打开对方文件夹或单击桌面，再重试。");
                string directory=Path.Combine(cacheRoot,message.Id+"-"+Guid.NewGuid().ToString("N"));
                await Task.Run(()=>BridgePacket.Read(packet,expected,local,activeKey,directory));token.ThrowIfCancellationRequested();
                string[] files=message.Files.Select(f=>Path.Combine(directory,f.Name)).ToArray();
                if(message.Action=="clipboard") {
                    var list=new StringCollection();list.AddRange(files);Clipboard.SetFileDropList(list);clipboardSequence=BridgeDesktop.GetClipboardSequenceNumber();
                    reply.Detail="剪贴板就绪："+files.Length+" 个文件，现在可以在目标文件夹按 Ctrl+V。";
                }else {
                    foreach(string file in files)CopyWithoutOverwrite(file,target);
                    reply.Detail="已复制 "+files.Length+" 个文件到 "+target;
                }
                File.WriteAllText(done,DateTime.UtcNow.ToString("o"));seen.Add(identity);Log(reply.Detail);
            }catch(Exception ex){reply.Action="error";reply.Detail=ex.Message;seen.Add(identity);Log("接收未完成："+ex.Message);}
            token.ThrowIfCancellationRequested();await SendPacket(reply,new string[0],token);
        }
        public static string CopyWithoutOverwrite(string file,string target){
            if(!Directory.Exists(target))throw new DirectoryNotFoundException("目标文件夹不存在。");
            string name=Path.GetFileNameWithoutExtension(file),extension=Path.GetExtension(file);
            for(int i=0;i<10000;i++){string path=Path.Combine(target,name+(i==0?"":" ("+i+")")+extension);try{File.Copy(file,path,false);return path;}catch(IOException){if(!File.Exists(path))throw;}}
            throw new IOException("同名文件过多，无法生成新文件名。");
        }
    }
}
