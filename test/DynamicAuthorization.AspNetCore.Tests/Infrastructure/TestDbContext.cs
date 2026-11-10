using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DynamicAuthorization.AspNetCore.Tests.Infrastructure;

internal sealed class TestDbContext(DbContextOptions<TestDbContext> options) : IdentityDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.EnableDynamicAuthorization();

        // Lets SQLite answer prefix searches for users from the Identity indexes (see "Searching users" in the README).
        builder.Entity<IdentityUser>(user =>
        {
            user.Property(u => u.NormalizedUserName).UseCollation("NOCASE");
            user.Property(u => u.NormalizedEmail).UseCollation("NOCASE");
        });
    }
}
