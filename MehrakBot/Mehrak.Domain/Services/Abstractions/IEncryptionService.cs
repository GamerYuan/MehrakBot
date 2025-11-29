namespace Mehrak.Domain.Services.Abstractions;

public interface IEncryptionService
{
    string Encrypt(string plainText, string passphrase);

    string Decrypt(string cipherText, string passphrase);
}
