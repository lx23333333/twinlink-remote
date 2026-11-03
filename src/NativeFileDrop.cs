using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace TwinLink {
    [StructLayout(LayoutKind.Sequential)]
    public struct DropPoint {public int X,Y;}

    [ComVisible(true),Guid("00000122-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileDropTarget {
        [PreserveSig] int DragEnter([MarshalAs(UnmanagedType.Interface)] IDataObject data,uint keys,DropPoint point,ref uint effect);
        [PreserveSig] int DragOver(uint keys,DropPoint point,ref uint effect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int Drop([MarshalAs(UnmanagedType.Interface)] IDataObject data,uint keys,DropPoint point,ref uint effect);
    }

    // Registered on the actual video HWND. No mouse polling, overlay, input hook or clipboard inference.
    [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
    public sealed class NativeFileDrop : IFileDropTarget,IDisposable {
        [DllImport("ole32.dll")] static extern int OleInitialize(IntPtr reserved);
        [DllImport("ole32.dll")] static extern void OleUninitialize();
        [DllImport("ole32.dll")] static extern int RegisterDragDrop(IntPtr window,[MarshalAs(UnmanagedType.Interface)] IFileDropTarget target);
        [DllImport("ole32.dll")] static extern int RevokeDragDrop(IntPtr window);
        [DllImport("ole32.dll")] static extern void ReleaseStgMedium(ref STGMEDIUM medium);
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern uint DragQueryFileW(IntPtr drop,uint index,StringBuilder name,uint size);
        readonly Func<bool> eligible;
        readonly Action<string[]> received;
        IntPtr window;
        bool disposed,validDrag;
        public IntPtr Window {get{return window;}}
        public NativeFileDrop(Func<bool> enabled,Action<string[]> onDrop){eligible=enabled;received=onDrop;int result=OleInitialize(IntPtr.Zero);if(result<0)Marshal.ThrowExceptionForHR(result);}
        public void Attach(IntPtr target){
            if(disposed)throw new ObjectDisposedException("NativeFileDrop");if(window==target)return;Detach();if(target==IntPtr.Zero)return;
            int result=RegisterDragDrop(target,this);if(result<0)throw new InvalidOperationException("无法注册远程画面的文件拖拽目标（0x"+unchecked((uint)result).ToString("X8")+"）。复制粘贴仍可使用；请确认两个程序权限一致。");window=target;
        }
        public void Detach(){validDrag=false;if(window!=IntPtr.Zero){RevokeDragDrop(window);window=IntPtr.Zero;}}
        public static string[] ReadFiles(IDataObject data){
            if(data==null)return null;
            var format=new FORMATETC{cfFormat=15,dwAspect=DVASPECT.DVASPECT_CONTENT,lindex=-1,tymed=TYMED.TYMED_HGLOBAL};
            if(data.QueryGetData(ref format)!=0)return null;
            STGMEDIUM medium;data.GetData(ref format,out medium);
            try{
                if(medium.tymed!=TYMED.TYMED_HGLOBAL||medium.unionmember==IntPtr.Zero)return null;
                uint count=DragQueryFileW(medium.unionmember,UInt32.MaxValue,null,0);if(count==0||count>256)return null;
                var paths=new string[count];
                for(uint i=0;i<count;i++){uint length=DragQueryFileW(medium.unionmember,i,null,0);if(length==0||length>32767)return null;var name=new StringBuilder((int)length+1);if(DragQueryFileW(medium.unionmember,i,name,(uint)name.Capacity)!=length)return null;string path=name.ToString();if(!Path.IsPathRooted(path)||!File.Exists(path))return null;paths[i]=path;}
                return paths;
            }finally{ReleaseStgMedium(ref medium);}
        }
        bool CanReceive(){try{return !disposed&&window!=IntPtr.Zero&&eligible();}catch{return false;}}
        public int DragEnter(IDataObject data,uint keys,DropPoint point,ref uint effect){
            validDrag=false;try{validDrag=CanReceive()&&(effect&1)!=0&&ReadFiles(data)!=null;}catch{}
            effect=validDrag?1u:0u;return 0;
        }
        public int DragOver(uint keys,DropPoint point,ref uint effect){effect=validDrag&&CanReceive()&&(effect&1)!=0?1u:0u;return 0;}
        public int DragLeave(){validDrag=false;return 0;}
        public int Drop(IDataObject data,uint keys,DropPoint point,ref uint effect){
            bool accepted=validDrag&&CanReceive()&&(effect&1)!=0;validDrag=false;effect=0;
            try{if(accepted){var paths=ReadFiles(data);if(paths!=null){received(paths);effect=1;}}}catch{}
            return 0;
        }
        public void Dispose(){if(disposed)return;Detach();disposed=true;OleUninitialize();}
    }
}
