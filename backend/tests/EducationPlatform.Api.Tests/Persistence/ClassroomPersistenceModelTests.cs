using EducationPlatform.Api.Persistence;
using EducationPlatform.Api.Persistence.Classrooms;
using EducationPlatform.Domain.Classrooms;
using Microsoft.EntityFrameworkCore;
using EducationPlatform.Api.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EducationPlatform.Api.Tests.Persistence;

public sealed class ClassroomPersistenceModelTests
{
    [Fact]
    public void Model_MapsClassroomAndActiveMembershipUniqueness()
    {
        var options = new DbContextOptionsBuilder<EducationPlatformDbContext>()
            .UseNpgsql("Host=localhost;Database=model_test;Username=test;Password=test")
            .Options;
        using var dbContext = new EducationPlatformDbContext(options);

        var classroom = dbContext.Model.FindEntityType(typeof(Classroom));
        var membership = dbContext.Model.FindEntityType(typeof(ClassroomMembership));

        Assert.NotNull(classroom);
        Assert.Equal("Classrooms", classroom.GetTableName());
        Assert.NotNull(membership);
        Assert.Equal("ClassroomMemberships", membership.GetTableName());
        var activeIndex = Assert.Single(
            membership.GetIndexes(),
            index => index.GetDatabaseName() == ClassroomMembership.ActiveMembershipIndexName);
        Assert.True(activeIndex.IsUnique);
        Assert.Equal("\"LeftAt\" IS NULL", activeIndex.GetFilter());

        var applicationUser = dbContext.Model.FindEntityType(typeof(ApplicationUser));
        Assert.NotNull(applicationUser);
        var studentCodeIndex = Assert.Single(
            applicationUser.GetIndexes(),
            index => index.GetDatabaseName() == ApplicationUser.StudentCodeIndexName);
        Assert.True(studentCodeIndex.IsUnique);

        var designTimeModel = dbContext.GetService<IDesignTimeModel>().Model;
        var role = designTimeModel.FindEntityType(typeof(IdentityRole<Guid>));
        Assert.NotNull(role);
        Assert.Equal(2, role.GetSeedData().Count());
    }
}
