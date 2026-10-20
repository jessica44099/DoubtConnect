
using System.ComponentModel.DataAnnotations;

namespace DoubtConnect.Api.DTOs;

public class ResendConfirmationRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}