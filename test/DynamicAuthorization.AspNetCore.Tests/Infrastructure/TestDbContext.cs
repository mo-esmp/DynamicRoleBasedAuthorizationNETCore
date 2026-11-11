using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DynamicAuthorization.AspNetCore.Tests.Infrastructure;

internal sealed class TestDbContext(DbContextOptions<TestDbContext> options) : IdentityDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.EnableDynamicAuthorization();
    }
}
