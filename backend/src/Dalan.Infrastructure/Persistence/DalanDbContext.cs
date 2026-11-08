using Dalan.Domain.Vehicles;
using Dalan.Domain.Drivers;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Dalan.Domain.Deliveries;

namespace Dalan.Infrastructure.Persistence;

public sealed class DalanDbContext(DbContextOptions<DalanDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DeliveryProof> DeliveryProofs => Set<DeliveryProof>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DalanDbContext).Assembly);
    }
}
