using Dalan.Domain.Vehicles;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace Dalan.Infrastructure.Persistence;

public sealed class DalanDbContext(DbContextOptions<DalanDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DalanDbContext).Assembly);
    }
}
