using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using TalePro.Services;

namespace TalePro
{
    public partial class MainWindow : Window
    {
        private string _currentVideoPath = string.Empty;
        private string _lastOutputPath = string.Empty;
        private double _originalSizeBytes = 0;
        private double _durationSeconds = 0;
        private int _width = 0, _height = 0;
        
        private CompressionService _compressionService;
        private bool _isProcessing = false;
        private CancellationTokenSource? _cts;
        
        private DispatcherTimer _timer;
        private string _tempOriginal5s = string.Empty;
        private string _tempProcessed5s = string.Empty;
        private bool _isPlaying = false;
        private bool _isScrubbing = false;
        private bool _isFullscreen = false;
        private bool _isSyncing = false;
        private Panel? _originalPlayerParent;

        public MainWindow()
        {
            try 
            {
                InitializeComponent();
                
                this.MaxHeight = SystemParameters.MaximizedPrimaryScreenHeight;
                this.MaxWidth = SystemParameters.MaximizedPrimaryScreenWidth;

                _compressionService = new CompressionService();
                _compressionService.OnProgress += (p, r, s) => Application.Current.Dispatcher.Invoke(() => UpdateProgress(p, r, s));
                _compressionService.OnComplete += (s, path) => Application.Current.Dispatcher.Invoke(() => ProcessingComplete(s, path));
                
                _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _timer.Tick += Timer_Tick;

                UpdateCircProgress(CircProgress, 0);
                UpdateCircProgress(CircProcessView, 0);
            }
            catch (Exception ex)
            {
                MessageBox.Show("CRITICAL STARTUP ERROR:\n" + ex.Message + "\n\n" + ex.StackTrace, "Tale-Pro Crash", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _cts?.Cancel();
            try { Process.Start(new ProcessStartInfo { FileName = "taskkill", Arguments = "/F /IM TalePro_Engine.exe /T", CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden })?.WaitForExit(); } catch { }
            try { Process.Start(new ProcessStartInfo { FileName = "taskkill", Arguments = "/F /IM TalePro_Probe.exe /T", CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden })?.WaitForExit(); } catch { }
            Environment.Exit(0);
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) Maximize_Click(sender, e); else DragMove(); }
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Normal ? WindowState.Maximized : WindowState.Normal;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void ReparentPlayer(Panel target)
        {
            if (PlayerRoot.Parent is Panel oldParent) oldParent.Children.Remove(PlayerRoot);
            target.Children.Add(PlayerRoot);
        }

        private void SwitchView(int viewIndex)
        {
            if (_isFullscreen && viewIndex != 0 && viewIndex != 2) ToggleFullscreen();

            ViewHome.Visibility = viewIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            ViewProcess.Visibility = viewIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            ViewCompare.Visibility = viewIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            ViewSettings.Visibility = viewIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
            ViewAbout.Visibility = viewIndex == 4 ? Visibility.Visible : Visibility.Collapsed;

            BtnBackToHome.Visibility = viewIndex == 0 ? Visibility.Collapsed : Visibility.Visible;
            
            // FIX 3: Free Will Responsive Player
            if (viewIndex == 0) { 
                TabHome.IsChecked = true; 
                if (PlayerRoot.Parent != HomePlayerContainer) ReparentPlayer(HomePlayerContainer); 
                if (!_isFullscreen) PlayerContainer.Height = 280; // Fixed on Home
            }
            if (viewIndex == 2) { 
                TabCompare.IsChecked = true; 
                if (PlayerRoot.Parent != ViewCompare) ReparentPlayer(ViewCompare); 
                if (!_isFullscreen) PlayerContainer.Height = double.NaN; // Unconstrained on Compare
            }
        }

        private void NavTab_Click(object sender, RoutedEventArgs e)
        {
            if (TabHome.IsChecked == true) SwitchView(0);
            else if (TabProcess.IsChecked == true) SwitchView(1);
            else if (TabCompare.IsChecked == true) SwitchView(2);
            else if (TabSettings.IsChecked == true) SwitchView(3);
            else if (TabAbout.IsChecked == true) SwitchView(4);
        }

        private void BtnBackToHome_Click(object sender, RoutedEventArgs e) => SwitchView(0);
        private void NavSettings_Click(object sender, RoutedEventArgs e) { TabSettings.IsChecked = true; SwitchView(3); }
        
        private void Config_Click(object sender, RoutedEventArgs e) => SyncSettings(sender);
        private void Config_SelectionChanged(object sender, SelectionChangedEventArgs e) => SyncSettings(sender);
        private void Config_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => SyncSettings(sender);

        private void SyncSettings(object sender)
        {
            if (_isSyncing) return;
            _isSyncing = true;
            
            try {
                if (sender == SetGpu || sender == SetCpu) HwAccelToggle.IsChecked = SetGpu.IsChecked == true;
                else if (sender == HwAccelToggle) { SetGpu.IsChecked = HwAccelToggle.IsChecked == true; SetCpu.IsChecked = HwAccelToggle.IsChecked == false; }
                
                if (sender == QualitySlider) SetQualitySlider.Value = QualitySlider.Value; else if (sender == SetQualitySlider) QualitySlider.Value = SetQualitySlider.Value;
                if (sender == PresetCombo) SetPresetCombo.SelectedIndex = PresetCombo.SelectedIndex; else if (sender == SetPresetCombo) PresetCombo.SelectedIndex = SetPresetCombo.SelectedIndex;
                if (sender == CodecCombo) SetCodecCombo.SelectedIndex = CodecCombo.SelectedIndex; else if (sender == SetCodecCombo) CodecCombo.SelectedIndex = SetCodecCombo.SelectedIndex;
                if (sender == AudioCombo) SetAudioCombo.SelectedIndex = AudioCombo.SelectedIndex; else if (sender == SetAudioCombo) AudioCombo.SelectedIndex = SetAudioCombo.SelectedIndex;
                if (sender == FpsCombo) SetFpsCombo.SelectedIndex = FpsCombo.SelectedIndex; else if (sender == SetFpsCombo) FpsCombo.SelectedIndex = SetFpsCombo.SelectedIndex;
                if (sender == MetadataCombo) SetMetadataCombo.SelectedIndex = MetadataCombo.SelectedIndex; else if (sender == SetMetadataCombo) MetadataCombo.SelectedIndex = SetMetadataCombo.SelectedIndex;
                if (sender == BypassCombo) SetBypassCombo.SelectedIndex = BypassCombo.SelectedIndex; else if (sender == SetBypassCombo) BypassCombo.SelectedIndex = SetBypassCombo.SelectedIndex;
            } catch { }

            _isSyncing = false;
            UpdateEstimates();
        }

        private void PromptFileSelection()
        {
            if (_isProcessing) return;
            OpenFileDialog dlg = new OpenFileDialog { Filter = "Video Files|*.mp4;*.mov;*.mkv" };
            if (dlg.ShowDialog() == true) LoadVideo(dlg.FileName);
        }
        private void SelectVideo_Click(object sender, MouseButtonEventArgs e) => PromptFileSelection();
        private void ChangeFile_Click(object sender, RoutedEventArgs e) => PromptFileSelection();

        private void ClearWorkspace()
        {
            if (_isProcessing) return;
            _currentVideoPath = string.Empty; _originalSizeBytes = 0; _durationSeconds = 0;
            ThumbImage.Source = null;
            PlayerOriginal.Source = null; PlayerProcessed.Source = null;
            LoadedStatePanel.Visibility = Visibility.Collapsed;
            EmptyStateBorder.Visibility = Visibility.Visible;
            PlayerRoot.Visibility = Visibility.Collapsed;
            UpdateProgress(0, TimeSpan.Zero, "Idle / Ready");
            TimeRemainingTxt.Text = "--:--"; TimeRemainingTxtProc.Text = "Time Remaining: --:--";
        }
        private void Trash_Click(object sender, RoutedEventArgs e) => ClearWorkspace();

        private void ToggleFullscreen()
        {
            _isFullscreen = !_isFullscreen;
            if (_isFullscreen)
            {
                _originalPlayerParent = PlayerRoot.Parent as Panel;
                _originalPlayerParent?.Children.Remove(PlayerRoot);
                FullscreenOverlay.Children.Add(PlayerRoot);
                FullscreenOverlay.Visibility = Visibility.Visible;
                RootGrid.Visibility = Visibility.Collapsed;
                PlayerContainer.Height = double.NaN;
            }
            else
            {
                FullscreenOverlay.Children.Remove(PlayerRoot);
                FullscreenOverlay.Visibility = Visibility.Collapsed;
                RootGrid.Visibility = Visibility.Visible;
                _originalPlayerParent?.Children.Add(PlayerRoot);
                PlayerContainer.Height = (TabCompare.IsChecked == true) ? double.NaN : 280;
            }
        }
        private void BtnModeFull_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

        private void TogglePlayPause()
        {
            _isPlaying = !_isPlaying;
            BtnPlayPause.Content = _isPlaying ? "⏸" : "▶";
            if (_isPlaying) { PlayerOriginal.Play(); PlayerProcessed.Play(); _timer.Start(); }
            else { PlayerOriginal.Pause(); PlayerProcessed.Pause(); _timer.Stop(); }
        }
        private void PlayPause_Click(object sender, RoutedEventArgs e) => TogglePlayPause();

        private void Grid_Drop(object sender, DragEventArgs e)
        {
            if (_isProcessing) return;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0 && (files[0].ToLower().EndsWith(".mp4") || files[0].ToLower().EndsWith(".mov") || files[0].ToLower().EndsWith(".mkv")))
                    LoadVideo(files[0]);
            }
        }

