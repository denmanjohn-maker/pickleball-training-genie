using System;

namespace PickleballGenie.Models;

public class UserAvatarImage
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    public byte[] Data { get; set; } = Array.Empty<byte>();
    public string ContentType { get; set; } = "image/jpeg";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
