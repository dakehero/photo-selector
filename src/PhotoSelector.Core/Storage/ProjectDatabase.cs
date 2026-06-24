using LinqToDB;
using LinqToDB.Data;
using LinqToDB.DataProvider.SQLite;
using Microsoft.Data.Sqlite;

namespace PhotoSelector.Core.Storage;

public sealed partial class ProjectDatabase : IDisposable
{
    private readonly DataConnection database;

    private ProjectDatabase(DataConnection database)
    {
        this.database = database;
    }

    private ITable<SchemaVersionRow> SchemaVersions => database.GetTable<SchemaVersionRow>();

    private ITable<ProjectRow> Projects => database.GetTable<ProjectRow>();

    private ITable<PhotoRow> Photos => database.GetTable<PhotoRow>();

    private ITable<RatingRow> Ratings => database.GetTable<RatingRow>();

    private ITable<RatingJobRow> RatingJobs => database.GetTable<RatingJobRow>();

    private ITable<RatingAuditLogRow> RatingAuditLogs => database.GetTable<RatingAuditLogRow>();

    private ITable<ArenaRunRow> ArenaRuns => database.GetTable<ArenaRunRow>();

    private ITable<ArenaRatingRow> ArenaRatings => database.GetTable<ArenaRatingRow>();

    private ITable<UserMarkRow> UserMarks => database.GetTable<UserMarkRow>();

    private ITable<GroupReviewRow> GroupReviews => database.GetTable<GroupReviewRow>();

    private ITable<GroupReviewItemRow> GroupReviewItems => database.GetTable<GroupReviewItemRow>();

    public static ProjectDatabase Open(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false,
        };

        var options = new DataOptions().UseSQLite(builder.ToString(), SQLiteProvider.Microsoft);
        return new ProjectDatabase(new DataConnection(options));
    }

    public void Dispose()
    {
        database.Dispose();
    }
}
