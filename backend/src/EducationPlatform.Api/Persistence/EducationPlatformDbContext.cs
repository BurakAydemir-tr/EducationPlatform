using EducationPlatform.Api.Persistence.Classrooms;
using EducationPlatform.Api.Persistence.Courses;
using EducationPlatform.Api.Persistence.Identity;
using EducationPlatform.Domain.Classrooms;
using EducationPlatform.Domain.Courses;
using EducationPlatform.Domain.Progress;
using EducationPlatform.Domain.Quizzes;
using EducationPlatform.Domain.QuizAttempts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EducationPlatform.Api.Persistence;

public sealed class EducationPlatformDbContext(
    DbContextOptions<EducationPlatformDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Classroom> Classrooms => Set<Classroom>();

    public DbSet<ClassroomMembership> ClassroomMemberships => Set<ClassroomMembership>();

    public DbSet<Course> Courses => Set<Course>();

    public DbSet<CourseWeek> CourseWeeks => Set<CourseWeek>();

    public DbSet<WeekContent> WeekContents => Set<WeekContent>();

    public DbSet<CourseClassroomAssignment> CourseClassroomAssignments => Set<CourseClassroomAssignment>();

    public DbSet<Quiz> Quizzes => Set<Quiz>();

    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();

    public DbSet<QuizAttemptAnswer> QuizAttemptAnswers => Set<QuizAttemptAnswer>();

    public DbSet<ContentProgress> ContentProgress => Set<ContentProgress>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(EducationPlatformDbContext).Assembly);
    }
}
