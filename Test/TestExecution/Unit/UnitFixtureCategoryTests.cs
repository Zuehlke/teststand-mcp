using System.Linq;
using NUnit.Framework;

namespace TestStandMCP.UnitTests;

/// <summary>
/// The pull-request check runs only <c>Category=EngineFree</c>, and a fixture without it silently never
/// runs there (18 install-locator tests did not). Every fixture in this namespace must carry it.
/// </summary>
[TestFixture]
[Category("EngineFree")]
public class UnitFixtureCategoryTests
{
    [Test]
    public void EveryUnitFixture_CarriesTheCategoryThePrCheckRuns()
    {
        var missing = typeof(UnitFixtureCategoryTests).Assembly.GetTypes()
            .Where(t => (t.Namespace == "TestStandMCP.UnitTests" || t.Namespace?.StartsWith("TestStandMCP.UnitTests.") == true)
                        && t.GetMethods().Any(m => m.GetCustomAttributes(false).OfType<NUnit.Framework.Interfaces.ITestBuilder>().Any()))
            .Where(t => !t.GetCustomAttributes(typeof(CategoryAttribute), true).Cast<CategoryAttribute>().Any(c => c.Name == "EngineFree"))
            .Select(t => t.Name)
            .ToList();
        Assert.That(missing, Is.Empty, "fixtures the PR check would skip");
    }
}
