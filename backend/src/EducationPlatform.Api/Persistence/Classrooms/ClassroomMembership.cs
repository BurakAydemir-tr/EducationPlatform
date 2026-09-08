namespace EducationPlatform.Api.Persistence.Classrooms;

public sealed class ClassroomMembership
{
    public const string ActiveMembershipIndexName = "UX_ClassroomMemberships_ClassroomId_StudentId_Active";

    public Guid Id { get; init; }

    public Guid ClassroomId { get; init; }

    public Guid StudentId { get; init; }

    public DateTimeOffset JoinedAt { get; init; }

    public DateTimeOffset? LeftAt { get; set; }
}
