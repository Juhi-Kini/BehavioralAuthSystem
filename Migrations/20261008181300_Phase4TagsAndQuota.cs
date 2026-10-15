using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BehavioralAuthSystem.Migrations;

public partial class Phase4TagsAndQuota : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "StorageQuotaBytes", table: "Users", type: "bigint", nullable: false, defaultValue: 524288000L);
        migrationBuilder.AddColumn<string>(name: "Tags", table: "Files", type: "nvarchar(max)", nullable: false, defaultValue: "");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "StorageQuotaBytes", table: "Users");
        migrationBuilder.DropColumn(name: "Tags", table: "Files");
    }
}
