
using System.Security.Claims;
using DoubtConnect.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoubtConnect.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public NotificationsController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetNotifications()
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        if (!await IsActiveUser(userId))
            return Unauthorized(new { message = "This account is not active." });

        var notifications = await _context.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(100)
            .Select(n => new
            {
                n.Id,
                n.Title,
                n.Message,
                n.Link,
                n.IsRead,
                n.CreatedAtUtc
            })
            .ToListAsync();

        return Ok(notifications);
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        if (!await IsActiveUser(userId))
            return Unauthorized(new { message = "This account is not active." });

        var count = await _context.Notifications.CountAsync(n =>
            n.UserId == userId && !n.IsRead);

        return Ok(new { unreadCount = count });
    }

    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        if (!await IsActiveUser(userId))
            return Unauthorized(new { message = "This account is not active." });

        var notification = await _context.Notifications.FirstOrDefaultAsync(n =>
            n.Id == id && n.UserId == userId);

        if (notification == null)
        {
            return NotFound(new
            {
                message = "Notification not found."
            });
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await _context.SaveChangesAsync();
        }

        return Ok(new
        {
            message = "Notification marked as read.",
            notification.Id,
            notification.IsRead
        });
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        if (!await IsActiveUser(userId))
            return Unauthorized(new { message = "This account is not active." });

        var unreadNotifications = await _context.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        foreach (var notification in unreadNotifications)
            notification.IsRead = true;

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "All notifications marked as read.",
            updatedCount = unreadNotifications.Count
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteNotification(Guid id)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        if (!await IsActiveUser(userId))
            return Unauthorized(new { message = "This account is not active." });

        var notification = await _context.Notifications.FirstOrDefaultAsync(n =>
            n.Id == id && n.UserId == userId);

        if (notification == null)
        {
            return NotFound(new
            {
                message = "Notification not found."
            });
        }

        _context.Notifications.Remove(notification);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private async Task<bool> IsActiveUser(Guid userId)
    {
        return await _context.Users.AnyAsync(u =>
            u.Id == userId && !u.IsDeleted);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out userId);
    }
}
