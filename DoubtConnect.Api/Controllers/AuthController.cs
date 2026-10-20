
using DoubtConnect.Api.DTOs;
using DoubtConnect.Domain.Entities;
using DoubtConnect.Domain.Enums;
using DoubtConnect.Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.WebUtilities;
using DoubtConnect.Api.Services;

namespace DoubtConnect.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly EmailService _emailService;




public AuthController(
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext dbContext,
    IConfiguration configuration,
    EmailService emailService)
{
    _userManager = userManager;
    _dbContext = dbContext;
    _configuration = configuration;
    _emailService = emailService;
}



[HttpPost("register")]
public async Task<IActionResult> Register(
    [FromBody] RegisterRequest request)
{
    var existingUser = await _userManager.FindByEmailAsync(
        request.Email.Trim());

    if (existingUser != null)
    {
        return Conflict(new
        {
            message = "An account with this email already exists."
        });
    }

    var isMentor = request.Role == "Mentor";

    var user = new ApplicationUser
    {
        UserName = request.Email.Trim(),
        Email = request.Email.Trim(),
        FullName = request.FullName.Trim(),
        Role = isMentor ? UserRole.Mentor : UserRole.Student,
        CreatedAtUtc = DateTime.UtcNow
    };

    var createResult = await _userManager.CreateAsync(
        user, request.Password);

    if (!createResult.Succeeded)
    {
        return BadRequest(new
        {
            errors = createResult.Errors.Select(
                error => error.Description)
        });
    }

    var roleResult = await _userManager.AddToRoleAsync(
        user, request.Role);

    if (!roleResult.Succeeded)
    {
        await _userManager.DeleteAsync(user);

        return BadRequest(new
        {
            errors = roleResult.Errors.Select(
                error => error.Description)
        });
    }

    if (isMentor)
    {
        _dbContext.MentorProfiles.Add(new MentorProfile
        {
            UserId = user.Id,
            Expertise = "General"
        });
    }
    else
    {
        _dbContext.StudentProfiles.Add(new StudentProfile
        {
            UserId = user.Id
        });
    }

    await _dbContext.SaveChangesAsync();

    // Generate the email confirmation token.
    var confirmationToken =
        await _userManager.GenerateEmailConfirmationTokenAsync(user);

    var encodedToken = WebEncoders.Base64UrlEncode(
        Encoding.UTF8.GetBytes(confirmationToken));

    var confirmationLink =
        $"http://localhost:4200/confirm-email?userId={user.Id}" +
        $"&token={Uri.EscapeDataString(encodedToken)}";

    // Send the verification email.
    var safeFullName =
        System.Net.WebUtility.HtmlEncode(user.FullName);

    var safeConfirmationLink =
        System.Net.WebUtility.HtmlEncode(confirmationLink);

    var htmlBody = $"""
        <div style="font-family: Arial, sans-serif; max-width: 600px; margin: auto; padding: 24px;">
            <h2>Welcome to DoubtConnect!</h2>
            <p>Hi {safeFullName},</p>
            <p>Thank you for creating your account.</p>
            <p>Please verify your email address to activate your account.</p>
            <p>
                <a href="{safeConfirmationLink}"
                   style="background-color:#2563eb;color:white;padding:12px 20px;text-decoration:none;border-radius:6px;">
                    Verify Email
                </a>
            </p>
            <p>If you did not create this account, you can ignore this email.</p>
            <p>Best regards,<br><strong>Team DoubtConnect</strong></p>
        </div>
        """;

    await _emailService.SendEmailAsync(
        user.Email!,
        "Verify your DoubtConnect email",
        htmlBody);

    return Ok(new
    {
        message = "Registration successful. Please check your email to verify your account.",
        userId = user.Id,
        fullName = user.FullName,
        email = user.Email,
        role = request.Role
    });
}


[HttpPost("login")]
public async Task<IActionResult> Login(
    [FromBody] LoginRequest request)
{
    var user = await _userManager.FindByEmailAsync(
        request.Email.Trim());

    if (user == null ||
        !await _userManager.CheckPasswordAsync(user, request.Password))
    {
        return Unauthorized(new
        {
            message = "Invalid email or password."
        });
    }

    // Require email verification before allowing login.
    if (!user.EmailConfirmed)
    {
        return Unauthorized(new
        {
            message = "Please verify your email before logging in."
        });
    }

    var roles = await _userManager.GetRolesAsync(user);

    var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.FullName),
        new Claim(ClaimTypes.Email, user.Email ?? string.Empty)
    };

    foreach (var role in roles)
    {
        claims.Add(new Claim(ClaimTypes.Role, role));
    }

    var key = _configuration["Jwt:Key"]
        ?? throw new InvalidOperationException("Jwt:Key is missing.");

    var issuer = _configuration["Jwt:Issuer"]
        ?? throw new InvalidOperationException("Jwt:Issuer is missing.");

    var audience = _configuration["Jwt:Audience"]
        ?? throw new InvalidOperationException("Jwt:Audience is missing.");

    var signingKey = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes(key));

    var credentials = new SigningCredentials(
        signingKey, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(30),
        signingCredentials: credentials);

    var accessToken = new JwtSecurityTokenHandler()
        .WriteToken(token);

    return Ok(new
    {
        message = "Login successful.",
        accessToken,
        expiresInMinutes = 30,
        user = new
        {
            id = user.Id,
            fullName = user.FullName,
            email = user.Email,
            roles
        }
    });
}


