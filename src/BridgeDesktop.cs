using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace TwinLink {
    public static class BridgeDesktop {
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] static extern int GetClassName(IntPtr hwnd,StringBuilder name,int count);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint id);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hwnd,out Rect rect);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hwnd,ref Point point);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        delegate bool EnumWindowProc(IntPtr window,IntPtr value);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowProc callback,IntPtr value);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int capacity);
        [StructLayout(LayoutKind.Sequential)] struct Rect {public int Left,Top,Right,Bottom;}
        public static bool IsMoonlight(IntPtr hwnd){uint id;GetWindowThreadProcessId(hwnd,out id);try{using(var p=Process.GetProcessById((int)id))return p.ProcessName.Equals("Moonlight",StringComparison.OrdinalIgnoreCase);}catch{return false;}}
        static IntPtr StreamWindow(){
            var ids=new System.Collections.Generic.HashSet<uint>();foreach(var process in Process.GetProcessesByName("Moonlight"))using(process)ids.Add((uint)process.Id);
            IntPtr found=IntPtr.Zero;int count=0;EnumWindows((window,value)=>{uint id;GetWindowThreadProcessId(window,out id);if(ids.Contains(id)){var text=new StringBuilder(1024);GetWindowText(window,text,text.Capacity);if(text.ToString().EndsWith(" - Moonlight",StringComparison.OrdinalIgnoreCase)){found=window;count++;}}return true;},IntPtr.Zero);return count==1?found:IntPtr.Zero;
        }
        public static bool HasStreamWindow(){return StreamWindow()!=IntPtr.Zero;}
        public static IntPtr StreamWindowHandle(){return StreamWindow();}
        public static Rectangle? MoonlightBounds(){
            IntPtr found=StreamWindow();if(found==IntPtr.Zero||!IsWindowVisible(found)||IsIconic(found))return null;Rect rect;Point point=new Point();if(!GetClientRect(found,out rect)||!ClientToScreen(found,ref point))return null;
            return new Rectangle(point.X,point.Y,rect.Right,rect.Bottom);
        }
        public static string CurrentFolder(){
            IntPtr foreground=GetForegroundWindow();var name=new StringBuilder(256);GetClassName(foreground,name,name.Capacity);
            if(name.ToString()=="Progman"||name.ToString()=="WorkerW")return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            object shell=null,windows=null;
            try {
                shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));dynamic app=shell;windows=app.Windows();
                foreach(dynamic window in (System.Collections.IEnumerable)windows)try{if(new IntPtr((long)window.HWND)!=foreground)continue;string path=window.Document.Folder.Self.Path;if(System.IO.Directory.Exists(path))return path;}catch{}
            }finally{if(windows!=null&&Marshal.IsComObject(windows))Marshal.ReleaseComObject(windows);if(shell!=null&&Marshal.IsComObject(shell))Marshal.ReleaseComObject(shell);}
            throw new InvalidOperationException("请先在被控端打开目标文件夹，或单击桌面空白处，再拖入文件。此版本不支持拖到文件夹图标、虚拟目录或应用窗口内。");
        }
    }
}
