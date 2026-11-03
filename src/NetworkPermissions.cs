using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TwinLink {
    public static class NetworkPermissions {
        public static bool IsConfigured(){
            object policy=null;
            try{policy=Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));dynamic firewall=policy;
                foreach(dynamic rule in firewall.Rules)try{string remote=(string)rule.RemoteAddresses;if((string)rule.Name=="TwinLink File Session 48179"&&String.Equals((string)rule.ApplicationName,Application.ExecutablePath,StringComparison.OrdinalIgnoreCase)&&(bool)rule.Enabled&&(int)rule.Action==1&&(int)rule.Direction==1&&(int)rule.Protocol==6&&(string)rule.LocalPorts==AutoFilesService.ControlPort.ToString()&&(remote=="100.64.0.0/10"||remote=="100.64.0.0/255.192.0.0"))return true;}catch{}
                return false;
            }catch{return false;}finally{if(policy!=null&&Marshal.IsComObject(policy))Marshal.ReleaseComObject(policy);}
        }
        public static void Configure(){
            object policy=null,rule=null;
            try{
                policy=Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));dynamic firewall=policy;
                rule=Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule"));dynamic entry=rule;
                entry.Name="TwinLink File Session 48179";entry.Description="Allow this TwinLink program to negotiate file sessions with personal Tailscale devices.";
                entry.ApplicationName=Application.ExecutablePath;entry.Protocol=6;entry.LocalPorts=AutoFilesService.ControlPort.ToString();entry.RemoteAddresses="100.64.0.0/10";entry.Direction=1;entry.Action=1;entry.Profiles=7;entry.Enabled=true;
                firewall.Rules.Add(entry);
            }finally{if(rule!=null&&Marshal.IsComObject(rule))Marshal.ReleaseComObject(rule);if(policy!=null&&Marshal.IsComObject(policy))Marshal.ReleaseComObject(policy);}
        }
        public static async Task<int> InstallRule(){
            var info=new ProcessStartInfo(Application.ExecutablePath,"--configure-file-network"){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
            using(var process=Process.Start(info)){await Task.Run(()=>process.WaitForExit());return process.ExitCode;}
        }
    }
}
