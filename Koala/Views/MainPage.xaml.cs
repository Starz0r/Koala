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
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.Activation;
using Windows.UI.Popups;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Navigation;
using System.IO;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;
using System.Globalization;

namespace Koala.Views
{
    public sealed partial class MainPage : Page, INotifyPropertyChanged
    {
        #region Properties
        public MainPage()
        {
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
        #endregion Properties

        #region Events
        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            InitializeComponent();

            var args = e.Parameter as IActivatedEventArgs;

            // Check if any arguments were passed
            if (args != null)
            {
                // Open with...
                if (args.Kind == ActivationKind.File)
                {
                    // Get the first file
                    // TODO: Get all the files and put them in a collection
                    var fileArgs = args as FileActivatedEventArgs;
                    string strFilePath = fileArgs.Files[0].Path;
                    var file = (StorageFile)fileArgs.Files[0];

                    VideoPlayer.streamcb_file = (StorageFile)fileArgs.Files[0];
                    await Task.Run(() =>
                    {
                        Task<Stream> tmp = VideoPlayer.streamcb_file.OpenStreamForReadAsync();
                        VideoPlayer.streamcb_stream = tmp.Result;
                    });

                    while (VideoPlayer.MediaPlayer == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Internal MediaPlayer Core still uninitialized, waiting...");
                    }

                    VideoPlayer.MediaPlayer.StreamCbAddReadOnly("buffer", strFilePath, VideoPlayer.StreamCbOpenFn);
                    VideoPlayer.MediaPlayer.ExecuteCommand("loadfile", "buffer://fake");

                    var prop = VideoPlayer.MediaPlayer.GetProperty("duration");
                    while (prop == null) {
                        System.Diagnostics.Debug.WriteLine("property was null, retrying");
                        prop = VideoPlayer.MediaPlayer.GetProperty("duration");
                    }
                    Double duration = Double.Parse(prop, CultureInfo.InvariantCulture.NumberFormat);
                    prop = VideoPlayer.MediaPlayer.GetProperty("demuxer-rawvideo-fps");
                    while (prop == null)
                    {
                        System.Diagnostics.Debug.WriteLine("property was null, retrying");
                        prop = VideoPlayer.MediaPlayer.GetProperty("duration");
                    }
                    Double fps = Double.Parse(prop, CultureInfo.InvariantCulture.NumberFormat);
                    VideoControls.SetSliderDuration(duration, 1/fps);
                }
            }
        }
        #endregion Events
    }
}
