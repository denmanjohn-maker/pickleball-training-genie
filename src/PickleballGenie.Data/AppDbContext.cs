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
    }
}
