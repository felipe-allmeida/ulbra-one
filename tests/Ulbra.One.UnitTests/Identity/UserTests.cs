using Ulbra.One.Identity.Domain;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.UnitTests.Identity;

public class UserTests
{
    private static User NewUser() => new("maria@ulbra.br", "Maria", Roles.User);

    [Fact]
    public void RegisterGoogleLogin_stores_the_profile_picture()
    {
        var user = NewUser();

        user.RegisterGoogleLogin("google-sub", "Maria Silva", "https://lh3.googleusercontent.com/a/foto", DateTimeOffset.UnixEpoch);

        Assert.Equal("https://lh3.googleusercontent.com/a/foto", user.PictureUrl);
    }

    [Fact]
    public void RegisterGoogleLogin_replaces_a_stale_picture()
    {
        var user = NewUser();
        user.RegisterGoogleLogin("google-sub", "Maria Silva", "https://lh3.googleusercontent.com/a/antiga", DateTimeOffset.UnixEpoch);

        user.RegisterGoogleLogin("google-sub", "Maria Silva", "https://lh3.googleusercontent.com/a/nova", DateTimeOffset.UnixEpoch);

        Assert.Equal("https://lh3.googleusercontent.com/a/nova", user.PictureUrl);
    }

    /// <summary>Google omits `picture` for an account with no photo — that must clear the stored one, not
    /// keep showing a picture the account no longer has.</summary>
    [Fact]
    public void RegisterGoogleLogin_clears_the_picture_when_google_sends_none()
    {
        var user = NewUser();
        user.RegisterGoogleLogin("google-sub", "Maria Silva", "https://lh3.googleusercontent.com/a/antiga", DateTimeOffset.UnixEpoch);

        user.RegisterGoogleLogin("google-sub", "Maria Silva", null, DateTimeOffset.UnixEpoch);

        Assert.Null(user.PictureUrl);
    }
}
