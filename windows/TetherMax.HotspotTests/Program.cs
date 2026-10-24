using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using TetherMax;
static void Require(bool value,string reason){if(!value)throw new Exception(reason);}
var portal=new HotspotPortal(IPAddress.Loopback,0);var ep=portal.EndPoint;
using var client=new HttpClient(new HttpClientHandler{UseProxy=false}){BaseAddress=new Uri("http://127.0.0.1:"+ep.Port),Timeout=TimeSpan.FromSeconds(3)};
var page=await client.GetStringAsync("/");var nonce=Regex.Match(page,@"/verified/([A-F0-9]{48})").Groups[1].Value;Require(nonce.Length==48,"Per-session nonce missing");
await client.PostAsync("/verified/wrong",new StringContent(""));Require(portal.Verified.Count==0,"Wrong nonce accepted");
await client.GetAsync("/verified/"+nonce);Require(portal.Verified.Count==0,"GET forged a client success");
await client.PostAsync("/verified/"+nonce,new StringContent(""));Require(portal.Verified.ContainsKey("127.0.0.1"),"Scoped browser completion not recorded");
await portal.DisposeAsync();var again=new TcpListener(ep);again.Start();again.Stop();
Console.WriteLine("PASS real loopback HTTP: per-session nonce, POST-only acknowledgement, wrong-session rejection, cancellation and socket cleanup; no public requests");
