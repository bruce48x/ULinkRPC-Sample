using Orleans;

namespace Orleans.Contracts.Users;

public interface IUserGrain : IGrainWithStringKey
{
    Task<UserLoginResult> LoginAsync(string password);
    Task<UserLoginResult> LoginAsync(string password, bool reconnect);
    Task<UserProfileSnapshot> GetProfileAsync();
    Task SetOnlineAsync(bool isOnline);
    Task SetScoreAsync(int score);
    Task AddScoreAsync(int delta);
    Task AddWinAsync();
}

[GenerateSerializer]
public sealed class UserLoginResult
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string SessionToken { get; set; } = "";

    [Id(2)]
    public int LoginCount { get; set; }

    [Id(3)]
    public DateTime LastLoginAtUtc { get; set; }

    [Id(4)]
    public int Score { get; set; }
    [Id(5)]
    public int WinCount { get; set; }
}

[GenerateSerializer]
public sealed class UserProfileSnapshot
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public int LoginCount { get; set; }

    [Id(2)]
    public DateTime CreatedAtUtc { get; set; }

    [Id(3)]
    public DateTime LastLoginAtUtc { get; set; }

    [Id(4)]
    public bool IsOnline { get; set; }

    [Id(5)]
    public int Score { get; set; }
    [Id(6)]
    public int WinCount { get; set; }
}

