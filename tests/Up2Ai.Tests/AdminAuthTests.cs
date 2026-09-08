using Xunit;
using Up2Ai.Services;

namespace Up2Ai.Tests;

public class AdminAuthTests
{
    [Fact]
    public void HashPassword_Creates_Valid_Hash()
    {
        // Arrange
        var password = "TestPassword123!";

        // Act
        var hash = AdminAuth.HashPassword(password);

        // Assert
        Assert.NotNull(hash);
        Assert.StartsWith("pbkdf2:", hash);
        Assert.Contains(":", hash.Substring("pbkdf2:".Length)); // salt
        Assert.True(hash.Split(':').Length == 4, "Hash format should be pbkdf2:iterations:salt:hash");
    }

    [Fact]
    public void VerifyPassword_Accepts_Correct_Password()
    {
        // Arrange
        var password = "TestPassword123!";
        var hash = AdminAuth.HashPassword(password);

        // Act
        var result = AdminAuth.VerifyPassword(password, hash);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_Rejects_Wrong_Password()
    {
        // Arrange
        var password = "TestPassword123!";
        var hash = AdminAuth.HashPassword(password);

        // Act
        var result = AdminAuth.VerifyPassword("WrongPassword456!", hash);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_Uses_FixedTime_Comparison()
    {
        // Arrange
        var password = "TestPassword123!";
        var hash = AdminAuth.HashPassword(password);
        var wrongPassword = "WrongPassword456!";

        // Act - verify multiple times to ensure consistent timing
        var result1 = AdminAuth.VerifyPassword(wrongPassword, hash);
        var result2 = AdminAuth.VerifyPassword(wrongPassword, hash);
        var result3 = AdminAuth.VerifyPassword(wrongPassword, hash);

        // Assert - all should be false (timing attack resistance not directly testable,
        // but we can verify consistent rejection)
        Assert.False(result1);
        Assert.False(result2);
        Assert.False(result3);
    }

    [Fact]
    public void HashPassword_Never_Returns_Same_Hash_For_Same_Password()
    {
        // Arrange
        var password = "TestPassword123!";

        // Act
        var hash1 = AdminAuth.HashPassword(password);
        var hash2 = AdminAuth.HashPassword(password);

        // Assert - different salt should produce different hashes
        Assert.NotEqual(hash1, hash2);
        
        // But both should verify
        Assert.True(AdminAuth.VerifyPassword(password, hash1));
        Assert.True(AdminAuth.VerifyPassword(password, hash2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("invalid:hash")]
    [InlineData("invalid:1:2:3:4")]
    public void VerifyPassword_Rejects_Invalid_Hash_Format(string invalidHash)
    {
        // Act
        var result = AdminAuth.VerifyPassword("anypassword", invalidHash);

        // Assert
        Assert.False(result);
    }
}
