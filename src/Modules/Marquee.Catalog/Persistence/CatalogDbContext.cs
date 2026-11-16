using Marquee.Catalog.Domain;
using Marquee.Common.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Marquee.Catalog.Persistence;

internal sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";

    public DbSet<Movie> Movies => Set<Movie>();

    public DbSet<Hall> Halls => Set<Hall>();

    public DbSet<Showtime> Showtimes => Set<Showtime>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema).AddOutbox();

        modelBuilder.Entity<Movie>(movie =>
        {
            movie.Property(m => m.Id).ValueGeneratedNever();
            movie.Property(m => m.Title).HasMaxLength(200);
            movie.Property(m => m.Tagline).HasMaxLength(300);
            movie.Property(m => m.Genre).HasMaxLength(50);
            movie.Property(m => m.Director).HasMaxLength(100);
            movie.Property(m => m.AgeRating).HasMaxLength(10);
        });

        modelBuilder.Entity<Hall>(hall =>
        {
            hall.Property(h => h.Id).ValueGeneratedNever();
            hall.Property(h => h.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<Showtime>(showtime =>
        {
            showtime.Property(s => s.Id).ValueGeneratedNever();
            showtime.Property(s => s.BasePrice).HasPrecision(10, 2);
            showtime.HasOne(s => s.Movie).WithMany().HasForeignKey(s => s.MovieId);
            showtime.HasOne(s => s.Hall).WithMany().HasForeignKey(s => s.HallId);
            showtime.HasIndex(s => new { s.MovieId, s.StartsAt });
            // A hall cannot show two films at once.
            showtime.HasIndex(s => new { s.HallId, s.StartsAt }).IsUnique();
        });
    }
}
