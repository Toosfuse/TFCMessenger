using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TFCMessenger
{
    public class NotificationManager
    {
        private static readonly NotificationManager _instance = new NotificationManager();
        public static NotificationManager Instance => _instance;

        private readonly Queue<NotificationPopup> _queue = new();
        private bool _isShowing = false;

        private NotificationManager()
        {
        }

        public void Show(NotificationPopup popup)
        {
            _queue.Enqueue(popup);
            ShowNext();
        }

        private void ShowNext()
        {
            if (_isShowing || _queue.Count == 0)
                return;

            _isShowing = true;
            var popup = _queue.Dequeue();

            popup.Closed += (s, e) =>
            {
                _isShowing = false;
                ShowNext();
            };

            popup.Show();
        }
    }
}
