using System;
using System.Drawing;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TwinLink {
    public class MainForm : Form {
        readonly Settings settings;
        readonly TextBox host = new TextBox(), app = new TextBox(), logs = new TextBox();
        readonly ComboBox fps = new ComboBox(), codec = new ComboBox();
        readonly NumericUpDown bitrate = new NumericUpDown();
        readonly CheckBox fullscreen = new CheckBox(), overlay = new CheckBox();
        readonly Label address = new Label(), status = new Label();
        readonly Color ink = Color.FromArgb(30,44,63), blue = Color.FromArgb(38,93,220);
        bool busy;
        SessionFilesForm sessionFiles;
        AutoFilesService automaticFiles;
        AutoFileConnection fileConnection;
        readonly System.Windows.Forms.Timer fileMonitor=new System.Windows.Forms.Timer{Interval=1000};
        string fileSession,filePeer,readyError;
        bool fileController,preparingFiles,monitoringFiles,sawStream,previousLive,shuttingDown;
        bool permissionAttempted,allowExit;
        readonly NotifyIcon tray=new NotifyIcon{Icon=SystemIcons.Application,Text="TwinLink"};
        int readyTicks;
        DateTime launchedStream,lastPulse;
        public MainForm(bool preview = false) {
            settings = Core.Load();
            settings.Overlay = false;
            foreach (var combo in new [] { fps, codec }) {
                combo.DrawMode = DrawMode.OwnerDrawFixed;
                combo.DrawItem += (sender, e) => {
                    e.DrawBackground();
                    var current = (ComboBox)sender;
                    if(e.Index >= 0) TextRenderer.DrawText(e.Graphics, current.Items[e.Index].ToString(), current.Font, e.Bounds, current.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                    e.DrawFocusRectangle();
                };
            }
            Text = "TwinLink 1.3.1 预览 · 双向远程桌面"; Font = new Font("Microsoft YaHei UI", 10);
            BackColor = Color.FromArgb(243,246,251); ForeColor = ink;
            ClientSize = new Size(1030, 790); MinimumSize = new Size(1000, 790);
            AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,76)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,76));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,410)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,30)); Controls.Add(root);
            var title = new Label { Text = "TwinLink    双向远程桌面\n1080p  /  144 FPS 目标  /  跨网络连接", Dock=DockStyle.Fill, Font=new Font(Font.FontFamily,16,FontStyle.Bold) }; root.Controls.Add(title,0,0);
            var bar = new FlowLayoutPanel { Dock=DockStyle.Fill, BackColor=Color.White, Padding=new Padding(10) };
            bar.Controls.Add(Button("安装三个必要组件", () => Install(), true));
            bar.Controls.Add(Button("查看准备状态", async () => await RefreshState(), false));
            bar.Controls.Add(Button("文件互传", () => { using(var transfer=new FileTransferForm(host.Text.Trim())) transfer.ShowDialog(this); }, true));
            bar.Controls.Add(Button("文件状态", () => OpenSessionFiles(), false));
            bar.Controls.Add(Button("使用说明", () => SystemTools.Open(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"README.md")), false));
            bar.Controls.Add(Button("选择 Moonlight 路径", () => SelectMoonlight(), false)); root.Controls.Add(bar,0,1);
            var cards = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, Padding=new Padding(0,12,0,12) }; cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,46)); cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,54)); root.Controls.Add(cards,0,2);
            var local = Card("1  让另一台电脑控制本机"); cards.Controls.Add(local,0,0);
            address.Text="本机地址：请先登录跨网账户"; address.AutoSize=true; address.MaximumSize=new Size(400,0); local.Controls.Add(address);
            local.Controls.Add(Button("登录跨网账户（Tailscale）", () => Login(), true));
            local.Controls.Add(Button("复制本机跨网 IP", () => { if (address.Tag == null) throw new Exception("请先登录账户并刷新状态。"); Clipboard.SetText((string)address.Tag); Log("本机 IP 已复制。"); }, false));
            local.Controls.Add(Button("打开本机设置 / 输入配对 PIN", () => SystemTools.Open("https://localhost:47990"), false));
            local.Controls.Add(Button("打开 Windows 显示设置", () => SystemTools.Open("ms-settings:display-advanced"), false));
            local.Controls.Add(Note("两台电脑都安装三个组件，并登录同一 Tailscale 网络。\n在本机 Sunshine 设置账户密码；对方配对时，在本机填入 PIN。\n被控端需保持开机并运行 Sunshine 服务。"));
            var remote = Card("2  从这里控制另一台电脑"); cards.Controls.Add(remote,1,0);
            var row = new FlowLayoutPanel { AutoSize=true, Width=480 }; row.Controls.Add(new Label { Text="对方 IP / 设备名", AutoSize=true, Padding=new Padding(0,6,0,0) }); host.Width=295; host.Text=settings.Host; row.Controls.Add(host); remote.Controls.Add(row);
            row = new FlowLayoutPanel { AutoSize=true, Width=480 }; row.Controls.Add(new Label { Text="应用名称", AutoSize=true, Padding=new Padding(0,6,0,0) }); app.Width=170; app.Text=settings.App; row.Controls.Add(app); row.Controls.Add(Button("首次配对", () => Pair(), false)); remote.Controls.Add(row);
            row = new FlowLayoutPanel { AutoSize=true, Width=480 }; fps.FlatStyle=FlatStyle.Flat; fps.DropDownStyle=ComboBoxStyle.DropDownList; fps.Width=75; fps.Items.AddRange(new object[]{60,120,144}); fps.SelectedItem=settings.Fps; if(fps.SelectedIndex<0)fps.SelectedItem=144;
            row.Controls.Add(new Label {Text="1080p  帧率",AutoSize=true,Padding=new Padding(0,6,0,0)}); row.Controls.Add(fps);
            bitrate.Minimum=10;bitrate.Maximum=150;bitrate.Value=Math.Max(10,Math.Min(150,settings.Bitrate));bitrate.Width=65; row.Controls.Add(new Label{Text="码率 Mbps",AutoSize=true,Padding=new Padding(10,6,0,0)});row.Controls.Add(bitrate); remote.Controls.Add(row);
            row = new FlowLayoutPanel { AutoSize=true, Width=480 }; codec.FlatStyle=FlatStyle.Flat;codec.DropDownStyle=ComboBoxStyle.DropDownList;codec.Width=100;codec.Items.AddRange(new object[]{"auto","H.264","HEVC","AV1"});codec.SelectedItem=settings.Codec;if(codec.SelectedIndex<0)codec.SelectedIndex=0;row.Controls.Add(codec);
            fullscreen.Text="全屏";fullscreen.AutoSize=true;fullscreen.Checked=settings.Fullscreen;overlay.Text="显示性能数据";overlay.AutoSize=true;overlay.Checked=settings.Overlay;row.Controls.Add(fullscreen);row.Controls.Add(overlay);remote.Controls.Add(row);
            row = new FlowLayoutPanel { AutoSize=true, Width=480 };row.Controls.Add(Button("连接远程桌面", async () => await Connect(), true));row.Controls.Add(Button("检测跨网链路", async () => await Diagnose(), false));remote.Controls.Add(row);
            remote.Controls.Add(Note("首次配对：看见 PIN 后，在对方的 Sunshine 页面输入。\n退出串流：Ctrl + Alt + Shift + Q。\n目标为 1920×1080 / 144 FPS，性能以叠加数据显示为准。"));
            logs.Multiline=true;logs.ReadOnly=true;logs.ScrollBars=ScrollBars.Vertical;logs.Dock=DockStyle.Fill;logs.BackColor=Color.White;logs.BorderStyle=BorderStyle.FixedSingle;logs.Font=new Font("Microsoft YaHei UI",9); root.Controls.Add(logs,0,3);
            status.Text="文件功能随远程连接自动启用；两端请保持新版 TwinLink 运行。";status.Dock=DockStyle.Fill;status.Font=new Font(Font.FontFamily,9);root.Controls.Add(status,0,4);
            fileMonitor.Tick+=async(s,e)=>await MonitorFiles();
            if (!preview) Shown += async (s,e) => {automaticFiles=new AutoFilesService(System.Threading.SynchronizationContext.Current,OnFileControl);fileMonitor.Start();await ReadyFiles();await GuardAsync(RefreshState);};
            if(!preview){var menu=new ContextMenuStrip();menu.Items.Add("打开 TwinLink",null,(s,e)=>{Show();WindowState=FormWindowState.Normal;Activate();});menu.Items.Add("退出 TwinLink",null,(s,e)=>{allowExit=true;Close();});tray.ContextMenuStrip=menu;tray.Visible=true;tray.DoubleClick+=(s,e)=>{Show();WindowState=FormWindowState.Normal;Activate();};FormClosing+=(s,e)=>{if(!allowExit&&e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();tray.ShowBalloonTip(3000,"TwinLink 正在后台运行","文件功能会随远程连接自动工作。右键托盘图标可完全退出。",ToolTipIcon.Info);}};}
            FormClosing += (s,e) => { try { Core.Save(Read(false)); } catch {} };
            FormClosing += (s,e) => {if(e.Cancel)return;shuttingDown=true;fileMonitor.Stop();if(fileConnection!=null){fileConnection.Dispose();fileConnection=null;}if(automaticFiles!=null)automaticFiles.Dispose();if(sessionFiles!=null&&!sessionFiles.IsDisposed){sessionFiles.CloseAutomatic();if(!sessionFiles.IsDisposed)e.Cancel=true;}};
            FormClosed+=(s,e)=>{fileMonitor.Dispose();tray.Dispose();};
        }
        FlowLayoutPanel Card(string heading) { var p=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,BackColor=Color.White,Padding=new Padding(14),Margin=new Padding(0,0,12,0)};p.Controls.Add(new Label{Text=heading,Font=new Font(Font.FontFamily,12,FontStyle.Bold),AutoSize=true,Margin=new Padding(0,0,0,15)});return p; }
        Label Note(string text) {return new Label{Text=text,AutoSize=true,MaximumSize=new Size(445,0),Font=new Font(Font.FontFamily,9),ForeColor=Color.FromArgb(85,101,123),Margin=new Padding(0,12,0,0)};}
        Button Button(string text, Action click, bool primary) {var b=new Button {Text=text,AutoSize=true,Height=36,Padding=new Padding(8,4,8,4),FlatStyle=FlatStyle.Flat,BackColor=primary?blue:Color.FromArgb(235,240,248),ForeColor=primary?Color.White:ink,Cursor=Cursors.Hand,Margin=new Padding(0,0,8,8)};b.FlatAppearance.BorderSize=0;b.Click+=(s,e)=>{try{click();}catch(Exception ex){Error(ex);}};return b;}
        Button Button(string text, Func<Task> click, bool primary) {return Button(text,(Action)(async () => await GuardAsync(click)),primary);}
        void Error(Exception ex){Log("未完成："+ex.Message);MessageBox.Show(this,ex.Message,"TwinLink",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        async Task GuardAsync(Func<Task> action){if(busy)return;busy=true;UseWaitCursor=true;try{await action();}catch(Exception ex){Error(ex);}finally{UseWaitCursor=false;busy=false;}}
        void Log(string message){logs.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine);}
        Settings Read(bool validate) {settings.Host=host.Text.Trim();settings.App=app.Text.Trim();settings.Fps=(int)fps.SelectedItem;settings.Bitrate=(int)bitrate.Value;settings.Codec=(string)codec.SelectedItem;settings.Fullscreen=fullscreen.Checked;settings.Overlay=overlay.Checked;if(validate)Core.StreamArgs(settings);return settings;}
        string Require(string name){string p=Core.Find(name,name=="Moonlight"?settings.MoonlightPath:null);if(p==null)throw new Exception("尚未找到 "+name+"。请先安装组件；使用便携版 Moonlight 时可手动选择路径。");return p;}
        void Install(){using(var installer=new InstallerForm()){installer.ShowDialog(this);}Log("安装向导已关闭。请点击“查看准备状态”检查组件。");}
        SessionFilesForm NewFileStatus(){var form=new SessionFilesForm(host.Text.Trim(),tray.Visible?tray:null);form.StopRequested+=()=>EndFiles();form.FormClosed+=(s,e)=>{if(shuttingDown&&allowExit&&!IsDisposed)BeginInvoke((Action)(()=>Close()));};return form;}
        void OpenSessionFiles(){if(sessionFiles==null||sessionFiles.IsDisposed)sessionFiles=NewFileStatus();if(!sessionFiles.Visible)sessionFiles.Show(this);else sessionFiles.Activate();}
        void Login(){string p=Require("Tailscale");SystemTools.Launch(Path.Combine(Path.GetDirectoryName(p),"tailscale-ipn.exe"),"");Log("请在 Tailscale 托盘菜单中登录，与另一台使用相同账户或同一网络。");}
        void SelectMoonlight(){using(var d=new OpenFileDialog{Filter="Moonlight (Moonlight.exe)|Moonlight.exe",Title="选择 Moonlight.exe"}){if(d.ShowDialog(this)==DialogResult.OK){settings.MoonlightPath=d.FileName;Core.Save(Read(false));Log("已保存 Moonlight 路径。");}}}
        void Pair(){string h=Core.ValidateHost(host.Text);Core.Save(Read(false));SystemTools.Launch(Require("Moonlight"),"pair "+Core.Quote(h));Log("配对窗口已启动。在对方 Sunshine 页面输入它显示的 PIN。配对成功后再连接。");}
        async Task Connect(){
            if(BridgeDesktop.HasStreamWindow())throw new InvalidOperationException("请先退出当前远程画面，再从这里开始新的连接。文件功能会自动随新连接启用。");
            Settings s=Read(true);string executable=Require("Moonlight"),args=Core.StreamArgs(s);if(s.Fullscreen)args=args.Replace("--display-mode fullscreen","--display-mode borderless");Core.Save(s);
            preparingFiles=true;
            try{
                EndFiles();
                if(automaticFiles==null)automaticFiles=new AutoFilesService(System.Threading.SynchronizationContext.Current,OnFileControl);
                try{
                    fileConnection=await automaticFiles.Connect(s.Host);fileSession=fileConnection.Initial.Session;filePeer=fileConnection.Peer;fileController=true;sawStream=previousLive=false;launchedStream=DateTime.UtcNow;lastPulse=DateTime.MinValue;
                    await ActivateFiles(fileConnection.Self,filePeer,fileConnection.Initial.Key,true);
                    status.Text="文件功能已自动准备，远程画面建立后直接拖拽或复制粘贴。";Log(status.Text);
                }catch(Exception ex){EndFiles();status.Text="远程画面可继续连接；文件功能未就绪，请查看日志。";Log("文件功能未就绪："+ex.Message);}
                try{SystemTools.Launch(executable,args);}catch{EndFiles();throw;}
                Log("已请求连接 "+s.Host+"：1080p / "+s.Fps+" FPS / "+s.Bitrate+" Mbps。连接是否成功及实测性能请查看 Moonlight。");
            }finally{preparingFiles=false;}
        }
        async Task ReadyFiles(){if(shuttingDown||automaticFiles==null)return;try{
                if(!permissionAttempted&&Core.Find("Tailscale",null)!=null){permissionAttempted=true;if(!NetworkPermissions.IsConfigured()){Log("首次准备文件连接的网络权限，请留意 Windows 管理员确认；以后同一路径无需重复配置。");try{if(await NetworkPermissions.InstallRule()!=0)Log("网络权限未配置成功；若文件连接失败，请允许本程序通过 Windows 防火墙。");}catch(Exception ex){Log("网络权限未配置："+ex.Message);}}}
                if(shuttingDown)return;await automaticFiles.Ready();if(fileSession==null)status.Text="文件功能已待命，远程连接时自动启用；无需口令或额外开启。";readyError=null;
            }catch(Exception ex){if(readyError!=ex.Message){Log("文件接收待命未就绪："+ex.Message);readyError=ex.Message;}}}
        async Task ActivateFiles(string local,string remote,string key,bool controller){
            if(sessionFiles!=null&&!sessionFiles.IsDisposed){sessionFiles.CloseAutomatic();for(int i=0;i<100&&!sessionFiles.IsDisposed;i++)await Task.Delay(50);if(!sessionFiles.IsDisposed)throw new InvalidOperationException("正在结束上一项文件处理，请稍后重连。");}
            sessionFiles=NewFileStatus();await sessionFiles.StartAutomatic(local,remote,key,controller);
        }
        async Task<FileControl> OnFileControl(string source,FileControl request){
            if(shuttingDown)return new FileControl{Error="TwinLink 正在退出。"};
            if(request.Action=="begin"){
                if(preparingFiles||fileSession!=null)return new FileControl{Error="此电脑已有远程文件会话，请结束原会话后重连。"};
                string key=BridgePacket.NewKey();fileSession=request.Session;filePeer=source;fileController=false;
                try{await ActivateFiles(automaticFiles.LocalIP,source,key,false);status.Text="文件功能已自动准备，等待远程画面就绪。";return new FileControl{Accepted=true,Key=key};}
                catch{EndFiles();throw;}
            }
            if(fileSession!=request.Session||filePeer!=source||fileController)return new FileControl{Error="文件会话已结束。"};
            if(request.Action=="end"){EndFiles();return new FileControl{Accepted=true};}
            if(request.Action=="pulse"&&sessionFiles!=null&&sessionFiles.IsEnabled){sessionFiles.SetLive(request.Live);status.Text=request.Live?"文件功能已随远程连接自动启用。":"正在等待远程画面，尚未同步剪贴板。";return new FileControl{Accepted=true};}
            return new FileControl{Error="文件同步已停止，请重新连接。"};
        }
        void EndFiles(){
            fileSession=null;filePeer=null;fileController=false;sawStream=previousLive=false;
            if(fileConnection!=null){fileConnection.Dispose();fileConnection=null;}
            if(sessionFiles!=null&&!sessionFiles.IsDisposed)sessionFiles.CloseAutomatic();
            if(!shuttingDown)status.Text="文件同步已停止，下次远程连接时自动恢复。";

        }
        async Task MonitorFiles(){
            if(monitoringFiles||preparingFiles||shuttingDown)return;monitoringFiles=true;
            try{
                if(fileConnection!=null&&fileController){
                    bool live=BridgeDesktop.HasStreamWindow();
                    if(live)sawStream=true;
                    if((sawStream&&!live)||(!sawStream&&(DateTime.UtcNow-launchedStream).TotalSeconds>45)){EndFiles();return;}
                    if(sessionFiles!=null)sessionFiles.SetLive(live);
                    if(live!=previousLive||(DateTime.UtcNow-lastPulse).TotalSeconds>=4){await fileConnection.Pulse(live);lastPulse=DateTime.UtcNow;previousLive=live;if(live)status.Text="文件功能已自动启用：可直接拖拽，或 Ctrl+C / Ctrl+V。";}
                }else if(fileSession==null&&++readyTicks%15==0)await ReadyFiles();
            }catch(Exception ex){Log("文件会话已停止："+ex.Message);EndFiles();}finally{monitoringFiles=false;}
        }
        async Task RefreshState(){
            foreach(string name in new[]{"Sunshine","Moonlight","Tailscale"})Log(name+"："+(Core.Find(name,name=="Moonlight"?settings.MoonlightPath:null)??"尚未安装 / 未找到"));
            Log("本机 Sunshine 管理端口："+(await SystemTools.Port("127.0.0.1",47990)?"正在监听（首次需设置密码）":"未启动；安装后检查 Sunshine 服务"));
            string ts=Core.Find("Tailscale",null);address.Tag=null;address.Text="本机地址：尚未连接跨网网络";
            if(ts!=null){try{string ip=await SystemTools.Run(ts,"ip -4",6000);System.Net.IPAddress parsed;if(System.Net.IPAddress.TryParse(ip.Trim(),out parsed)){address.Tag=ip.Trim();address.Text="本机跨网 IP："+ip.Trim();Log("本机跨网 IP："+ip.Trim()+"。请确认 Tailscale 已登录并连接。");}}catch(Exception ex){Log("跨网网络未就绪："+ex.Message);}}
            try{using(var search=new ManagementObjectSearcher("SELECT Name, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController"))foreach(ManagementObject gpu in search.Get()){Log("显卡："+gpu["Name"]+"；显示模式："+gpu["CurrentHorizontalResolution"]+"×"+gpu["CurrentVerticalResolution"]+" @ "+gpu["CurrentRefreshRate"]+" Hz（系统报告，不代表编码性能）");}}catch{Log("无法读取显卡信息，请在 Windows 高级显示设置中确认刷新率。");}
        }
        async Task Diagnose(){string h=Core.ValidateHost(host.Text);Log("正在检测 "+h+" …");Log("Sunshine TCP 47989："+(await SystemTools.Port(h,47989)?"可达":"不可达：检查地址、服务、Tailscale 访问规则与防火墙"));Log("TCP 可达不代表 UDP 串流或 144 FPS 已通过。");string ts=Core.Find("Tailscale",null);if(ts!=null){try{Log(await SystemTools.Run(ts,"ping --c 3 --timeout 4s "+Core.Quote(h),18000));}catch(Exception ex){Log("Tailscale 检测："+ex.Message);}Log("输出出现 direct 或 via 公网端点表示直连；DERP/relay 表示中继。高帧率需优先建立直连，无法打洞时可自行部署近端中继后实测。");}}
    }
    static class Program {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread] static void Main(string[] args){if(args.Length==1&&args[0]=="--configure-file-network"){try{NetworkPermissions.Configure();}catch{Environment.ExitCode=1;}return;}SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);bool preview=args.Length==2&&args[0]=="--render-check";try{using(var form=new MainForm(preview)){if(preview){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();using(var bmp=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bmp,new Rectangle(0,0,bmp.Width,bmp.Height));bmp.Save(args[1]);}form.Close();return;}Application.Run(form);}}catch(Exception ex){if(preview){Environment.ExitCode=1;return;}MessageBox.Show(ex.ToString(),"TwinLink 启动失败");}}
    }
}
