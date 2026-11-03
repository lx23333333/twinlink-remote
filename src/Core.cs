using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace TwinLink {
    public class Settings {
        public string Host = "";
        public string App = "Desktop";
        public int Fps = 144;
        public int Bitrate = 50;
        public string Codec = "auto";
        public bool Fullscreen = true;
        public bool Overlay = false;
        public string MoonlightPath = "";
    }
    public static class Core {
        public static string Home {
            get { return Environment.GetEnvironmentVariable("TWINLINK_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwinLink"); }
        }
        public static string ValidateHost(string value) {
            string host = (value ?? "").Trim();
            IPAddress ip;
            if (IPAddress.TryParse(host, out ip)) return ip.ToString();
            if (host.Length > 253 || !Regex.IsMatch(host, @"^(?=.{1,253}$)[a-zA-Z0-9](?:[a-zA-Z0-9.-]*[a-zA-Z0-9])?$"))
                throw new ArgumentException("请输入对方的 IP 或设备名，例如 100.101.102.103。不要填写网址或端口。");
            if (host.Split('.').Any(p => p.Length == 0 || p.Length > 63 || p.StartsWith("-") || p.EndsWith("-")) || host.All(c => Char.IsDigit(c) || c == '.'))
                throw new ArgumentException("设备地址格式不正确。");
            return host;
        }
        // Windows CommandLineToArgvW / C runtime quoting; never invoke a command shell.
        public static string Quote(string value) {
            if (value == null || value.IndexOfAny(new char[] {'\r','\n','\0'}) >= 0) throw new ArgumentException("输入包含无效字符。");
            var result = new System.Text.StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value) {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') result.Append('\\', slashes * 2 + 1).Append(c);
                else result.Append('\\', slashes).Append(c);
                slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
        public static string StreamArgs(Settings s) {
            string host = ValidateHost(s.Host);
            if (s.Fps != 60 && s.Fps != 120 && s.Fps != 144) throw new ArgumentException("请选择 60、120 或 144 FPS。");
            if (s.Bitrate < 10 || s.Bitrate > 150) throw new ArgumentException("码率范围为 10–150 Mbps。");
            if (!new [] {"auto", "H.264", "HEVC", "AV1"}.Contains(s.Codec)) throw new ArgumentException("编码格式无效。");
            if (String.IsNullOrWhiteSpace(s.App) || s.App.Length > 128) throw new ArgumentException("请填写 Sunshine 中的应用名称，默认 Desktop。");
            return "stream " + Quote(host) + " " + Quote(s.App.Trim()) + " --resolution 1920x1080 --fps " + s.Fps + " --bitrate " + (s.Bitrate * 1000) +
                " --video-codec " + s.Codec + " --video-decoder hardware --display-mode " + (s.Fullscreen ? "fullscreen" : "windowed") +
                " --absolute-mouse --capture-system-keys fullscreen --game-optimization --no-hdr" + (s.Overlay ? " --performance-overlay" : " --no-performance-overlay");
        }
        public static Settings Load() {
            string p = Path.Combine(Home, "settings.json");
            if (!File.Exists(p)) return new Settings();
            try { return new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(p)) ?? new Settings(); }
            catch { return new Settings(); }
        }
        public static void Save(Settings s) {
            Directory.CreateDirectory(Home);
            string p = Path.Combine(Home, "settings.json");
            File.WriteAllText(p, new JavaScriptSerializer().Serialize(s));
        }
        public static string Find(string component, string custom) {
            if (!String.IsNullOrWhiteSpace(custom) && File.Exists(custom)) return Path.GetFullPath(custom);
            string relative = component == "Moonlight" ? @"Moonlight Game Streaming\Moonlight.exe" : component == "Sunshine" ? @"Sunshine\sunshine.exe" : @"Tailscale\tailscale.exe";
            foreach (string root in new [] {Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), AppDomain.CurrentDomain.BaseDirectory}) {
                string p = Path.Combine(root, relative);
                if (File.Exists(p)) return p;
            }
            string portable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "components", component, component + ".exe");
            return File.Exists(portable) ? portable : null;
        }
    }
}
