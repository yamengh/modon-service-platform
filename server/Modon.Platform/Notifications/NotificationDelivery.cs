using System.Collections.Concurrent;
using FormCMS.Auth.Models;
using FormCMS.Notify.Services;
using Microsoft.EntityFrameworkCore;
using Modon.Platform.Data;

namespace Modon.Platform.Notifications;

/// <summary>Who a notification goes to, with the contact details the e-mail/SMS channels need.</summary>
public sealed record NotificationRecipient(string UserId, string? Email, string? Phone);

/// <summary>A way to deliver a saved notification beyond the Modon inbox (FormCMS inbox, e-mail, SMS).</summary>
public interface INotificationChannel
{
    string Name { get; }
    Task DeliverAsync(ModonNotification n, NotificationRecipient to, CancellationToken ct);
}

/// <summary>
/// Development stand-ins for e-mail and SMS: they only LOG what would be sent and keep the last messages in memory
/// (for diagnostics and tests). No provider is called and no credentials are needed. Real delivery is not enabled.
/// </summary>
public sealed class DevDeliveryLog
{
    public sealed record Entry(string Channel, string To, string Text, DateTime At);

    private readonly ConcurrentQueue<Entry> _entries = new();

    public void Add(Entry e)
    {
        _entries.Enqueue(e);
        while (_entries.Count > 500 && _entries.TryDequeue(out _)) { }
    }

    public IReadOnlyList<Entry> Entries => _entries.ToArray();
}

public sealed class LoggingEmailChannel(DevDeliveryLog log, ILogger<LoggingEmailChannel> logger, TimeProvider clock) : INotificationChannel
{
    public string Name => "email";

    public Task DeliverAsync(ModonNotification n, NotificationRecipient to, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(to.Email)) return Task.CompletedTask;
        logger.LogInformation("[DEV E-MAIL — NOT SENT] To {Email}: {TitleAr} / {TitleEn}", to.Email, n.TitleAr, n.TitleEn);
        log.Add(new DevDeliveryLog.Entry(Name, to.Email, n.TitleEn, clock.GetUtcNow().UtcDateTime));
        return Task.CompletedTask;
    }
}

public sealed class LoggingSmsChannel(DevDeliveryLog log, ILogger<LoggingSmsChannel> logger, TimeProvider clock) : INotificationChannel
{
    public string Name => "sms";

    public Task DeliverAsync(ModonNotification n, NotificationRecipient to, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(to.Phone)) return Task.CompletedTask;
        logger.LogInformation("[DEV SMS — NOT SENT] To {Phone}: {TitleAr}", to.Phone, n.TitleAr);
        log.Add(new DevDeliveryLog.Entry(Name, to.Phone, n.TitleAr, clock.GetUtcNow().UtcDateTime));
        return Task.CompletedTask;
    }
}

/// <summary>
/// Copies the notification into FormCMS's own notification store (<c>__notifications</c>, <c>/api/notifications</c>),
/// using FormCMS's <see cref="NotificationCollectService"/> so its unread counter stays right.
/// </summary>
public sealed class FormCmsInboxChannel(NotificationContext context) : INotificationChannel
{
    public string Name => "formcms";

    public Task DeliverAsync(ModonNotification n, NotificationRecipient to, CancellationToken ct)
        => new NotificationCollectService(context).Insert(new FormCMS.Notify.Models.Notification(
            UserId: to.UserId,
            SenderId: n.History?.ActorUserId ?? "",
            ActionType: n.Kind.ToString(),
            MessageType: "modonRequest",
            Message: $"{n.TitleAr} | {n.TitleEn}",
            Url: n.Link), ct);
}

/// <summary>
/// Hands saved notifications to the channels. Each notification is claimed with a conditional update before it is
/// delivered, so two dispatchers never deliver the same one; a failed delivery is released and retried later.
/// </summary>
public sealed class NotificationDispatcher(
    ModonDbContext db,
    CmsDbContext identityDb,
    IEnumerable<INotificationChannel> channels,
    TimeProvider clock,
    ILogger<NotificationDispatcher> logger)
{
    public const int MaxAttempts = 5;

    /// <summary>Delivers the given notifications, or every pending one when <paramref name="ids"/> is null.</summary>
    public async Task<int> DispatchAsync(IReadOnlyCollection<long>? ids, CancellationToken ct)
    {
        var query = db.Notifications.AsNoTracking().Include(n => n.History)
            .Where(n => n.DispatchedAt == null && n.DispatchAttempts < MaxAttempts);
        if (ids is not null) query = query.Where(n => ids.Contains(n.Id));
        var pending = await query.OrderBy(n => n.Id).Take(100).ToListAsync(ct);
        if (pending.Count == 0) return 0;

        var userIds = pending.Select(n => n.RecipientUserId).Distinct().ToArray();
        var contacts = await identityDb.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => new NotificationRecipient(u.Id, u.Email, u.PhoneNumber), ct);

        var delivered = 0;
        foreach (var n in pending)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            // Claim: only one dispatcher wins this update.
            var claimed = await db.Notifications.Where(x => x.Id == n.Id && x.DispatchedAt == null)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.DispatchedAt, now).SetProperty(x => x.DispatchAttempts, x => x.DispatchAttempts + 1), ct);
            if (claimed == 0) continue;
            try
            {
                var to = contacts.GetValueOrDefault(n.RecipientUserId) ?? new NotificationRecipient(n.RecipientUserId, null, null);
                foreach (var channel in channels) await channel.DeliverAsync(n, to, ct);
                delivered++;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Notification {Id} could not be delivered; it will be retried", n.Id);
                await db.Notifications.Where(x => x.Id == n.Id)
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.DispatchedAt, (DateTime?)null), CancellationToken.None);
            }
        }
        return delivered;
    }
}

/// <summary>Retries notifications whose delivery failed or was interrupted (every 30 seconds).</summary>
public sealed class NotificationRetryService(IServiceScopeFactory scopes, ILogger<NotificationRetryService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchAsync(null, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Notification retry sweep failed");
            }
        }
    }
}
