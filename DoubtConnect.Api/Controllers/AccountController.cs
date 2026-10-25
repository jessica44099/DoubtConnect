
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using DoubtConnect.Api.Services;
using DoubtConnect.Domain.Entities;
using DoubtConnect.Domain.Enums;
using DoubtConnect.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace DoubtConnect.Api.Controllers;

[ApiController]
[Route("api/account")]
[Authorize]
public class AccountController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly EmailService _emailService;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        EmailService emailService)
    {
        _userManager = userManager;
        _db = db;
        _emailService = emailService;
    }

    [HttpGet("deletion-summary")]
    public async Task<IActionResult> GetDeletionSummary()
    {
        var user = await GetActiveUserAsync();

        if (user == null)
            return Unauthorized(new { message = "This account is not active." });

        var now = DateTime.UtcNow;

        var pendingCount = await _db.Bookings.CountAsync(b =>
            (b.StudentId == user.Id || b.MentorId == user.Id) &&
            b.Status == BookingStatus.Pending);

        var upcomingCount = await _db.Bookings.CountAsync(b =>
            (b.StudentId == user.Id || b.MentorId == user.Id) &&
            (b.Status == BookingStatus.Approved ||
             b.Status == BookingStatus.Rescheduled ||
             b.Status == BookingStatus.NeedsReschedule) &&
            b.SlotStartUtc >= now);

        var ongoingCount = await _db.Bookings.CountAsync(b =>
            (b.StudentId == user.Id || b.MentorId == user.Id) &&
            b.Status == BookingStatus.Ongoing);

        return Ok(new
        {
            PendingBookings = pendingCount,
            UpcomingBookings = upcomingCount,
            OngoingSessions = ongoingCount,
            CanDeleteAccount = ongoingCount == 0,
            Warning = ongoingCount > 0
                ? "Finish your ongoing sessions before deactivating your account. Eligible future bookings will be cancelled."
                : "Deleting your account will deactivate access and cancel eligible bookings. Completed session history will be retained."
        });
    }

    [HttpPut("email")]
    public async Task<IActionResult> RequestEmailChange(
        [FromBody] ChangeEmailRequest request)
    {
        var user = await GetActiveUserAsync();

        if (user == null)
            return Unauthorized(new { message = "This account is not active." });

        if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
            string.IsNullOrWhiteSpace(request.NewEmail))
        {
            return BadRequest(new
            {
                message = "Current password and new email are required."
            });
        }

        var newEmail = request.NewEmail.Trim();

        if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute()
                .IsValid(newEmail))
        {
            return BadRequest(new { message = "Enter a valid email address." });
        }

        if (string.Equals(user.Email, newEmail,
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "This is already your email address." });
        }

        if (!await _userManager.CheckPasswordAsync(
                user, request.CurrentPassword))
        {
            return BadRequest(new { message = "Your current password is incorrect." });
        }

        var existingUser = await _userManager.FindByEmailAsync(newEmail);

        if (existingUser != null && existingUser.Id != user.Id)
            return Conflict(new { message = "This email address is already in use." });

        var token = await _userManager.GenerateChangeEmailTokenAsync(
            user, newEmail);

        var encodedToken = WebEncoders.Base64UrlEncode(
            Encoding.UTF8.GetBytes(token));

        var confirmationUrl =
            $"{Request.Scheme}://{Request.Host}/api/account/confirm-email-change" +
            $"?userId={user.Id}&email={Uri.EscapeDataString(newEmail)}" +
            $"&token={Uri.EscapeDataString(encodedToken)}";

        var safeUrl = HtmlEncoder.Default.Encode(confirmationUrl);

        await _emailService.SendEmailAsync(
            newEmail,
            "Confirm your DoubtConnect email change",
            $"""
             <h2>Confirm your new email address</h2>
             <p>You requested to change the email address associated with your DoubtConnect account.</p>
             <p><a href="{safeUrl}">Confirm email change</a></p>
             <p>If you did not request this change, you can ignore this email.</p>
             """);

        return Ok(new
        {
            message = "A verification link has been sent to your new email address."
        });
    }

    [AllowAnonymous]
    [HttpGet("confirm-email-change")]
    public async Task<IActionResult> ConfirmEmailChange(
        [FromQuery] Guid userId,
        [FromQuery] string email,
        [FromQuery] string token)
    {
        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new
            {
                message = "The verification details are incomplete."
            });
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());

        if (user == null || user.IsDeleted)
            return BadRequest(new { message = "The account could not be verified." });

        var decodedToken = WebEncoders.Base64UrlDecode(token);
        var originalToken = Encoding.UTF8.GetString(decodedToken);
        var oldEmail = user.Email;
        var oldUserName = user.UserName;

        var existingUser = await _userManager.FindByEmailAsync(email);

        if (existingUser != null && existingUser.Id != user.Id)
            return Conflict(new { message = "This email address is already in use." });

        var result = await _userManager.ChangeEmailAsync(
            user, email, originalToken);

        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                message = "Email verification failed.",
                errors = result.Errors.Select(e => e.Description)
            });
        }

        // Update the username only when it was aligned with the old email.
        if (string.Equals(oldUserName, oldEmail,
                StringComparison.OrdinalIgnoreCase))
        {
            user.UserName = email;
            var updateResult = await _userManager.UpdateAsync(user);

            if (!updateResult.Succeeded)
            {
                return BadRequest(new
                {
                    message = "The email changed, but the username could not be updated.",
                    errors = updateResult.Errors.Select(e => e.Description)
                });
            }
        }

        // Revoke existing refresh tokens so sessions must authenticate again.
        var now = DateTime.UtcNow;
        var refreshTokens = await _db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
            .ToListAsync();

        foreach (var refreshToken in refreshTokens)
            refreshToken.RevokedAtUtc = now;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "Your email address has been verified and updated. Please sign in again."
        });
    }

    [HttpPut("password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request)
    {
        var user = await GetActiveUserAsync();

        if (user == null)
            return Unauthorized(new { message = "This account is not active." });

        if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
            string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new
            {
                message = "Current password and new password are required."
            });
        }

        if (request.NewPassword.Length < 8)
            return BadRequest(new
            {
                message = "The new password must be at least 8 characters."
            });

        var result = await _userManager.ChangePasswordAsync(
            user, request.CurrentPassword, request.NewPassword);

        if (!result.Succeeded)
        {
            return BadRequest(new
            {
                message = "The password could not be changed.",
                errors = result.Errors.Select(e => e.Description)
            });
        }

        // Invalidate refresh tokens after a credential change.
        var refreshTokens = await _db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
            .ToListAsync();

        foreach (var token in refreshTokens)
            token.RevokedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "Your password has been changed. Please sign in again."
        });
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAccount(
        [FromBody] DeleteAccountRequest request)
    {
        var user = await GetActiveUserAsync();

        if (user == null)
            return Unauthorized(new { message = "This account is not active." });

        if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
            request.ConfirmationText != "DELETE")
        {
            return BadRequest(new
            {
                message = "Enter your current password and type DELETE to confirm."
            });
        }

        if (!await _userManager.CheckPasswordAsync(
                user, request.CurrentPassword))
        {
            return BadRequest(new { message = "Your current password is incorrect." });
        }

        var now = DateTime.UtcNow;

        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        var hasOngoingBooking = await _db.Bookings.AnyAsync(b =>
            (b.StudentId == user.Id || b.MentorId == user.Id) &&
            b.Status == BookingStatus.Ongoing);

        if (hasOngoingBooking)
        {
            await transaction.RollbackAsync();

            return Conflict(new
            {
                message = "You cannot deactivate your account while a session is ongoing. Finish the session first."
            });
        }

        var bookings = await _db.Bookings
            .Where(b =>
                (b.StudentId == user.Id || b.MentorId == user.Id) &&
                (
                    b.Status == BookingStatus.Pending ||
                    (
                        (b.Status == BookingStatus.Approved ||
                         b.Status == BookingStatus.Rescheduled ||
                         b.Status == BookingStatus.NeedsReschedule) &&
                        b.SlotStartUtc >= now
                    )
                ))
            .ToListAsync();

        foreach (var booking in bookings)
        {
            booking.Status = BookingStatus.Cancelled;
            booking.UpdatedAtUtc = now;
            booking.RejectionReason =
                "Cancelled because an account was deactivated.";

            var otherUserId = booking.StudentId == user.Id
                ? booking.MentorId
                : booking.StudentId;

            _db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(),
                UserId = otherUserId,
                Title = "Booking cancelled",
                Message = "A booking was cancelled because the other participant deactivated their account.",
                Link = "/dashboard",
                IsRead = false,
                CreatedAtUtc = now
            });

            var slot = await _db.AvailabilitySlots.FirstOrDefaultAsync(s =>
                s.MentorId == booking.MentorId &&
                s.StartTimeUtc == booking.SlotStartUtc &&
                s.EndTimeUtc == booking.SlotEndUtc);

            if (slot != null)
            {
                var conflictingActiveBooking = await _db.Bookings.AnyAsync(b =>
                    b.Id != booking.Id &&
                    b.MentorId == slot.MentorId &&
                    b.SlotStartUtc < slot.EndTimeUtc &&
                    b.SlotEndUtc > slot.StartTimeUtc &&
                    (
                        b.Status == BookingStatus.Pending ||
                        b.Status == BookingStatus.Approved ||
                        b.Status == BookingStatus.Ongoing ||
                        b.Status == BookingStatus.Rescheduled ||
                        b.Status == BookingStatus.NeedsReschedule
                    ));

                if (!conflictingActiveBooking)
                    slot.Status = AvailabilityStatus.Free;
            }
        }

        // Deactivate the mentor's future availability and calendar entries.
        if (user.Role == UserRole.Mentor)
        {
            var futureSlots = await _db.AvailabilitySlots
                .Where(s => s.MentorId == user.Id && s.StartTimeUtc >= now)
                .ToListAsync();

            foreach (var slot in futureSlots)
                slot.Status = AvailabilityStatus.Busy;

            var calendarEvents = await _db.MentorCalendarEvents
                .Where(e => e.MentorId == user.Id)
                .ToListAsync();

            // Remove calendar events because this mentor is no longer bookable.
            _db.MentorCalendarEvents.RemoveRange(calendarEvents);

            var weeklyTemplates = await _db.WeeklyAvailabilityTemplates
                .Where(t => t.MentorId == user.Id)
                .ToListAsync();

            _db.WeeklyAvailabilityTemplates.RemoveRange(weeklyTemplates);
        }

        var userMessages = await _db.Messages
            .Where(m => m.SenderId == user.Id)
            .ToListAsync();

        foreach (var message in userMessages)
            message.Content =
                "[Message removed because the sender deactivated their account]";

        var refreshTokens = await _db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAtUtc == null)
            .ToListAsync();

        foreach (var refreshToken in refreshTokens)
            refreshToken.RevokedAtUtc = now;

        _db.Notifications.RemoveRange(
            await _db.Notifications
                .Where(n => n.UserId == user.Id)
                .ToListAsync());

        // Keep historical bookings and reviews; remove identifiable profile data.
        var mentorProfile = await _db.MentorProfiles
            .FirstOrDefaultAsync(p => p.UserId == user.Id);

        if (mentorProfile != null)
        {
            mentorProfile.Expertise = string.Empty;
            mentorProfile.Bio = null;
            mentorProfile.HourlyRate = null;
            mentorProfile.TotalDoubtsCleared = 0;
        }

        var studentProfile = await _db.StudentProfiles
            .FirstOrDefaultAsync(p => p.UserId == user.Id);

        if (studentProfile != null)
            studentProfile.EducationLevel = null;

        user.IsDeleted = true;
        user.DeletedAtUtc = now;
        user.FullName = "Deleted user";
        user.Email = $"deleted-{user.Id:N}@account.invalid";
        user.NormalizedEmail = user.Email.ToUpperInvariant();
        user.UserName = $"deleted-{user.Id:N}";
        user.NormalizedUserName = user.UserName.ToUpperInvariant();
        user.EmailConfirmed = false;
        user.PhoneNumber = null;
        user.PhoneNumberConfirmed = false;
        user.TwoFactorEnabled = false;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");

        // UserManager uses the same scoped DbContext. Its update is within this transaction.
        var updateResult = await _userManager.UpdateAsync(user);

        if (!updateResult.Succeeded)
        {
            await transaction.RollbackAsync();

            return BadRequest(new
            {
                message = "The account could not be deactivated.",
                errors = updateResult.Errors.Select(e => e.Description)
            });
        }

        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(new
        {
            message = "Your account has been deactivated. Completed session history has been preserved."
        });
    }

    private async Task<ApplicationUser?> GetActiveUserAsync()
    {
        var userId = GetUserId();

        if (userId == null)
            return null;

        return await _db.Users.FirstOrDefaultAsync(
            u => u.Id == userId.Value && !u.IsDeleted);
    }

    private Guid? GetUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}

public sealed class ChangeEmailRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewEmail { get; set; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public sealed class DeleteAccountRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string ConfirmationText { get; set; } = string.Empty;
}
