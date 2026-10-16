using FluentAssertions;
using HornetStudio.Core.Storage.Security;
using Xunit;

namespace HornetStudio.Tests.Storage;

public class MasterPasswordVaultTests : IDisposable
{
    private readonly string _tempDir;

    public MasterPasswordVaultTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HornetTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Fact]
    public void DefaultVault_WithoutMasterPassword_EncryptsAndDecrypts()
    {
        var vault = new MasterPasswordVault(_tempDir);
        vault.IsMasterPasswordConfigured().Should().BeFalse();
        vault.IsUnlocked().Should().BeTrue();

        var secret = "P@ssw0rd123!_SuperSecure";
        var encrypted = vault.Encrypt(secret);

        encrypted.Should().StartWith("enc:v1:");
        encrypted.Should().NotBe(secret);

        var decrypted = vault.Decrypt(encrypted);
        decrypted.Should().Be(secret);
    }

    [Fact]
    public void SettingMasterPassword_LocksVault_RequiresUnlock()
    {
        var vault = new MasterPasswordVault(_tempDir);
        var pass = "MasterSecretKey!2026";
        vault.SetMasterPassword(pass).Should().BeTrue();

        vault.IsMasterPasswordConfigured().Should().BeTrue();

        // Zamknij i otwórz ponownie instancję magazynu
        var vault2 = new MasterPasswordVault(_tempDir);
        vault2.IsMasterPasswordConfigured().Should().BeTrue();
        vault2.IsUnlocked().Should().BeFalse();

        // Błędne hasło nie odblokowuje
        vault2.Unlock("WrongPassword").Should().BeFalse();
        vault2.IsUnlocked().Should().BeFalse();

        // Próba deszyfrowania zablokowanego rzuca wyjątek
        var act = () => vault2.Encrypt("test");
        act.Should().Throw<InvalidOperationException>();

        // Prawidłowe hasło odblokowuje
        vault2.Unlock(pass).Should().BeTrue();
        vault2.IsUnlocked().Should().BeTrue();

        var text = "Database_Secret_Payload";
        var cipher = vault2.Encrypt(text);
        vault2.Decrypt(cipher).Should().Be(text);
    }
}
