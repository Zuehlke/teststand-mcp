using System.Linq;
using NUnit.Framework;

namespace TestStandMCP.UnitTests;

/// <summary>
/// The pull-request check runs only <c>Category=EngineFree</c>, and a fixture without it silently never
/// runs there (18 install-locator tests did not). Every fixture in this namespace or a sub-namespace must carry it.
/// </summary>
[TestFixture]
[Category("EngineFree")]
public class UnitFixtureCategoryTests
{
    [Test]
    public void EveryUnitFixture_CarriesTheCategoryThePrCheckRuns()
    {
        var fixtures = typeof(UnitFixtureCategoryTests).Assembly.GetTypes()
            .Where(t => (t.Namespace == "TestStandMCP.UnitTests" || t.Namespace?.StartsWith("TestStandMCP.UnitTests.") == true)
                        && t.GetMethods().Any(m => m.GetCustomAttributes(false).Any(a => a is NUnit.Framework.Interfaces.ITestBuilder or NUnit.Framework.Interfaces.ISimpleTestBuilder)))
            .ToList();
        // [Test] is an ISimpleTestBuilder and [TestCase] an ITestBuilder: the guard must see both kinds.
        Assert.That(fixtures, Does.Contain(typeof(ServerInfoVersionTests)).And.Contain(typeof(T34_InstallLocatorTests)), "fixtures the guard detects");
        var missing = fixtures
            .Where(t => !t.GetCustomAttributes(typeof(CategoryAttribute), true).Cast<CategoryAttribute>().Any(c => c.Name == "EngineFree"))
            .Select(t => t.Name)
            .ToList();
        Assert.That(missing, Is.Empty, "fixtures the PR check would skip");
    }
}
