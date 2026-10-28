using System.Runtime.InteropServices;
using DesktopDeck.Layout;

namespace DesktopDeck.Services;

/// <summary>
/// Where Explorer has put the desktop icons, read through the shell's own objects (IShellWindows → the desktop's browser → its view →
/// IFolderView), never with list-view messages into another process. Read only: no icon is ever moved. The COM part lives here and nowhere else;
/// the placement itself is <see cref="IconSpace"/> (pure).
/// </summary>
public static class DesktopIcons
{
    /// <summary>The icon cells on screen in physical pixels (none when Explorer shows no icons), or null when the shell could not be asked.</summary>
    /// <summary>Why the last <see cref="Read"/> returned null (for the host test and diagnostics).</summary>
    public static string LastProblem { get; private set; } = "";

    public static List<IconBox>? Read()
    {
        LastProblem = "";
        try { return ReadCore(); }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or InvalidOperationException or NotSupportedException
                                       or MarshalDirectiveException or SEHException or DllNotFoundException or EntryPointNotFoundException or UnauthorizedAccessException)
        {
            LastProblem = ex.GetType().Name + ": " + ex.Message;
            return null;
        }
    }

    private static List<IconBox>? ReadCore()
    {
        var type = Type.GetTypeFromCLSID(ClsidShellWindows);
        if (type is null || Activator.CreateInstance(type) is not IShellWindows windows) { LastProblem = "IShellWindows is not available"; return null; }
        object? view = null;
        try
        {
            object location = 0; // CSIDL_DESKTOP
            object? root = null;
            windows.FindWindowSW(ref location, ref root, SwcDesktop, out _, SwfoNeedDispatch, out object? dispatch);
            if (dispatch is not IServiceProvider services) { LastProblem = "the shell has no desktop window to ask"; return null; }
            var service = SidTopLevelBrowser;
            var browserId = typeof(IShellBrowser).GUID;
            services.QueryService(ref service, ref browserId, out object browser);
            ((IShellBrowser)browser).QueryActiveShellView(out view);
            if (view is not IFolderView folder) { LastProblem = "the desktop view has no IFolderView"; return null; }

            // The view's window: the icon view that sits beside the decks (Explorer's own, found the way the decks find their parent).
            nint defView = 0;
            EnumWindows((top, _) =>
            {
                defView = FindWindowEx(top, 0, "SHELLDLL_DefView", null);
                return defView == 0;
            }, 0);
            nint list = defView == 0 ? 0 : FindWindowEx(defView, 0, "SysListView32", null);
            // Icons turned off in Explorer's View menu: the list is hidden and nothing is in the way.
            if (list == 0) { LastProblem = "the desktop list view was not found"; return null; }
            if (!IsWindowVisible(list)) return [];
            var origin = new NativePoint();
            if (!ClientToScreen(list, ref origin)) return null;

            folder.ItemCount(ViewAll, out int count);
            if (count < 0 || count > 5000) return null;
            var spacing = new NativePoint { X = 76, Y = 100 };
            try { folder.GetSpacing(out spacing); } catch (COMException) { }
            if (spacing.X <= 0 || spacing.Y <= 0) spacing = new NativePoint { X = 76, Y = 100 };

            var cells = new List<IconBox>(count);
            for (int i = 0; i < count; i++)
            {
                nint pidl = 0;
                try
                {
                    folder.Item(i, out pidl);
                    folder.GetItemPosition(pidl, out var position);
                    cells.Add(new IconBox(origin.X + position.X, origin.Y + position.Y, spacing.X, spacing.Y));
                }
                catch (COMException) { }
                finally { if (pidl != 0) Marshal.FreeCoTaskMem(pidl); }
            }
            return cells;
        }
        finally
        {
            if (view is not null && Marshal.IsComObject(view)) Marshal.FinalReleaseComObject(view);
            Marshal.FinalReleaseComObject(windows);
        }
    }

    private const int SwcDesktop = 8, SwfoNeedDispatch = 1;
    private const uint ViewAll = 2; // SVGIO_ALLVIEW
    private static readonly Guid ClsidShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SidTopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }

    // IDispatch's four methods come first in the vtable; they are never called here.
    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellWindows
    {
        void GetTypeInfoCount(); void GetTypeInfo(); void GetIDsOfNames(); void Invoke();
        void Count(); void Item(); void NewEnum(); void Register(); void RegisterPending(); void Revoke(); void OnNavigate(); void OnActivated();
        void FindWindowSW([MarshalAs(UnmanagedType.Struct)] ref object location, [MarshalAs(UnmanagedType.Struct)] ref object? root, int windowClass, out int hwnd, int options,
            [MarshalAs(UnmanagedType.IDispatch)] out object? dispatch);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        void QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object result);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow(); void ContextSensitiveHelp();
        void InsertMenusSB(); void SetMenuSB(); void RemoveMenusSB(); void SetStatusTextSB(); void EnableModelessSB(); void TranslateAcceleratorSB();
        void BrowseObject(); void GetViewStateStream(); void GetControlWindow(); void SendControlMsg();
        void QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out object view);
    }

    [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderView
    {
        void GetCurrentViewMode(); void SetCurrentViewMode(); void GetFolder();
        void Item(int index, out nint pidl);
        void ItemCount(uint flags, out int count);
        void Items(); void GetSelectionMarkedItem(); void GetFocusedItem();
        void GetItemPosition(nint pidl, out NativePoint position);
        void GetSpacing(out NativePoint spacing);
    }

    private delegate bool EnumProc(nint hwnd, nint data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint hwnd, ref NativePoint point);
}