        private async void LoadVideo(string path)
        {
            _currentVideoPath = path;
            EmptyStateBorder.Visibility = Visibility.Collapsed;
            LoadedStatePanel.Visibility = Visibility.Visible;
            PlayerRoot.Visibility = Visibility.Visible;
            if (PlayerRoot.Parent != HomePlayerContainer) ReparentPlayer(HomePlayerContainer);
            VidTitle.Text = Path.GetFileName(path);
            
            _originalSizeBytes = new FileInfo(path).Length;
            double gb = _originalSizeBytes / 1024.0 / 1024.0 / 1024.0;
            VidSize.Text = gb >= 1.0 ? $"{gb:0.00} GB" : $"{(_originalSizeBytes / 1024.0 / 1024.0):0.0} MB";

            VidResolution.Text = "Analyzing..."; VidFPS.Text = "..."; VidDuration.Text = "..."; VidCodec.Text = "...";
            ThumbImage.Source = null;

            await Task.Run(() => ExtractMetadata(path));
            await Task.Run(() => ExtractThumbnail(path));
            
            _tempOriginal5s = Path.Combine(Path.GetTempPath(), "talepro_orig_" + Guid.NewGuid().ToString("N") + ".mp4");
            await _compressionService.Extract5sClipAsync(path, _tempOriginal5s);
            
            PlayerOriginal.Source = new Uri(_tempOriginal5s);
            PlayerProcessed.Source = null;
            ProcLabel.Visibility = Visibility.Collapsed;
            ProcessingPreviewTxt.Visibility = Visibility.Collapsed;
            
            UpdateProgress(0, TimeSpan.Zero, "Idle / Ready");
            TimeRemainingTxt.Text = "--:--"; TimeRemainingTxtProc.Text = "Time Remaining: --:--";
            BtnProcessTxt.Text = "Process Video";
        }

