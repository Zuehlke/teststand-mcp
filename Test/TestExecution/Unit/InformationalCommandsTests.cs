using NUnit.Framework;

// Deliberately NOT TestStandMCP.IntegrationTests: that namespace owns the [SetUpFixture] that connects
// the TestStand engine once for the whole run, and these tests must run on a machine without one.
namespace TestStandMCP.UnitTests;

/// <summary>
/// <see cref="InformationalCommands.Parse"/> — engine-free. The flags are answered before the console, the
/// configuration and the engine exist, so what each combination means is pinned here.
/// </summary>
[TestFixture]
[Category("EngineFree")]
public class InformationalCommandsTests
{
    [Test]
    public void ListTools_WithoutJson_IsTheHumanReadableList()
    {
        Assert.That(InformationalCommands.Parse(new[] { "--list-tools" }), Is.EqualTo(InformationalCommand.ListTools));
    }

    [Test]
    public void ListTools_WithJson_IsTheJsonList_WhateverTheOrder()
    {
        Assert.That(InformationalCommands.Parse(new[] { "--list-tools", "--json" }), Is.EqualTo(InformationalCommand.ListToolsJson));
        Assert.That(InformationalCommands.Parse(new[] { "--json", "--list-tools" }), Is.EqualTo(InformationalCommand.ListToolsJson));
    }

    [Test]
    public void Version_WithListTools_VersionWins()
    {
        Assert.That(InformationalCommands.Parse(new[] { "--list-tools", "--version" }), Is.EqualTo(InformationalCommand.Version));
        Assert.That(InformationalCommands.Parse(new[] { "--version", "--list-tools", "--json" }), Is.EqualTo(InformationalCommand.Version));
    }

    [Test]
    public void Version_Alone_IsVersion_AndJsonDoesNotChangeIt()
    {
        Assert.That(InformationalCommands.Parse(new[] { "--version" }), Is.EqualTo(InformationalCommand.Version));
        Assert.That(InformationalCommands.Parse(new[] { "--version", "--json" }), Is.EqualTo(InformationalCommand.Version));
    }

    [Test]
    public void Json_Alone_AsksForNothing_SoTheProcessServesMcp()
    {
        Assert.That(InformationalCommands.Parse(new[] { "--json" }), Is.EqualTo(InformationalCommand.None));
    }

    [Test]
    public void NoArguments_AskForNothing()
    {
        Assert.That(InformationalCommands.Parse(new string[0]), Is.EqualTo(InformationalCommand.None));
    }

    [TestCase("--setup-agents")]
    [TestCase("--VERSION")]
    [TestCase("--list-tools=true")]
    [TestCase("-version")]
    [TestCase("--versions")]
    public void AnythingElse_AsksForNothing(string argument)
    {
        Assert.That(InformationalCommands.Parse(new[] { argument }), Is.EqualTo(InformationalCommand.None));
    }
}
