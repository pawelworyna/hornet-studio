using System.Security.Cryptography;
using System.Text;

namespace HornetStudio.Core.Storage.Security;

public interface ISecretVault
{
    bool IsMasterPasswordConfigured();
    bool IsUnlocked();
    bool SetMasterPassword(string password);
    bool Unlock(string password);
    void Lock();
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}

public class MasterPasswordVault : ISecretVault
{
    private const int KeySize = 32; // 256 bits
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int Pbkdf2Iterations = 100_000;

    private readonly string _storageDir;
    private readonly string _vaultConfigPath;
    private readonly string _defaultKeyPath;

    private byte[]? _activeKey;
    private bool _isMasterPasswordSet;

    public MasterPasswordVault(string? customStorageDir = null)
    {
        _storageDir = customStorageDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HornetStudio");

        Directory.CreateDirectory(_storageDir);

        _vaultConfigPath = Path.Combine(_storageDir, "vault.conf");
        _defaultKeyPath = Path.Combine(_storageDir, "hornet.key");

        InitializeVault();
    }

    private void InitializeVault()
    {
        if (File.Exists(_vaultConfigPath))
        {
            _isMasterPasswordSet = true;
            _activeKey = null; // Wymaga odblokowania hasłem głównym
        }
        else
        {
            _isMasterPasswordSet = false;
            // Domyślny klucz maszyny
            if (!File.Exists(_defaultKeyPath))
            {
                var newKey = new byte[KeySize];
                RandomNumberGenerator.Fill(newKey);
                File.WriteAllBytes(_defaultKeyPath, newKey);
            }
            _activeKey = File.ReadAllBytes(_defaultKeyPath);
        }
    }

    public bool IsMasterPasswordConfigured() => _isMasterPasswordSet;

    public bool IsUnlocked() => _activeKey != null;

    public bool SetMasterPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password)) return false;

        var salt = new byte[SaltSize];
        RandomNumberGenerator.Fill(salt);

        var derivedKey = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        // Verification token: zaszyfruj znany ciąg "HORNET_VERIFY"
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var plain = Encoding.UTF8.GetBytes("HORNET_VERIFY");
        var cipher = new byte[plain.Length];

        using var aes = new AesGcm(derivedKey, TagSize);
        RandomNumberGenerator.Fill(nonce);
        aes.Encrypt(nonce, plain, cipher, tag);

        // Zapisz konfigurację: salt + nonce + tag + cipher
        var totalPayload = new byte[SaltSize + NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(salt, 0, totalPayload, 0, SaltSize);
        Buffer.BlockCopy(nonce, 0, totalPayload, SaltSize, NonceSize);
        Buffer.BlockCopy(tag, 0, totalPayload, SaltSize + NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, totalPayload, SaltSize + NonceSize + TagSize, cipher.Length);

        File.WriteAllText(_vaultConfigPath, Convert.ToBase64String(totalPayload));

        _isMasterPasswordSet = true;
        _activeKey = derivedKey;
        return true;
    }

    public bool Unlock(string password)
    {
        if (!_isMasterPasswordSet || !File.Exists(_vaultConfigPath))
            return true;

        try
        {
            var raw = Convert.FromBase64String(File.ReadAllText(_vaultConfigPath).Trim());
            var salt = new byte[SaltSize];
            var nonce = new byte[NonceSize];
            var tag = new byte[TagSize];
            var cipherLen = raw.Length - (SaltSize + NonceSize + TagSize);
            var cipher = new byte[cipherLen];

            Buffer.BlockCopy(raw, 0, salt, 0, SaltSize);
            Buffer.BlockCopy(raw, SaltSize, nonce, 0, NonceSize);
            Buffer.BlockCopy(raw, SaltSize + NonceSize, tag, 0, TagSize);
            Buffer.BlockCopy(raw, SaltSize + NonceSize + TagSize, cipher, 0, cipherLen);

            var derivedKey = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Pbkdf2Iterations,
                HashAlgorithmName.SHA256,
                KeySize);

            using var aes = new AesGcm(derivedKey, TagSize);
            var decrypted = new byte[cipher.Length];
            aes.Decrypt(nonce, cipher, tag, decrypted);

            if (Encoding.UTF8.GetString(decrypted) == "HORNET_VERIFY")
            {
                _activeKey = derivedKey;
                return true;
            }
        }
        catch
        {
            // Błędne hasło lub uszkodzony plik
        }

        return false;
    }

    public void Lock()
    {
        if (_isMasterPasswordSet)
        {
            if (_activeKey != null)
            {
                CryptographicOperations.ZeroMemory(_activeKey);
                _activeKey = null;
            }
        }
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;
        if (_activeKey == null)
            throw new InvalidOperationException("Magazyn poświadczeń jest zablokowany. Wymagane hasło główne.");

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipher = new byte[plainBytes.Length];

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_activeKey, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var payload = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);

        return "enc:v1:" + Convert.ToBase64String(payload);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return cipherText;
        if (!cipherText.StartsWith("enc:v1:")) return cipherText; // Niezaszyfrowany tekst wstecznie zgodny
        if (_activeKey == null)
            throw new InvalidOperationException("Magazyn poświadczeń jest zablokowany. Wymagane hasło główne.");

        var raw = Convert.FromBase64String(cipherText["enc:v1:".Length..]);
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipherLen = raw.Length - (NonceSize + TagSize);
        var cipher = new byte[cipherLen];

        Buffer.BlockCopy(raw, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(raw, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(raw, NonceSize + TagSize, cipher, 0, cipherLen);

        using var aes = new AesGcm(_activeKey, TagSize);
        var plain = new byte[cipherLen];
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }
}
