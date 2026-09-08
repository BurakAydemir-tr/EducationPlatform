namespace EducationPlatform.Api.Persistence.Courses;

public sealed class CourseClassroomAssignment
{
    public const string ActiveAssignmentIndexName = "IX_CourseClassroomAssignments_Active";

    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public Guid ClassroomId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
}
