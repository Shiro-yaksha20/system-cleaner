using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace SystemCleaner.App.Services;

public sealed class NotificationService : INotificationService
{
    private const int MaxNotifications = 50;
    private readonly ObservableCollection<NotificationMessage> _notifications = new();
    private readonly ReadOnlyObservableCollection<NotificationMessage> _readonlyNotifications;
    private readonly Dispatcher _dispatcher;

    public NotificationService()
    {
        _readonlyNotifications = new ReadOnlyObservableCollection<NotificationMessage>(_notifications);
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public ReadOnlyObservableCollection<NotificationMessage> Notifications => _readonlyNotifications;

    public event EventHandler<NotificationMessage>? NotificationPublished;

    public NotificationMessage Publish(string message, NotificationSeverity severity, string? detail = null, string? context = null, string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Message cannot be null or whitespace.", nameof(message));
        }

        var notification = new NotificationMessage(
            Id: Guid.NewGuid().ToString("N"),
            Severity: severity,
            Message: message,
            Detail: detail,
            Context: context,
            Timestamp: DateTimeOffset.Now,
            CorrelationId: !string.IsNullOrWhiteSpace(correlationId) ? correlationId : null);

        if (_dispatcher.CheckAccess())
        {
            AppendNotification(notification);
        }
        else
        {
            _ = _dispatcher.BeginInvoke(new Action(() => AppendNotification(notification)));
        }

        return notification;
    }

    public NotificationMessage PublishInfo(string message, string? detail = null, string? context = null, string? correlationId = null) =>
        Publish(message, NotificationSeverity.Info, detail, context, correlationId);

    public NotificationMessage PublishWarning(string message, string? detail = null, string? context = null, string? correlationId = null) =>
        Publish(message, NotificationSeverity.Warning, detail, context, correlationId);

    public NotificationMessage PublishError(string message, string? detail = null, string? context = null, string? correlationId = null) =>
        Publish(message, NotificationSeverity.Error, detail, context, correlationId);

    public void Dismiss(NotificationMessage message)
    {
        if (message is null)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            _ = _notifications.Remove(message);
        }
        else
        {
            _ = _dispatcher.BeginInvoke(new Action(() => _notifications.Remove(message)));
        }
    }

    public void Clear(Func<NotificationMessage, bool>? predicate = null)
    {
        if (_dispatcher.CheckAccess())
        {
            ClearInternal(predicate);
        }
        else
        {
            _ = _dispatcher.BeginInvoke(new Action(() => ClearInternal(predicate)));
        }
    }

    private void AppendNotification(NotificationMessage notification)
    {
        _notifications.Insert(0, notification);
        if (_notifications.Count > MaxNotifications)
        {
            while (_notifications.Count > MaxNotifications)
            {
                _notifications.RemoveAt(_notifications.Count - 1);
            }
        }

        NotificationPublished?.Invoke(this, notification);
    }

    private void ClearInternal(Func<NotificationMessage, bool>? predicate)
    {
        if (predicate is null)
        {
            _notifications.Clear();
            return;
        }

        var toRemove = _notifications.Where(predicate).ToList();
        foreach (var entry in toRemove)
        {
            _notifications.Remove(entry);
        }
    }
}
