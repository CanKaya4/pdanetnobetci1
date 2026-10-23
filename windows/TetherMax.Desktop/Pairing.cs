using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TetherMax;

internal sealed record TrustedPhone(string PublicKey);
internal static class Pairing
{
    internal static string Folder { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TetherMax");
    static readonly byte[] Entropy = Encoding.ASCII.GetBytes("TetherMax.identity.v2");
    static byte[] Join(params byte[][] parts) => parts.SelectMany(x => x).ToArray();
    static byte[] Mac(byte[] key, byte[] value) => HMACSHA256.HashData(key, value);
    static void Save(string name, byte[] data)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, name); var temp = path + ".tmp";
        File.WriteAllBytes(temp, ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser));
        File.Move(temp, path, true);
    }
    static byte[]? Load(string name)
    {
        var path = Path.Combine(Folder, name);
        return File.Exists(path) ? ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser) : null;
    }
    static string TrustName(string serial) => "phone-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serial))) + ".bin";
    public static void Forget(string serial) { File.Delete(Path.Combine(Folder, TrustName(serial))); }
    static async Task<byte[]> Read(NetworkStream stream, int length, CancellationToken cancel)
    {
        byte[] bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, cancel); return bytes;
    }
    static byte[] Encrypt(byte[] key, byte[] nonce, byte[] aad, byte[] value)
    {
        byte[] cipher = new byte[value.Length], tag = new byte[16];
        using var aes = new AesGcm(key,16); aes.Encrypt(nonce,value,cipher,tag,aad); return Join(cipher,tag);
    }
    public static async Task<string> Connect(int port, string serial, Func<string,Task<bool>> approve, CancellationToken cancel)
    {
        using var identity = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        byte[]? saved = Load("identity.bin");
        if (saved != null) { try { identity.ImportPkcs8PrivateKey(saved,out _); } finally { CryptographicOperations.ZeroMemory(saved); } }
        else { byte[] privateKey = identity.ExportPkcs8PrivateKey(); try { Save("identity.bin",privateKey); } finally { CryptographicOperations.ZeroMemory(privateKey); } }
        byte[] pub = identity.ExportSubjectPublicKeyInfo(), nonce = RandomNumberGenerator.GetBytes(32);
        using var client = new TcpClient(); await client.ConnectAsync("127.0.0.1",port,cancel); using var stream = client.GetStream();
        await stream.WriteAsync(Join(Encoding.ASCII.GetBytes("TPM2"),SHA256.HashData(Join(pub,nonce))),cancel);
        byte[] commit = await Read(stream,32,cancel);
        await stream.WriteAsync(Join(new byte[]{0,(byte)pub.Length},pub,nonce),cancel);
        byte[] size = await Read(stream,2,cancel);
        if (size[0] != 0 || size[1] != 91) throw new IOException("Geçersiz cihaz anahtarı.");
        byte[] peerPub = await Read(stream,91,cancel), peerNonce = await Read(stream,32,cancel);
        if (!CryptographicOperations.FixedTimeEquals(commit,SHA256.HashData(Join(peerPub,peerNonce)))) throw new CryptographicException("Kimlik taahhüdü doğrulanamadı.");
        using var peer = ECDiffieHellman.Create(); peer.ImportSubjectPublicKeyInfo(peerPub,out int used);
        if (used != 91 || peer.KeySize != 256) throw new CryptographicException("Geçersiz P256 anahtarı.");
        byte[] transcript = SHA256.HashData(Join(Encoding.ASCII.GetBytes("TetherMax-Pair-v2"),pub,nonce,peerPub,peerNonce));
        byte[] shared = identity.DeriveRawSecretAgreement(peer.PublicKey);
        byte[] prk = Mac(transcript,shared), key = Mac(prk,Join(Encoding.ASCII.GetBytes("session"),new byte[]{1}));
        CryptographicOperations.ZeroMemory(shared); CryptographicOperations.ZeroMemory(prk);
        try
        {
            var trusted = Load(TrustName(serial));
            if (trusted != null && !CryptographicOperations.FixedTimeEquals(trusted,peerPub))
                throw new CryptographicException("Telefon kimliği değişti. Telefonu doğrulayıp eski eşleştirmeyi unutun.");
            byte[] sas = Mac(key,Encoding.ASCII.GetBytes("sas"));
            uint value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(sas);
            if (trusted == null && !await approve((value % 1000000).ToString("D6"))) throw new OperationCanceledException("Eşleştirme reddedildi.");
            byte[] confirmationNonce = RandomNumberGenerator.GetBytes(12);
            await stream.WriteAsync(Join(confirmationNonce,Encrypt(key,confirmationNonce,transcript,new byte[]{1})),cancel);
            byte[] responseNonce = await Read(stream,12,cancel), encrypted = await Read(stream,48,cancel), token = new byte[32];
            using var aes = new AesGcm(key,16); aes.Decrypt(responseNonce,encrypted.AsSpan(0,32),encrypted.AsSpan(32,16),token,transcript);
            string code = Encoding.ASCII.GetString(token); CryptographicOperations.ZeroMemory(token);
            if (code.Length != 32 || code.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f'))) throw new CryptographicException("Geçersiz oturum anahtarı.");
            Save(TrustName(serial),peerPub);
            return code;
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
}
