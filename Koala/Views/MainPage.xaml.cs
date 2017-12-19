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
        public static IntPtr mpvGLContext;
        Mpv mpv;

        #endregion OpenGL

        #region Events
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
        #endregion Events

        #region Methods
        private IntPtr MyProcAddress(IntPtr context, string name)
        {
            System.Diagnostics.Debug.WriteLine(name);
            //System.Diagnostics.Debug.WriteLine(Marshal.PtrToStringAnsi(context));
            return mOpenGLES.GetProcAddress(name);
        }

        private async void DrawNextFrame(IntPtr context)
        {
            // Wait for the UI Thread to run to render the next frame.
            await videoBox.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal, new DispatchedHandler(() =>
            {
                // Get the Width and Height of the Window (it can change at anytime)
                int w = (int)((Frame)Window.Current.Content).ActualWidth;
                int h = (int)((Frame)Window.Current.Content).ActualHeight;

                // Draw the next frame, swap buffers, and report the frame has been flipped
                Mpv.OpenGLCallbackDraw(mpvGLContext, 0, w, -h);
                mOpenGLES.SwapBuffers(mRenderSurface);
                Mpv.OpenGLCallbackReportFlip(mpvGLContext);

                // Stop the render loop to prevent the frame from being overwritten
                StopRenderLoop();
            }));

            return;
        }

        private IntPtr mpv_handle;

        private void InitalizeMpvDynamic()
        {
            mOpenGLES.MakeCurrent(mRenderSurface);

            Mpv mpv = new Mpv();

            mpv_handle = mpv.Create();
            mpv.Initalize(mpv_handle);

            Windows.Storage.StorageFolder storageFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
            mpv.SetOptionString(mpv_handle, Mpv.GetUtf8Bytes("log-file"), Mpv.GetUtf8Bytes(@storageFolder.Path + @"\koala.log"));
            mpv.SetOptionString(mpv_handle, Mpv.GetUtf8Bytes("msg-level"), Mpv.GetUtf8Bytes("all=v"));
            mpv.SetOptionString(mpv_handle, Mpv.GetUtf8Bytes("vo"), Mpv.GetUtf8Bytes("opengl-cb"));

            mpvGLContext = mpv.GetSubApi(mpv_handle, 1);

            mpv.OpenGLCallbackInitialize(mpvGLContext, null, MyProcAddress, IntPtr.Zero);
            Mpv.OpenGLCallbackSetUpdate(mpvGLContext, DrawNextFrame, IntPtr.Zero);

            mpv.ExecuteCommand(mpv_handle, "loadfile", "http://download.blender.org/peach/bigbuckbunny_movies/BigBuckBunny_640x360.m4v");

        }
        #endregion Methods
    }
}
