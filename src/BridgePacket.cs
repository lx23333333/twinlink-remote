using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace TwinLink {
    public class BridgeFile { public string Name; public long Size; public string Hash; }
    public class BridgeMessage {
        public int Version=1;
        public string Id;
        public string From;
        public string To;
        public long Created;
        public string Action;
        public string Detail;
        public BridgeFile[] Files=new BridgeFile[0];
    }
    public static class BridgePacket {
        public const long MaxBytes=2L*1024*1024*1024;
        public static string NewKey(){var data=new byte[32];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(data);return Convert.ToBase64String(data);}
        public static byte[] Key(string value){byte[] key;try{key=Convert.FromBase64String(value.Trim());}catch{throw new ArgumentException("配对口令格式不正确，请使用生成的完整口令。");}if(key.Length!=32)throw new ArgumentException("请使用完整的 32 字节配对口令。");return key;}
        public static string Hash(Stream stream){using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(stream));}
        static bool Equal(byte[] a,byte[] b){if(a.Length!=b.Length)return false;int value=0;for(int i=0;i<a.Length;i++)value|=a[i]^b[i];return value==0;}
        public static BridgeMessage Message(string id,string from,string to,string action){return new BridgeMessage{Id=id,From=from,To=to,Action=action,Created=DateTimeOffset.UtcNow.ToUnixTimeSeconds()};}
        static void Validate(BridgeMessage message,string from,string to) {
            Guid id;if(message==null||message.Version!=1||!Guid.TryParseExact(message.Id,"N",out id)||message.From!=from||message.To!=to)throw new InvalidDataException("文件助手的配对设备信息不匹配。");
            if(Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds()-message.Created)>600)throw new InvalidDataException("文件请求已过期，请重新复制或拖拽。");
            if(!new[]{"begin-drop","clipboard","drop","ack","error"}.Contains(message.Action))throw new InvalidDataException("未知文件请求。");
            if(message.Files==null||message.Files.Length>256)throw new InvalidDataException("一次最多传输 256 个文件。");
            long total=0;var names=new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var file in message.Files){
                if(file==null||String.IsNullOrWhiteSpace(file.Name)||file.Name.Length>180||file.Name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||file.Name.EndsWith(".")||file.Name.EndsWith(" ")||file.Name=="."||file.Name==".."||!names.Add(file.Name))throw new InvalidDataException("文件名无效或存在重名。");
                string stem=Path.GetFileNameWithoutExtension(file.Name);if(System.Text.RegularExpressions.Regex.IsMatch(stem,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",System.Text.RegularExpressions.RegexOptions.IgnoreCase))throw new InvalidDataException("不支持 Windows 保留文件名。");
                if(file.Size<0||file.Size>MaxBytes||file.Hash==null)throw new InvalidDataException("文件大小或校验信息无效。");total=checked(total+file.Size);if(total>MaxBytes)throw new InvalidDataException("本版会话助手每批最多支持 2 GB 文件。");
            }
            if((message.Action=="clipboard"||message.Action=="drop")&&message.Files.Length==0)throw new InvalidDataException("文件请求为空。");
            if(message.Action!="clipboard"&&message.Action!="drop"&&message.Files.Length!=0)throw new InvalidDataException("控制消息不能携带文件。");
        }
        public static void Write(string packet,BridgeMessage message,string[] paths,byte[] key) {
            if(paths.Length>256)throw new ArgumentException("一次最多传输 256 个文件。");
            long total=0;foreach(string path in paths){var info=new FileInfo(path);if(!info.Exists)throw new FileNotFoundException("文件不存在："+path);if(info.Length>MaxBytes||(total=checked(total+info.Length))>MaxBytes)throw new ArgumentException("本版会话助手每批最多支持 2 GB 文件。");}
            message.Files=paths.Select(path=>{var info=new FileInfo(path);if(!info.Exists)throw new FileNotFoundException("文件不存在："+path);using(var input=info.OpenRead())return new BridgeFile{Name=info.Name,Size=info.Length,Hash=Hash(input)};}).ToArray();
            Validate(message,message.From,message.To);
            byte[] json=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(message));
            if(json.Length>65536)throw new InvalidDataException("文件清单过大。");
            using(var output=new FileStream(packet,FileMode.CreateNew,FileAccess.Write))using(var zip=new ZipArchive(output,ZipArchiveMode.Create)) {
                using(var entry=zip.CreateEntry("manifest.json").Open())entry.Write(json,0,json.Length);
                using(var mac=new HMACSHA256(key))using(var entry=zip.CreateEntry("signature").Open()){byte[] signature=mac.ComputeHash(json);entry.Write(signature,0,signature.Length);}
                for(int i=0;i<paths.Length;i++)using(var input=File.OpenRead(paths[i]))using(var entry=zip.CreateEntry("files/"+i,CompressionLevel.NoCompression).Open())input.CopyTo(entry);
            }
        }
        public static BridgeMessage Read(string packet,string expectedFrom,string localIp,byte[] key,string destination) {
            var info=new FileInfo(packet);if(info.Length>MaxBytes+4*1024*1024)throw new InvalidDataException("文件包过大。");
            using(var input=File.Open(packet,FileMode.Open,FileAccess.Read,FileShare.Read))using(var zip=new ZipArchive(input,ZipArchiveMode.Read)) {
                var manifest=zip.GetEntry("manifest.json");var signature=zip.GetEntry("signature");
                if(manifest==null||manifest.Length>65536||signature==null||signature.Length!=32)throw new InvalidDataException("无效会话文件包。");
                byte[] json=ReadLimited(manifest,65536),actual=ReadLimited(signature,32);
                using(var mac=new HMACSHA256(key))if(!Equal(mac.ComputeHash(json),actual))throw new InvalidDataException("配对口令不匹配或文件清单已损坏。");
                var message=new JavaScriptSerializer().Deserialize<BridgeMessage>(Encoding.UTF8.GetString(json));Validate(message,expectedFrom,localIp);
                if(zip.Entries.Count!=message.Files.Length+2)throw new InvalidDataException("文件包条目数量不匹配。");
                if(destination==null)return message;
                Directory.CreateDirectory(destination);
                for(int i=0;i<message.Files.Length;i++) {
                    var file=message.Files[i];var entry=zip.GetEntry("files/"+i);if(entry==null||entry.Length!=file.Size)throw new InvalidDataException("文件大小不匹配。");
                    string path=Path.Combine(destination,file.Name);
                    using(var source=entry.Open())using(var target=new FileStream(path,FileMode.CreateNew,FileAccess.Write)) {
                        byte[] buffer=new byte[65536];long written=0;int read;while((read=source.Read(buffer,0,buffer.Length))>0){written+=read;if(written>file.Size)throw new InvalidDataException("文件内容超出声明大小。");target.Write(buffer,0,read);}if(written!=file.Size)throw new InvalidDataException("文件内容不完整。");
                    }
                    using(var verify=File.OpenRead(path))if(Hash(verify)!=file.Hash)throw new InvalidDataException("文件校验失败："+file.Name);
                }
                return message;
            }
        }
        static byte[] ReadLimited(ZipArchiveEntry entry,int max){using(var input=entry.Open())using(var output=new MemoryStream()){var buffer=new byte[4096];int read;while((read=input.Read(buffer,0,buffer.Length))!=0){if(output.Length+read>max)throw new InvalidDataException("文件包头部过大。");output.Write(buffer,0,read);}return output.ToArray();}}
    }
}
