using EducationPlatform.Api.Persistence;
using EducationPlatform.Domain.Courses;
using Microsoft.EntityFrameworkCore;

namespace EducationPlatform.Api.Features.Learning;

internal static class LearningAccess
{
    public static IQueryable<Course> AccessibleCourses(EducationPlatformDbContext db, Guid studentId) =>
        db.Courses.Where(course => course.Status == CourseStatus.Published
            && db.CourseClassroomAssignments.Any(assignment => assignment.CourseId == course.Id && assignment.RemovedAt == null
                && db.ClassroomMemberships.Any(membership => membership.ClassroomId == assignment.ClassroomId
                    && membership.StudentId == studentId && membership.LeftAt == null)));

    public static IQueryable<WeekContent> AccessibleContents(EducationPlatformDbContext db, Guid studentId) =>
        db.WeekContents.Where(content => content.IsActive
            && db.CourseWeeks.Any(week => week.Id == EF.Property<Guid>(content, "CourseWeekId")
                && AccessibleCourses(db, studentId).Any(course => course.Id == EF.Property<Guid>(week, "CourseId"))));

    public static async Task DeleteIncompleteAttemptsWithoutCourseAccess(
        EducationPlatformDbContext db,
        Guid studentId,
        IReadOnlyCollection<Guid> courseIds,
        CancellationToken cancellationToken)
    {
        foreach (var courseId in courseIds.Distinct())
        {
            if (await AccessibleCourses(db, studentId).AnyAsync(course => course.Id == courseId, cancellationToken))
                continue;

            var quizIds = db.WeekContents
                .Where(content => content.QuizId != null
                    && db.CourseWeeks.Any(week => week.Id == EF.Property<Guid>(content, "CourseWeekId")
                        && EF.Property<Guid>(week, "CourseId") == courseId))
                .Select(content => content.QuizId!.Value);

            await db.QuizAttempts
                .Where(attempt => attempt.StudentId == studentId
                    && attempt.CompletedAt == null
                    && quizIds.Contains(attempt.QuizId))
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