        private void ExtractMetadata(string path)
        {
            try
            {
                var psi = new ProcessStartInfo("TalePro_Probe.exe", $"-v quiet -print_format json -show_format -show_streams \"{path}\"") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                using var process = Process.Start(psi);
                string json = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                using var doc = JsonDocument.Parse(json);
                _width = 0; _height = 0; double fps = 0; string codec = "", ext = Path.GetExtension(path).ToUpper().Replace(".", "");
                
                if (doc.RootElement.TryGetProperty("streams", out var streams)) {
                    foreach (var stream in streams.EnumerateArray()) {
                        if (stream.TryGetProperty("codec_type", out var ctProp) && ctProp.GetString() == "video") {
                            _width = stream.GetProperty("width").GetInt32(); _height = stream.GetProperty("height").GetInt32();
                            codec = stream.TryGetProperty("codec_name", out var cProp) ? (cProp.GetString() ?? "").ToUpper() : "";
                            var rRate = stream.TryGetProperty("r_frame_rate", out var rProp) ? rProp.GetString() : "";
                            if (!string.IsNullOrEmpty(rRate)) {
                                var p = rRate.Split('/');
                                if (p.Length == 2 && double.TryParse(p[0], out double n) && double.TryParse(p[1], out double d) && d != 0) fps = n / d;
                            }
                            break;
                        }
                    }
                }
                if (doc.RootElement.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var dp))
                    _durationSeconds = double.Parse(dp.GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);

                Application.Current.Dispatcher.Invoke(() => {
                    int maxDim = Math.Max(_width, _height);
                    string resBadge = maxDim >= 3840 ? "4K" : (maxDim >= 2560 ? "2K" : (maxDim >= 1920 ? "1080p" : (maxDim >= 1280 ? "720p" : "SD")));
                    VidResolution.Text = $"{_width} x {_height} ({resBadge})"; ThumbResBadge.Text = resBadge;
                    VidFPS.Text = $"{Math.Round(fps)} FPS"; TimeSpan t = TimeSpan.FromSeconds(_durationSeconds);
                    VidDuration.Text = $"{(int)t.TotalHours:D2}:{t.Minutes:D2}:{t.Seconds:D2}"; VidCodec.Text = $"{codec} ({ext})";
                    UpdateEstimates();
                });
            } catch { }
        }

        private void ExtractThumbnail(string path)
        {
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), "talepro_thumb_" + Guid.NewGuid().ToString("N") + ".jpg");
                string ssArg = _durationSeconds > 1.0 ? "-ss 00:00:01" : "-ss 00:00:00";
                var psi = new ProcessStartInfo("TalePro_Engine.exe", $"-y {ssArg} -i \"{path}\" -vframes 1 -vf scale=320:-1 -q:v 2 \"{tmp}\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WindowStyle = ProcessWindowStyle.Hidden };
                using var p = Process.Start(psi); p.WaitForExit();
                if (File.Exists(tmp)) {
                    Application.Current.Dispatcher.Invoke(() => {
                        byte[] bytes = File.ReadAllBytes(tmp);
                        using (var ms = new MemoryStream(bytes)) {
                            var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.StreamSource = ms; bmp.EndInit(); bmp.Freeze();
                            ThumbImage.Source = bmp;
                        }
                        try { File.Delete(tmp); } catch { }
                    });
                }
            } catch { }
        }

        private void UpdateEstimates()
        {
            if (_originalSizeBytes == 0 || EstSizeTxt == null) return;
            
            double val = QualitySlider != null ? QualitySlider.Value : 6;
            bool isHevc = CodecCombo != null && CodecCombo.SelectedIndex == 1;
            bool hwAccel = HwAccelToggle != null && HwAccelToggle.IsChecked == true;
            SetGpu.IsChecked = hwAccel; SetCpu.IsChecked = !hwAccel;
            NavHwStatusTxt.Text = hwAccel ? "Universal GPU Engine" : "CPU • Multi-Core Engine";
            
            if (val == 10) {
                double gb = _originalSizeBytes / 1024.0 / 1024.0 / 1024.0;
                EstSizeTxt.Text = gb >= 1.0 ? $"~ {gb:0.00} GB" : $"~ {(_originalSizeBytes / 1024.0 / 1024.0):0.0} MB";
                EstBadgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#374151"));
                EstBadgeTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF"));
                EstBadgeTxt.Text = "Bypass Mode"; EstTimeTxt.Text = "⏱ < 2s";
            } else {
                double multiplier = 0.15 + ((val - 1) * 0.0875);
                if (isHevc) multiplier *= 0.80; 
                double estBytes = _originalSizeBytes * multiplier;
                double savingsPct = Math.Max(0, (1.0 - multiplier) * 100.0);
                
                double gb = estBytes / 1024.0 / 1024.0 / 1024.0;
                EstSizeTxt.Text = gb >= 1.0 ? $"~ {gb:0.00} GB" : $"~ {(estBytes / 1024.0 / 1024.0):0.0} MB";
                EstBadgeBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#064E3B"));
                EstBadgeTxt.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                EstBadgeTxt.Text = $"{savingsPct:0}% smaller";
                
                double pixels = _width * _height; if (pixels == 0) pixels = 1920 * 1080;
                double baseTimeSec = _durationSeconds * (pixels / 2073600.0);
                if (!hwAccel) baseTimeSec *= 4.0; else baseTimeSec *= 0.25;
                if (isHevc) baseTimeSec *= 1.3;
                baseTimeSec *= (0.5 + (val * 0.05));
                if (PresetCombo != null) {
                    string pre = ((ComboBoxItem)PresetCombo.SelectedItem)?.Content?.ToString() ?? "Tale-Pro (Recommended)";
                    if (pre.Contains("Fast")) baseTimeSec *= 0.5;
                    if (pre.Contains("Studio")) baseTimeSec *= 1.5;
                }
                
                TimeSpan t = TimeSpan.FromSeconds(Math.Max(1, baseTimeSec));
                EstTimeTxt.Text = t.TotalMinutes >= 1 ? $"⏱ ~ {(int)t.TotalMinutes}m {t.Seconds}s" : $"⏱ ~ {t.Seconds}s";
            }
        }

        private async void BtnProcess_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentVideoPath)) return;
            if (_isProcessing) return;

            _isProcessing = true;
            _cts = new CancellationTokenSource();
            BtnProcessTxt.Text = "Processing...";
            BtnStop.Visibility = Visibility.Visible;
            BtnClear.Visibility = Visibility.Collapsed;
            ProcessingPreviewTxt.Visibility = Visibility.Visible;
            UpdateProgress(0, TimeSpan.Zero, "Starting Engine...");

            string dir = Path.GetDirectoryName(_currentVideoPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(_currentVideoPath);
            _lastOutputPath = Path.Combine(dir, $"{name}_TalePro.mp4");

            double val = QualitySlider.Value;
            bool isHevc = CodecCombo.SelectedIndex == 1;
            bool useHw = HwAccelToggle.IsChecked == true;
            bool is160kAudio = AudioCombo.SelectedIndex == 1;
            string preset = ((ComboBoxItem)PresetCombo.SelectedItem)?.Content?.ToString() ?? "Tale-Pro (Recommended)";
            string fpsMode = ((ComboBoxItem)FpsCombo.SelectedItem)?.Content?.ToString() ?? "Same as Source";
            bool stripMeta = MetadataCombo.SelectedIndex == 0; 
            bool bypass = BypassCombo.SelectedIndex == 0; 

            await _compressionService.ProcessVideoAsync(_currentVideoPath, _lastOutputPath, val, isHevc, useHw, is160kAudio, fpsMode, stripMeta, bypass, _durationSeconds, preset, _cts.Token);
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _isProcessing = false;
            BtnStop.Visibility = Visibility.Collapsed;
            BtnClear.Visibility = Visibility.Visible;
            BtnProcessTxt.Text = "Process Video";
            ProcessingPreviewTxt.Visibility = Visibility.Collapsed;
            
            if (_isPlaying) TogglePlayPause();
            UpdateProgress(0, TimeSpan.Zero, "Processing Cancelled by User.");
            if (File.Exists(_lastOutputPath)) { try { File.Delete(_lastOutputPath); } catch { } }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            BtnClear.Visibility = Visibility.Collapsed;
            UpdateProgress(0, TimeSpan.Zero, "Idle / Ready");
            TimeRemainingTxt.Text = "--:--"; TimeRemainingTxtProc.Text = "Time Remaining: --:--";
        }

        private void UpdateCircProgress(System.Windows.Shapes.Ellipse ellipse, double percent)
        {
            double stroke = ellipse.StrokeThickness;
            double w = ellipse.Width > 0 ? ellipse.Width : (ellipse.Name == "CircProgress" ? 110.0 : 200.0);
            double radius = (w - stroke) / 2.0;
            double circumference = 2.0 * Math.PI * radius;
            double dashUnit = circumference / stroke;
            double filled = (percent / 100.0) * dashUnit;
            ellipse.StrokeDashArray = new DoubleCollection { filled, 1000 };
        }

        private void UpdateProgress(double percent, TimeSpan remaining, string status)
        {
            StatusTxt.Text = status; StatusTxtProc.Text = status;
            RingTxt.Text = $"{Math.Floor(percent)}%"; RingTxtProc.Text = $"{Math.Floor(percent)}%";
            LinearProgress.Value = percent; LinearProgressProc.Value = percent;
            
            if (remaining.TotalSeconds > 0 || (percent > 0 && percent < 100)) {
                string t = $"{(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";
                TimeRemainingTxt.Text = t; TimeRemainingTxtProc.Text = $"Time Remaining: {t}";
            }
            
            UpdateCircProgress(CircProgress, percent);
            UpdateCircProgress(CircProcessView, percent);
        }

        private async void ProcessingComplete(bool success, string path)
        {
            // FIX 1: Complete Suppression of FFmpeg Fail Error on User Cancellation
            if (_cts != null && _cts.IsCancellationRequested) return;

            _isProcessing = false;
            BtnStop.Visibility = Visibility.Collapsed;
            BtnClear.Visibility = Visibility.Collapsed;
            BtnProcessTxt.Text = "+ New Video";
            ProcessingPreviewTxt.Visibility = Visibility.Collapsed;
            
            if (success && !string.IsNullOrEmpty(path))
            {
                UpdateProgress(100, TimeSpan.Zero, "Completed successfully!");
                TimeRemainingTxt.Text = "00:00"; TimeRemainingTxtProc.Text = "Time Remaining: 00:00";
                _tempProcessed5s = Path.Combine(Path.GetTempPath(), "talepro_proc_" + Guid.NewGuid().ToString("N") + ".mp4");
                await _compressionService.Extract5sClipAsync(path, _tempProcessed5s);
                PlayerProcessed.Source = new Uri(_tempProcessed5s);
                ProcLabel.Visibility = Visibility.Visible;
            }
            else
            {
                UpdateProgress(0, TimeSpan.Zero, "Processing failed.");
                TimeRemainingTxt.Text = "--:--"; TimeRemainingTxtProc.Text = "Time Remaining: --:--";
                MessageBox.Show("FFmpeg failed to process. Check settings.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(_lastOutputPath)) Process.Start("explorer.exe", $"/select,\"{_lastOutputPath}\"");
            else if (!string.IsNullOrEmpty(_currentVideoPath)) Process.Start("explorer.exe", $"/select,\"{_currentVideoPath}\"");
        }

        private void SetModeStyle(Button activeBtn)
        {
            BtnModeSide.Background = Brushes.Transparent; BtnModeSide.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A95A5")); BtnModeSide.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A354D"));
            BtnModeOverlay.Background = Brushes.Transparent; BtnModeOverlay.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8A95A5")); BtnModeOverlay.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A354D"));
            activeBtn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#113668"));
            activeBtn.Foreground = Brushes.White;
            activeBtn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F80ED"));
        }

        private void BtnModeSide_Click(object sender, RoutedEventArgs e)
        {
            SetModeStyle(BtnModeSide);
            ColOrig.Width = new GridLength(1, GridUnitType.Star);
            ColProc.Width = new GridLength(1, GridUnitType.Star);
            PlayerOriginal.SetValue(Grid.ColumnSpanProperty, 1);
            ProcContainer.SetValue(Grid.ColumnSpanProperty, 1);
            if (ProcessedClip != null) ProcessedClip.Rect = new Rect(0,0,10000,10000);
            WipeSlider.Visibility = Visibility.Collapsed;
            CenterDivider.Visibility = Visibility.Visible;
            ProcLabel.HorizontalAlignment = HorizontalAlignment.Right;
            ProcLabel.Margin = new Thickness(0, 15, 15, 0);
        }

        private void BtnModeOverlay_Click(object sender, RoutedEventArgs e)
        {
            SetModeStyle(BtnModeOverlay);
            ColOrig.Width = new GridLength(1, GridUnitType.Star);
            ColProc.Width = new GridLength(0);
            PlayerOriginal.SetValue(Grid.ColumnSpanProperty, 2);
            ProcContainer.SetValue(Grid.ColumnSpanProperty, 2);
            WipeSlider.Visibility = Visibility.Visible;
            CenterDivider.Visibility = Visibility.Collapsed;
            ProcLabel.HorizontalAlignment = HorizontalAlignment.Left;
            ProcLabel.Margin = new Thickness(15, 15, 0, 0);
            UpdateClip();
        }

        private void PlayerContainer_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateClip();
        private void WipeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateClip();
        
        private void UpdateClip()
        {
            if (WipeSlider.Visibility == Visibility.Visible && PlayerContainer.ActualWidth > 0 && ProcessedClip != null)
                ProcessedClip.Rect = new Rect(0, 0, PlayerContainer.ActualWidth * (WipeSlider.Value / 100.0), PlayerContainer.ActualHeight);
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            PlayerOriginal.IsMuted = !PlayerOriginal.IsMuted;
            PlayerProcessed.IsMuted = PlayerOriginal.IsMuted;
            BtnMute.Content = PlayerOriginal.IsMuted ? "🔇" : "🔊";
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!_isScrubbing && PlayerOriginal.NaturalDuration.HasTimeSpan)
            {
                TimeScrubber.Value = PlayerOriginal.Position.TotalSeconds;
                TimeCodeTxt.Text = $"{PlayerOriginal.Position.TotalSeconds:00.00} / 05.00";
            }
        }

        private void TimeScrubber_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (Math.Abs(e.NewValue - PlayerOriginal.Position.TotalSeconds) > 0.1)
            {
                _isScrubbing = true;
                TimeSpan t = TimeSpan.FromSeconds(e.NewValue);
                PlayerOriginal.Position = t; PlayerProcessed.Position = t;
                TimeCodeTxt.Text = $"{e.NewValue:00.00} / 05.00";
                _isScrubbing = false;
            }
        }

        private void Player_MediaEnded(object sender, RoutedEventArgs e)
        {
            PlayerOriginal.Position = TimeSpan.Zero; PlayerProcessed.Position = TimeSpan.Zero;
            if (_isPlaying) { PlayerOriginal.Play(); PlayerProcessed.Play(); }
        }
    }
}


