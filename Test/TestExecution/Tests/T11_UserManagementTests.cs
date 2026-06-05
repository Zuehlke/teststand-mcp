using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace TestStandMCP.IntegrationTests.Tests;

[TestFixture]
[Category("UserManagement")]
public class T11_UserManagementTests : TestBase
{
    // A unique login name per run so we never collide with real station users.
    private static string NewLogin() => $"mcp_test_{Guid.NewGuid():N}".Substring(0, 20);

    [Test]
    public async Task GetUsers_ReturnsList()
    {
        var users = await Ts.GetUsersAsync();
        Assert.That(users, Is.Not.Null);
        TestContext.WriteLine($"Users defined: {users.Count}");
    }

    [Test]
    public async Task GetCurrentUser_DoesNotThrow()
    {
        Assert.DoesNotThrowAsync(async () =>
        {
            var user = await Ts.GetCurrentUserAsync();
            TestContext.WriteLine($"Current user: {user?.LoginName ?? "(none)"}");
        });
    }

    [Test]
    public async Task CreateUser_ThenExists_ThenDelete_InMemory()
    {
        var login = NewLogin();

        // persist:false → modify only the in-memory users file, never touch users.ini on disk.
        Assert.That(await Ts.UserNameExistsAsync(login), Is.False,
            "Freshly generated login should not exist yet");

        await Ts.CreateUserAsync(login, "MCP Test User", "secret", persist: false);
        try
        {
            Assert.That(await Ts.UserNameExistsAsync(login), Is.True,
                "User should exist after creation");

            var users = await Ts.GetUsersAsync();
            Assert.That(users.Any(u => string.Equals(u.LoginName, login,
                    StringComparison.OrdinalIgnoreCase)), Is.True,
                "Created user should appear in the user list");
        }
        finally
        {
            await Ts.DeleteUserAsync(login, persist: false);
        }

        Assert.That(await Ts.UserNameExistsAsync(login), Is.False,
            "User should be gone after deletion");
    }

    [Test]
    public async Task SetUserPassword_DoesNotThrow()
    {
        var login = NewLogin();
        await Ts.CreateUserAsync(login, "Pwd User", "old", persist: false);
        try
        {
            Assert.DoesNotThrowAsync(() =>
                Ts.SetUserPasswordAsync(login, "newsecret", persist: false));
        }
        finally
        {
            await Ts.DeleteUserAsync(login, persist: false);
        }
    }

    [Test]
    public async Task GetUserPrivileges_ReturnsList()
    {
        var login = NewLogin();
        await Ts.CreateUserAsync(login, "Priv User", "", persist: false);
        try
        {
            var privileges = await Ts.GetUserPrivilegesAsync(login);
            Assert.That(privileges, Is.Not.Null);
            TestContext.WriteLine($"Enabled privileges: {privileges.Count}");
        }
        finally
        {
            await Ts.DeleteUserAsync(login, persist: false);
        }
    }

    [Test]
    public async Task CheckUserPrivilege_DoesNotThrow()
    {
        var login = NewLogin();
        await Ts.CreateUserAsync(login, "Check User", "", persist: false);
        try
        {
            Assert.DoesNotThrowAsync(async () =>
            {
                var has = await Ts.CheckUserPrivilegeAsync(login, "OperatorInterface.Run");
                TestContext.WriteLine($"Has OperatorInterface.Run: {has}");
            });
        }
        finally
        {
            await Ts.DeleteUserAsync(login, persist: false);
        }
    }
}
