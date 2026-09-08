using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace EducationPlatform.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedIdentityRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                name: "IX_AspNetUsers_StudentCode",
                table: "AspNetUsers",
                newName: "UX_AspNetUsers_StudentCode");

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { new Guid("8f044a1d-46e9-4b06-a810-779b50844d42"), "8f044a1d-46e9-4b06-a810-779b50844d42", "Teacher", "TEACHER" },
                    { new Guid("e1011df9-3755-49fb-88b0-bcc38850bccc"), "e1011df9-3755-49fb-88b0-bcc38850bccc", "Student", "STUDENT" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("8f044a1d-46e9-4b06-a810-779b50844d42"));

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("e1011df9-3755-49fb-88b0-bcc38850bccc"));

            migrationBuilder.RenameIndex(
                name: "UX_AspNetUsers_StudentCode",
                table: "AspNetUsers",
                newName: "IX_AspNetUsers_StudentCode");
        }
    }
}
