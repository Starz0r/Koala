using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Nito.AsyncEx;
using Windows.ApplicationModel;
using Windows.Foundation.Diagnostics;
using Windows.Media.Core;
using Windows.Media.Devices;
using Windows.Storage;
using Windows.System.Profile;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;


using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Windows.Foundation;
using Windows.System.Threading;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.Activation;
using Windows.UI.Popups;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Navigation;
using Windows.Storage.AccessCache;
using Windows.Storage.Pickers;

namespace Koala.ViewModels
{
    /// <summary>
    /// Represents a control that contains audio and/or video.
    /// </summary>
    public
#if !CLASS_LIBRARY
    sealed
#endif
    class MediaElement : Control
    {
        #region Properties
        public enum MediaState
        {
            Manual,
            Ended,
            Paused,
            Stopped,
            Error,
            NothingSpecial,
            Playing,
        }

        public enum MediaElementState
        {
            //
            // Summary:
            //     The MediaElement contains no media. The MediaElement displays a transparent frame.
            Closed = 0,
            //
            // Summary:
            //     The MediaElement is validating and attempting to load the specified source.
            Opening = 1,
            //
            // Summary:
            //     The MediaElement is loading the media for playback. Its Position does not advance
            //     during this state. If the MediaElement was already playing video, it continues
            //     to display the last displayed frame.
            Buffering = 2,
            //
            // Summary:
            //     The MediaElement is playing the current media source.
            Playing = 3,
            //
            // Summary:
            //     The MediaElement does not advance its Position. If the MediaElement was playing
            //     video, it continues to display the current frame.
            Paused = 4,
            //
            // Summary:
            //     The MediaElement contains media but is not playing or paused. Its Position is
            //     0 and does not advance. If the loaded media is video, the MediaElement displays
            //     the first frame.
            Stopped = 5
        }

        private static SemaphoreSlim s_logSemaphoreSlim = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Occurs when zoom has changed.
        /// </summary>
        public event RoutedEventHandler ZoomChanged;

        /// <summary>
        /// Occurs when deinterlace mode has changed.
        /// </summary>
        public event RoutedEventHandler DeinterlaceModeChanged;

        /// <summary>
        /// Occurs when the current state has changed.
        /// </summary>
        public event RoutedEventHandler CurrentStateChanged;

        /// <summary>
        /// Occurs when a login dialog box must be shown.
        /// </summary>
        public event EventHandler<LoginDialogEventArgs> ShowLoginDialog;

        /// <summary>
        /// Occurs when a dialog box must be shown.
        /// </summary>
        public event EventHandler<DialogEventArgs> ShowDialog;

        /// <summary>
        /// Occurs when the current dialog box must be cancelled.
        /// </summary>
        public event EventHandler<DeferrableEventArgs> CancelCurrentDialog;

        /// <summary>
        /// Occurs before a message is logged.
        /// </summary>
        public event EventHandler<LogRoutedEventArgs> Logging;

        /// <summary>
        /// Occurs when the media stream has been validated and opened.
        /// </summary>
        public event EventHandler<RoutedEventArgs> MediaOpened;

        /// <summary>
        /// Occurs when the MediaElement finishes playing audio or video.
        /// </summary>
        public event EventHandler<RoutedEventArgs> MediaEnded;

        /// <summary>
        /// Occurs when there is an error associated with the media source.
        /// </summary>
        public event EventHandler<MediaFailedRoutedEventArgs> MediaFailed;

        private IntPtr mRenderSurface;
        private OpenGLES mOpenGLES;
        private object mRenderSurfaceCriticalSection = new object();
        private IAsyncAction mRenderLoopWorker;

        private GCHandle streamcb_current_file_allocated;
        public StorageFile streamcb_file;
        private IntPtr streamcb_file_pointer = IntPtr.Zero;
        private String streamcb_userdata;
        public Stream streamcb_stream;
        private Byte[] streamcb_buffer;
        private UInt64 streamcb_buffer_size;
        private IntPtr streamcb_buffer_reference;

        private Mpv.MyStreamCbReadFn streamcb_callback_read_method;
        private IntPtr streamcb_callback_read_ptr;
        private GCHandle streamcb_callback_read_gc;

        private Mpv.MyStreamCbSeekFn streamcb_callback_seek_method;
        private IntPtr streamcb_callback_seek_ptr;
        private GCHandle streamcb_callback_seek_gc;

