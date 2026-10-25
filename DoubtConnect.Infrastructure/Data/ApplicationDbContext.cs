
using DoubtConnect.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DoubtConnect.Infrastructure.Data;

public class ApplicationDbContext
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<MentorProfile> MentorProfiles => Set<MentorProfile>();
    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();
    public DbSet<AvailabilitySlot> AvailabilitySlots => Set<AvailabilitySlot>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<SessionRecord> SessionRecords => Set<SessionRecord>();
    public DbSet<MentorReview> MentorReviews => Set<MentorReview>();

    public DbSet<WeeklyAvailabilityTemplate> WeeklyAvailabilityTemplates
        => Set<WeeklyAvailabilityTemplate>();

    public DbSet<MentorCalendarEvent> MentorCalendarEvents
        => Set<MentorCalendarEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.FullName)
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(u => u.Role)
                .HasConversion<int>();
        });

        builder.Entity<MentorProfile>(entity =>
        {
            entity.HasKey(p => p.Id);

            entity.HasIndex(p => p.UserId)
                .IsUnique();

            entity.Property(p => p.Expertise)
                .HasMaxLength(250)
                .IsRequired();

            entity.Property(p => p.Bio)
                .HasMaxLength(2000);

            entity.Property(p => p.HourlyRate)
                .HasPrecision(10, 2);

            entity.Property(p => p.Education)
                .HasMaxLength(200);

            entity.Property(p => p.Certifications)
                .HasMaxLength(2000);

            entity.Property(p => p.Achievements)
                .HasMaxLength(2000);

            entity.HasOne(p => p.User)
                .WithOne(u => u.MentorProfile)
                .HasForeignKey<MentorProfile>(p => p.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<StudentProfile>(entity =>
        {
            entity.HasKey(p => p.Id);

            entity.HasIndex(p => p.UserId)
                .IsUnique();

            entity.Property(p => p.EducationLevel)
                .HasMaxLength(100);

            entity.HasOne(p => p.User)
                .WithOne(u => u.StudentProfile)
                .HasForeignKey<StudentProfile>(p => p.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<AvailabilitySlot>(entity =>
        {
            entity.HasKey(s => s.Id);

            entity.HasIndex(s => new
            {
                s.MentorId,
                s.StartTimeUtc,
                s.EndTimeUtc
            }).IsUnique();

            entity.Property(s => s.Status)
                .HasConversion<int>();

            entity.HasOne(s => s.Mentor)
                .WithMany()
                .HasForeignKey(s => s.MentorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Booking>(entity =>
        {
            entity.HasKey(b => b.Id);

            entity.Property(b => b.Mode)
                .HasConversion<int>();

            entity.Property(b => b.Status)
                .HasConversion<int>();

            entity.Property(b => b.DoubtTitle)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(b => b.DoubtDescription)
                .HasMaxLength(5000)
                .IsRequired();

            entity.Property(b => b.MeetingLink)
                .HasMaxLength(1000);

            entity.Property(b => b.AttachmentUrl)
                .HasMaxLength(1000);

            entity.Property(b => b.RejectionReason)
                .HasMaxLength(1000);

            entity.HasIndex(b => new
            {
                b.MentorId,
                b.SlotStartUtc
            });

            entity.HasIndex(b => new
            {
                b.StudentId,
                b.SlotStartUtc
            });

            entity.HasOne(b => b.Student)
                .WithMany()
                .HasForeignKey(b => b.StudentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(b => b.Mentor)
                .WithMany()
                .HasForeignKey(b => b.MentorId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(b => b.ShiftedFromBooking)
                .WithMany(b => b.ShiftedBookings)
                .HasForeignKey(b => b.ShiftedFromBookingId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Notification>(entity =>
        {
            entity.HasKey(n => n.Id);

            entity.Property(n => n.Title)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(n => n.Message)
                .HasMaxLength(2000)
                .IsRequired();

            entity.Property(n => n.Link)
                .HasMaxLength(1000);

            entity.HasIndex(n => new
            {
                n.UserId,
                n.IsRead,
                n.CreatedAtUtc
            });

            entity.HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Message>(entity =>
        {
            entity.HasKey(m => m.Id);

            entity.Property(m => m.Content)
                .HasMaxLength(5000)
                .IsRequired();

            entity.HasIndex(m => new
            {
                m.BookingId,
                m.SentAtUtc
            });

            entity.HasOne(m => m.Booking)
                .WithMany()
                .HasForeignKey(m => m.BookingId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(m => m.Sender)
                .WithMany()
                .HasForeignKey(m => m.SenderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(t => t.Id);

            entity.Property(t => t.TokenHash)
                .HasMaxLength(128)
                .IsRequired();

            entity.HasIndex(t => t.TokenHash)
                .IsUnique();

            entity.HasIndex(t => new
            {
                t.UserId,
                t.ExpiresAtUtc
            });

            entity.HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SessionRecord>(entity =>
        {
            entity.HasKey(r => r.Id);

            entity.HasIndex(r => r.BookingId)
                .IsUnique();

            entity.Property(r => r.DoubtTitle)
                .HasMaxLength(200)
                .IsRequired();
        });

        builder.Entity<MentorReview>(entity =>
        {
            entity.HasKey(r => r.Id);

            entity.HasIndex(r => r.BookingId)
                .IsUnique();

            entity.Property(r => r.Rating)
                .IsRequired();

            entity.Property(r => r.Comment)
                .HasMaxLength(500);

            entity.Property(r => r.CreatedAtUtc)
                .IsRequired();

            entity.HasOne<Booking>()
                .WithMany()
                .HasForeignKey(r => r.BookingId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(r => r.StudentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(r => r.MentorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<WeeklyAvailabilityTemplate>(entity =>
        {
            entity.HasKey(t => t.Id);

            entity.Property(t => t.DayOfWeek)
                .HasConversion<int>();

            entity.HasIndex(t => new
            {
                t.MentorId,
                t.DayOfWeek,
                t.StartTime,
                t.EndTime
            }).IsUnique();

            entity.HasOne(t => t.Mentor)
                .WithMany()
                .HasForeignKey(t => t.MentorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<MentorCalendarEvent>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ActivityType)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.StudentName)
                .HasMaxLength(150);

            entity.HasIndex(e => new
            {
                e.MentorId,
                e.EventDate,
                e.StartTime
            });

            entity.HasOne(e => e.Mentor)
                .WithMany()
                .HasForeignKey(e => e.MentorId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
