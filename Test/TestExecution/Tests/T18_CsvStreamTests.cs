using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

[TestFixture]
[Category("CsvStreams")]
public class T18_CsvStreamTests : TestBase
{
    [Test]
    public async Task WriteThenReadCsvLines_RoundTrips()
    {
        var csv = Path.Combine(Path.GetTempPath(), $"MCP_CSV_{Guid.NewGuid():N}.csv");
        var lines = new List<string>
        {
            "SerialNumber,Voltage,Result",
            "SN001,3.30,Pass",
            "SN002,3.28,Pass"
        };

        try
        {
            await Ts.WriteCsvLinesAsync(csv, lines);
            Assert.That(File.Exists(csv), Is.True, "CSV file should have been created");

            var read = await Ts.ReadCsvLinesAsync(csv, 100);
            Assert.That(read, Is.Not.Null);
            Assert.That(read.LineCount, Is.GreaterThanOrEqualTo(3),
                "All written lines should be read back");
            Assert.That(read.Lines[0], Does.Contain("SerialNumber"));
            TestContext.WriteLine($"Read {read.LineCount} CSV lines");
        }
        finally
        {
            try { File.Delete(csv); } catch { }
        }
    }
}
