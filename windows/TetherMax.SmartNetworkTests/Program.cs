using System.Text.Json;
using TetherMax;
int passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception(name);passed++;}
var now=new DateTime(2026,10,30,12,0,0,DateTimeKind.Utc);
var m=new UsageLedger();m.SetBudget(1000);
Check(m.Accept("a",1,300,400,now)&&m.State.Used==700,"first sample counts all observed payload");
Check(!m.Accept("a",1,300,400,now)&&m.State.Used==700,"duplicate ignored");
Check(m.Accept("a",2,400,400,now)&&m.Level==80,"80 threshold");
Check(!m.Accept("a",3,399,500,now)&&m.State.Used==800,"counter rollback rejected");
Check(!m.Accept("a",1,900,900,now),"out of order rejected");
m=new UsageLedger(JsonSerializer.Deserialize<UsageState>(JsonSerializer.Serialize(m.State)));
Check(m.Accept("a",3,500,500,now)&&m.State.Used==1000&&m.Level==100,"restart same engine no double count");
Check(m.Accept("b",1,10,20,now)&&m.State.Used==1030,"new engine retains previous total");
Check(!m.Accept("a",4,9999,9999,now),"late retired session ignored");
Check(m.Accept("b",2,110,120,now.AddMonths(1))&&m.State.Used==200,"month rollover counts interval delta");
Check(m.Accept("b",3,160,170,now)&&m.State.Used==300&&m.State.Month=="2026-11","clock rollback preserves month");
Check(!m.Accept("b",4,ulong.MaxValue,ulong.MaxValue,now),"overflow rejected");
var saturated=new UsageLedger(new UsageState("2026-10",long.MaxValue-1));saturated.Accept("s",1,3,3,now);
Check(saturated.State.Used==long.MaxValue,"saturating total");
for(int i=0;i<100;i++)m.Accept("restart-"+i,1,1,1,now.AddMonths(1));
Check(m.State.Retired?.Length==16,"bounded retired session history");
bool invalid=false;try{new UsageLedger(new UsageState("bad",-1));}catch(IOException){invalid=true;}Check(invalid,"corrupt history rejected");
string temp=Path.Combine(Path.GetTempPath(),"tethermax-usage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
try{
 string path=Path.Combine(temp,"usage.json");SettingsFile.Write(path,m.State);m.Accept("final",1,5,5,now.AddMonths(1));SettingsFile.Write(path,m.State);
 Check(SettingsFile.Read<UsageState>(path)?.Used==m.State.Used,"atomic persisted state roundtrip");
 File.WriteAllText(path,"truncated {");Check(SettingsFile.Read<UsageState>(path)?.Used==m.State.Used-10,"last parseable backup recovered");
 File.WriteAllText(path+".previous","bad");Check(SettingsFile.Read<UsageState>(path)==null,"both corrupt files detectable");
}finally{foreach(string f in Directory.GetFiles(temp))File.Delete(f);Directory.Delete(temp);}
using var empty=JsonDocument.Parse("{}");Check(NetworkDoctor.Explain(empty.RootElement).Contains("tek başına DNS arızası kanıtı değildir"),"missing metrics not fake diagnosis");
using var faulty=JsonDocument.Parse("{\"phoneNetwork\":\"UNAVAILABLE\",\"ProbeSamples\":20,\"ProbeLossPercent\":25,\"QueueP95Ms\":70,\"OverlimitDrops\":1,\"VisibleHosts\":1}");
var explanation=NetworkDoctor.Explain(faulty.RootElement);
Check(explanation.Contains("hücresel")&&explanation.Contains("yanıtsızlık")&&explanation.Contains("oyun pingi değildir")&&explanation.Contains("ICS/NAT"),"observed fault explanations retain scope");
Console.WriteLine($"PASS {passed} isolated Smart Network assertions; no network or device mutations.");
namespace TetherMax {internal static class Diagnostics {public static void Record(string operation,Exception error){}}}
