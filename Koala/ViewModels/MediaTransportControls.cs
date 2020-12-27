using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Koala.Helpers;
using Windows.ApplicationModel.Resources;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.System;
using Windows.System.Profile;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Markup;
using Windows.UI.Xaml.Media;
using static Koala.ViewModels.MediaElement;
using MediaElementState = Koala.ViewModels.MediaElement.MediaElementState;

namespace Koala.ViewModels
{
    /// <summary>
    /// Represents the playback controls for a media player element.
    /// </summary>
    [ContentProperty(Name = "Content")]
    public
#if !CLASS_LIBRARY
    sealed
#endif
    class MediaTransportControls : Control
    {
        /// <summary>
        /// Occurs when view mode is changing.
        /// </summary>
        public event EventHandler<DeferrableCancelEventArgs> ViewModeChanging;
        /// <summary>
        /// Occurs when view mode has changed.
        /// </summary>
        public event RoutedEventHandler ViewModeChanged;

        /// <summary>
        /// Initializes a new instance of MediaTransportControls class.
        /// </summary>
        public MediaTransportControls()
        {
            DefaultStyleKey = typeof(MediaTransportControls);
            Loaded += (sender, e) => IsLoaded = true;
            Unloaded += (sender, e) => IsLoaded = false;
        }

        private bool HasError { get; set; }
        private bool Seekable { get; set; }
        private TimeSpan Length { get; set; }
        private Point? PreviousPointerPosition { get; set; }
        private CoreCursor Cursor { get; set; }
        private DispatcherTimer Timer { get; set; } = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(3) };
        private DispatcherTimer ProgressSliderTimer { get; } = new DispatcherTimer() { Interval = TimeSpan.FromSeconds(1) };
        private FrameworkElement LeftSeparator { get; set; }
        private FrameworkElement RightSeparator { get; set; }
        private FrameworkElement ControlPanelGrid { get; set; }
        private TextBlock ErrorTextBlock { get; set; }
        private Slider ProgressSlider { get; set; }
        private FrameworkElement TimeTextGrid { get; set; }
        private Slider VolumeSlider { get; set; }
        private Button DeinterlaceModeButton { get; set; }
        private FrameworkElement PlayPauseButton { get; set; }
        private FrameworkElement PlayPauseButtonOnLeft { get; set; }
        private FrameworkElement ZoomButton { get; set; }
        private FrameworkElement FullWindowButton { get; set; }
        private FrameworkElement CompactOverlayModeButton { get; set; }
        private FrameworkElement StopButton { get; set; }
        private ToggleButton RepeatButton { get; set; }
        private TextBlock TimeElapsedTextBlock { get; set; }
        private TextBlock TimeRemainingTextBlock { get; set; }
        private MenuFlyout DeinterlaceModeMenu { get; set; }

        private bool Xbox => AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Xbox";

        private bool _isLoaded;
        private bool IsLoaded
        {
            get => _isLoaded;
            set
            {
                if (_isLoaded != value)
                {
                    _isLoaded = value;
                    if (AutoHide)
                    {
                        SubscribeKeyDownEvent(value);
                    }
                }
            }
        }

        private double Position
        {
            get => (ProgressSlider?.Value) ?? 0;
            set
            {
                if (ProgressSlider != null && ProgressSlider.Value != value && !ProgressSliderTimer.IsEnabled)
                {
                    ProgressSlider.ValueChanged -= ProgressSlider_ValueChanged;
                    try
                    {
                        ProgressSlider.Value = value;
                    }
                    finally
                    {
                        ProgressSlider.ValueChanged += ProgressSlider_ValueChanged;
                    }
                }
            }
        }

        /// <summary>
        /// Gets a value indicating whether the playback will loop when the end is reached.
        /// </summary>
        internal bool AutoRepeatEnabled => RepeatButton.IsChecked == true;

        /// <summary>
        /// Gets or sets the media element.
        /// </summary>
        internal MediaElement MediaElement { get; set; }

        /// <summary>
        /// Gets the command bar.
        /// </summary>
        public CommandBar CommandBar { get; private set; }

        /// <summary>
        /// Gets the style of the bar buttons.
        /// </summary>
        public Style AppBarButtonStyle { get; private set; }

