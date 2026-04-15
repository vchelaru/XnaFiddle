using Microsoft.EntityFrameworkCore;
using XnaFiddle.Api.Entities;

namespace XnaFiddle.Api.Data;

public class FiddleDbContext : DbContext
{
    public FiddleDbContext(DbContextOptions<FiddleDbContext> options) : base(options)
    {
    }

    public DbSet<Fiddle> Fiddles => Set<Fiddle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Fiddle>()
            .HasIndex(f => f.Slug)
            .IsUnique();
    }
}
