using System.Security.Cryptography;
using System.Text;

var password = args.Length > 0 ? args[0] : "admin123";

var salt = RandomNumberGenerator.GetBytes(16);
var key = Rfc2898DeriveBytes.Pbkdf2(
    Encoding.UTF8.GetBytes(password), 
    salt, 
    210_000, 
    HashAlgorithmName.SHA256, 
    32);

var hash = $"pbkdf2:210000:{Convert.ToHexString(salt).ToLowerInvariant()}:{Convert.ToHexString(key).ToLowerInvariant()}";

Console.WriteLine($"Password: {password}");
Console.WriteLine($"Hash: {hash}");
Console.WriteLine($"\nSet this environment variable:");
Console.WriteLine($"Admin__InitialPasswordHash={hash}");
