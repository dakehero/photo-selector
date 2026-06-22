using LinqToDB;

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
}
