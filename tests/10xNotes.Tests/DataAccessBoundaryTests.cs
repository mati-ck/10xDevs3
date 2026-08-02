using System.Reflection;
using _10xnotes.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;

namespace _10xNotes.Tests;

/// <summary>
/// Keeps the data-access boundary executable rather than merely documented.
/// </summary>
/// <remarks>
/// <c>AppDbContext</c> is registered in DI for infrastructure that must resolve it — DataProtection's
/// key store, the startup migration service, the EF health check — so nothing stops application code
/// injecting it too. A context obtained that way has <c>CurrentUserId</c> at <see cref="Guid.Empty"/>:
/// reads return no rows and writes throw. Fail-closed, but it presents as "the database is empty",
/// which is the slowest possible thing to diagnose. Application code goes through
/// <c>UserScopedDbContextFactory</c>, which applies the signed-in user before handing a context over.
/// </remarks>
public sealed class DataAccessBoundaryTests
{
    [Fact]
    public void Application_code_does_not_take_a_DbContext_directly()
    {
        // Reached through a public type: top-level statements make Program internal.
        var applicationAssembly = typeof(AuthCookie).Assembly;

        var offenders = applicationAssembly.GetTypes()
            .SelectMany(DataContextDependenciesOf)
            .OrderBy(member => member)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Application code must obtain data through UserScopedDbContextFactory, never a DbContext "
            + "directly — a directly-resolved context has no current user, so it reads nothing and "
            + "throws on write. Offending members: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// Both ways a type can end up holding a context: constructor injection for plain services, and
    /// an <see cref="InjectAttribute"/> property, which is what a component's <c>@inject</c> compiles to.
    /// </summary>
    private static IEnumerable<string> DataContextDependenciesOf(Type type)
    {
        var fromConstructors = type
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => IsDataContext(parameter.ParameterType))
            .Select(parameter => $"{type.FullName}(.ctor {parameter.Name})");

        var fromInjectedProperties = type
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(property => property.IsDefined(typeof(InjectAttribute), inherit: true))
            .Where(property => IsDataContext(property.PropertyType))
            .Select(property => $"{type.FullName}.{property.Name}");

        return fromConstructors.Concat(fromInjectedProperties);
    }

    // Deliberately DbContext and not AppDbContext: a second context type added later inherits the
    // rule without anyone remembering to widen this test.
    private static bool IsDataContext(Type type) => typeof(DbContext).IsAssignableFrom(type);
}
