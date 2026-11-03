using System;
using System.IO;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TwinLink {
    public class Tests {
        [DllImport("shell32.dll", SetLastError=true)] static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string cmd, out int count);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr p);
        static int count;
        static void Check(bool ok, string message) { count++; if(!ok)throw new Exception(message); }
        static void Reject(Action action) { bool failed=false;try{action();}catch(ArgumentException){failed=true;}Check(failed,"Invalid input accepted"); }
        [STAThread] public static int Main(string[] args) {
            if(args.Length==2&&args[0]=="--foreign-drop-window"){Application.EnableVisualStyles();using(var form=new Form{StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000),Text="TwinLink isolated drop test"}){form.Shown+=(s,e)=>File.WriteAllText(args[1],form.Handle.ToInt64().ToString());using(var timeout=new Timer{Interval=12000}){timeout.Tick+=(s,e)=>form.Close();timeout.Start();Application.Run(form);}}return 0;}
            if(args.Length==1&&args[0]=="--fake-transfer"){Console.WriteLine("transfer-stdout");Console.Error.WriteLine("transfer-stderr");return 7;}
            if(args.Length==1&&args[0]=="--slow-transfer"){System.Threading.Thread.Sleep(10000);return 0;}
            try {
                Check(Core.ValidateHost(" 100.66.12.8 ")=="100.66.12.8","Tailscale IPv4");
                Check(Core.ValidateHost("pc.example.ts.net")=="pc.example.ts.net","DNS");
                Check(Core.ValidateHost("fd7a:115c:a1e0::1")=="fd7a:115c:a1e0::1","IPv6");
                foreach(string h in new[]{"","https://pc","pc --fps 1","pc;calc","--option","999.999.1.1","a..b","a.-b","127.0.0.1:47989"})Reject(()=>Core.ValidateHost(h));
                foreach(string arg in new[]{"Desktop","a b","x\"y","tail\\","trailing ","C:\\a b\\", "a\\\"b"}) {
                    int n;IntPtr p=CommandLineToArgvW("test.exe "+Core.Quote(arg),out n);
                    try{Check(n==2&&Marshal.PtrToStringUni(Marshal.ReadIntPtr(p,IntPtr.Size))==arg,"Quoting roundtrip: "+arg);}finally{LocalFree(p);}
                }
                var s=new Settings{Host="100.66.12.8"};string cmd=Core.StreamArgs(s);
                Check(cmd.Contains("--fps 144")&&cmd.Contains("--resolution 1920x1080")&&cmd.Contains("--bitrate 50000"),"Requested quality");
                Check(cmd.Contains("--absolute-mouse")&&cmd.Contains("--video-decoder hardware"),"Input and hardware decoding");
                s.Codec="shell";Reject(()=>Core.StreamArgs(s));s.Codec="auto";s.Fps=0;Reject(()=>Core.StreamArgs(s));s.Fps=144;
                s.Bitrate=151;Reject(()=>Core.StreamArgs(s));s.Bitrate=50;
                Core.Save(s);Check(Core.Load().Host==s.Host&&Core.Load().Fps==144,"Persistence");
                File.WriteAllText(Path.Combine(Core.Home,"settings.json"),"{broken");Check(Core.Load().Fps==144,"Corrupted settings fallback");
                Application.EnableVisualStyles();using(var form=new MainForm(true)){form.CreateControl();Check(form.Text.Contains("TwinLink"),"Form construction");Check(CombosValid(form)==2,"Quality selectors show defaults");}
                CheckInstallerButton();
                CheckInstallerProcess();
                CheckFileTransfer();
                CheckFileTransferButton();
                CheckSessionPackets();
                CheckSessionWindow();
                CheckAutomaticIdentity();
                System.Threading.Tasks.Task.Run((Func<System.Threading.Tasks.Task>)CheckAutomaticChannel).GetAwaiter().GetResult();
                CheckOverlayDefaults();
                CheckForeignDropRegistration();
                CheckNativeDropPolicy();
                Console.WriteLine("PASS: "+count+" checks (address validation, native quoting, quality arguments, settings, form).");return 0;
            } catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
        }
        static void Fails(Action action,string reason){bool failed=false;try{action();}catch(Exception ex){if(ex is System.IO.InvalidDataException||ex is ArgumentException)failed=true;else throw;}Check(failed,reason);}
        static void CheckForeignDropRegistration(){
            string ready=Path.Combine(Core.Home,"drop-ready-"+Guid.NewGuid().ToString("N")+".txt");
            var info=new System.Diagnostics.ProcessStartInfo(Application.ExecutablePath,"--foreign-drop-window "+Core.Quote(ready)){UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden};
            using(var process=System.Diagnostics.Process.Start(info)){
                try{for(int i=0;i<100&&!File.Exists(ready);i++){Application.DoEvents();System.Threading.Thread.Sleep(30);}Check(File.Exists(ready),"Foreign process test HWND ready");IntPtr hwnd=new IntPtr(Int64.Parse(File.ReadAllText(ready)));
                    using(var target=new NativeFileDrop(()=>true,paths=>{})){target.Attach(hwnd);Check(target.Window==hwnd,"Native drop target registers on another process window");target.Detach();Check(target.Window==IntPtr.Zero,"Native target detaches cleanly");}
                }finally{if(!process.HasExited){process.Kill();process.WaitForExit(2000);}}
            }
        }
        static void CheckNativeDropPolicy(){
            string root=Path.Combine(Core.Home,"native-drop-tests");Directory.CreateDirectory(root);string file=Path.Combine(root,"中文 拖拽.txt");File.WriteAllText(file,"drop-test");
            var paths=new System.Collections.Specialized.StringCollection();paths.Add(file);var data=new DataObject();data.SetFileDropList(paths);
            var native=(System.Runtime.InteropServices.ComTypes.IDataObject)data;
            string[] decoded=NativeFileDrop.ReadFiles(native);Check(decoded!=null&&decoded.Length==1&&decoded[0]==file,"Native CF_HDROP reads real Unicode file list");
            var text=new DataObject();text.SetText("not a file");var nativeText=(System.Runtime.InteropServices.ComTypes.IDataObject)text;
            Check(NativeFileDrop.ReadFiles(nativeText)==null,"Text drag does not count as file transfer");
            bool active=true;int sent=0;string[] received=null;
            using(var window=new Form())using(var target=new NativeFileDrop(()=>active,files=>{sent++;received=files;})){
                target.Attach(window.Handle);var point=new DropPoint();uint effect=1;
                for(int i=0;i<100;i++){effect=1;target.DragOver(1,point,ref effect);if(effect!=0)throw new Exception("Left button manufactured a file drag");}
                Check(sent==0,"Holding mouse button without OLE file DragEnter never transfers");
                effect=1;target.Drop(native,0,point,ref effect);Check(effect==0&&sent==0,"Drop without active file drag is rejected");
                effect=1;target.DragEnter(nativeText,1,point,ref effect);Check(effect==0&&sent==0,"Non-file OLE drag is rejected");
                effect=2;target.DragEnter(native,1,point,ref effect);Check(effect==0,"Move-only file operation is not accepted as copy");
                effect=1;target.DragEnter(native,1,point,ref effect);Check(effect==1&&sent==0,"File hover accepts copy but does not send yet");
                effect=1;target.DragOver(1,point,ref effect);Check(effect==1&&sent==0,"Real file drag retains copy cursor without overlay");
                target.DragLeave();effect=1;target.Drop(native,0,point,ref effect);Check(effect==0&&sent==0,"Leaving target cancels pending file drop");
                effect=1;target.DragEnter(native,1,point,ref effect);target.Drop(native,0,point,ref effect);Check(effect==1&&sent==1&&received[0]==file,"Only completed valid native file drop sends once");
                effect=1;target.Drop(native,0,point,ref effect);Check(effect==0&&sent==1,"Repeated drop cannot reuse old drag state");
                effect=1;target.DragEnter(native,1,point,ref effect);active=false;target.Drop(native,0,point,ref effect);Check(effect==0&&sent==1,"Ending remote session rejects in-flight drop");active=true;
                using(var second=new NativeFileDrop(()=>true,files=>{})){bool rejected=false;try{second.Attach(window.Handle);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Existing native drop registration is preserved");}
                using(var third=new NativeFileDrop(()=>true,files=>{})){bool rejected=false;try{third.Attach(window.Handle);}catch(InvalidOperationException){rejected=true;}Check(rejected,"Failed secondary registration does not revoke original target");}
                target.Detach();effect=1;target.DragEnter(native,1,point,ref effect);Check(effect==0&&sent==1,"Detached window cannot accept files");
            }
        }
        static void CheckOverlayDefaults(){
            var settings=new Settings{Host="100.64.1.2"};Check(!settings.Overlay&&Core.StreamArgs(settings).Contains("--no-performance-overlay"),"Statistics disabled by default");
            settings.Overlay=true;Core.Save(settings);Check(Core.Load().Overlay,"Legacy enabled statistics fixture");
            using(var form=new MainForm(true)){Check(OverlayOff(form),"Old saved setting does not re-enable yellow statistics");}
        }
        static bool OverlayOff(Control root){foreach(Control control in root.Controls){var box=control as CheckBox;if(box!=null&&box.Text=="显示性能数据")return !box.Checked;if(OverlayOff(control))return true;}return false;}
        static void CheckSessionPackets(){
            byte[] key=BridgePacket.Key(BridgePacket.NewKey());Check(key.Length==32,"Random pairing key");Reject(()=>BridgePacket.Key("invalid"));
            Check(SessionFilesForm.PeerIp("100.64.1.2")=="100.64.1.2","Bridge peer accepts Tailscale IPv4");Reject(()=>SessionFilesForm.PeerIp("192.168.1.2"));Reject(()=>SessionFilesForm.PeerIp("100.128.1.1"));
            string root=Path.Combine(Core.Home,"bridge-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            string file=Path.Combine(root,"中文 文件.txt"),empty=Path.Combine(root,"empty.bin"),packet=Path.Combine(root,"roundtrip.tlbridge");File.WriteAllText(file,"hello 中文\r\n");File.WriteAllBytes(empty,new byte[0]);
            var message=BridgePacket.Message(Guid.NewGuid().ToString("N"),"100.64.1.1","100.64.1.2","clipboard");BridgePacket.Write(packet,message,new[]{file,empty},key);
            string received=Path.Combine(root,"received");var decoded=BridgePacket.Read(packet,"100.64.1.1","100.64.1.2",key,received);
            Check(decoded.Files.Length==2&&File.ReadAllText(Path.Combine(received,Path.GetFileName(file)))==File.ReadAllText(file),"Signed packet roundtrip preserves Chinese names and content");
            Check(new FileInfo(Path.Combine(received,"empty.bin")).Length==0,"Empty file survives roundtrip");
            string wrong=Path.Combine(root,"wrong-key");Fails(()=>BridgePacket.Read(packet,"100.64.1.1","100.64.1.2",new byte[32],wrong),"Wrong pairing key rejected");Check(!Directory.Exists(wrong),"Wrong key extracts nothing");
            Fails(()=>BridgePacket.Read(packet,"100.64.1.9","100.64.1.2",key,null),"Wrong sender rejected");Fails(()=>BridgePacket.Read(packet,"100.64.1.1","100.64.1.9",key,null),"Wrong recipient rejected");
            string corrupt=Path.Combine(root,"corrupt.tlbridge");File.Copy(packet,corrupt);using(var zip=System.IO.Compression.ZipFile.Open(corrupt,System.IO.Compression.ZipArchiveMode.Update)){var entry=zip.GetEntry("files/0");long size=entry.Length;entry.Delete();using(var stream=zip.CreateEntry("files/0").Open()){var bytes=new byte[(int)size];stream.Write(bytes,0,bytes.Length);}}
            Fails(()=>BridgePacket.Read(corrupt,"100.64.1.1","100.64.1.2",key,Path.Combine(root,"corrupt")),"Payload tampering rejected by SHA256");
            var hostile=BridgePacket.Message(Guid.NewGuid().ToString("N"),"100.64.1.1","100.64.1.2","clipboard");hostile.Files=new[]{new BridgeFile{Name="../escape.txt",Size=0,Hash="x"}};
            string forged=Path.Combine(root,"forged.tlbridge");ForgePacket(forged,hostile,key);Fails(()=>BridgePacket.Read(forged,"100.64.1.1","100.64.1.2",key,null),"Signed traversal filename rejected");
            hostile.Files[0].Name="CON.txt";ForgePacket(forged,hostile,key);Fails(()=>BridgePacket.Read(forged,"100.64.1.1","100.64.1.2",key,null),"Reserved filename rejected");
            hostile.Files[0].Name="huge.bin";hostile.Files[0].Size=BridgePacket.MaxBytes+1;ForgePacket(forged,hostile,key);Fails(()=>BridgePacket.Read(forged,"100.64.1.1","100.64.1.2",key,null),"Oversized request rejected before extraction");
            hostile.Files=new BridgeFile[0];hostile.Action="ack";hostile.Created-=1000;ForgePacket(forged,hostile,key);Fails(()=>BridgePacket.Read(forged,"100.64.1.1","100.64.1.2",key,null),"Expired signed request rejected");
            string folder=Path.Combine(root,"destination");Directory.CreateDirectory(folder);string first=SessionFilesForm.CopyWithoutOverwrite(file,folder),second=SessionFilesForm.CopyWithoutOverwrite(file,folder);Check(first!=second&&File.ReadAllText(first)==File.ReadAllText(second),"Drop copy preserves existing same-name files");
            var copy=new DataObject();copy.SetData("Preferred DropEffect",new MemoryStream(BitConverter.GetBytes(1)));Check(SessionFilesForm.IsCopy(copy),"Clipboard copy is eligible");copy.SetData("Preferred DropEffect",new MemoryStream(BitConverter.GetBytes(2)));Check(!SessionFilesForm.IsCopy(copy),"Clipboard cut is excluded");
        }
        static void ForgePacket(string file,BridgeMessage message,byte[] key){
            byte[] json=System.Text.Encoding.UTF8.GetBytes(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(message));
            using(var output=new FileStream(file,FileMode.Create))using(var zip=new System.IO.Compression.ZipArchive(output,System.IO.Compression.ZipArchiveMode.Create)){
                using(var entry=zip.CreateEntry("manifest.json").Open())entry.Write(json,0,json.Length);
                using(var mac=new System.Security.Cryptography.HMACSHA256(key))using(var entry=zip.CreateEntry("signature").Open()){byte[] signature=mac.ComputeHash(json);entry.Write(signature,0,signature.Length);}
            }
        }
        static void CheckSessionWindow(){
            using(var form=new MainForm(true)){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();Check(FindButton(form,"会话文件助手")==null,"No separate helper entry required");var button=FindButton(form,"文件状态");Check(button!=null,"Read-only file status entry exists");button.PerformClick();Application.DoEvents();bool visible=false;
                foreach(Form dialog in form.OwnedForms){if(!(dialog is SessionFilesForm))continue;visible=dialog.Visible;Check(FindButton(dialog,"生成配对口令")==null&&FindButton(dialog,"启用助手")==null,"No manual key or enable workflow");dialog.Location=new Point(-20000,-20000);using(var bitmap=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),"session-files-window.png"));}dialog.Close();Check(!dialog.Visible&&!dialog.IsDisposed,"Closing status view only hides it");((SessionFilesForm)dialog).CloseAutomatic();}Check(visible,"File status opens without blocking remote connection controls");form.Close();}
        }
        static string NetworkFixture(long otherUser,bool online,string[] tags,bool shared=false){
            var peer=new {UserID=otherUser,Online=online,Tags=tags,ShareeNode=shared,TailscaleIPs=new[]{"100.64.1.2"},HostName="remote-pc",DNSName="remote-pc.example.ts.net."};
            return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new{BackendState="Running",Self=new{UserID=123,TailscaleIPs=new[]{"100.64.1.1"},Tags=new string[0]},Peer=new System.Collections.Generic.Dictionary<string,object>{{"nodekey:test",peer}}});
        }
        static void Denied(Action action,string reason){bool denied=false;try{action();}catch(InvalidOperationException){denied=true;}catch(ArgumentException){denied=true;}Check(denied,reason);}
        static void CheckAutomaticIdentity(){
            var network=AutoNetwork.Parse(NetworkFixture(123,true,new string[0]));Check(network.Self=="100.64.1.1"&&network.User==123,"Real Tailscale status schema parses");Check(network.Resolve("100.64.1.2").Host=="remote-pc","Authenticated peer source resolves");Check(network.Resolve("remote-pc.example.ts.net").IP=="100.64.1.2","DNS target resolves to fixed Tailscale IP");
            Denied(()=>AutoNetwork.Parse(NetworkFixture(456,true,new string[0])).Resolve("100.64.1.2"),"Other account denied");Denied(()=>AutoNetwork.Parse(NetworkFixture(123,false,new string[0])).Resolve("100.64.1.2"),"Offline peer denied");Denied(()=>AutoNetwork.Parse(NetworkFixture(123,true,new[]{"tag:server"})).Resolve("100.64.1.2"),"Tagged peer denied");Denied(()=>AutoNetwork.Parse(NetworkFixture(123,true,new string[0],true)).Resolve("100.64.1.2"),"Shared peer denied");Denied(()=>network.Resolve("192.168.1.1"),"Nonpeer source denied");
            Fails(()=>FileControlWire.Read(new MemoryStream(BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(16385)))).GetAwaiter().GetResult(),"Control frame bounded before allocation");
        }
        static async System.Threading.Tasks.Task CheckAutomaticChannel(){
            var ended=new System.Threading.Tasks.TaskCompletionSource<bool>();int begins=0,pulses=0;bool live=false;string previous=null;
            using(var listener=new AutoFileListener("127.0.0.1",0,(source,request)=>{
                if(request.Action=="begin"){System.Threading.Interlocked.Increment(ref begins);return System.Threading.Tasks.Task.FromResult(new FileControl{Accepted=source=="127.0.0.1",Key=BridgePacket.NewKey()});}
                if(request.Action=="pulse"){System.Threading.Interlocked.Increment(ref pulses);live=request.Live;return System.Threading.Tasks.Task.FromResult(new FileControl{Accepted=true});}
                ended.TrySetResult(true);return System.Threading.Tasks.Task.FromResult(new FileControl{Accepted=true});
            })){
                using(var connection=await AutoFileConnection.Open("127.0.0.1","127.0.0.1",listener.Port)){
                    Check(BridgePacket.Key(connection.Initial.Key).Length==32,"Key negotiated without manual configuration");previous=connection.Initial.Key;
                    await connection.Pulse(false);Check(!live,"Pending video does not activate clipboard");await connection.Pulse(true);Check(live&&pulses==2,"Live video pulse activates session");
                }
                await System.Threading.Tasks.Task.WhenAny(ended.Task,System.Threading.Tasks.Task.Delay(3000));Check(ended.Task.IsCompleted,"Closing connection delivers immediate session end");
                using(var next=await AutoFileConnection.Open("127.0.0.1","127.0.0.1",listener.Port)){Check(next.Initial.Key!=previous&&begins==2,"New connection uses fresh session key");}
            }
            using(var denied=new AutoFileListener("127.0.0.1",0,(source,request)=>System.Threading.Tasks.Task.FromResult(new FileControl{Accepted=false,Error="denied-test"}))){bool failed=false;try{using(var connection=await AutoFileConnection.Open("127.0.0.1","127.0.0.1",denied.Port)){}}catch(InvalidOperationException ex){failed=ex.Message=="denied-test";}Check(failed,"Rejected device cannot establish file session");}
        }
        class TransferCollector : IProgress<TransferProgress> {
            public readonly System.Collections.Generic.List<TransferProgress> Values=new System.Collections.Generic.List<TransferProgress>();
            public void Report(TransferProgress value){Values.Add(value);}
        }
        static void CheckFileTransfer() {
            Check(FileTransfer.Target("100.64.1.2")=="100.64.1.2:","Taildrop IPv4 target");
            Check(FileTransfer.Target("fd7a:115c:a1e0::1")=="[fd7a:115c:a1e0::1]:","Bracketed IPv6 target");
            Reject(()=>FileTransfer.Target("pc:1234"));
            string dir=Path.Combine(Core.Home,"transfer-tests");Directory.CreateDirectory(dir);
            string one=Path.Combine(dir,"中文 文件 1.txt"),two=Path.Combine(dir,"--option.txt");File.WriteAllText(one,"test1");File.WriteAllText(two,"test2");
            string command=FileTransfer.SendArgs("100.64.1.2",one);int n;IntPtr argv=CommandLineToArgvW("tailscale.exe "+command,out n);
            try{Check(n==7,"File transfer CLI argument count");Check(Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv,5*IntPtr.Size))==one,"Chinese/space path preserved");Check(Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv,6*IntPtr.Size))=="100.64.1.2:","Destination remains a single argument");}finally{LocalFree(argv);}
            Check(FileTransfer.SendArgs("pc",two).Contains(Core.Quote(two)),"Dash filename uses absolute path");
            var updates=new TransferCollector();int calls=0;
            FileTransfer.Send("fake","pc",new[]{one,two},updates,System.Threading.CancellationToken.None,(e,a,c)=>{calls++;return System.Threading.Tasks.Task.FromResult(new TransferResult{ExitCode=0,Output=""});}).GetAwaiter().GetResult();
            Check(calls==2&&updates.Values[updates.Values.Count-1].Completed==2,"Both queued files confirmed complete");
            updates=new TransferCollector();calls=0;bool failed=false;
            try{FileTransfer.Send("fake","pc",new[]{one,two},updates,System.Threading.CancellationToken.None,(e,a,c)=>{calls++;return System.Threading.Tasks.Task.FromResult(new TransferResult{ExitCode=7,Output="offline-test"});}).GetAwaiter().GetResult();}catch(IOException ex){failed=ex.Message.Contains("offline-test");}
            Check(failed&&calls==1&&updates.Values[updates.Values.Count-1].Completed==0,"Failed send stops queue and never reports complete");
            calls=0;try{FileTransfer.Send("fake","pc",new[]{one,Path.Combine(dir,"missing.txt")},new TransferCollector(),System.Threading.CancellationToken.None,(e,a,c)=>{calls++;return System.Threading.Tasks.Task.FromResult(new TransferResult());}).GetAwaiter().GetResult();}catch(FileNotFoundException){}
            Check(calls==0,"Missing queued file detected before any send");
            var result=FileTransfer.Run(Application.ExecutablePath,"--fake-transfer",System.Threading.CancellationToken.None).GetAwaiter().GetResult();
            Check(result.ExitCode==7&&result.Output.Contains("transfer-stdout")&&result.Output.Contains("transfer-stderr"),"Transfer process preserves errors and exit code");
            bool cancelled=false;using(var token=new System.Threading.CancellationTokenSource(250)){try{FileTransfer.Run(Application.ExecutablePath,"--slow-transfer",token.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}}
            Check(cancelled,"Cancel terminates running transfer process");
            Check(Path.IsPathRooted(FileTransfer.DownloadDirectory()),"Downloads known folder resolves");
        }
        static void CheckFileTransferButton() {
            using(var form=new MainForm(true)) {
                form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();
                var button=FindButton(form,"文件互传");Check(button!=null,"File transfer entry exists");bool opened=false;
                using(var timer=new Timer{Interval=150}) {
                    timer.Tick+=(s,e)=>{timer.Stop();foreach(Form dialog in form.OwnedForms){if(!(dialog is FileTransferForm))continue;opened=dialog.Visible;dialog.Location=new Point(-20000,-20000);using(var bmp=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(bmp,new Rectangle(0,0,bmp.Width,bmp.Height));bmp.Save(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),"file-transfer-window.png"));}dialog.Close();}if(!opened)form.Close();};
                    timer.Start();button.PerformClick();
                }
                Check(opened,"File transfer click opens visible window");form.Close();
            }
        }
        static void CheckInstallerButton() {
            using(var form=new MainForm(true)) {
                form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);
                form.Show();Application.DoEvents();
                var button=FindButton(form,"安装三个必要组件");
                Check(button!=null,"Install button exists");
                bool opened=false,owned=false,visible=false;Exception error=null;
                using(var timer=new Timer{Interval=150}) {
                    timer.Tick+=(sender,args)=>{
                        timer.Stop();
                        try {
                            foreach(Form dialog in form.OwnedForms) {
                                var installer=dialog as InstallerForm;
                                if(installer==null)continue;
                                opened=true;owned=installer.Owner==form;visible=installer.Visible;
                                installer.Location=new Point(-20000,-20000);
                                using(var image=new Bitmap(installer.Width,installer.Height)) {
                                    installer.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));
                                    image.Save(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),"installer-window.png"));
                                }
                                installer.Close();
                            }
                        } catch(Exception ex){error=ex;} finally {if(!opened)form.Close();}
                    };
                    timer.Start();
                    button.PerformClick();
                    if(error!=null)throw error;
                }
                Check(opened,"Click opens native installer, without PowerShell/script");
                Check(owned&&visible,"Installer is visible and owned by main window");
                form.Close();
            }
        }
        static Button FindButton(Control root,string text) {
            foreach(Control c in root.Controls) {var button=c as Button;if(button!=null&&button.Text==text)return button;var nested=FindButton(c,text);if(nested!=null)return nested;}
            return null;
        }
        static void CheckInstallerProcess() {
            // Exercise both redirected streams and nonzero exit without installing anything.
            var output=new System.Collections.Generic.List<string>();
            int code=InstallerForm.RunInstaller(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"cmd.exe"),"/d /c \"echo installer-stdout& echo installer-stderr 1>&2& exit /b 7\"",s=>{lock(output)output.Add(s);}).GetAwaiter().GetResult();
            Check(code==7,"Installer preserves nonzero exit code");
            string all=String.Join("\n",output);Check(all.Contains("installer-stdout")&&all.Contains("installer-stderr"),"Installer captures progress and errors");
        }
        static int CombosValid(Control root) {
            int n=0;
            foreach(Control c in root.Controls) {var combo=c as ComboBox;if(combo!=null){Check(combo.SelectedIndex>=0&&!String.IsNullOrEmpty(combo.Text),"Selected combo value");n++;}n+=CombosValid(c);}
            return n;
        }
    }
}
