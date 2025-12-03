using System;
using System.Collections.ObjectModel;

namespace SystemCleaner.App.Services;

public interface INotificationService
{
    ReadOnlyObservableCollection<NotificationMessage> Notifications { get; }

    event EventHandler<NotificationMessage>? NotificationPublished;

    NotificationMessage Publish(string message, NotificationSeverity severity, string? detail = null, string? context = null, string? correlationId = null);

    NotificationMessage PublishInfo(string message, string? detail = null, string? context = null, string? correlationId = null);

    NotificationMessage PublishWarning(string message, string? detail = null, string? context = null, string? correlationId = null);

    NotificationMessage PublishError(string message, string? detail = null, string? context = null, string? correlationId = null);

    void Dismiss(NotificationMessage message);

    void Clear(Func<NotificationMessage, bool>? predicate = null);
}
