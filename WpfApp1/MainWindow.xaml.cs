using Microsoft.Web.WebView2.Core;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TFCMessenger;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using WpfApplication = System.Windows.Application;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;

namespace TFCMessenger
{
    public partial class MainWindow : Window
    {
        private Forms.NotifyIcon _notifyIcon;
        private Drawing.Icon? _originalIcon;
        private int _previousUnreadCount = 0;

        public MainWindow()
        {
            InitializeComponent();
            InitializeTrayIcon();
            InitializeAsync();
        }

        private void InitializeTrayIcon()
        {
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "favicon.ico");
            _originalIcon = File.Exists(iconPath)
                ? new Drawing.Icon(iconPath)
                : Drawing.SystemIcons.Application;

            _notifyIcon = new Forms.NotifyIcon
            {
                Icon = _originalIcon,
                Visible = true,
                Text = "TFC Messenger"
            };

            _notifyIcon.DoubleClick += (s, e) =>
            {
                Show();
                WindowState = WindowState.Normal;
            };

            var contextMenu = new Forms.ContextMenuStrip();
            contextMenu.Items.Add("خروج", null, (s, e) => WpfApplication.Current.Shutdown());
            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
            base.OnStateChanged(e);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
        }

        private async void InitializeAsync()
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
                    await Task.Delay(500);
                    await InjectSignalRBridge();

