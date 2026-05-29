namespace Mehrak.Domain.Shared.Services;

public interface IEncryptionService
{
    string Encrypt(string plainText, string passphrase);
    string Decrypt(string cipherText, string passphrase);
}
