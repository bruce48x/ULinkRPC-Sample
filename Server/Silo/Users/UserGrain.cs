using Orleans;
using Orleans.Contracts.Users;
using Orleans.Runtime;
using System.Security.Cryptography;
using System.Text;

namespace ULinkRPC.Sample.Silo.Users;

[GenerateSerializer]
public sealed class UserState
{
    [Id(0)]
    public string UserId { get; set; } = "";

    [Id(1)]
    public string PasswordHash { get; set; } = "";

    [Id(2)]
    public string SessionToken { get; set; } = "";

    [Id(3)]
    public int LoginCount { get; set; }

    [Id(4)]
    public DateTime CreatedAtUtc { get; set; }

    [Id(5)]
    public DateTime LastLoginAtUtc { get; set; }

    [Id(6)]
    public bool IsOnline { get; set; }

    [Id(7)]
    public float Score { get; set; }

    [Id(8)]
    public int WinCount { get; set; }
}

public sealed class UserGrain : Grain, IUserGrain
{
    private readonly IPersistentState<UserState> _state;

    public UserGrain([PersistentState("user", "users")] IPersistentState<UserState> state)
    {
        _state = state;
    }

    public async Task<UserLoginResult> LoginAsync(string password)
    {
        return await LoginAsync(password, reconnect: false).ConfigureAwait(false);
    }

    public async Task<UserLoginResult> LoginAsync(string password, bool reconnect)
    {
        var userId = this.GetPrimaryKeyString();
        var passwordHash = ComputePasswordHash(password);
        var now = DateTime.UtcNow;

        if (!_state.RecordExists)
        {
            _state.State = new UserState
            {
                UserId = userId,
                PasswordHash = passwordHash,
                CreatedAtUtc = now,
                Score = 0f
            };
        }
        else if (!string.Equals(_state.State.PasswordHash, passwordHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid password.");
        }

        if (!reconnect || string.IsNullOrWhiteSpace(_state.State.SessionToken))
        {
            _state.State.SessionToken = Guid.NewGuid().ToString("N");
        }

        _state.State.LoginCount += 1;
        _state.State.LastLoginAtUtc = now;
        _state.State.IsOnline = true;
        _state.State.Score = NormalizeScore(_state.State.Score);
        await _state.WriteStateAsync();

        return new UserLoginResult
        {
            UserId = _state.State.UserId,
            SessionToken = _state.State.SessionToken,
            LoginCount = _state.State.LoginCount,
            LastLoginAtUtc = _state.State.LastLoginAtUtc,
            Score = NormalizeScore(_state.State.Score),
            WinCount = Math.Max(0, _state.State.WinCount)
        };
    }

    public Task<UserProfileSnapshot> GetProfileAsync()
    {
        var snapshot = new UserProfileSnapshot
        {
            UserId = _state.State.UserId,
            LoginCount = _state.State.LoginCount,
            CreatedAtUtc = _state.State.CreatedAtUtc,
            LastLoginAtUtc = _state.State.LastLoginAtUtc,
            IsOnline = _state.State.IsOnline,
            Score = NormalizeScore(_state.State.Score),
            WinCount = Math.Max(0, _state.State.WinCount)
        };
        return Task.FromResult(snapshot);
    }

    public async Task SetOnlineAsync(bool isOnline)
    {
        if (!_state.RecordExists)
        {
            return;
        }

        _state.State.IsOnline = isOnline;
        await _state.WriteStateAsync();
    }

    public async Task SetScoreAsync(int score)
    {
        if (!_state.RecordExists)
        {
            return;
        }

        _state.State.Score = NormalizeScore(score);
        await _state.WriteStateAsync();
    }

    public async Task AddScoreAsync(int delta)
    {
        if (!_state.RecordExists)
        {
            return;
        }

        _state.State.Score = Math.Max(0f, _state.State.Score + delta);
        await _state.WriteStateAsync();
    }

    public async Task AddWinAsync()
    {
        if (!_state.RecordExists)
        {
            return;
        }

        _state.State.WinCount = Math.Max(0, _state.State.WinCount + 1);
        await _state.WriteStateAsync();
    }

    private static int NormalizeScore(float score)
    {
        return Math.Max(0, (int)Math.Round(score, MidpointRounding.AwayFromZero));
    }

    private static string ComputePasswordHash(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes);
    }
}


