using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PickleballGenie.Models;

namespace PickleballGenie.Data;

public class AppDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
{
    public DbSet<Drill> Drills { get; set; }
    public DbSet<UserDrillProgress> UserDrillProgresses { get; set; }
    public DbSet<UserAvatarImage> UserAvatarImages { get; set; }
    public DbSet<WorkoutSession> WorkoutSessions { get; set; }
    public DbSet<WorkoutSessionDrill> WorkoutSessionDrills { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<UserDrillProgress>()
            .HasOne(udp => udp.User)
            .WithMany(u => u.DrillProgresses)
            .HasForeignKey(udp => udp.UserId);

        builder.Entity<UserDrillProgress>()
            .HasOne(udp => udp.Drill)
            .WithMany(d => d.UserProgresses)
            .HasForeignKey(udp => udp.DrillId);

        builder.Entity<UserAvatarImage>(entity =>
        {
            entity.HasKey(a => a.UserId);
            entity.HasOne(a => a.User)
                .WithOne()
                .HasForeignKey<UserAvatarImage>(a => a.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WorkoutSession>(entity =>
        {
            entity.HasOne(s => s.User)
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(s => new { s.UserId, s.CompletedAt });
        });

        builder.Entity<WorkoutSessionDrill>(entity =>
        {
            entity.HasOne(d => d.WorkoutSession)
                .WithMany(s => s.Drills)
                .HasForeignKey(d => d.WorkoutSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
