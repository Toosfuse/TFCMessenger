using System;
using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;

namespace TFCMessenger
{
    public partial class NotificationPopup : Window
    {
        private readonly string _chatUrl;
        private readonly DispatcherTimer _timer;

        public NotificationPopup(string sender, string message, string avatarUrl, string chatUrl)
        {
            InitializeComponent();

            txtMessage.Text = $"{sender}: {message}";
            _chatUrl = chatUrl;

            // لود آواتار
            try
            {
                if (!string.IsNullOrEmpty(avatarUrl))
                    imgAvatar.Source = new BitmapImage(new Uri(avatarUrl));
            }
            catch { }

            // پخش صدا
            try
            {
                new SoundPlayer("notification.wav").Play();
            }
            catch { }

            // موقعیت پایین راست
            PositionBottomRight();

            // تایمر ۵ ثانیه‌ای
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(5);
            _timer.Tick += (s, e) =>
            {
                _timer.Stop();
                this.Close();
            };

            _timer.Start();
        }

        private void PositionTopRight()
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Width - Width - 20;
            Top = workArea.Top + 40;
        }
        private void PositionBottomRight()
        {
            // کار با Taskbar
            var workArea = SystemParameters.WorkArea;

            // پایین-راست (چپ مرجع Width نوتیف)
            Left = workArea.Right - Width - 20; // فاصله از راست
            Top = workArea.Bottom - Height - 10; // فاصله از پایین (پایین ساعت)
        }
        // توقف تایمر هنگام هاور
        private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _timer?.Stop();
        }

        // ادامه تایمر هنگام خروج ماوس
        private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _timer?.Start();
        }

        // کلیک برای باز کردن چت
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var mainWindow = WpfApplication.Current.MainWindow as MainWindow;

            if (mainWindow != null)
            {
                mainWindow.Activate();

                if (mainWindow.WindowState == WindowState.Minimized)
                    mainWindow.WindowState = WindowState.Normal;

                if (!string.IsNullOrEmpty(_chatUrl))
                    mainWindow.webView.CoreWebView2.Navigate(_chatUrl);
            }

            this.Close();
        }
    }
}