                    if (webView.CoreWebView2.Source.Contains("/Chat/GuestLogin", StringComparison.OrdinalIgnoreCase) ||
                        webView.CoreWebView2.Source.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase))
                    {
                        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Loger.txt");
                        if (File.Exists(logPath))
                        {
                            File.Delete(logPath);
                        }

                        await InjectLoginLogger();

                        var credentials = ReadLastLogin();
                        bool hasCredentials = !string.IsNullOrEmpty(credentials.username) && !string.IsNullOrEmpty(credentials.password);

                        if (!hasCredentials)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                loadingPanel.Visibility = Visibility.Collapsed;
                                webView.Visibility = Visibility.Visible;
                            });
                        }
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            loadingPanel.Visibility = Visibility.Collapsed;
                            webView.Visibility = Visibility.Visible;
                        });
                    }

                    await CheckInitialUnreadCount();
                }
            };

            webView.CoreWebView2.Navigate("http://localhost:5235/chat");
        }

        private async Task InjectLoginLogger()
        {
            var credentials = ReadLastLogin();
            var username = credentials.username?.Replace("\\", "\\\\").Replace("'", "\\'") ?? "";
            var password = credentials.password?.Replace("\\", "\\\\").Replace("'", "\\'") ?? "";
            var rememberMe = credentials.rememberMe ? "true" : "false";
            var hasCredentials = !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);

            string script = $@"
                (function() {{
                    setTimeout(() => {{
                        const usernameField = document.getElementById('username');
                        const passwordField = document.getElementById('password');
                        const rememberMeField = document.getElementById('rememberMe');
                        const loginBtn = document.getElementById('companyLoginBtn');

                        if (usernameField) usernameField.value = '{username}';
                        if (passwordField) passwordField.value = '{password}';
                        if (rememberMeField) rememberMeField.checked = {rememberMe};

                        if ({hasCredentials.ToString().ToLower()}) {{
                            setTimeout(() => {{
                                if (loginBtn && !loginBtn.disabled) {{
                                    loginBtn.click();
                                }}
                            }}, 1200);
                        }}
                    }}, 800);

                    // Intercept logout
                    const originalFetch = window.fetch;
                    window.fetch = async function(...args) {{
                        const url = args[0];
                        const options = args[1] || {{}};

                        if (url?.includes('/LogOut') || url?.includes('/Logout') || url?.includes('/Account/Logout')) {{
                            if (window.chrome?.webview) {{
                                window.chrome.webview.postMessage({{ type: 'loggedOut' }});
                            }}
                        }}

                        // Also intercept login for saving credentials
                        if (url?.includes('/Account/Login') && options?.method === 'POST') {{
                            const body = options.body;
                            const params = new URLSearchParams(body);
                            const username = params.get('UserName') || params.get('username');
                            const password = params.get('Password') || params.get('password');
                            const rememberMe = params.get('RememberMe') === 'true' || params.get('rememberMe') === 'true';

                            return originalFetch.apply(this, args).then(response => {{
                                response.clone().json().then(data => {{
                                    if (data?.success === true) {{
                                        window.chrome?.webview?.postMessage({{
                                            type: 'login',
                                            username: username,
                                            password: password,
                                            rememberMe: rememberMe
                                        }});
                                    }}
                                }}).catch(() => {{}});
                                return response;
                            }});
                        }}

                        return originalFetch.apply(this, args);
                    }};
                }})();
            ";

            await webView.CoreWebView2.ExecuteScriptAsync(script);
        }

        private async Task InjectSignalRBridge()
        {
            string script = @"
                console.log('[WPF Bridge] Injected at ' + new Date().toISOString());

                function tryRegister() {
                    if (typeof connection !== 'undefined' && connection.state === 'Connected') {
                        console.log('[WPF Bridge] SignalR Connected');

                        if (window._wpfBridgeRegistered) return;
                        window._wpfBridgeRegistered = true;

                        connection.on('UpdateUnreadCount', function(count) {
                            window.chrome?.webview?.postMessage({
                                type: 'unreadCount',
                                count: count
                            });
                        });

                        connection.on('ReceiveMessage', function(data) {
                            window.chrome?.webview?.postMessage({
                                type: 'newMessage',
                                senderName: data.senderName,
                                message: data.message,
                                senderImage: data.senderImage,
                                senderId: data.senderId
                            });
                        });

                        console.log('[WPF Bridge] Listeners Registered');
                    } else {
                        console.log('[WPF Bridge] Waiting for SignalR...');
                        setTimeout(tryRegister, 1200);
                    }
                }

                tryRegister();
            ";

            await webView.CoreWebView2.ExecuteScriptAsync(script);
        }

        private async Task CheckInitialUnreadCount()
        {
            try
            {
                var result = await webView.CoreWebView2.ExecuteScriptAsync(
                    "fetch('/Chat/GetUnreadCount').then(r => r.json()).then(d => d.unreadCount || 0)"
                );

                if (int.TryParse(result?.Trim('"'), out int count))
                {
                    Dispatcher.Invoke(() => UpdateTaskbarBadge(count));
                }
            }
            catch { /* silent fail */ }
        }

        private void WebView_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string raw = e.WebMessageAsJson;
                var json = JsonDocument.Parse(raw);
                var root = json.RootElement;

                if (!root.TryGetProperty("type", out var typeElement)) return;
                string? type = typeElement.GetString();

                Dispatcher.Invoke(() =>
                {
                    switch (type)
                    {
                        case "login":
                            string username = root.TryGetProperty("username", out var u) ? u.GetString() ?? "" : "";
                            string password = root.TryGetProperty("password", out var p) ? p.GetString() ?? "" : "";
                            bool rememberMe = root.TryGetProperty("rememberMe", out var r) && r.GetBoolean();
                            bool isGuest = root.TryGetProperty("isGuest", out var g) && g.GetBoolean();
                            
                            if (isGuest)
                            {
                                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Loger.txt");
                                if (!File.Exists(logPath))
                                {
                                    File.Create(logPath).Close();
                                }
                            }
                            else
                            {
                                LogLogin(username, password, rememberMe);
                            }
                            break;

                        case "unreadCount":
                            if (root.TryGetProperty("count", out var cnt))
                            {
                                int count = cnt.GetInt32();
                                UpdateTaskbarBadge(count);
                                _previousUnreadCount = count;
                            }
                            break;

                        case "newMessage":
                            string senderName = root.TryGetProperty("senderName", out var s) ? s.GetString() ?? "کاربر" : "کاربر";
                            string msg = root.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "";
                            string img = root.TryGetProperty("senderImage", out var i) ? i.GetString() : null;
                            string sid = root.TryGetProperty("senderId", out var id) ? id.GetString() : null;

                            msg = Regex.Replace(msg ?? "", "<.*?>", "");
                            msg = Shorten(msg, 60);

                            if (!IsActive)
                            {
                                var popup = new NotificationPopup(
                                    sender: senderName,
                                    message: msg,
                                    avatarUrl: string.IsNullOrEmpty(img) ? null : "http://localhost:5235" + img,
                                    chatUrl: string.IsNullOrEmpty(sid) ? null : $"http://localhost:5235/chat?userId={sid}"
                                );
                                NotificationManager.Instance.Show(popup);
                            }

                            if (root.TryGetProperty("count", out var newCnt))
                            {
                                int newCount = newCnt.GetInt32();
                                UpdateTaskbarBadge(newCount);
                                _previousUnreadCount = newCount;
                            }
                            break;

                        case "loggedOut":
                            try
                            {
                                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Loger.txt");
                                if (File.Exists(logPath))
                                {
                                    File.Delete(logPath);
                                }
                                // هدایت به صفحه لاگین یا صفحه اصلی
                                webView.CoreWebView2.Navigate("http://localhost:5235/chat");
                            }
                            catch { /* silent */ }
                            break;
                    }
                });
            }
            catch (Exception ex)
            {
                // برای دیباگ مفید است
                System.Diagnostics.Debug.WriteLine("WebMessage error: " + ex.Message);
            }
        }

        private void UpdateTaskbarBadge(int count)
        {
            TaskbarItemInfo = new System.Windows.Shell.TaskbarItemInfo
            {
                Overlay = count > 0 ? CreateBadgeIcon(count) : null,
                Description = count > 0 ? $"{count} پیام خوانده نشده" : ""
            };

            _notifyIcon.Icon = count > 0 ? CreateTrayIconWithBadge(count) : _originalIcon;
            _notifyIcon.Text = count > 0 ? $"TFC Messenger ({count})" : "TFC Messenger";
        }

        private Drawing.Icon CreateTrayIconWithBadge(int count)
        {
            using var bmp = new Drawing.Bitmap(32, 32);
            using var g = Drawing.Graphics.FromImage(bmp);

            g.Clear(Drawing.Color.Transparent);
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (_originalIcon != null)
            {
                using var iconBmp = _originalIcon.ToBitmap();
                g.DrawImage(iconBmp, 0, 0, 32, 32);
            }

            g.FillEllipse(Drawing.Brushes.Red, 16, 16, 16, 16);

            string text = count > 99 ? "99+" : count.ToString();
            using var font = new Drawing.Font("Arial", count > 99 ? 6f : 8f, Drawing.FontStyle.Bold);
            var size = g.MeasureString(text, font);

            g.DrawString(text, font, Drawing.Brushes.White,
                24 - size.Width / 2, 24 - size.Height / 2);

            return Drawing.Icon.FromHandle(bmp.GetHicon());
        }

        private ImageSource CreateBadgeIcon(int count)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawEllipse(Brushes.Red, null, new Point(8, 8), 8, 8);

                var text = new FormattedText(
                    count > 99 ? "99+" : count.ToString(),
                    System.Globalization.CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight,
                    new Typeface("Arial"),
                    count > 99 ? 6 : 8,
                    Brushes.White,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip
                );

                dc.DrawText(text, new Point(8 - text.Width / 2, 8 - text.Height / 2));
            }

            var rtb = new RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            return rtb;
        }

        private string Shorten(string text, int maxLength = 60)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return text.Length > maxLength ? text.Substring(0, maxLength) + "..." : text;
        }

        private void LogLogin(string username, string password, bool rememberMe)
        {
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Loger.txt");
                var encrypted = EncryptString(password);
                File.WriteAllText(logPath, $"{username}/{encrypted}");
            }
            catch { /* silent */ }
        }

        private (string? username, string? password, bool rememberMe) ReadLastLogin()
        {
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Loger.txt");
                if (!File.Exists(logPath)) return (null, null, false);

                var content = File.ReadAllText(logPath).Trim();
                if (string.IsNullOrEmpty(content)) return (null, null, false);

                var parts = content.Split('/');
                if (parts.Length >= 2)
                {
                    var decrypted = DecryptString(parts[1]);
                    return (parts[0], decrypted, true);
                }
            }
            catch { /* silent */ }

            return (null, null, false);
        }

        private string EncryptString(string text)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] ^= 0x5A;
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }

        private string DecryptString(string encrypted)
        {
            encrypted = encrypted.Replace("-", "+").Replace("_", "/");
            while (encrypted.Length % 4 != 0)
                encrypted += "=";

            var bytes = Convert.FromBase64String(encrypted);
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] ^= 0x5A;

            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }
}