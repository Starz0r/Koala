using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Windows.UI.Xaml.Controls;

namespace Koala.Views
{
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        #region Default
        public MainPage()
        {
            InitalizeMpvDynamic();
            InitializeComponent();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Set<T>(ref T storage, T value, [CallerMemberName]string propertyName = null)
        {
            if (Equals(storage, value))
            {
                return;
            }

            storage = value;
            OnPropertyChanged(propertyName);
        }

        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        #endregion Default

        #region Delegates

        delegate IntPtr get_proc_address(IntPtr context, string name);

        delegate void mpv_opengl_cb_update_fn(IntPtr context);

        private IntPtr MyProcAddress(IntPtr context, string name)
        {
            System.Diagnostics.Debug.WriteLine(name);
            System.Diagnostics.Debug.WriteLine(Marshal.PtrToStringAnsi(context));
            //return wglGetProcAddress(name);
            //return GetProcAddress(context, name);
            return eglGetProcAddress(name);
        }



        #endregion Delegates

        #region mpv
        private const int MpvFormatString = 1;
        private IntPtr libmpv;
        private IntPtr mpvHandle;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false)]
        public static extern IntPtr LoadPackagedLibrary([MarshalAs(UnmanagedType.LPWStr)]string libraryName, int reserved = 0);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibrary(string librayName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false)]
        internal static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);

        [DllImport("libEGL.dll", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false)]
        internal static extern IntPtr eglGetProcAddress(string procedureName);

        [DllImport("opengl32.dll", SetLastError = true, CharSet = CharSet.Ansi, BestFitMapping = false)]
        internal static extern IntPtr wglGetProcAddress(string procedureName);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr _mpv_create();
        private _mpv_create mpv_create;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int _mpv_initialize(IntPtr mpv_handle);
        private _mpv_initialize mpv_initialize;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int _mpv_set_option_string(IntPtr mpv_handle, byte[] option, byte[] value);
        private _mpv_set_option_string mpv_set_option_string;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr _mpv_get_sub_api(IntPtr mpv_handle, int value);
        private _mpv_get_sub_api mpv_get_sub_api;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int _mpv_opengl_cb_init_gl(IntPtr context, byte[] exts, get_proc_address callback, IntPtr fnContext);
        private _mpv_opengl_cb_init_gl mpv_opengl_cb_init_gl;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int _mpv_opengl_cb_set_update_callback(IntPtr gl_context, mpv_opengl_cb_update_fn callback, IntPtr callback_context);
        private _mpv_opengl_cb_set_update_callback mpv_opengl_cb_set_update_callback;

        private object dlltypeof(Type type, string name)
        {
            IntPtr address = GetProcAddress(libmpv, name);
            if (address != IntPtr.Zero)
                return Marshal.GetDelegateForFunctionPointer(address, type);
            return null;
        }

        private static byte[] GetUtf8Bytes(string s)
        {
            return Encoding.UTF8.GetBytes(s + "\0");
        }


        private void LoadMpvDynamic()
        {
            libmpv = LoadPackagedLibrary("mpv.dll");
            mpv_create = (_mpv_create)dlltypeof(typeof(_mpv_create), "mpv_create");
            mpv_initialize = (_mpv_initialize)dlltypeof(typeof(_mpv_initialize), "mpv_initialize");
            mpv_set_option_string = (_mpv_set_option_string)dlltypeof(typeof(_mpv_set_option_string), "mpv_set_option_string");
            mpv_opengl_cb_init_gl = (_mpv_opengl_cb_init_gl)dlltypeof(typeof(_mpv_opengl_cb_init_gl), "mpv_opengl_cb_init_gl");
            mpv_get_sub_api = (_mpv_get_sub_api)dlltypeof(typeof(_mpv_get_sub_api), "mpv_get_sub_api");
            mpv_opengl_cb_set_update_callback = (_mpv_opengl_cb_set_update_callback)dlltypeof(typeof(_mpv_opengl_cb_set_update_callback), "mpv_opengl_cb_set_update_callback");
        }

        private void InitalizeMpvDynamic()
        {
            LoadMpvDynamic();

            mpvHandle = mpv_create.Invoke();
            mpv_initialize(mpvHandle);

            //debug
            Windows.Storage.StorageFolder storageFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
            mpv_set_option_string(mpvHandle, GetUtf8Bytes("log-file"), GetUtf8Bytes(@storageFolder.Path+@"\urbunshun.log"));
            mpv_set_option_string(mpvHandle, GetUtf8Bytes("msg-level"), GetUtf8Bytes("all=v"));

            mpv_set_option_string(mpvHandle, GetUtf8Bytes("vo"), GetUtf8Bytes("opengl-cb"));

            IntPtr mpv_gl_context = mpv_get_sub_api(mpvHandle, 1);

            mpv_opengl_cb_set_update_callback(mpv_gl_context, null, IntPtr.Zero);

            mpv_opengl_cb_init_gl(mpv_gl_context, null, MyProcAddress, IntPtr.Zero);

        }
        #endregion mpv
    }
}
