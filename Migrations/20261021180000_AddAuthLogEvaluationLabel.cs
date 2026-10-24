using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BehavioralAuthSystem.Migrations;

public partial class AddAuthLogEvaluationLabel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Some existing databases already have this column (for example, if it
        // was added manually). Only add it when it is missing.
        migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.AuthLogs', N'EvaluationLabel') IS NULL
BEGIN
    ALTER TABLE [dbo].[AuthLogs]
    ADD [EvaluationLabel] nvarchar(max) NULL;
END
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Intentionally do not drop this column automatically: it may have
        // existed before this migration was applied and may contain data.
    }
}
