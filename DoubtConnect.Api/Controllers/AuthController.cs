
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
            request.Email);

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

        return Ok(new
        {
            message = "Registration successful.",
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

    var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

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

}
