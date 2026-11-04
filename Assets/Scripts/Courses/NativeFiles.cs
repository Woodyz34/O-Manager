using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

// Picking files from the player's computer:
//  - CanBrowse / OpenFile: the computer's own "Open file" window (Windows, and the Unity Editor)
//  - CanDrop / StartDrop: files dragged onto the game window (Windows)
// On other computers (Mac, Linux) the player pastes the file's path instead.
public static class NativeFiles
{
    public static bool CanBrowse
    {
        get
        {
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
            return true;
#else
            return false;
#endif
        }
    }

    public static bool CanDrop
    {
        get
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

    // Returns the chosen path, or null. extensions without dots, e.g. { "ocd", "omap", "xmap" }
    public static string OpenFile(string title, string kind, string[] extensions, string startFolder)
    {
#if UNITY_EDITOR
        return NullIfEmpty(UnityEditor.EditorUtility.OpenFilePanelWithFilters(title, startFolder ?? "", new[] { kind, string.Join(",", extensions), "All files", "*" }));
#elif UNITY_STANDALONE_WIN
        try
        {
            var ofn = new OpenFileName();
            ofn.structSize = Marshal.SizeOf(ofn);
            ofn.dlgOwner = GetActiveWindow();
            ofn.filter = kind + "\0" + "*." + string.Join(";*.", extensions) + "\0All files\0*.*\0\0";
            ofn.file = new string('\0', 2048);
            ofn.maxFile = ofn.file.Length;
            ofn.fileTitle = new string('\0', 512);
            ofn.maxFileTitle = ofn.fileTitle.Length;
            ofn.initialDir = startFolder;
            ofn.title = title;
            ofn.flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008;   // explorer, file and path must exist, keep the folder
            if (!GetOpenFileNameW(ofn)) return null;
            return NullIfEmpty(ofn.file.TrimEnd('\0'));
        }
        catch (Exception e)
        {
            Debug.LogWarning("NativeFiles: no file window: " + e.Message);
            return null;
        }
#else
        return null;
#endif
    }

    static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

    // ---- Drag and drop (Windows): listens for WM_DROPFILES on the game window ----

    static Action<string> onDrop;

    public static void StartDrop(Action<string> dropped)
    {
        onDrop = dropped;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (hook != IntPtr.Zero) return;
        try
        {
            window = GetActiveWindow();
            hookProc = HookCallback;
            hook = SetWindowsHookEx(WH_GETMESSAGE, hookProc, GetModuleHandle(null), GetCurrentThreadId());
            DragAcceptFiles(window, true);
        }
        catch (Exception e)
        {
            Debug.LogWarning("NativeFiles: no drag and drop: " + e.Message);
        }
#endif
    }

    public static void StopDrop()
    {
        onDrop = null;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (hook == IntPtr.Zero) return;
        try
        {
            DragAcceptFiles(window, false);
            UnhookWindowsHookEx(hook);
        }
        catch (Exception) { }
        hook = IntPtr.Zero;
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    const int WH_GETMESSAGE = 3;
    const uint WM_DROPFILES = 0x0233;
    static IntPtr hook, window;
    static HookProc hookProc;   // kept alive while the hook is set

    delegate IntPtr HookProc(int code, IntPtr wParam, ref Msg lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x, y;
    }

    [AOT.MonoPInvokeCallback(typeof(HookProc))]
    static IntPtr HookCallback(int code, IntPtr wParam, ref Msg msg)
    {
        if (code == 0 && msg.message == WM_DROPFILES)
        {
            IntPtr drop = msg.wParam;
            uint count = DragQueryFileW(drop, 0xFFFFFFFF, null, 0);
            for (uint i = 0; i < count; i++)
            {
                var path = new StringBuilder(1024);
                DragQueryFileW(drop, i, path, path.Capacity);
                try { onDrop?.Invoke(path.ToString()); }
                catch (Exception e) { Debug.LogWarning("NativeFiles: " + e); }
                break;   // one file is enough
            }
            DragFinish(drop);
        }
        return CallNextHookEx(hook, code, wParam, ref msg);
    }

    [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, ref Msg lParam);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("shell32.dll")] static extern void DragAcceptFiles(IntPtr hwnd, bool accept);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint DragQueryFileW(IntPtr drop, uint file, StringBuilder name, int size);
    [DllImport("shell32.dll")] static extern void DragFinish(IntPtr drop);
#endif

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    class OpenFileName
    {
        public int structSize;
        public IntPtr dlgOwner;
        public IntPtr instance;
        public string filter;
        public string customFilter;
        public int maxCustFilter;
        public int filterIndex;
        public string file;
        public int maxFile;
        public string fileTitle;
        public int maxFileTitle;
        public string initialDir;
        public string title;
        public int flags;
        public short fileOffset;
        public short fileExtension;
        public string defExt;
        public IntPtr custData;
        public IntPtr hook;
        public string templateName;
        public IntPtr reservedPtr;
        public int reservedInt;
        public int flagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)] static extern bool GetOpenFileNameW([In, Out] OpenFileName ofn);
#endif
}