        private Mpv.MyStreamCbSizeFn streamcb_callback_size_method;
        private IntPtr streamcb_callback_size_ptr;
        private GCHandle streamcb_callback_size_gc;

        private Mpv.MyStreamCbCloseFn streamcb_callback_close_method;
        private IntPtr streamcb_callback_close_ptr;
        private GCHandle streamcb_callback_close_gc;
        #endregion

        /// <summary>
        /// Instantiates a new instance of the MediaElement class.
        /// </summary>
        public MediaElement()
        {
            DefaultStyleKey = typeof(MediaElement);
            //Unloaded += (sender, e) => { Source = null; Logger.RemoveLoggingChannel(LoggingChannel); };

            // Start OpenGLES from ANGLE
            mOpenGLES = new OpenGLES();
            mRenderSurface = OpenGLES.EGL_NO_SURFACE;

            // Hook handlers into events
            Window.Current.VisibilityChanged += OnVisibilityChanged;

            Loaded += OnElementLoaded;
            Unloaded += OnElementUnloaded;
        }

        private SwapChainPanel SwapChainPanel { get; set; }
        //private Instance Instance { get; set; }
        //private Media Media { get; set; }
        private LoggingChannel LoggingChannel { get; set; }
        private AsyncLock SourceChangedMutex { get; } = new AsyncLock();
        private bool UpdatingPosition { get; set; }

        private float VideoScale
        {
            get => MediaPlayer.scale();
            set
            {
                if (VideoScale != value)
                {
                    MediaPlayer.setScale(value);
                }
            }
        }

        private string _audioDeviceId;
        private string AudioDeviceId
        {
            get => _audioDeviceId;
            set
            {
                if (_audioDeviceId != value)
                {
                    _audioDeviceId = value;
                    SetAudioDevice();
                }
            }
        }

        /// <summary>
        /// Gets the underlying media player
        /// </summary>
        public Mpv MediaPlayer { get; private set; }

        /// <summary>
        /// Gets or sets the keystore filename
        /// </summary>
        public string KeyStoreFilename { get; set; } = "mpv_MediaElement_KeyStore";

        /// <summary>
        /// Gets or sets the log filename
        /// </summary>
        public string LogFilename { get; set; } = "mpv_MediaElement_Log.etl";

        /// <summary>
        /// Gets the current state.
        /// </summary>
        internal MediaState State => MediaPlayer?.state() ?? MediaState.NothingSpecial;

