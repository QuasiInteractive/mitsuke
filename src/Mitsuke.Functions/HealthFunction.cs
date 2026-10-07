using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Npgsql;

namespace Mitsuke.Functions;

/// <summary>GET /api/health for uptime checks: is the app up and can it reach the database? Reveals nothing else.</summary>
public sealed class HealthFunction(NpgsqlDataSource db)
{
    [Function("Health")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = await db.OpenConnectionAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand("select 1", conn);
            await cmd.ExecuteScalarAsync(cancellationToken);
            return new OkObjectResult(new { status = "healthy" });
        }
        catch (NpgsqlException)
        {
            return new ObjectResult(new { status = "unhealthy", database = "unreachable" }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
        }
    }
}
