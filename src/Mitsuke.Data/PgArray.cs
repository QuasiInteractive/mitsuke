using System.Data;
using Dapper;
using Npgsql;

namespace Mitsuke.Data;

/// <summary>
/// Passes a .NET array as one Postgres array parameter. Without this, Dapper treats any IEnumerable
/// parameter as an IN-list and rewrites <c>@x</c> into <c>(@x1, @x2, ...)</c>, which breaks text[] columns.
/// </summary>
internal sealed class PgArray<T>(T[] values) : SqlMapper.ICustomQueryParameter
{
    public void AddParameter(IDbCommand command, string name) =>
        command.Parameters.Add(new NpgsqlParameter<T[]>(name, values));
}

internal static class PgArray
{
    public static PgArray<T> Of<T>(IEnumerable<T> values) => new(values.ToArray());
}
