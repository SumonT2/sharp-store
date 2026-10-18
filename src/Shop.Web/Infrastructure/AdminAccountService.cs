using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shop.Core.Entities;
using Shop.Infrastructure;
using Shop.Infrastructure.Data;

namespace Shop.Web.Infrastructure;

public class AdminAccountService(ShopDbContext db, IConfiguration config, ShopClock clock, ILogger<AdminAccountService> logger)
{
    private static readonly PasswordHasher<AdminUser> Hasher = new();

    /// <summary>Creates the first admin from configuration (Admin:Email / Admin:Password) when none exists.</summary>
    public async Task EnsureInitialAdminAsync()
    {
        if (await db.AdminUsers.AnyAsync())
            return;

        var email = config["Admin:Email"];
        var password = config["Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("No admin user exists. Set Admin:Email and Admin:Password to create one.");
            return;
        }

        var user = new AdminUser { Email = email.Trim().ToLowerInvariant(), CreatedAt = clock.UtcNow };
        user.PasswordHash = Hasher.HashPassword(user, password);
        db.AdminUsers.Add(user);
        await db.SaveChangesAsync();
        logger.LogInformation("Created admin user {Email}. Change the password after first login.", user.Email);
    }

    public async Task<AdminUser?> VerifyAsync(string email, string password)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = await db.AdminUsers.FirstOrDefaultAsync(u => u.Email == normalized);
        if (user is null)
        {
            // Hash anyway so response time doesn't reveal whether the email exists.
            Hasher.HashPassword(new AdminUser(), password);
            return null;
        }

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = Hasher.HashPassword(user, password);
            await db.SaveChangesAsync();
        }
        return user;
    }

    public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
    {
        var user = await db.AdminUsers.FindAsync(userId);
        if (user is null || Hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            return false;
        user.PasswordHash = Hasher.HashPassword(user, newPassword);
        await db.SaveChangesAsync();
        return true;
    }
}