        /// <summary>
        /// Identifies the <see cref="AutoHide"/> dependency property.
        /// </summary>
        public static DependencyProperty AutoHideProperty { get; } = DependencyProperty.Register(nameof(AutoHide), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(true, (d, e) => ((MediaTransportControls)d).OnAutoHidePropertyChanged()));
        /// <summary>
        /// Gets or sets a value indicating whether the media transport controls must be hidden automatically or not.
        /// </summary>
        public bool AutoHide
        {
            get => (bool)GetValue(AutoHideProperty);
            set => SetValue(AutoHideProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="CursorAutoHide"/> dependency property.
        /// </summary>
        public static DependencyProperty CursorAutoHideProperty { get; } = DependencyProperty.Register(nameof(CursorAutoHide), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(false));
        /// <summary>
        /// Gets or sets a value indicating whether the mouse cursor must be hidden automatically or not.
        /// </summary>
        public bool CursorAutoHide
        {
            get => (bool)GetValue(CursorAutoHideProperty);
            set => SetValue(CursorAutoHideProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsDeinterlaceModeButtonVisible"/> dependency property.
        /// </summary>
        public static DependencyProperty IsDeinterlaceModeButtonVisibleProperty { get; } = DependencyProperty.Register(nameof(IsDeinterlaceModeButtonVisible), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(false, (d, e) => ((MediaTransportControls)d).UpdateDeinterlaceModeButton()));
        /// <summary>
        /// Gets or sets a value indicating whether the deinterlace mode button is shown.
        /// </summary>
        public bool IsDeinterlaceModeButtonVisible
        {
            get => (bool)GetValue(IsDeinterlaceModeButtonVisibleProperty);
            set => SetValue(IsDeinterlaceModeButtonVisibleProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsDeinterlaceModeButtonEnabled"/> dependency property
        /// </summary>
        public static DependencyProperty IsDeinterlaceModeButtonEnabledProperty { get; } = DependencyProperty.Register(nameof(IsDeinterlaceModeButtonEnabled), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(true, (d, e) => ((MediaTransportControls)d).UpdateDeinterlaceModeButton()));
        /// <summary>
        /// Gets or sets a value indicating whether a user can choose a deinterlace mode.
        /// </summary>
        public bool IsDeinterlaceModeButtonEnabled
        {
            get => (bool)GetValue(IsDeinterlaceModeButtonEnabledProperty);
            set => SetValue(IsDeinterlaceModeButtonEnabledProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsPlayPauseButtonVisible"/> dependency property.
        /// </summary>
        public static DependencyProperty IsPlayPauseButtonVisibleProperty { get; } = DependencyProperty.Register(nameof(IsPlayPauseButtonVisible), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(true, (d, e) => ((MediaTransportControls)d).UpdatePlayPauseButton()));
        /// <summary>
        /// Gets or sets a value indicating whether the play/pause button is shown.
        /// </summary>
        public bool IsPlayPauseButtonVisible
        {
            get => (bool)GetValue(IsPlayPauseButtonVisibleProperty);
            set => SetValue(IsPlayPauseButtonVisibleProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsPlayPauseEnabled"/> dependency property
        /// </summary>
        public static DependencyProperty IsPlayPauseEnabledProperty { get; } = DependencyProperty.Register(nameof(IsPlayPauseEnabled), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(true, (d, e) => ((MediaTransportControls)d).UpdatePlayPauseButton()));
        /// <summary>
        /// Gets or sets a value indicating whether a user can play/pause the media.
        /// </summary>
        public bool IsPlayPauseEnabled
        {
            get => (bool)GetValue(IsPlayPauseEnabledProperty);
            set => SetValue(IsPlayPauseEnabledProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsZoomButtonVisible"/> dependency property.
        /// </summary>
        public static DependencyProperty IsZoomButtonVisibleProperty { get; } = DependencyProperty.Register(nameof(IsZoomButtonVisible), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(true, (d, e) => ((MediaTransportControls)d).UpdateZoomButton()));
        /// <summary>
        /// Gets or sets a value indicating whether the zoom button is shown.
        /// </summary>
        public bool IsZoomButtonVisible
        {
            get => (bool)GetValue(IsZoomButtonVisibleProperty);
            set => SetValue(IsZoomButtonVisibleProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsZoomEnabled"/> dependency property.
        /// </summary>
        public static DependencyProperty IsZoomEnabledProperty { get; } = DependencyProperty.Register(nameof(IsZoomEnabled), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(true, (d, e) => ((MediaTransportControls)d).UpdateZoomButton()));
        /// <summary>
        /// Gets or sets a value indicating whether a user can zoom the media.
        /// </summary>
        public bool IsZoomEnabled
        {
            get => (bool)GetValue(IsZoomEnabledProperty);
            set => SetValue(IsZoomEnabledProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsFullWindowButtonVisible"/> dependency property.
        /// </summary>
        public static DependencyProperty IsFullWindowButtonVisibleProperty { get; } = DependencyProperty.Register(nameof(IsFullWindowButtonVisible), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(true, (d, e) => ((MediaTransportControls)d).UpdateFullWindowButton()));
        /// <summary>
        /// Gets or sets a value indicating whether the full screen button is shown.
        /// </summary>
        public bool IsFullWindowButtonVisible
        {
            get => (bool)GetValue(IsFullWindowButtonVisibleProperty);
            set => SetValue(IsFullWindowButtonVisibleProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsFullWindowEnabled"/> dependency property.
        /// </summary>
        public static DependencyProperty IsFullWindowEnabledProperty { get; } = DependencyProperty.Register(nameof(IsFullWindowEnabled), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(true, (d, e) => ((MediaTransportControls)d).UpdateFullWindowButton()));
        /// <summary>
        /// Gets or sets a value indicating whether a user can play the media in full-screen mode.
        /// </summary>
        public bool IsFullWindowEnabled
        {
            get => (bool)GetValue(IsFullWindowEnabledProperty);
            set => SetValue(IsFullWindowEnabledProperty, value);
        }

        private static bool IsCompactOverlayViewModeSupported =>
            ApiInformation.IsApiContractPresent("Windows.Foundation.UniversalApiContract", 4) &&
            ApplicationView.GetForCurrentView().IsViewModeSupported(ApplicationViewMode.CompactOverlay);
        /// <summary>
        /// Identifies the <see cref="IsCompactOverlayButtonVisible"/> dependency property.
        /// </summary>
        public static DependencyProperty IsCompactOverlayButtonVisibleProperty { get; } = DependencyProperty.Register(nameof(IsCompactOverlayButtonVisible), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(IsCompactOverlayViewModeSupported, (d, e) => ((MediaTransportControls)d).UpdateCompactOverlayModeButton()));
        /// <summary>
        /// Gets or sets a value that indicates whether the compact overlay button is shown.
        /// </summary>
        public bool IsCompactOverlayButtonVisible
        {
            get => (bool)GetValue(IsCompactOverlayButtonVisibleProperty);
            set => SetValue(IsCompactOverlayButtonVisibleProperty, value && IsCompactOverlayViewModeSupported);
        }

        /// <summary>
        /// Identifies the <see cref="IsCompactOverlayEnabled"/> dependency property.
        /// </summary>
        public static DependencyProperty IsCompactOverlayModeButtonEnabledProperty { get; } = DependencyProperty.Register(nameof(IsCompactOverlayEnabled), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(true, (d, e) => ((MediaTransportControls)d).UpdateCompactOverlayModeButton()));
        /// <summary>
        /// Gets or sets a value that indicates whether a user can enter compact overlay mode.
        /// </summary>
        public bool IsCompactOverlayEnabled
        {
            get => (bool)GetValue(IsCompactOverlayModeButtonEnabledProperty);
            set => SetValue(IsCompactOverlayModeButtonEnabledProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsStopButtonVisible"/> dependency property.
        /// </summary>
        public static DependencyProperty IsStopButtonVisibleProperty { get; } = DependencyProperty.Register(nameof(IsStopButtonVisible), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(false, (d, e) => ((MediaTransportControls)d).UpdateStopButton()));
        /// <summary>
        /// Gets or sets a value indicating whether the stop button is shown.
        /// </summary>
        public bool IsStopButtonVisible
        {
            get => (bool)GetValue(IsStopButtonVisibleProperty);
            set => SetValue(IsStopButtonVisibleProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsStopEnabled"/> dependency property.
        /// </summary>
        public static DependencyProperty IsStopEnabledProperty { get; } = DependencyProperty.Register(nameof(IsStopEnabled), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(true, (d, e) => ((MediaTransportControls)d).UpdateStopButton()));
        /// <summary>
        /// Gets or sets a value indicating whether a user can stop the media playback.
        /// </summary>
        public bool IsStopEnabled
        {
            get => (bool)GetValue(IsStopEnabledProperty);
            set => SetValue(IsStopEnabledProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsRepeatButtonVisible"/> dependency property.
        /// </summary>
        public static DependencyProperty IsRepeatButtonVisibleProperty { get; } = DependencyProperty.Register(nameof(IsRepeatButtonVisible), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(false, (d, e) => ((MediaTransportControls)d).UpdateRepeatButton()));
        /// <summary>
        ///Gets or sets a value that indicates whether the repeat button is shown.
        /// </summary>
        public bool IsRepeatButtonVisible
        {
            get => (bool)GetValue(IsRepeatButtonVisibleProperty);
            set => SetValue(IsRepeatButtonVisibleProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsRepeatEnabled"/> dependency property.
        /// </summary>
        public static DependencyProperty IsRepeatEnabledProperty { get; } = DependencyProperty.Register(nameof(IsRepeatEnabled), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(true, (d, e) => ((MediaTransportControls)d).UpdateRepeatButton()));
        /// <summary>
        /// Gets or sets a value that indicates whether a user repeat the playback of the media.
        /// </summary>
        public bool IsRepeatEnabled
        {
            get => (bool)GetValue(IsRepeatEnabledProperty);
            set => SetValue(IsRepeatEnabledProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="Content"/> dependency property.
        /// </summary>
        public static DependencyProperty ContentProperty { get; } = DependencyProperty.Register(nameof(Content), typeof(object), typeof(MediaTransportControls),
            new PropertyMetadata(null));
        /// <summary>
        /// Gets or sets the content to show over the media element.
        /// </summary>
        public object Content
        {
            get => GetValue(ContentProperty);
            set => SetValue(ContentProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsCompact"/> dependency property.
        /// </summary>
        public static DependencyProperty IsCompactProperty { get; } = DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(false, (d, e) => ((MediaTransportControls)d).UpdateMediaTransportControlMode()));
        /// <summary>
        /// Gets or sets a value indicating whether transport controls are shown on one row instead of two.
        /// </summary>
        public bool IsCompact
        {
            get => (bool)GetValue(IsCompactProperty);
            set => SetValue(IsCompactProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsSeekBarVisible"/> dependency property.
        /// </summary>
        public static DependencyProperty IsSeekBarVisibleProperty { get; } = DependencyProperty.Register(nameof(IsSeekBarVisible), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(true, (d, e) => ((MediaTransportControls)d).UpdateSeekBarVisibility()));
        /// <summary>
        /// Gets or sets a value indicating whether the seek bar is shown.
        /// </summary>
        public bool IsSeekBarVisible
        {
            get => (bool)GetValue(IsSeekBarVisibleProperty);
            set => SetValue(IsSeekBarVisibleProperty, value);
        }

        /// <summary>
        /// Identifies the <see cref="IsSeekBarEnabled"/> dependency property.
        /// </summary>
        public static DependencyProperty IsSeekBarEnabledProperty { get; } = DependencyProperty.Register(nameof(IsSeekBarEnabled), typeof(bool), typeof(MediaTransportControls),
            new PropertyMetadata(true, (d, e) => ((MediaTransportControls)d).UpdatePlayPauseButton()));
        /// <summary>
        /// Gets or sets a value indicating whether a user can use the seek bar to find a location in the media.
        /// </summary>
        public bool IsSeekBarEnabled
        {
            get => (bool)GetValue(IsSeekBarEnabledProperty);
            set => SetValue(IsSeekBarEnabledProperty, value);
        }

        /// <summary>
        /// Invoked whenever application code or internal processes (such as a rebuilding layout pass) call ApplyTemplate. 
        /// In simplest terms, this means the method is called just before a UI element displays in your app.
        /// Override this method to influence the default post-template logic of a class.
        /// </summary>
        protected override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            LeftSeparator = GetTemplateChild("LeftSeparator") as FrameworkElement;
            RightSeparator = GetTemplateChild("RightSeparator") as FrameworkElement;

            if (GetTemplateChild("RootGrid") is FrameworkElement rootGrid)
            {
                rootGrid.DoubleTapped += Grid_DoubleTapped;
                rootGrid.PointerEntered += Grid_PointerMoved;
                rootGrid.PointerMoved += Grid_PointerMoved;
                rootGrid.Tapped += OnPointerMoved;
                rootGrid.PointerExited += Grid_PointerExited;

                AppBarButtonStyle = rootGrid.Resources["AppBarButtonStyle"] as Style;
            }

            var commandBar = GetTemplateChild("MediaControlsCommandBar") as CommandBar;
            CommandBar = commandBar;
            if (commandBar != null)
            {
                commandBar.LayoutUpdated += CommandBar_LayoutUpdated;
            }

            ControlPanelGrid = GetTemplateChild("ControlPanelGrid") as FrameworkElement;

            ErrorTextBlock = GetTemplateChild("ErrorTextBlock") as TextBlock;

            ProgressSlider = GetTemplateChild("ProgressSlider") as Slider;
            if (ProgressSlider != null)
            {
                ProgressSlider.Minimum = 0;
                ProgressSlider.Maximum = (Xbox ? 100 : 1000);
                ProgressSlider.ValueChanged += ProgressSlider_ValueChanged;
            }
            TimeTextGrid = GetTemplateChild("TimeTextGrid") as FrameworkElement;
            VolumeSlider = GetTemplateChild("VolumeSlider") as Slider;
            if (VolumeSlider != null)
            {
                VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
                UpdateVolume();
            }
            PlayPauseButton = GetTemplateChild("PlayPauseButton") as FrameworkElement;
            PlayPauseButtonOnLeft = GetTemplateChild("PlayPauseButtonOnLeft") as FrameworkElement;
            ZoomButton = GetTemplateChild("ZoomButton") as FrameworkElement;
            FullWindowButton = GetTemplateChild("FullWindowButton") as FrameworkElement;
            CompactOverlayModeButton = GetTemplateChild("CompactOverlayModeButton") as FrameworkElement;
            StopButton = GetTemplateChild("StopButton") as FrameworkElement;
            RepeatButton = GetTemplateChild("RepeatButton") as ToggleButton;
            var deinterlaceModeButton = GetTemplateChild("DeinterlaceModeButton") as Button;
            DeinterlaceModeButton = deinterlaceModeButton;

            TimeElapsedTextBlock = GetTemplateChild("TimeElapsedElement") as TextBlock;
            TimeRemainingTextBlock = GetTemplateChild("TimeRemainingElement") as TextBlock;

            SetButtonClick(PlayPauseButtonOnLeft, PlayPauseButton_Click);
            SetButtonClick(PlayPauseButton, PlayPauseButton_Click);
            SetButtonClick(ZoomButton, ZoomButton_Click);
            SetButtonClick(FullWindowButton, FullWindowButton_Click);
            SetButtonClick(CompactOverlayModeButton, CompactOverlayModeButton_ClickAsync);
            SetButtonClick(StopButton, StopButton_Click);
            var audioMuteButton = GetTemplateChild("AudioMuteButton");
            SetButtonClick(audioMuteButton, AudioMuteButton_Click);
            if (RepeatButton != null)
            {
                RepeatButton.Checked += RepeatButton_CheckedChanged;
                RepeatButton.Unchecked += RepeatButton_CheckedChanged;
            }

            UpdateMediaTransportControlMode();
            UpdateSeekBarVisibility();
            UpdatePlayPauseButton();
            UpdateZoomButton();
            UpdateFullWindowButton();
            UpdateCompactOverlayModeButton();
            UpdateStopButton();
            UpdateRepeatButton();
            UpdateMuteState();
            UpdateDeinterlaceModeButton();

            ApplicationView.GetForCurrentView().VisibleBoundsChanged += (sender, args) =>
            {
                UpdateWindowState();
            };
            UpdateWindowState();
            UpdateRepeatButtonState();

            Timer.Tick += Timer_Tick;
            ProgressSliderTimer.Tick += ProgressSliderTimer_Tick;
        }

        private void CommandBar_LayoutUpdated(object sender, object e)
        {
            var leftSeparator = LeftSeparator;
            var rightSeparator = RightSeparator;
            if (leftSeparator == null || rightSeparator == null)
            {
                return;
            }

            var commandBar = CommandBar;
            var width = commandBar.PrimaryCommands.Where(el => !(el is AppBarSeparator) && ((FrameworkElement)el).Visibility == Visibility.Visible).Sum(el => ((FrameworkElement)el).Width);
            width = (commandBar.ActualWidth - width) / 2;
            if (width >= 0 && leftSeparator.Width != width)
            {
                leftSeparator.Width = width;
                rightSeparator.Width = width;
            }
        }

        private void SetButtonClick(DependencyObject dependencyObject, RoutedEventHandler eventHandler)
        {
            if (dependencyObject is ButtonBase button)
            {
                button.Click += eventHandler;
            }
        }

        private void OnAutoHidePropertyChanged()
        {
            if (AutoHide)
            {
                SubscribeKeyDownEvent(true);
                StartTimer();
            }
            else
            {
                SubscribeKeyDownEvent(false);
                Show();
            }
        }

        private void SubscribeKeyDownEvent(bool subscribe)
        {
            if (Xbox && IsLoaded)
            {
                var coreWindow = CoreWindow.GetForCurrentThread();
                if (subscribe)
                {
                    coreWindow.KeyDown += MediaTransportControls_KeyDown;
                }
                else
                {
                    coreWindow.KeyDown -= MediaTransportControls_KeyDown;
                }
            }
        }

        private void MediaTransportControls_KeyDown(CoreWindow sender, KeyEventArgs args)
        {
            switch (args.VirtualKey)
            {
                case VirtualKey.Select:
                case VirtualKey.Back:
                case VirtualKey.GamepadA:
                case VirtualKey.GamepadB:
                case VirtualKey.GamepadX:
                case VirtualKey.GamepadY:
                case VirtualKey.GamepadRightShoulder:
                case VirtualKey.GamepadLeftShoulder:
                case VirtualKey.GamepadLeftTrigger:
                case VirtualKey.GamepadRightTrigger:
                case VirtualKey.GamepadDPadUp:
                case VirtualKey.GamepadDPadDown:
                case VirtualKey.GamepadDPadLeft:
                case VirtualKey.GamepadDPadRight:
                case VirtualKey.GamepadMenu:
                case VirtualKey.GamepadView:
                case VirtualKey.GamepadLeftThumbstickButton:
                case VirtualKey.GamepadRightThumbstickButton:
                case VirtualKey.GamepadLeftThumbstickUp:
                case VirtualKey.GamepadLeftThumbstickDown:
                case VirtualKey.GamepadLeftThumbstickRight:
                case VirtualKey.GamepadLeftThumbstickLeft:
                case VirtualKey.GamepadRightThumbstickUp:
                case VirtualKey.GamepadRightThumbstickDown:
                case VirtualKey.GamepadRightThumbstickRight:
                case VirtualKey.GamepadRightThumbstickLeft:
                    OnPointerMoved(sender, null);
                    break;
            }
        }

        private void StartTimer()
        {
            if (AutoHide && MediaElement?.State == MediaState.Playing)
            {
                Timer.Start();
            }
        }

        /// <summary>
        /// Show controls with a fade in animation
        /// </summary>
        public void Show()
        {
            Timer.Stop();
            VisualStateManager.GoToState(this, "ControlPanelFadeIn", true);
            if (CursorAutoHide)
            {
                var cursor = Cursor;
                if (cursor != null && Window.Current.CoreWindow.PointerCursor != cursor)
                {
                    Window.Current.CoreWindow.PointerCursor = cursor;
                    Cursor = null;
                }
            }
        }

        private void Timer_Tick(object sender, object e)
        {
            Timer.Stop();
            VisualStateManager.GoToState(this, "ControlPanelFadeOut", true);
            if (CursorAutoHide)
            {
                if (Cursor == null)
                {
                    Cursor = Window.Current.CoreWindow.PointerCursor;
                }
                Window.Current.CoreWindow.PointerCursor = null;
            }
        }

        private void Grid_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var position = e.GetCurrentPoint((UIElement)sender).Position;
            if (PreviousPointerPosition != position)
            {
                PreviousPointerPosition = position;
                OnPointerMoved(sender, e);
            }
        }

        private void OnPointerMoved(object sender, RoutedEventArgs e)
        {
            Show();
            if (e == null || e.OriginalSource == sender || e.OriginalSource == ControlPanelGrid)
            {
                StartTimer();
            }
            else
            {
                var controlPanelGrid = ControlPanelGrid;
                if (controlPanelGrid != null && e.OriginalSource is Panel)
                {
                    var renderSize = controlPanelGrid.RenderSize;
                    var currentWindow = Window.Current;
                    var bounds = currentWindow.Bounds;
                    var pointerPosition = currentWindow.CoreWindow.PointerPosition;
                    pointerPosition = new Point(pointerPosition.X - bounds.X, pointerPosition.Y - bounds.Y);
                    var controlPanelGridPosition = controlPanelGrid.TransformToVisual(currentWindow.Content).TransformPoint(new Point(0, 0));
                    if (pointerPosition.X >= controlPanelGridPosition.X && pointerPosition.X < controlPanelGridPosition.X + renderSize.Width &&
                    pointerPosition.Y >= controlPanelGridPosition.Y && pointerPosition.Y < controlPanelGridPosition.Y + renderSize.Height)
                    {
                        StartTimer();
                    }
                }
            }
        }

        private void Grid_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            Show();

            if (e.Pointer.PointerDeviceType == PointerDeviceType.Touch)
            {
                StartTimer();
            }
            else
            {
                var element = (UIElement)sender;
                var renderSize = element.RenderSize;
                var currentWindow = Window.Current;
                var bounds = currentWindow.Bounds;
                var pointerPosition = currentWindow.CoreWindow.PointerPosition;
                pointerPosition = new Point(pointerPosition.X - bounds.X, pointerPosition.Y - bounds.Y);
                var currentPosition = element.TransformToVisual(currentWindow.Content).TransformPoint(new Point(0, 0));
                if (pointerPosition.X < currentPosition.X || pointerPosition.X >= currentPosition.X + renderSize.Width ||
                    pointerPosition.Y < currentPosition.Y || pointerPosition.Y >= currentPosition.Y + renderSize.Height)
                {
                    StartTimer();
                }
            }
        }

        private void Grid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
        {
            if (e.OriginalSource == sender)
            {
                ToggleFullscreen();
            }
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            var state = MediaElement.State;
            switch (state)
            {
                case MediaState.Ended:
                case MediaState.Paused:
                case MediaState.Stopped:
                case MediaState.Error:
                case MediaState.NothingSpecial:
                    MediaElement.Play();
                    ProgressSliderTimer.Start();
                    break;
                default:
                    MediaElement.Pause();
                    ProgressSliderTimer.Stop();
                    break;
            }
        }

        private void UpdateSeekBarVisibility()
        {
            UpdateControl(ProgressSlider, IsSeekBarVisible, IsSeekBarEnabled);
            UpdateControl(TimeTextGrid, IsSeekBarVisible);
        }

        private void UpdatePlayPauseButton()
        {
            UpdateControl(PlayPauseButton, IsPlayPauseButtonVisible, IsPlayPauseEnabled);
            UpdateControl(PlayPauseButtonOnLeft, IsPlayPauseButtonVisible, IsPlayPauseEnabled);
        }

        /// <summary>
        /// Updates deinterlace mode button properties.
        /// </summary>
        internal void UpdateDeinterlaceModeButton()
        {
            UpdateControl(DeinterlaceModeButton, IsDeinterlaceModeButtonVisible && !MediaElement.HardwareAcceleration, IsDeinterlaceModeButtonEnabled);
        }

        /// <summary>
        /// Updates zoom button properties.
        /// </summary>
        internal void UpdateZoomButton()
        {
            UpdateControl(ZoomButton, IsZoomButtonVisible, IsZoomEnabled);
        }

        private void UpdateFullWindowButton()
        {
            UpdateControl(FullWindowButton, IsFullWindowButtonVisible && !Xbox, IsFullWindowEnabled);
        }

        private void UpdateCompactOverlayModeButton()
        {
            UpdateControl(CompactOverlayModeButton, IsCompactOverlayButtonVisible, IsCompactOverlayEnabled);
        }

        private void UpdateStopButton()
        {
            UpdateControl(StopButton, IsStopButtonVisible, IsStopEnabled);
        }

        private void UpdateRepeatButton()
        {
            UpdateControl(RepeatButton, IsRepeatButtonVisible, IsRepeatEnabled);
        }

        private void UpdateControl(FrameworkElement control, bool visible, bool enabled = true)
        {
            if (control != null)
            {
                control.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (control is Control)
                {
                    ((Control)control).IsEnabled = enabled;
                }
            }
        }

        private void AudioMuteButton_Click(object sender, RoutedEventArgs e)
        {
            MediaElement.IsMuted = !MediaElement.IsMuted;
        }

        private void ZoomButton_Click(object sender, RoutedEventArgs e)
        {
            MediaElement.Zoom = !MediaElement.Zoom;
        }

        /// <summary>
        /// Resets the control.
        /// </summary>
        internal void Clear()
        {
            Length = TimeSpan.Zero;
            UpdateTime();
            Position = 0;
            ProgressSlider.IsEnabled = false;
            Seekable = true;
            UpdateProgressSliderIsEnabled(MediaState.Stopped);
        }

        /// <summary>
        /// Updates the mute state.
        /// </summary>
        internal void UpdateMuteState()
        {
            VisualStateManager.GoToState(this, MediaElement?.IsMuted == true ? "MuteState" : "VolumeState", true);
        }

        /// <summary>
        /// Called when the media length has changed.
        /// </summary>
        /// <param name="length">media length</param>
        internal void OnLengthChanged(long length)
        {
            Length = TimeSpan.FromMilliseconds(length);
            UpdateTime();
        }

        /// <summary>
        /// Called when the current position of progress through the media's playback time has changed.
        /// </summary>
        /// <param name="time">current position of progress</param>
        internal void OnTimeChanged(long time)
        {
            if (!ProgressSliderTimer.IsEnabled)
            {
                UpdateTime();
            }
        }

        private void UpdateTime(bool fromSlider = false)
        {
            var time = (fromSlider ? TimeSpan.FromTicks((long)(ProgressSlider.Value * Length.Ticks / ProgressSlider.Maximum)) :
                MediaElement.Position);
            var timeElapsed = time.ToString(); // ToShortString() doesn't exist?
            var timeRemaining = (Length - time).ToString(); // ToShortString() doesn't exist?
            if (TimeElapsedTextBlock?.Text != timeElapsed)
            {
                TimeElapsedTextBlock.Text = timeElapsed;
            }
            if (TimeRemainingTextBlock?.Text != timeRemaining)
            {
                TimeRemainingTextBlock.Text = timeRemaining;
            }
        }

        private void ProgressSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            ProgressSliderTimer.Stop();
            ProgressSliderTimer.Start();
            UpdateTime(true);
        }

        private void ProgressSliderTimer_Tick(object sender, object e)
        {
            // progress slider
            ProgressSlider.Value += 1;

            // set text blocks
            //TimeSpan current_duration = TimeSpan.FromSeconds(ProgressSlider.Maximum - ProgressSlider.Value);
            //TimeRemainingTextBlock.Text = String.Format("{0:D2}:{1:D2}:{2:D2}", current_duration.Hours, current_duration.Minutes, current_duration.Seconds);

            //TimeSpan elasped_duration = TimeSpan.FromSeconds(ProgressSlider.Value);
            //TimeElapsedTextBlock.Text = String.Format("{0:D2}:{1:D2}:{2:D2}", elasped_duration.Hours, elasped_duration.Minutes, elasped_duration.Seconds);

            // unused
            //MediaElement.SetPosition((float)(ProgressSlider.Value / ProgressSlider.Maximum));
            //ProgressSliderTimer.Stop();
        }

        /// <summary>
        /// Called when the seekable property of the media has changed.
        /// </summary>
        /// <param name="seekable">true if the media is seekable, false otherwise.</param>
        internal void OnSeekableChanged(bool seekable)
        {
            Seekable = seekable;
            UpdateProgressSliderIsEnabled();
        }

        private void UpdateProgressSliderIsEnabled(MediaState? state = null)
        {
            if (!Seekable)
            {
                ProgressSlider.IsEnabled = false;
            }

            var currentState = state ?? MediaElement.State;
            ProgressSlider.IsEnabled = currentState != MediaState.Ended && currentState != MediaState.Stopped && currentState != MediaState.Error;
        }

        private void UpdateMediaTransportControlMode()
        {
            VisualStateManager.GoToState(this, IsCompact ? "CompactMode" : "NormalMode", true);
        }

        private void UpdateWindowState()
        {
            UpdateFullWindowState();
        }

        private void UpdateFullWindowState()
        {
            var fullScreen = ApplicationView.GetForCurrentView().IsFullScreenMode;
            VisualStateManager.GoToState(this, fullScreen ? "FullWindowState" : "NonFullWindowState", true);
        }

        private void UpdateRepeatButtonState()
        {
            var autoRepeatEnabled = AutoRepeatEnabled;
            VisualStateManager.GoToState(this, autoRepeatEnabled ? "RepeatOneState" : "RepeatNoneState", true);
        }

        private void FullWindowButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullscreen();
        }

        private async void CompactOverlayModeButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            var cancelEventArgs = new DeferrableCancelEventArgs();
            var viewModeChanging = ViewModeChanging;
            if (viewModeChanging != null)
            {
                viewModeChanging(this, cancelEventArgs);
                await cancelEventArgs.WaitForDeferralsAsync();
            }
            var applicationView = ApplicationView.GetForCurrentView();
            if (cancelEventArgs.Cancel || await applicationView.TryEnterViewModeAsync(
                applicationView.ViewMode == ApplicationViewMode.CompactOverlay ? ApplicationViewMode.Default : ApplicationViewMode.CompactOverlay))
            {
                if (!cancelEventArgs.Cancel)
                {
                    ViewModeChanged?.Invoke(this, new RoutedEventArgs());
                }
                Show();
                StartTimer();
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            MediaElement?.Stop();
        }

        private void RepeatButton_CheckedChanged(object sender, RoutedEventArgs e)
        {
            UpdateRepeatButtonState();
        }

        private void ToggleFullscreen()
        {
            MediaElement?.ToggleFullscreen();
            Show();
            StartTimer();
        }

        private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            MediaElement.Volume = (int)e.NewValue;
        }

        /// <summary>
        /// Updates the volume slider.
        /// </summary>
        internal void UpdateVolume()
        {
            VolumeSlider.Value = MediaElement.Volume;
        }

        /// <summary>
        /// Updates the state of the media.
        /// </summary>
        /// <param name="previousState">previous state of the media element.</param>
        /// <param name="state">state of the media element.</param>
        internal void UpdateState(MediaElementState previousState, MediaElementState state)
        {
            string statusStateName, playPauseStateName, playPauseToolTip;
            switch (state)
            {
                case MediaElementState.Closed:
                case MediaElementState.Stopped:
                    statusStateName = (HasError ? null : "Disabled");
                    playPauseStateName = "PlayState";
                    playPauseToolTip = "Play";
                    Clear();
                    break;
                case MediaElementState.Paused:
                    statusStateName = "Disabled";
                    playPauseStateName = "PlayState";
                    playPauseToolTip = "Play";
                    break;
                case MediaElementState.Buffering:
                    statusStateName = (previousState == MediaElementState.Playing || previousState == MediaElementState.Opening ? "Buffering" : null);
                    playPauseStateName = null;
                    playPauseToolTip = null;
                    break;
                default:
                    statusStateName = "Normal";
                    playPauseStateName = "PauseState";
                    playPauseToolTip = "Pause";
                    break;
            }
            if (statusStateName != null)
            {
                HasError = false;
                VisualStateManager.GoToState(this, statusStateName, true);
            }
            if (playPauseStateName != null)
            {
                VisualStateManager.GoToState(this, playPauseStateName, true);
            }

            Show();
            StartTimer();
        }

        /// <summary>
        /// Sets the error message.
        /// </summary>
        /// <param name="error">error message.</param>
        internal void SetError(string error)
        {
            if (ErrorTextBlock != null)
            {
                ErrorTextBlock.Text = error;
                if (!string.IsNullOrWhiteSpace(error))
                {
                    VisualStateManager.GoToState(this, "Error", true);
                    HasError = true;
                }
            }
        }

        /// <summary>
        /// Called when the current position of progress has changed.
        /// </summary>
        /// <param name="position">current position of progress.</param>
        internal void OnPositionChanged(float position)
        {
            if (ProgressSlider != null)
            {
                Position = position * ProgressSlider.Maximum;
            }
        }

        public void SetSliderDuration(Double duration, Double interval)
        {
            ProgressSlider.Maximum = duration;

            TimeSpan real_duration = TimeSpan.FromSeconds(duration);

            //ProgressSliderTimer.Interval = TimeSpan.FromMilliseconds(interval);
            ProgressSliderTimer.Interval = TimeSpan.FromSeconds(1);
            ProgressSliderTimer.Start();

            //TimeRemainingTextBlock.Text = String.Format("{0:D2}:{1:D2}:{2:D3}", real_duration.Hours, real_duration.Minutes, real_duration.Seconds);
            Length = real_duration;

            ProgressSlider.PointerCaptureLost += ProgressSlider_PointerCaptureLost;
        }

        private void ProgressSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            MediaElement.MediaPlayer.setPosition((float)ProgressSlider.Value);
        }
    }
}
