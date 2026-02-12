using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WpfApp1
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            InitializeAsync();
        }

        async void InitializeAsync()
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ERPChat"
            );

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await webView.EnsureCoreWebView2Async(env);

            webView.CoreWebView2.WebMessageReceived += WebView_WebMessageReceived;
            webView.CoreWebView2.NavigationCompleted += async (s, e) =>
            {
                if (e.IsSuccess)
                {
                    await Task.Delay(2000);
                    await InjectSignalRBridge();
                    await CheckInitialUnreadCount();
                }
            };
            webView.CoreWebView2.Navigate("http://localhost:5235/Chat");
        }

        private async Task InjectSignalRBridge()
        {
            await webView.CoreWebView2.ExecuteScriptAsync(@"
                if (typeof connection !== 'undefined') {
                    connection.on('UpdateUnreadCount', function(count) {
                        if (window.chrome && window.chrome.webview) {
                            window.chrome.webview.postMessage({ type: 'unreadCount', count: count });
                        }
                    });
                }
            ");
        }

        private async Task CheckInitialUnreadCount()
        {
            try
            {
                var result = await webView.CoreWebView2.ExecuteScriptAsync(
                    "fetch('/Chat/GetUnreadCount').then(r => r.json()).then(d => d.unreadCount)"
                );
                if (int.TryParse(result, out int count))
                {
                    Dispatcher.Invoke(() => UpdateTaskbarBadge(count));
                }
            }
            catch { }
        }

        private void WebView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(e.WebMessageAsJson);
                if (json.RootElement.TryGetProperty("type", out var typeElement) &&
                    typeElement.GetString() == "unreadCount")
                {
                    if (json.RootElement.TryGetProperty("count", out var countElement))
                    {
                        int count = countElement.GetInt32();
                        Dispatcher.Invoke(() => UpdateTaskbarBadge(count));
                    }
                }
            }
            catch { }
        }

        private void UpdateTaskbarBadge(int count)
        {
            var taskbarInfo = this.TaskbarItemInfo ?? new System.Windows.Shell.TaskbarItemInfo();
            this.TaskbarItemInfo = taskbarInfo;

            if (count > 0)
            {
                taskbarInfo.Overlay = CreateBadgeIcon(count);
                taskbarInfo.Description = $"{count} پیام خوانده نشده";
            }
            else
            {
                taskbarInfo.Overlay = null;
                taskbarInfo.Description = "";
            }
        }

        private ImageSource CreateBadgeIcon(int count)
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawEllipse(Brushes.Red, null, new Point(8, 8), 8, 8);

                var text = new FormattedText(
                    count > 99 ? "99+" : count.ToString(),
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Arial"),
                    count > 99 ? 6 : 8,
                    Brushes.White,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip
                );

                context.DrawText(text, new Point(8 - text.Width / 2, 8 - text.Height / 2));
            }

            var bitmap = new RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return bitmap;
        }
    }
}
