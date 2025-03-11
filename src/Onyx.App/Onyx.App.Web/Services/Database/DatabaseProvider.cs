namespace Onyx.App.Web.Services.Database;

public record DatabaseProvider(string Name, string Assembly)
{
    public static DatabaseProvider SQLite = new(nameof(SQLite), typeof(Onyx.Data.SQLite.Marker).Assembly.GetName().Name!);

    public static DatabaseProvider SqlServer =
        new(nameof(SqlServer), typeof(Onyx.Data.SqlServer.Marker).Assembly.GetName().Name!);
}