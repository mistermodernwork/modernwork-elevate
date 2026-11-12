namespace ModernWorkElevate.Tests.Auth;

using ModernWorkElevate.Core.Auth;

public sealed class InteractiveAuthScopeTests
{
    [Fact]
    public void Default_IsNotAllowed()
    {
        Assert.False(InteractiveAuthScope.IsAllowed);
    }

    [Fact]
    public void Allow_IsUndoneOnDispose()
    {
        using (InteractiveAuthScope.Allow())
        {
            Assert.True(InteractiveAuthScope.IsAllowed);
            using (InteractiveAuthScope.Suppress())
            {
                Assert.False(InteractiveAuthScope.IsAllowed);
            }

            Assert.True(InteractiveAuthScope.IsAllowed);
        }

        Assert.False(InteractiveAuthScope.IsAllowed);
    }

    [Fact]
    public async Task Allow_FlowsIntoAwaitedCallsButNotBackToTheCaller()
    {
        // The refresh timer must never inherit the permission an activation had.
        Assert.True(await RunAllowedAsync());
        Assert.False(InteractiveAuthScope.IsAllowed);
    }

    private static async Task<bool> RunAllowedAsync()
    {
        using var prompts = InteractiveAuthScope.Allow();
        await Task.Yield();
        return InteractiveAuthScope.IsAllowed;
    }
}
