using Xunit;
using Up2Ai.Services;

namespace Up2Ai.Tests;

public class PasswordHashGenerator
{
    [Fact(DisplayName = "Generate hash for 123456")]
    public void GenerateHash()
    {
        var password = "123456";
        var hash = AdminAuth.HashPassword(password);
        Assert.NotNull(hash);
        // Output the hash to help with configuration
        Console.WriteLine($"Hash for '{password}': {hash}");
        Assert.StartsWith("pbkdf2:", hash);
    }
}
