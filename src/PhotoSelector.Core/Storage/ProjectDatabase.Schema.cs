using LinqToDB;
using LinqToDB.Data;

namespace PhotoSelector.Core.Storage;

public sealed partial class ProjectDatabase
{
    public void Migrate()
    {
        NormalizeSchemaVersionTable();

        database.CreateTable<SchemaVersionRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<ProjectRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<PhotoRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<RatingRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<RatingJobRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<RatingAuditLogRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<ArenaRunRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<ArenaRatingRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<UserMarkRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<GroupReviewRow>(tableOptions: TableOptions.CreateIfNotExists);
        database.CreateTable<GroupReviewItemRow>(tableOptions: TableOptions.CreateIfNotExists);
        EnsureColumn("group_reviews", "request_json_redacted", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("group_reviews", "raw_message_content", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("group_reviews", "raw_response_json", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("group_reviews", "http_status", "INTEGER NULL");
        EnsureColumn("group_reviews", "error", "TEXT NULL");

        if (!SchemaVersions.Any(row => row.Id == 1))
        {
            database.Insert(new SchemaVersionRow { Id = 1, Version = 1 });
        }
    }

    private void NormalizeSchemaVersionTable()
    {
        if (!TableExists("schema_version") || TableHasColumn("schema_version", "id"))
        {
            return;
        }

        database.DropTable<SchemaVersionRow>(throwExceptionIfNotExists: false);
        database.CreateTable<SchemaVersionRow>(tableOptions: TableOptions.CreateIfNotExists);
    }

    private bool TableExists(string tableName)
    {
        var schema = database.DataProvider.GetSchemaProvider().GetSchema(database);
        return schema.Tables.Any(table => table.TableName == tableName);
    }

    private bool TableHasColumn(string tableName, string columnName)
    {
        var schema = database.DataProvider.GetSchemaProvider().GetSchema(database);
        var table = schema.Tables.FirstOrDefault(item => item.TableName == tableName);
        return table?.Columns.Any(column => column.ColumnName == columnName) == true;
    }

    private void EnsureColumn(string tableName, string columnName, string definition)
    {
        if (TableHasColumn(tableName, columnName))
        {
            return;
        }

        database.Execute($"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}");
    }
}
