
using System.ComponentModel.DataAnnotations;

namespace DoubtConnect.Api.DTOs;

public class ConfirmEmailRequest
{
    [Required]
    public Guid UserId { get; set; }

    [Required]
    public string Token { get; set; } = string.Empty;
}
