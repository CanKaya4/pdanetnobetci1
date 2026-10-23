using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using TetherMax;

class Tests {
 static byte[] Join(params byte[][] xs)=>xs.SelectMany(x=>x).ToArray();
 static byte[] Mac(byte[] key,byte[] b)=>HMACSHA256.HashData(key,b);
 static async Task<byte[]> Read(NetworkStream s,int n){var b=new byte[n];await s.ReadExactlyAsync(b);return b;}
 static async Task Serve(TcpListener listener,ECDiffieHellman identity,bool corruptCommit=false,bool corruptTag=false){
  using var c=await listener.AcceptTcpClientAsync();using var s=c.GetStream();
  try {
   byte[] hello=await Read(s,36),pub=identity.ExportSubjectPublicKeyInfo(),nonce=RandomNumberGenerator.GetBytes(32);
   byte[] commitment=SHA256.HashData(Join(pub,nonce));if(corruptCommit)commitment[0]^=1;
   await s.WriteAsync(commitment);byte[] size=await Read(s,2),peerPub=await Read(s,size[1]),peerNonce=await Read(s,32);
   if(!CryptographicOperations.FixedTimeEquals(hello.AsSpan(4),SHA256.HashData(Join(peerPub,peerNonce))))throw new Exception("Client commitment failed");
   await s.WriteAsync(Join(new byte[]{0,91},pub,nonce));
   using var peer=ECDiffieHellman.Create();peer.ImportSubjectPublicKeyInfo(peerPub,out _);
   byte[] transcript=SHA256.HashData(Join(Encoding.ASCII.GetBytes("TetherMax-Pair-v2"),peerPub,peerNonce,pub,nonce));
   byte[] key=Mac(Mac(transcript,identity.DeriveRawSecretAgreement(peer.PublicKey)),Join(Encoding.ASCII.GetBytes("session"),new byte[]{1}));
   byte[] confirmation=await Read(s,29),plain=new byte[1];using var aes=new AesGcm(key,16);aes.Decrypt(confirmation.AsSpan(0,12),confirmation.AsSpan(12,1),confirmation.AsSpan(13,16),plain,transcript);
   if(plain[0]!=1)throw new Exception("Confirmation invalid");
   byte[] responseNonce=RandomNumberGenerator.GetBytes(12),cipher=new byte[32],tag=new byte[16];
   aes.Encrypt(responseNonce,Encoding.ASCII.GetBytes(new string('a',32)),cipher,tag,transcript);if(corruptTag)tag[0]^=1;
   await s.WriteAsync(Join(responseNonce,cipher,tag));
  }catch(IOException){} catch(SocketException){}
 }
 static async Task Run(string label,ECDiffieHellman identity,string serial,bool shouldPass,bool approve=true,bool corruptCommit=false,bool corruptTag=false,bool mustSkipPrompt=false){
  using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var server=Serve(listener,identity,corruptCommit,corruptTag);
  bool success=false,prompted=false;using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(4));
  try{var token=await Pairing.Connect(((IPEndPoint)listener.LocalEndpoint).Port,serial,code=>{prompted=true;return Task.FromResult(approve&&code.Length==6);},timeout.Token);success=token==new string('a',32);}catch(Exception ex)when(ex is CryptographicException or OperationCanceledException or IOException){}
  await server.WaitAsync(TimeSpan.FromSeconds(5));if(success!=shouldPass||(mustSkipPrompt&&prompted))throw new Exception("FAIL "+label);Console.WriteLine("PASS "+label);
 }
 static async Task Main(){
  Pairing.Folder=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"TetherMax-tests-"+Guid.NewGuid().ToString("N"));
  using var phone=ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);using var impostor=ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
  await Run("first consent and authenticated token",phone,"fixture",true);
  await Run("restart identity and skip repeated consent",phone,"fixture",true,mustSkipPrompt:true);
  await Run("changed phone identity rejected",impostor,"fixture",false);
  await Run("user rejection releases no token",phone,"rejected",false,approve:false);
  await Run("changed commitment rejected",phone,"commit",false,corruptCommit:true);
  await Run("tampered AES-GCM rejected",phone,"tag",false,corruptTag:true);
  Pairing.Forget("fixture");await Run("forgotten identity requires new consent",phone,"fixture",false,approve:false);
  Console.WriteLine("7 offline pairing safety checks passed; no physical traffic.");
 }
}