        /// <summary>
        /// Identifies the <see cref="IsMuted"/> dependency property.
        /// </summary>
        public static DependencyProperty IsMutedProperty { get; } = DependencyProperty.Register(nameof(IsMuted), typeof(bool), typeof(MediaElement),
            new PropertyMetadata(false, (d, e) => ((MediaElement)d).OnIsMutedChanged()));
        /// <summary>
        /// Gets or sets a value indicating whether the audio is muted.
        /// </summary>
        public bool IsMuted
        {
            get => (bool)GetValue(IsMutedProperty);
            set => SetValue(IsMutedProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="Position"/> dependency property.
        /// </summary>
        public static DependencyProperty PositionProperty { get; } = DependencyProperty.Register(nameof(Position), typeof(TimeSpan), typeof(MediaElement),
            new PropertyMetadata(TimeSpan.Zero, (d, e) => ((MediaElement)d).OnPositionChanged()));
        /// <summary>
        /// Gets or sets the current position of progress through the media's playback time.
        /// </summary>
        public TimeSpan Position
        {
            get => (TimeSpan)GetValue(PositionProperty);
            set => SetValue(PositionProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="Volume"/> dependency property.
        /// </summary>
        public static DependencyProperty VolumeProperty { get; } = DependencyProperty.Register(nameof(Volume), typeof(int), typeof(MediaElement),
            new PropertyMetadata(100, (d, e) => ((MediaElement)d).OnVolumeChanged()));
        /// <summary>
        /// Gets or sets the media's volume.
        /// </summary>
        public int Volume
        {
            get => (int)GetValue(VolumeProperty);
            set => SetValue(VolumeProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="CurrentState"/> dependency property.
        /// </summary>
        public static DependencyProperty CurrentStateProperty { get; } = DependencyProperty.Register(nameof(CurrentState), typeof(MediaElementState), typeof(MediaElement),
            new PropertyMetadata(MediaElementState.Closed, (d, e) => ((MediaElement)d).OnCurrentStateChanged()));
        /// <summary>
        /// Gets the current state.
        /// </summary>
        public MediaElementState CurrentState
        {
            get => (MediaElementState)GetValue(CurrentStateProperty);
            private set => SetValue(CurrentStateProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="TransportControls"/> dependency property.
        /// </summary>
        public static DependencyProperty TransportControlsProperty { get; } = DependencyProperty.Register(nameof(TransportControls), typeof(MediaTransportControls), typeof(MediaElement),
            new PropertyMetadata(null, OnTransportControlsPropertyChanged));
        /// <summary>
        /// Gets or sets the transport controls for the media.
        /// </summary>
        public MediaTransportControls TransportControls
        {
            get => (MediaTransportControls)GetValue(TransportControlsProperty);
            set => SetValue(TransportControlsProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="AreTransportControlsEnabled"/> dependency property.
        /// </summary>
        public static DependencyProperty AreTransportControlsEnabledProperty { get; } = DependencyProperty.Register(nameof(AreTransportControlsEnabled), typeof(bool), typeof(MediaElement),
            new PropertyMetadata(false, (d, e) => UpdateMediaTransportControls(d)));
        /// <summary>
        /// Gets or sets a value that determines whether the standard transport controls are enabled.
        /// </summary>
        public bool AreTransportControlsEnabled
        {
            get => (bool)GetValue(AreTransportControlsEnabledProperty);
            set => SetValue(AreTransportControlsEnabledProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="Zoom"/> dependency property.
        /// </summary>
        public static DependencyProperty ZoomProperty { get; } = DependencyProperty.Register(nameof(Zoom), typeof(bool), typeof(MediaElement),
            new PropertyMetadata(false, async (d, e) => await ((MediaElement)d).OnZoomChangedAsync()));
        /// <summary>
        /// Gets or sets a value indicating whether the video is zoomed.
        /// </summary>
        public bool Zoom
        {
            get => (bool)GetValue(ZoomProperty);
            set => SetValue(ZoomProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="HardwareAcceleration"/> dependency property.
        /// </summary>
        public static DependencyProperty HardwareAccelerationProperty { get; } = DependencyProperty.Register(nameof(HardwareAcceleration), typeof(bool), typeof(MediaElement),
            new PropertyMetadata(true, (d, e) => ((MediaElement)d).TransportControls?.UpdateDeinterlaceModeButton()));
        /// <summary>
        /// Gets or sets a value indicating whether the hardware acceleration must be used.
        /// </summary>
        public bool HardwareAcceleration
        {
            get => (bool)GetValue(HardwareAccelerationProperty);
            set => SetValue(HardwareAccelerationProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="Source"/> dependency property.
        /// </summary>
        /*public static DependencyProperty SourceProperty { get; } = DependencyProperty.Register(nameof(Source), typeof(string), typeof(MediaElement),
            new PropertyMetadata(null, (d, e) => ((MediaElement)d).MediaSource = e.NewValue == null ? null : VLC.MediaSource.CreateFromUri((string)e.NewValue)));*/
        /// <summary>
        /// Gets or sets a media source on the MediaElement.
        /// </summary>
        /*public string Source
        {
            get => GetValue(SourceProperty) as string;
            set => SetValue(SourceProperty, value);
        }*/

        /// <summary>
        /// Identifies the <see cref="MediaSource"/> dependency property.
        /// </summary>
        /*public static DependencyProperty MediaSourceProperty { get; } = DependencyProperty.Register(nameof(MediaSource), typeof(IMediaSource), typeof(MediaElement),
            new PropertyMetadata(null, async (d, e) => await ((MediaElement)d).OnMediaSourceChangedAsync(e.OldValue as IMediaSource, e.NewValue as IMediaSource)));*/
        /// <summary>
        /// Gets or sets a media source on the MediaElement.
        /// </summary>
        /*public IMediaSource MediaSource
        {
            get => GetValue(MediaSourceProperty) as IMediaSource;
            set => SetValue(MediaSourceProperty, value);
        }*/

        /// <summary>
        /// Identifies the <see cref="AutoPlay"/> dependency property.
        /// </summary>
        public static DependencyProperty AutoPlayProperty { get; } = DependencyProperty.Register(nameof(AutoPlay), typeof(bool), typeof(MediaElement),
            new PropertyMetadata(true));
        /// <summary>
        /// Gets or sets a value indicating whether media will begin playback automatically when the <see cref="Source"/> property is set.
        /// </summary>
        public bool AutoPlay
        {
            get => (bool)GetValue(AutoPlayProperty);
            set => SetValue(AutoPlayProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="PosterSource"/> dependency property.
        /// </summary>
        public static DependencyProperty PosterSourceProperty { get; } = DependencyProperty.Register(nameof(PosterSource), typeof(ImageSource), typeof(MediaElement),
            new PropertyMetadata(null));
        /// <summary>
        /// Gets or sets the image source that is used for a placeholder image during MediaElement loading transition states.
        /// </summary>
        public ImageSource PosterSource
        {
            get => (ImageSource)GetValue(PosterSourceProperty);
            set => SetValue(PosterSourceProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="Stretch"/> dependency property.
        /// </summary>
        public static DependencyProperty StretchProperty { get; } = DependencyProperty.Register(nameof(Stretch), typeof(Stretch), typeof(MediaElement),
            new PropertyMetadata(Stretch.Uniform));
        /// <summary>
        /// Gets or sets a value that describes how an MediaElement should be stretched to fill the destination rectangle.
        /// </summary>
        public Stretch Stretch
        {
            get => (Stretch)GetValue(StretchProperty);
            set => SetValue(StretchProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="Options"/> dependency property.
        /// </summary>
        public static DependencyProperty OptionsProperty { get; } = DependencyProperty.Register(nameof(Options), typeof(IDictionary<string, object>), typeof(MediaElement),
            new PropertyMetadata(new Dictionary<string, object>()));
        /// <summary>
        /// Gets or sets the options for the media
        /// </summary>
        public IDictionary<string, object> Options
        {
            get => (IDictionary<string, object>)GetValue(OptionsProperty);
            set => SetValue(OptionsProperty, value);
        }

        /// <summary>
        /// Invoked whenever application code or internal processes (such as a rebuildinglayout pass) call ApplyTemplate. 
        /// In simplest terms, this means the method is called just before a UI element displays in your app.
        /// Override this method to influence the default post-template logic of a class.
        /// </summary>
        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            var swapChainPanel = (SwapChainPanel)GetTemplateChild("SwapChainPanel");
            SwapChainPanel = swapChainPanel;
            swapChainPanel.CompositionScaleChanged += async (sender, e) => await UpdateScaleAsync();
            swapChainPanel.SizeChanged += async (sender, e) => await UpdateSizeAsync();
        }

        private static void OnTransportControlsPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is MediaTransportControls transportControls)
            {
                transportControls.MediaElement = ((MediaElement)d);
            }
        }

        private static void UpdateMediaTransportControls(DependencyObject d)
        {
            var mediaElement = (MediaElement)d;
            if (!mediaElement.AreTransportControlsEnabled)
            {
                return;
            }

            var mediaTransportControls = mediaElement.TransportControls;
            if (mediaTransportControls == null)
            {
                mediaElement.TransportControls = new MediaTransportControls();
            }
        }

        private void OnIsMutedChanged()
        {
            MediaPlayer?.setVolume(IsMuted ? 0 : Volume);
            TransportControls?.UpdateMuteState();
        }

        private void OnPositionChanged()
        {
            if (!UpdatingPosition)
            {
                var mediaPlayer = MediaPlayer;
                if (mediaPlayer != null)
                {
                    var length = mediaPlayer.length();
                    if (length > 0)
                    {
                        mediaPlayer.setPosition((float)(Position.TotalMilliseconds / length));
                    }
                }
            }
        }

        private void OnVolumeChanged()
        {
            if (!IsMuted)
            {
                MediaPlayer?.setVolume(Volume);
            }
            TransportControls?.UpdateVolume();
        }

        private void OnCurrentStateChanged()
        {
            CurrentStateChanged?.Invoke(this, new RoutedEventArgs());
        }

        private async Task OnZoomChangedAsync()
        {
            ZoomChanged?.Invoke(this, new RoutedEventArgs());
            await UpdateZoomAsync();
            TransportControls?.UpdateZoomButton();
        }

        private async Task InitAsync(SwapChainPanel swapChainPanel)
        {
            /*LoggingChannel = Logger.AddLoggingChannel(Name);
            var instance = new Instance(new List<string>
                {
                    "-I",
                    "dummy",
                    "--no-osd",
                    "--verbose=3",
                    "--no-stats",
                    "--avcodec-fast",
                    "--subsdec-encoding",
                    string.Empty,
                    AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Mobile" ? "--deinterlace-mode=bob" : string.Empty,
                    "--aout=winstore",
                    $"--keystore-file={Path.Combine(ApplicationData.Current.LocalFolder.Path, KeyStoreFilename)}"
                }, swapChainPanel);
            Instance = instance;
            instance.logSet((param0, param1) => OnLog((LogLevel)param0, param1));

            await UpdateScaleAsync();

            AudioDeviceId = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
            MediaDevice.DefaultAudioRenderDeviceChanged += (sender, e) => { if (e.Role == AudioDeviceRole.Default) { AudioDeviceId = e.Id; } };

            await OnMediaSourceChangedAsync();*/
        }

        private async void EventManager_OnPositionChangedAsync(float position)
        {
            await UpdateStateAsync(MediaElementState.Playing);
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => TransportControls?.OnPositionChanged(position));
        }

        private async void EventManager_OnTimeChangedAsync(long time)
        {
            await SetPositionAsync(time);
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => TransportControls?.OnTimeChanged(time));
        }

        private async void OnOpeningAsync()
        {
            await UpdateStateAsync(MediaElementState.Opening);
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => MediaOpened?.Invoke(this, new RoutedEventArgs()));
        }

        private async void OnEndReachedAsync()
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
            {
                var autoRepeatEnabled = TransportControls?.AutoRepeatEnabled ?? false;
                if (!autoRepeatEnabled)
                {
                    await ClearMediaAsync();
                    await UpdateStateAsync(MediaElementState.Closed);
                }
                MediaEnded?.Invoke(this, new RoutedEventArgs());
                if (autoRepeatEnabled)
                {
                    Stop();
                    Play();
                }
            });
        }

        private async Task UpdateStateAsync(MediaElementState state)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                var previousState = CurrentState;
                if (previousState != state)
                {
                    CurrentState = state;
                    TransportControls?.UpdateState(previousState, state);
                }
            });
        }

        private async Task UpdateZoomAsync()
        {
            /*if (MediaPlayer == null)
            {
                return;
            }

            var swapChainPanel = SwapChainPanel;
            var screenWidth = swapChainPanel.ActualWidth;
            var screenHeight = swapChainPanel.ActualHeight;

            if ((Stretch == Stretch.None || Stretch == Stretch.Uniform) && !Zoom ||
                (Stretch == Stretch.Fill || Stretch == Stretch.UniformToFill && Zoom))
            {
                VideoScale = 0;
            }
            else
            {
                MediaTrack videoTrack;
                try
                {
                    videoTrack = Media?.tracks()?.FirstOrDefault(x => x.type() == TrackType.Video);
                }
                catch (Exception)
                {
                    videoTrack = null;
                }
                if (videoTrack == null)
                    return;

                var videoWidth = videoTrack.width();
                var videoHeight = videoTrack.height();
                if (videoWidth == 0 || videoHeight == 0)
                {
                    await Task.Delay(500);
                    await UpdateZoomAsync();
                    return;
                }

                var sarDen = videoTrack.sarDen();
                var sarNum = videoTrack.sarNum();
                if (sarNum != sarDen)
                {
                    videoWidth = videoWidth * sarNum / sarDen;
                }

                var var = (double)videoWidth / videoHeight;
                var screenar = screenWidth / screenHeight;
                VideoScale = (float)(screenar >= var ? screenWidth / videoWidth : screenHeight / videoHeight);
            }*/
        }

        private async Task UpdateSizeAsync()
        {
            /*if (Instance == null)
            {
                await InitAsync(SwapChainPanel);
            }

            var scp = SwapChainPanel;
            Instance.UpdateSize((float)(scp.ActualWidth * scp.CompositionScaleX), (float)(scp.ActualHeight * scp.CompositionScaleY));
            await UpdateZoomAsync();*/
        }

        private async Task UpdateScaleAsync()
        {
            /*var instance = Instance;
            if (instance != null)
            {
                var scp = SwapChainPanel;
                instance.UpdateScale(scp.CompositionScaleX, scp.CompositionScaleY);
                await UpdateSizeAsync();
            }*/
        }

        private void SetAudioDevice()
        {
            /*var audioDeviceId = AudioDeviceId;
            if (!string.IsNullOrEmpty(audioDeviceId))
            {
                MediaPlayer?.outputDeviceSet(audioDeviceId);
            }*/
        }

        private async Task ClearMediaAsync()
        {
            /*await SetPositionAsync(0);
            Media = null;
            MediaPlayer = null;*/
        }

        /*private async Task OnMediaSourceChangedAsync(IMediaSource oldValue, IMediaSource newValue)
        {
            if (DesignMode.DesignModeEnabled)
            {
                return;
            }

            if (oldValue?.ExternalTimedTextSources is INotifyCollectionChanged)
            {
                ((INotifyCollectionChanged)oldValue.ExternalTimedTextSources).CollectionChanged -= ExternalTimedTextSourcesCollectionChanged;
            }
            if (newValue?.ExternalTimedTextSources is INotifyCollectionChanged)
            {
                ((INotifyCollectionChanged)newValue.ExternalTimedTextSources).CollectionChanged += ExternalTimedTextSourcesCollectionChanged;
            }

            await OnMediaSourceChangedAsync();
        }*/

        private async Task OnMediaSourceChangedAsync(bool forcePlay = false)
        {
            /*if (Instance == null || DesignMode.DesignModeEnabled)
            {
                return;
            }

            using (await SourceChangedMutex.LockAsync())
            {
                Stop();
                TransportControls?.Clear();

                var mediaSource = MediaSource;
                if (mediaSource == null)
                {
                    await ClearMediaAsync();
                    return;
                }

                var source = mediaSource.Uri;
                FromType type;
                if (!Uri.TryCreate(source, UriKind.RelativeOrAbsolute, out var location) || location.IsAbsoluteUri && !location.IsFile)
                {
                    type = FromType.FromLocation;
                }
                else
                {
                    if (!location.IsAbsoluteUri)
                    {
                        source = Path.Combine(Package.Current.InstalledLocation.Path, source);
                    }
                    type = FromType.FromPath;
                }
                var media = new Media(Instance, source, type);
                var hw = HardwareAcceleration;
                media.addOption($":avcodec-hw={(hw ? "d3d11va" : "none")}");
                if (AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Mobile")
                {
                    media.addOption($":avcodec-threads={Convert.ToInt32(hw)}");
                }
                var options = Options;
                if (options != null)
                {
                    foreach (var option in options)
                    {
                        media.addOption($":{option.Key}={option.Value}");
                    }
                }
                foreach (var timedTextSource in mediaSource.ExternalTimedTextSources)
                {
                    media.addSlave(SlaveType.Subtitle, 0, timedTextSource.Uri);
                }
                Media = media;

                var mediaPlayer = new MediaPlayer(media);
                var eventManager = mediaPlayer.eventManager();
                eventManager.OnBuffering += async p => await UpdateStateAsync(MediaElementState.Buffering);
                eventManager.OnOpening += OnOpeningAsync;
                eventManager.OnPlaying += async () => await UpdateStateAsync(MediaElementState.Playing);
                eventManager.OnPaused += async () => await UpdateStateAsync(MediaElementState.Paused);
                eventManager.OnStopped += async () => await UpdateStateAsync(MediaElementState.Stopped);
                eventManager.OnEndReached += OnEndReachedAsync;
                eventManager.OnPositionChanged += EventManager_OnPositionChangedAsync;
                eventManager.OnVoutCountChanged += async p => await Dispatcher.RunAsync(async () => { await UpdateZoomAsync(); });
                eventManager.OnTrackSelected += async (trackType, trackId) => await Dispatcher.RunAsync(() => TransportControls?.OnTrackSelected(trackType, trackId));
                eventManager.OnTrackDeleted += async (trackType, trackId) => await Dispatcher.RunAsync(() => TransportControls?.OnTrackDeleted(trackType, trackId));
                eventManager.OnLengthChanged += async length => await Dispatcher.RunAsync(() => TransportControls?.OnLengthChanged(length));
                eventManager.OnTimeChanged += EventManager_OnTimeChangedAsync;
                eventManager.OnSeekableChanged += async seekable => await Dispatcher.RunAsync(() => TransportControls?.OnSeekableChanged(seekable));
                MediaPlayer = mediaPlayer;

                SetAudioDevice();

                if (forcePlay || AutoPlay)
                { Play(); }
            }*/
        }

        /// <summary>
        /// Switches the application between windowed and full-screen modes.
        /// </summary>
        public void ToggleFullscreen()
        {
            var v = ApplicationView.GetForCurrentView();
            if (v.IsFullScreenMode)
            {
                v.ExitFullScreenMode();
            }
            else
            {
                v.TryEnterFullScreenMode();
            }
        }

        /// <summary>
        /// Pauses media at the current position.
        /// </summary>
        public void Pause()
        {
            MediaPlayer?.Pause();
        }

        /// <summary>
        /// Plays media from the current position.
        /// </summary>
        public void Play()
        {
            /*if (Media == null)
            {
                OnMediaSourceChangedAsync(true).ConfigureAwait(false);
            }
            else
            {
                MediaPlayer?.play();
            }*/
            MediaPlayer?.UnpauseResume();
        }

        /// <summary>
        /// Stops and resets media to be played from the beginning.
        /// </summary>
        public void Stop()
        {
            //MediaPlayer?.stop();
        }

        /// <summary>
        /// Sets the current position of progress.
        /// </summary>
        /// <param name="position">position.</param>
        internal void SetPosition(float position)
        {
            MediaPlayer?.setPosition(position);
        }

        private async Task SetPositionAsync(long time)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                UpdatingPosition = true;
                try
                {
                    Position = TimeSpan.FromMilliseconds(time);
                }
                finally
                {
                    UpdatingPosition = false;
                }
            });
        }

        private void InitMediaPlayer()
        {
            mOpenGLES.MakeCurrent(mRenderSurface);

            MediaPlayer = new Mpv();

            Windows.Storage.StorageFolder installationPath = Windows.Storage.ApplicationData.Current.LocalFolder;
            MediaPlayer.SetOptionString("log-file", @installationPath.Path + @"\koala.log");
            MediaPlayer.SetOptionString("msg-level", "all=v");
            MediaPlayer.SetOptionString("vo", "opengl-cb");

            var err = MediaPlayer.OpenGLCallbackInitialize(null, MyProcAddress, IntPtr.Zero);
            if (err != Mpv.MpvErrorCode.MPV_ERROR_SUCCESS)
            {
                throw new Exception("Failed to initalize mpv OpenGL callback.");
            }

            err = MediaPlayer.OpenGLCallbackSetUpdate(DrawNextFrame, IntPtr.Zero);
            if (err != Mpv.MpvErrorCode.MPV_ERROR_SUCCESS)
            {
                throw new Exception("Failed to set mpv OpenGL callback.");
            }


            //MediaPlayer.ExecuteCommand("loadfile", "http://download.blender.org/peach/bigbuckbunny_movies/big_buck_bunny_480p_h264.mov");
            //MediaPlayer.ExecuteCommand("loadfile", @"Test.mp4");
        }

        private void OnElementLoaded(object sender, RoutedEventArgs e)
        {
            // The SwapChainPanel has been created and arranged in the page layout, so EGL can be initialized. 
            CreateRenderSurface();
            InitMediaPlayer();
            StartRenderLoop();
        }

        private void OnElementUnloaded(object sender, RoutedEventArgs e)
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
                mRenderSurface = mOpenGLES.CreateSurface(SwapChainPanel);
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
            mRenderLoopWorker = Windows.System.Threading.ThreadPool.RunAsync(RenderLoop, WorkItemPriority.Normal, WorkItemOptions.TimeSliced);
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        async private void RenderLoop(IAsyncAction action)
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
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
                        SwapChainPanel.Dispatcher.RunAsync(CoreDispatcherPriority.High, new DispatchedHandler(() => { RecoverFromLostDevice(); }));

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

        private IntPtr MyProcAddress(IntPtr context, string name)
        {
            return mOpenGLES.GetProcAddress(name);
        }

        private void DrawNextFrame(IntPtr context)
        {
            // Wait for the UI Thread to run to render the next frame.
            Task.WhenAny(SwapChainPanel.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.High, new DispatchedHandler(() =>
            {
                // Get the Width and Height of the Window (it can change at anytime)
                int w = (int)((Frame)Window.Current.Content).ActualWidth;
                int h = (int)((Frame)Window.Current.Content).ActualHeight;

                // Draw the next frame, swap buffers, and report the frame has been flipped

                //await Task.WhenAny(Task.Run(() => MediaPlayer.OpenGLCallbackDraw(0, w, -h)), Task.Delay(42));
                var err = MediaPlayer.OpenGLCallbackDraw(0, w, -h);
                if (err != Mpv.MpvErrorCode.MPV_ERROR_SUCCESS) {
                    // TODO: log an error, don't throw an exception
                    throw new Exception("mpv OpenGL draw call dropped.");
                }
                mOpenGLES.SwapBuffers(mRenderSurface);
                MediaPlayer.OpenGLCallbackReportFlip();

                // Stop the render loop to prevent the frame from being overwritten
                StopRenderLoop();
            })).AsTask(), Task.Delay(42));
        }

        public Int64 StreamCbReadFn(IntPtr cookie, IntPtr buf, UInt64 numbytes)
        {
            IntPtr fp = cookie;
            streamcb_buffer_reference = buf;
            streamcb_buffer_size = numbytes;

            // * Not 64bit safe :(
            streamcb_buffer = new Byte[numbytes];
            int result = streamcb_stream.Read(streamcb_buffer, 0, Convert.ToInt32(numbytes));
            Marshal.Copy(streamcb_buffer, 0, streamcb_buffer_reference, result);

            // End of File
            if (result == 0)
            {
                return 0;
            }

            return result;

            // TODO: Add a try/catch to return a -1 on an exception
        }

        public Int64 StreamCbSeekFn(IntPtr cookie, Int64 offset)
        {
            IntPtr fp = cookie;

            Int64 result = streamcb_stream.Seek(offset, SeekOrigin.Begin);

            return result < 0 ? (Int64)Mpv.MpvErrorCode.MPV_ERROR_GENERIC : result;
        }

        public Int64 StreamCbSizeFn(IntPtr cookie)
        {
            return streamcb_stream.Length;
        }

        public void StreamCbCloseFn(IntPtr cookie)
        {
            streamcb_stream.Dispose();
        }

        public int StreamCbOpenFn(String userdata, String uri, ref Mpv.MPV_STREAM_CB_INFO info)
        {
            // Store File Path
            streamcb_userdata = userdata;

            // Allocate Methods
            streamcb_callback_read_method = StreamCbReadFn;
            streamcb_callback_read_ptr = Marshal.GetFunctionPointerForDelegate(streamcb_callback_read_method);
            streamcb_callback_read_gc = GCHandle.Alloc(streamcb_callback_read_ptr, GCHandleType.Pinned);

            streamcb_callback_seek_method = StreamCbSeekFn;
            streamcb_callback_seek_ptr = Marshal.GetFunctionPointerForDelegate(streamcb_callback_seek_method);
            streamcb_callback_seek_gc = GCHandle.Alloc(streamcb_callback_seek_ptr, GCHandleType.Pinned);

            streamcb_callback_size_method = StreamCbSizeFn;
            streamcb_callback_size_ptr = Marshal.GetFunctionPointerForDelegate(streamcb_callback_size_method);
            streamcb_callback_size_gc = GCHandle.Alloc(streamcb_callback_size_ptr, GCHandleType.Pinned);

            streamcb_callback_close_method = StreamCbCloseFn;
            streamcb_callback_close_ptr = Marshal.GetFunctionPointerForDelegate(streamcb_callback_close_method);
            streamcb_callback_close_gc = GCHandle.Alloc(streamcb_callback_close_ptr, GCHandleType.Pinned);

            // Set Struct Methods
            info.Cookie = streamcb_file_pointer;
            info.ReadFn = streamcb_callback_read_ptr;
            info.SeekFn = streamcb_callback_seek_ptr;
            info.SizeFn = streamcb_callback_size_ptr;
            info.CloseFn = streamcb_callback_close_ptr;

            // TODO: Return a MPV_ERROR_LOADING_FAILED if we aren't able to allocate the file to memory

            return 0;
        }

        /*private void ExternalTimedTextSourcesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add || e.Action == NotifyCollectionChangedAction.Replace)
            {
                var mediaPlayer = MediaPlayer;
                if (mediaPlayer == null || e.NewItems == null)
                {
                    return;
                }
                foreach (TimedTextSource timedTextSource in e.NewItems)
                {
                    mediaPlayer.addSlave(SlaveType.Subtitle, timedTextSource.Uri, false);
                }
            }
        }*/
    }
}
