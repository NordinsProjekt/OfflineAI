using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Data.EntityFramework.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MemoryFragments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()"),
                    CollectionName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Content = table.Column<string>(type: "NVARCHAR(MAX)", nullable: false),
                    Embedding = table.Column<byte[]>(type: "VARBINARY(MAX)", nullable: true),
                    EmbeddingDimension = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    SourceFile = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ChunkIndex = table.Column<int>(type: "int", nullable: true),
                    ContentLength = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryFragments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryFragments_Category",
                table: "MemoryFragments",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_MemoryFragments_CollectionName",
                table: "MemoryFragments",
                column: "CollectionName");

            migrationBuilder.CreateIndex(
                name: "IX_MemoryFragments_ContentLength",
                table: "MemoryFragments",
                column: "ContentLength");

            migrationBuilder.CreateIndex(
                name: "IX_MemoryFragments_CreatedAt",
                table: "MemoryFragments",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemoryFragments");
        }
    }
}
