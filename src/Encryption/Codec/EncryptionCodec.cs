namespace TemporalioSamples.Encryption.Codec;

using System.Security.Cryptography;
using Google.Protobuf;
using Temporalio.Api.Common.V1;
using Temporalio.Converters;

public sealed class EncryptionCodec : IPayloadCodec, IDisposable
{
    public const string DefaultKeyID = "test-key-id";
    private static string DefaultKey = "test-key-test-key-test-key-test!";
    // Taken from other language samples for compatibility
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly ByteString EncodingByteString = ByteString.CopyFromUtf8("binary/encrypted");

    private readonly byte[] KeyBytes;
    private readonly ByteString keyIDByteString;
    private readonly AesGcm aes;

    public EncryptionCodec(string keyID = DefaultKeyID, string? key = DefaultKey)
    {
        KeyID = keyID;
        keyIDByteString = ByteString.CopyFromUtf8(keyID);
        KeyBytes = System.Text.Encoding.ASCII.GetBytes(key);
    }

    ~EncryptionCodec() => Dispose();

    public string KeyID { get; private init; }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public Task<IReadOnlyCollection<Payload>> EncodeAsync(IReadOnlyCollection<Payload> payloads) =>
        Task.FromResult<IReadOnlyCollection<Payload>>(payloads.Select(p =>
        {
            return new Payload()
            {
                Metadata =
                {
                    ["encoding"] = EncodingByteString,
                    ["encryption-key-id"] = keyIDByteString,
                },
                // TODO(cretz): Not clear here how to prevent copy
                Data = ByteString.CopyFrom(Encrypt(p.ToByteArray())),
            };
        }).ToList());

    public Task<IReadOnlyCollection<Payload>> DecodeAsync(IReadOnlyCollection<Payload> payloads) =>
        Task.FromResult<IReadOnlyCollection<Payload>>(payloads.Select(p =>
        {
            // Ignore if it doesn't have our expected encoding
            if (p.Metadata.GetValueOrDefault("encoding") != EncodingByteString)
            {
                return p;
            }
            // Confirm same key
            var keyID = p.Metadata.GetValueOrDefault("encryption-key-id");
            if (keyID != keyIDByteString)
            {
                throw new InvalidOperationException($"Unrecognized key ID {keyID?.ToStringUtf8()}, expected {KeyID}");
            }
            // Decrypt
            return Payload.Parser.ParseFrom(Decrypt(p.Data.ToByteArray()));
        }).ToList());

    private byte[] Encrypt(byte[] data)
    {
        // Our byte array will have a const-length nonce, const-length tag, and
        // then the encrypted data. In real-world use, one may want to put nonce
        // and/or tag lengths in here.
        var bytes = new byte[NonceSize + TagSize + data.Length];

        // Generate random nonce
        var nonceSpan = bytes.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonceSpan);

        // Perform encryption
        using (var aes = new AesGcm(KeyBytes)) {
            aes.Encrypt(nonceSpan, data, bytes.AsSpan(NonceSize + TagSize), bytes.AsSpan(NonceSize, TagSize));
            return bytes;
        }
    }

    private byte[] Decrypt(byte[] data)
    {
        var bytes = new byte[data.Length - NonceSize - TagSize];

        using (var aes = new AecGcm(KeyBytes)) {
            aes.Decrypt(
                data.AsSpan(0, NonceSize), data.AsSpan(NonceSize + TagSize), data.AsSpan(NonceSize, TagSize), bytes.AsSpan());
            return bytes;
        }
    }
}