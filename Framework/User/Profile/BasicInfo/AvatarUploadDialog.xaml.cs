using System;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TM.Framework.Common.Helpers.Id;
using Microsoft.Win32;

namespace TM.Framework.User.Profile.BasicInfo
{
    [Obfuscation(Exclude = true, ApplyToMembers = true)]
    [Obfuscation(Feature = "no NecroBit", Exclude = false, ApplyToMembers = true)]
    public partial class AvatarUploadDialog : Window
    {
        private static readonly SolidColorBrush s_placeholderBg;
        static AvatarUploadDialog()
        {
            s_placeholderBg = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128));
            s_placeholderBg.Freeze();
        }

        private string _tempAvatarPath = string.Empty;

        public string SelectedAvatarPath => _tempAvatarPath;

        public AvatarUploadDialog()
        {
            InitializeComponent();

            SetDefaultPreview();
        }

        private void SetDefaultPreview()
        {
            try
            {
                var renderBitmap = new RenderTargetBitmap(150, 150, 96, 96, PixelFormats.Pbgra32);
                var drawingVisual = new DrawingVisual();

                using (DrawingContext dc = drawingVisual.RenderOpen())
                {
                    dc.DrawRectangle(s_placeholderBg, null, new Rect(0, 0, 150, 150));

                    var formattedText = new FormattedText(
                        "选择头像",
                        System.Globalization.CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Microsoft YaHei"),
                        16,
                        Brushes.Gray,
                        VisualTreeHelper.GetDpi(this).PixelsPerDip);

                    dc.DrawText(formattedText, new Point(
                        (150 - formattedText.Width) / 2,
                        (150 - formattedText.Height) / 2));
                }

                renderBitmap.Render(drawingVisual);
                PreviewImage.Source = renderBitmap;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AvatarDialog] 设置默认预览失败: {ex.Message}");
            }
        }

        private async void SelectImage_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp",
                    Title = "选择头像图片"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    await LoadImageAsync(openFileDialog.FileName);
                    _tempAvatarPath = openFileDialog.FileName;
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AvatarDialog] 选择图片失败: {ex.Message}");
                StandardDialog.ShowError($"选择图片失败：{ex.Message}", "选择失败");
            }
        }

        private async void PresetAvatar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Content is Image img && img.Source is ImageSource imageSource)
                {
                    string tempPath = CreateIconAvatar(imageSource);

                    if (!string.IsNullOrEmpty(tempPath))
                    {
                        await LoadImageAsync(tempPath);
                        _tempAvatarPath = tempPath;
                    }
                }
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AvatarDialog] 选择预设头像失败: {ex.Message}");
                StandardDialog.ShowError($"选择预设头像失败：{ex.Message}", "选择失败");
            }
        }

        private string CreateIconAvatar(ImageSource iconSource)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "TM_Avatars");
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }

                string tempPath = Path.Combine(tempDir, $"icon_avatar_{ShortIdGenerator.NewGuid()}.png");

                var renderBitmap = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32);
                var drawingVisual = new DrawingVisual();

                using (DrawingContext dc = drawingVisual.RenderOpen())
                {
                    dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 256, 256));

                    const double padding = 28;
                    dc.DrawImage(iconSource, new Rect(padding, padding, 256 - padding * 2, 256 - padding * 2));
                }

                renderBitmap.Render(drawingVisual);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

                using (var fileStream = new FileStream(tempPath, FileMode.Create))
                {
                    encoder.Save(fileStream);
                }

                TM.App.Log($"[AvatarDialog] 图标头像创建成功: {tempPath}");
                return tempPath;
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AvatarDialog] 创建图标头像失败: {ex.Message}");
                return string.Empty;
            }
        }

        private async Task LoadImageAsync(string imagePath)
        {
            try
            {
                var bitmap = await Task.Run(() =>
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(imagePath, UriKind.Absolute);
                    bmp.EndInit();
                    bmp.Freeze();
                    return bmp;
                });

                PreviewImage.Source = bitmap;

                TM.App.Log($"[AvatarDialog] 图片加载成功: {imagePath}");
            }
            catch (Exception ex)
            {
                TM.App.Log($"[AvatarDialog] 加载图片失败: {ex.Message}");
                StandardDialog.ShowError($"加载图片失败：{ex.Message}", "加载失败");
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_tempAvatarPath))
            {
                GlobalToast.Warning("未选择头像", "请先选择一个头像");
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            {
                DragMove();
            }
        }
    }
}

