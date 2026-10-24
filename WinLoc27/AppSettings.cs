using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WinLoc27;

sealed class AppSettings
{
    public string PasswordSalt { get; set; } = "";
    public string PasswordHash { get; set; } = "";

    public bool ComboEnabled { get; set; } = true;
    public string ComboSalt { get; set; } = "";
    public string ComboHash { get; set; } = "";
    public int ComboLength { get; set; }

    public bool QrEnabled { get; set; } = true;
    public int Port { get; set; } = 8765;

    public bool BluetoothEnabled { get; set; }
    public string? BluetoothDeviceId { get; set; }
    public string? BluetoothDeviceName { get; set; }

    public int LockDelaySeconds { get; set; } = 5;
    public bool FullScreenOverlay { get; set; } = true;
    public bool KeepAwake { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;
    /// <summary>Safety net: unlock by itself after this many minutes. 0 = never.</summary>
    public int AutoUnlockMinutes { get; set; }

    public bool HasPassword => PasswordHash.Length > 0;
    public bool HasCombo => ComboHash.Length > 0;

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinLoc27");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { /* corrupt file: start fresh */ }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    public void SetPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        PasswordSalt = Convert.ToBase64String(salt);
        PasswordHash = Convert.ToBase64String(Pbkdf2(password, salt));
    }

    public bool VerifyPassword(string password)
    {
        if (!HasPassword) return false;
        var actual = Pbkdf2(password, Convert.FromBase64String(PasswordSalt));
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(PasswordHash));
    }

    public void SetCombo(IEnumerable<int> keys)
    {
        var list = keys.Distinct().ToList();
        var salt = RandomNumberGenerator.GetBytes(16);
        ComboSalt = Convert.ToBase64String(salt);
        ComboHash = Convert.ToBase64String(HashCombo(list, salt));
        ComboLength = list.Count;
    }

    /// <summary>Cheap enough to run inside the keyboard hook (a single SHA-256).</summary>
    public bool MatchesCombo(IReadOnlyCollection<int> held)
    {
        if (!ComboEnabled || !HasCombo || held.Count != ComboLength) return false;
        var actual = HashCombo(held, Convert.FromBase64String(ComboSalt));
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(ComboHash));
    }

    static byte[] Pbkdf2(string password, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, 150_000, HashAlgorithmName.SHA256, 32);

    static byte[] HashCombo(IEnumerable<int> keys, byte[] salt)
    {
        var text = string.Join(",", keys.OrderBy(k => k));
        return SHA256.HashData(salt.Concat(Encoding.UTF8.GetBytes(text)).ToArray());
    }
}
