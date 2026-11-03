using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TwinLink {
    public class AutoPeer {
        public string IP,Host,Dns;
        public long User;
        public bool Online,Tagged,Shared;
    }
    public class AutoNetwork {
        public string Self;
        public long User;
        public readonly List<AutoPeer> Peers=new List<AutoPeer>();
        static object Get(Dictionary<string,object> value,string key){object result;return value.TryGetValue(key,out result)?result:null;}
        static string String(Dictionary<string,object> value,string key){return Convert.ToString(Get(value,key));}
        static bool Flag(Dictionary<string,object> value,string key){return Get(value,key) is bool&&(bool)Get(value,key);}
        static object[] Array(Dictionary<string,object> value,string key){var list=Get(value,key) as System.Collections.IEnumerable;return list==null?new object[0]:list.Cast<object>().ToArray();}
        static long Number(Dictionary<string,object> value,string key){return Convert.ToInt64(Get(value,key)??0);}
        static string IPv4(Dictionary<string,object> value){return Array(value,"TailscaleIPs").Select(Convert.ToString).FirstOrDefault(ip=>{IPAddress parsed;return IPAddress.TryParse(ip,out parsed)&&parsed.AddressFamily==AddressFamily.InterNetwork;});}
        public static AutoNetwork Parse(string json){
            var data=new JavaScriptSerializer{MaxJsonLength=8*1024*1024}.Deserialize<Dictionary<string,object>>(json);
            if(String(data,"BackendState")!="Running")throw new InvalidOperationException("Tailscale 尚未登录或未连接。");
            var self=Get(data,"Self") as Dictionary<string,object>;if(self==null)throw new InvalidDataException("无法识别本机 Tailscale 身份。");
            var network=new AutoNetwork{Self=SessionFilesForm.PeerIp(IPv4(self)??""),User=Number(self,"UserID")};
            if(network.User<=0||Array(self,"Tags").Length!=0)throw new InvalidOperationException("默认文件功能仅支持同一 Tailscale 账户的个人设备。");
            var peers=Get(data,"Peer") as Dictionary<string,object>;
            if(peers!=null)foreach(var entry in peers.Values){var peer=entry as Dictionary<string,object>;if(peer==null)continue;network.Peers.Add(new AutoPeer{IP=IPv4(peer),Host=String(peer,"HostName"),Dns=String(peer,"DNSName").TrimEnd('.'),User=Number(peer,"UserID"),Online=Flag(peer,"Online"),Tagged=Array(peer,"Tags").Length!=0,Shared=Flag(peer,"ShareeNode")||Number(peer,"AltSharerUserID")!=0});}
            return network;
        }
        public AutoPeer Resolve(string host){
            host=Core.ValidateHost(host).TrimEnd('.');
            var matches=Peers.Where(p=>p.IP==host||System.String.Equals(p.Host,host,StringComparison.OrdinalIgnoreCase)||System.String.Equals(p.Dns,host,StringComparison.OrdinalIgnoreCase)||System.String.Equals((p.Dns??"").Split('.')[0],host,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length!=1)throw new InvalidOperationException("未找到唯一的对方设备，请填写对方 Tailscale IPv4。");
            var peer=matches[0];
            if(peer.User!=User||peer.Tagged||peer.Shared||!peer.Online||peer.IP==null)throw new InvalidOperationException("文件功能只接受同一 Tailscale 账户下、在线且未设置设备标签的个人设备。");
            SessionFilesForm.PeerIp(peer.IP);return peer;
        }
    }
    public class FileControl {
        public int Version=1;
        public string Action,Session,Nonce,Key,Error;
        public bool Accepted,Live;
    }
    public static class FileControlWire {
        public static async Task Write(Stream stream,FileControl message){byte[] bytes=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(message));if(bytes.Length>16384)throw new InvalidDataException("会话消息过大。");byte[] size=BitConverter.GetBytes(IPAddress.HostToNetworkOrder(bytes.Length));await stream.WriteAsync(size,0,size.Length).ConfigureAwait(false);await stream.WriteAsync(bytes,0,bytes.Length).ConfigureAwait(false);}
        static async Task ReadExact(Stream stream,byte[] data){int offset=0;while(offset<data.Length){int count=await stream.ReadAsync(data,offset,data.Length-offset).ConfigureAwait(false);if(count==0)throw new EndOfStreamException();offset+=count;}}
        public static async Task<FileControl> Read(Stream stream){var size=new byte[4];await ReadExact(stream,size).ConfigureAwait(false);int length=IPAddress.NetworkToHostOrder(BitConverter.ToInt32(size,0));if(length<=0||length>16384)throw new InvalidDataException("会话消息长度无效。");var data=new byte[length];await ReadExact(stream,data).ConfigureAwait(false);var result=new JavaScriptSerializer{MaxJsonLength=16384,RecursionLimit=8}.Deserialize<FileControl>(Encoding.UTF8.GetString(data));if(result==null||result.Version!=1)throw new InvalidDataException("会话协议不兼容，请更新两端 TwinLink。");return result;}
        public static async Task<T> Deadline<T>(Task<T> operation,TcpClient client,int timeout){if(await Task.WhenAny(operation,Task.Delay(timeout)).ConfigureAwait(false)!=operation){client.Close();var observed=operation.ContinueWith(t=>{var ignored=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);throw new TimeoutException("文件会话连接超时。请确认对方已运行新版 TwinLink，并允许其通过 Windows 防火墙。");}return await operation.ConfigureAwait(false);}
    }
    public class AutoFileConnection : IDisposable {
        readonly TcpClient client;
        readonly SemaphoreSlim gate=new SemaphoreSlim(1,1);
        public readonly FileControl Initial;
        public readonly string Peer,Self;
        AutoFileConnection(TcpClient socket,string self,string peer,FileControl initial){client=socket;Self=self;Peer=peer;Initial=initial;}
        public static async Task<AutoFileConnection> Open(string self,string peer,int port){
            var client=new TcpClient(AddressFamily.InterNetwork);client.NoDelay=true;client.Client.Bind(new IPEndPoint(IPAddress.Parse(self),0));
            try {
                var connecting=client.ConnectAsync(peer,port);await FileControlWire.Deadline(Connect(connecting),client,5000).ConfigureAwait(false);
                var request=new FileControl{Action="begin",Session=Guid.NewGuid().ToString("N"),Nonce=Guid.NewGuid().ToString("N")};
                await FileControlWire.Write(client.GetStream(),request).ConfigureAwait(false);
                var reply=await FileControlWire.Deadline(FileControlWire.Read(client.GetStream()),client,12000).ConfigureAwait(false);
                if(!reply.Accepted)throw new InvalidOperationException(reply.Error??"对方无法准备文件功能。");
                if(reply.Nonce!=request.Nonce||reply.Session!=request.Session)throw new InvalidDataException("文件会话确认不匹配。");BridgePacket.Key(reply.Key);
                return new AutoFileConnection(client,self,peer,reply);
            }catch{client.Close();throw;}
        }
        static async Task<bool> Connect(Task operation){await operation.ConfigureAwait(false);return true;}
        public async Task Pulse(bool live){await gate.WaitAsync().ConfigureAwait(false);try{var request=new FileControl{Action="pulse",Session=Initial.Session,Nonce=Guid.NewGuid().ToString("N"),Live=live};await FileControlWire.Write(client.GetStream(),request).ConfigureAwait(false);var reply=await FileControlWire.Deadline(FileControlWire.Read(client.GetStream()),client,8000).ConfigureAwait(false);if(!reply.Accepted||reply.Session!=request.Session||reply.Nonce!=request.Nonce)throw new IOException("对方文件会话已结束。");}finally{gate.Release();}}
        public void Dispose(){client.Close();}
    }
    public class AutoFileListener : IDisposable {
        readonly TcpListener listener;
        readonly Func<string,FileControl,Task<FileControl>> handle;
        readonly HashSet<TcpClient> clients=new HashSet<TcpClient>();
        bool disposed;
        public int Port {get{return ((IPEndPoint)listener.LocalEndpoint).Port;}}
        public AutoFileListener(string address,int port,Func<string,FileControl,Task<FileControl>> callback){listener=new TcpListener(IPAddress.Parse(address),port);handle=callback;listener.Start(4);var running=Accept();}
        async Task Accept(){while(!disposed){TcpClient client;try{client=await listener.AcceptTcpClientAsync().ConfigureAwait(false);}catch{break;}lock(clients){if(clients.Count>=4){client.Close();continue;}clients.Add(client);}var running=Serve(client);}}
        async Task Serve(TcpClient client){
            FileControl current=null;string source=((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();bool accepted=false;
            try {
                current=await FileControlWire.Deadline(FileControlWire.Read(client.GetStream()),client,10000).ConfigureAwait(false);
                Guid id,nonce;if(current.Action!="begin"||!Guid.TryParseExact(current.Session,"N",out id)||!Guid.TryParseExact(current.Nonce,"N",out nonce))throw new InvalidDataException("无效文件会话请求。");
                var reply=await handle(source,current).ConfigureAwait(false);reply.Nonce=current.Nonce;reply.Session=current.Session;accepted=reply.Accepted;
                await FileControlWire.Write(client.GetStream(),reply).ConfigureAwait(false);if(!accepted)return;
                while(!disposed){var pulse=await FileControlWire.Deadline(FileControlWire.Read(client.GetStream()),client,15000).ConfigureAwait(false);if(pulse.Action!="pulse"||pulse.Session!=current.Session)break;var answer=await handle(source,pulse).ConfigureAwait(false);answer.Nonce=pulse.Nonce;answer.Session=current.Session;await FileControlWire.Write(client.GetStream(),answer).ConfigureAwait(false);if(!answer.Accepted)break;}
            }catch{}finally{client.Close();lock(clients)clients.Remove(client);}
            if(accepted)try{await handle(source,new FileControl{Action="end",Session=current.Session}).ConfigureAwait(false);}catch{}
        }
        public void Dispose(){disposed=true;listener.Stop();lock(clients)foreach(var client in clients.ToArray())client.Close();}
    }
    public class AutoFilesService : IDisposable {
        public const int ControlPort=48179;
        readonly SynchronizationContext ui;
        readonly Func<string,FileControl,Task<FileControl>> onControl;
        AutoFileListener listener;
        readonly SemaphoreSlim readyGate=new SemaphoreSlim(1,1);
        string bound;
        public string LocalIP {get{return bound;}}
        bool disposed;
        public AutoFilesService(SynchronizationContext context,Func<string,FileControl,Task<FileControl>> callback){ui=context;onControl=callback;}
        public static async Task<AutoNetwork> Network(){string exe=Core.Find("Tailscale",null);if(exe==null)throw new FileNotFoundException("请先安装并登录 Tailscale。");return AutoNetwork.Parse(await SystemTools.Run(exe,"status --json",8000).ConfigureAwait(false));}
        public async Task Ready(){await readyGate.WaitAsync().ConfigureAwait(false);try{var network=await Network().ConfigureAwait(false);if(disposed)return;if(listener!=null&&bound==network.Self)return;if(listener!=null)listener.Dispose();listener=new AutoFileListener(network.Self,ControlPort,Handle);bound=network.Self;}finally{readyGate.Release();}}
        async Task<FileControl> Handle(string source,FileControl request){
            try{if(disposed)return new FileControl{Accepted=false,Error="TwinLink 已退出。"};if(request.Action=="begin"){var network=await Network().ConfigureAwait(false);network.Resolve(source);}
                var completion=new TaskCompletionSource<FileControl>();ui.Post(async _=>{try{completion.TrySetResult(await onControl(source,request));}catch(Exception ex){completion.TrySetResult(new FileControl{Accepted=false,Error=ex.Message});}},null);return await completion.Task.ConfigureAwait(false);
            }catch(Exception ex){return new FileControl{Accepted=false,Error=ex.Message};}
        }
        public async Task<AutoFileConnection> Connect(string host){await Ready().ConfigureAwait(false);var network=await Network().ConfigureAwait(false);var peer=network.Resolve(host);return await AutoFileConnection.Open(network.Self,peer.IP,ControlPort).ConfigureAwait(false);}
        public void Dispose(){disposed=true;if(listener!=null)listener.Dispose();}
    }
}
