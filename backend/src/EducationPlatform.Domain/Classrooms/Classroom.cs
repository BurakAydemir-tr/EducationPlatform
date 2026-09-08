namespace EducationPlatform.Domain.Classrooms;

public sealed class Classroom
{
    public Classroom(Guid id, string name, Guid teacherId)
    {
        EnsureNotEmpty(id, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        EnsureNotEmpty(teacherId, nameof(teacherId));

        Id = id;
        Name = name.Trim();
        TeacherId = teacherId;
    }

    public Guid Id { get; }

    public string Name { get; }

    public Guid TeacherId { get; }

    private static void EnsureNotEmpty(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Identifier cannot be empty.", parameterName);
        }
    }
}
