using System.Reflection;
using Marquee.Audit;
using Marquee.Booking;
using Marquee.Catalog;
using Marquee.Payments;

namespace Marquee.Tests;

public sealed class ArchitectureTests
{
    private static readonly Assembly[] Modules =
    [
        typeof(AuditModule).Assembly,
        typeof(BookingModule).Assembly,
        typeof(CatalogModule).Assembly,
        typeof(PaymentsModule).Assembly,
    ];

    /// <summary>
    /// Modules talk through the contracts and events in Marquee.Common, never by reaching into
    /// each other. This is what keeps "modular monolith" from quietly becoming just "monolith".
    /// </summary>
    [Fact]
    public void Modules_do_not_reference_each_other()
    {
        var moduleNames = Modules.Select(module => module.GetName().Name).ToHashSet();

        foreach (var module in Modules)
        {
            var forbidden = module.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(moduleNames.Contains);

            Assert.Empty(forbidden);
        }
    }

    [Fact]
    public void Modules_expose_nothing_but_their_registration_and_options()
    {
        var unexpected = Modules
            .SelectMany(module => module.GetExportedTypes())
            .Where(type => !type.Name.EndsWith("Module", StringComparison.Ordinal)
                && !type.Name.EndsWith("Options", StringComparison.Ordinal)
                && !IsGeneratedMigration(type))
            .Select(type => type.FullName);

        Assert.Empty(unexpected);
    }

    // EF Core generates migrations as public classes.
    private static bool IsGeneratedMigration(Type type) =>
        type.Namespace?.EndsWith(".Migrations", StringComparison.Ordinal) == true;
}
