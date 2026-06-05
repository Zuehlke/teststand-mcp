using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

[TestFixture]
[Category("SearchDirectories")]
public class T16_SearchDirectoryTests : TestBase
{
    [Test]
    public async Task GetSearchDirectories_ReturnsList()
    {
        var dirs = await Ts.GetSearchDirectoriesAsync();
        Assert.That(dirs, Is.Not.Null);
        TestContext.WriteLine($"Search directories: {dirs.Count}");
    }

    [Test]
    public async Task AddThenRemoveSearchDirectory_RoundTrips()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"MCP_SD_{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            await Ts.AddSearchDirectoryAsync(temp, index: -1, searchSubdirectories: true);

            var afterAdd = await Ts.GetSearchDirectoriesAsync();
            Assert.That(afterAdd.Any(d => string.Equals(d.Path, temp,
                    StringComparison.OrdinalIgnoreCase)), Is.True,
                "Added directory should appear in the list");

            await Ts.RemoveSearchDirectoryAsync(temp);

            var afterRemove = await Ts.GetSearchDirectoriesAsync();
            Assert.That(afterRemove.Any(d => string.Equals(d.Path, temp,
                    StringComparison.OrdinalIgnoreCase)), Is.False,
                "Removed directory should be gone");
        }
        finally
        {
            try { await Ts.RemoveSearchDirectoryAsync(temp); } catch { }
            try { Directory.Delete(temp); } catch { }
        }
    }
}
