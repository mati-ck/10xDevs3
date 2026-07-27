using Microsoft.EntityFrameworkCore;

namespace _10xnotes.Data;

/// <summary>
/// The application's single EF Core entry point.
/// Entity sets and the owner-scoping contract arrive in phase 2 of the persistence baseline.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
