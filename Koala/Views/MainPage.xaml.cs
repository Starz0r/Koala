using System;
using System.ComponentModel;
using System.Reflection;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using Windows.Foundation;
using Windows.System.Threading;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Koala.Views
{
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        #region OpenGL

        private IntPtr mRenderSurface;
        private OpenGLES mOpenGLES;
        private object mRenderSurfaceCriticalSection = new object();
        private IAsyncAction mRenderLoopWorker;
        IntPtr mpvGLContext;
        private static EventWaitHandle waitHandle = new ManualResetEvent(initialState: true);

        #endregion OpenGL

        #region Default
        public MainPage()
        {
            InitializeComponent();

            mOpenGLES = new OpenGLES();
            mRenderSurface = OpenGLES.EGL_NO_SURFACE;

            Window.Current.VisibilityChanged += OnVisibilityChanged;

            Loaded += OnPageLoaded;
            Unloaded += OnPageUnloaded;
        }


        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            // The SwapChainPanel has been created and arranged in the page layout, so EGL can be initialized. 
            CreateRenderSurface();
            StartRenderLoop();
            InitalizeMpvDynamic();
        }

        private void OnPageUnloaded(object sender, RoutedEventArgs e)
        {
            StopRenderLoop();
            DestroyRenderSurface();
        }

        private void OnVisibilityChanged(object sender, VisibilityChangedEventArgs e)
        {
            if (e.Visible && mRenderSurface != OpenGLES.EGL_NO_SURFACE)
            {
                StartRenderLoop();
            }
            else
            {
                StopRenderLoop();
            }
        }

        private void CreateRenderSurface()
        {
            if (mOpenGLES != null && mRenderSurface == OpenGLES.EGL_NO_SURFACE)
            {
                mRenderSurface = mOpenGLES.CreateSurface(videoBox);
            }
        }

        private void DestroyRenderSurface()
        {
            if (mOpenGLES == null)
            {
                mOpenGLES.DestroySurface(mRenderSurface);
            }
            mRenderSurface = OpenGLES.EGL_NO_SURFACE;
        }

        private void RecoverFromLostDevice()
        {
            // Stops the render loop, reset OpenGLES, recreates the render surface
            // and starts the render loop again to recover from a lost device.
            StopRenderLoop();

            lock (mRenderSurfaceCriticalSection)
            {
                DestroyRenderSurface();
                mOpenGLES.Reset();
                CreateRenderSurface();
            }
            StartRenderLoop();
        }

        private void StartRenderLoop()
        {
            // If the render loop is already running then do not start another thread
            if (mRenderLoopWorker != null && mRenderLoopWorker.Status == AsyncStatus.Started)
            {
                return;
            }

            // Run task on a dedicated high priority background thread.
            mRenderLoopWorker = ThreadPool.RunAsync(RenderLoop, WorkItemPriority.High, WorkItemOptions.TimeSliced);
        }

        private void RenderLoop(IAsyncAction action)
        {
            lock (mRenderSurfaceCriticalSection)
            {
                mOpenGLES.MakeCurrent(mRenderSurface);

                SimpleRenderer renderer = new SimpleRenderer();

                while (action.Status == AsyncStatus.Started)
                {
                    var size = mOpenGLES.GetSurfaceDimensions(mRenderSurface);

                    // Logic to update the scene could go here

                    renderer.UpdateWindowSize(size);
                    renderer.Draw();

                    // The call to the eglSawpBuffers might not be successful (i.e. due to Device Lost)
                    // If the call fails, then we must reinitialize EGL and the GL resources.
                    if (mOpenGLES.SwapBuffers(mRenderSurface) != OpenGLES.EGL_TRUE)
                    {
                        // XAML objects like the SwapChainPanel must only be manipulated on the UI thread.
                        videoBox.Dispatcher.RunAsync(CoreDispatcherPriority.High, new DispatchedHandler(() => { RecoverFromLostDevice(); }));

                        return;
                    }
                }
            }
        }

        private void StopRenderLoop()
        {
            if (mRenderLoopWorker != null)
            {
                mRenderLoopWorker.Cancel();
                mRenderLoopWorker = null;
            }
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
            //System.Diagnostics.Debug.WriteLine(Marshal.PtrToStringAnsi(context));
            return mOpenGLES.GetProcAddress(name);
        }

        private async void DrawNextFrame(IntPtr context)
        {
            //await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, () =>
            await videoBox.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, () =>
            {
                mpv_opengl_cb_draw(mpvGLContext, 0, 917, -770);

                mOpenGLES.SwapBuffers(mRenderSurface);

                StopRenderLoop();
            });

            return;
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
        private delegate int _mpv_command(IntPtr mpvHandle, IntPtr strings);
        private _mpv_command mpv_command;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr _mpv_get_sub_api(IntPtr mpv_handle, int value);
        private _mpv_get_sub_api mpv_get_sub_api;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int _mpv_opengl_cb_init_gl(IntPtr context, byte[] exts, get_proc_address callback, IntPtr fnContext);
        private _mpv_opengl_cb_init_gl mpv_opengl_cb_init_gl;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int _mpv_opengl_cb_set_update_callback(IntPtr gl_context, mpv_opengl_cb_update_fn callback, IntPtr callback_context);
        private _mpv_opengl_cb_set_update_callback mpv_opengl_cb_set_update_callback;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int _mpv_opengl_cb_draw(IntPtr context, int fbo, int width, int height);
        private _mpv_opengl_cb_draw mpv_opengl_cb_draw;

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
            mpv_command = (_mpv_command)dlltypeof(typeof(_mpv_command), "mpv_command");
            mpv_opengl_cb_draw = (_mpv_opengl_cb_draw)dlltypeof(typeof(_mpv_opengl_cb_draw), "mpv_opengl_cb_draw");
        }

        private void InitalizeMpvDynamic()
        {
            mOpenGLES.MakeCurrent(mRenderSurface);

            LoadMpvDynamic();

            mpvHandle = mpv_create.Invoke();
            mpv_initialize(mpvHandle);

            //debug
            Windows.Storage.StorageFolder storageFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
            mpv_set_option_string(mpvHandle, GetUtf8Bytes("log-file"), GetUtf8Bytes(@storageFolder.Path+@"\urbunshun.log"));
            mpv_set_option_string(mpvHandle, GetUtf8Bytes("msg-level"), GetUtf8Bytes("all=v"));

            mpv_set_option_string(mpvHandle, GetUtf8Bytes("vo"), GetUtf8Bytes("opengl-cb"));

            mpvGLContext = mpv_get_sub_api(mpvHandle, 1);

            mpv_opengl_cb_init_gl(mpvGLContext, null, MyProcAddress, IntPtr.Zero);

            mpv_opengl_cb_set_update_callback(mpvGLContext, DrawNextFrame, IntPtr.Zero);

            DoMpvCommand("loadfile", "http://download.blender.org/peach/bigbuckbunny_movies/BigBuckBunny_640x360.m4v");
        }

        private void DoMpvCommand(params string[] args)
        {
            IntPtr[] byteArrayPointers;
            var mainPtr = AllocateUtf8IntPtrArrayWithSentinel(args, out byteArrayPointers);
            mpv_command(mpvHandle, mainPtr);
            foreach (var ptr in byteArrayPointers)
            {
                Marshal.FreeHGlobal(ptr);
            }
            Marshal.FreeHGlobal(mainPtr);
        }

        public static IntPtr AllocateUtf8IntPtrArrayWithSentinel(string[] arr, out IntPtr[] byteArrayPointers)
        {
            int numberOfStrings = arr.Length + 1; // add extra element for extra null pointer last (sentinel)
            byteArrayPointers = new IntPtr[numberOfStrings];
            IntPtr rootPointer = Marshal.AllocCoTaskMem(IntPtr.Size * numberOfStrings);
            for (int index = 0; index < arr.Length; index++)
            {
                var bytes = GetUtf8Bytes(arr[index]);
                IntPtr unmanagedPointer = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, unmanagedPointer, bytes.Length);
                byteArrayPointers[index] = unmanagedPointer;
            }
            Marshal.Copy(byteArrayPointers, 0, rootPointer, numberOfStrings);
            return rootPointer;
        }


        #endregion mpv
    }
}