[HttpPost("forgot-password")]
public async Task<IActionResult> ForgotPassword(
    [FromBody] ForgotPasswordRequest request)
{
    var user = await _userManager.FindByEmailAsync(
        request.Email.Trim());

    // Return the same response whether the account exists or not.
    if (user != null)
    {
        var token = await _userManager
            .GeneratePasswordResetTokenAsync(user);

        var encodedToken = WebEncoders.Base64UrlEncode(
            Encoding.UTF8.GetBytes(token));

        var resetLink =
            $"http://localhost:4200/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(encodedToken)}";

        var htmlBody = $"""
            <h2>Reset your DoubtConnect password</h2>
            <p>Hello {System.Net.WebUtility.HtmlEncode(user.FullName)},</p>
            <p>We received a request to reset your password.</p>
            <p>
                <a href="{resetLink}">Reset your password</a>
            </p>
            <p>If you did not request this, you can ignore this email.</p>
            <p>This link will expire and can only be used according to your account's password-reset token settings.</p>
            """;

        await _emailService.SendEmailAsync(
            user.Email!,
            "Reset your DoubtConnect password",
            htmlBody);
    }

    return Ok(new
    {
        message = "If an account exists for that email, password reset instructions will be sent."
    });
}


[HttpPost("reset-password")]
public async Task<IActionResult> ResetPassword(
    [FromBody] ResetPasswordRequest request)
{
    var user = await _userManager.FindByEmailAsync(
        request.Email.Trim());

    if (user == null)
    {
        return BadRequest(new
        {
            message = "The password reset link is invalid or has expired."
        });
    }

    string decodedToken;

    try
    {
        decodedToken = Encoding.UTF8.GetString(
            WebEncoders.Base64UrlDecode(request.Token));
    }
    catch (FormatException)
    {
        return BadRequest(new
        {
            message = "The password reset link is invalid or has expired."
        });
    }

    var result = await _userManager.ResetPasswordAsync(
        user,
        decodedToken,
        request.NewPassword);

    if (!result.Succeeded)
    {
        return BadRequest(new
        {
            errors = result.Errors.Select(
                error => error.Description)
        });
    }

    return Ok(new
    {
        message = "Password reset successful. You can now log in with your new password."
    });
}

[HttpPost("confirm-email")]
public async Task<IActionResult> ConfirmEmail(
    [FromBody] ConfirmEmailRequest request)
{
    var user = await _userManager.FindByIdAsync(
        request.UserId.ToString());

    if (user == null)
    {
        return BadRequest(new
        {
            message = "The confirmation link is invalid or has expired."
        });
    }

    string decodedToken;

    try
    {
        decodedToken = Encoding.UTF8.GetString(
            WebEncoders.Base64UrlDecode(request.Token));
    }
    catch (FormatException)
    {
        return BadRequest(new
        {
            message = "The confirmation link is invalid or has expired."
        });
    }

    var result = await _userManager.ConfirmEmailAsync(
        user, decodedToken);

    if (!result.Succeeded)
    {
        return BadRequest(new
        {
            errors = result.Errors.Select(
                error => error.Description)
        });
    }

    return Ok(new
    {
        message = "Your email has been confirmed. You can now log in."
    });
}

[HttpPost("resend-confirmation")]
public async Task<IActionResult> ResendConfirmation(
    [FromBody] ResendConfirmationRequest request)
{
    var user = await _userManager.FindByEmailAsync(request.Email);

    if (user == null || user.EmailConfirmed)
    {
        return Ok(new
        {
            message = "If verification is required, an email will be sent."
        });
    }

    var confirmationToken =
        await _userManager.GenerateEmailConfirmationTokenAsync(user);

    var encodedToken = WebEncoders.Base64UrlEncode(
        Encoding.UTF8.GetBytes(confirmationToken));

    var confirmationLink =
        $"http://localhost:4200/confirm-email?userId={user.Id}&token={Uri.EscapeDataString(encodedToken)}";

    var emailBody = $"""
        <h2>Verify your DoubtConnect email</h2>
        <p>Click the link below to verify your account.</p>
        <p><a href="{confirmationLink}">Verify Email</a></p>
        """;

    await _emailService.SendEmailAsync(
        user.Email!,
        "Verify your DoubtConnect email",
        emailBody);

    return Ok(new
    {
        message = "If verification is required, an email will be sent."
    });
}


}
