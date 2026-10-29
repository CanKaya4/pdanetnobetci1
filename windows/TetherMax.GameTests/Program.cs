using TetherMax;

// No native calls, public probes, adapters or physical game processes.
int passed=0;
void Check(bool result,string name){if(!result)throw new Exception(name);passed++;}
var game=new ObservedGame(42,1234,@"C:\Games\game.exe");
var bg=new ObservedGame(43,1235,@"C:\Apps\download.exe");
var normal=new ProcessTuning(0x20,0,0);
var fake=new Fake();fake.Values[game]=normal;fake.Values[bg]=normal;
TuningLease[] journal=[];
var optimizer=new GameOptimization(fake,[],saved=>journal=saved);
fake.BeforeWrite=()=>Check(journal.Length>0,"journal persisted before write");
optimizer.Update([game],[bg]);
Check(fake.Values[game]==new ProcessTuning(0x8000,1,0),"game AboveNormal + HighQoS");
Check(fake.Values[bg]==new ProcessTuning(0x4000,1,1),"selected background BelowNormal + EcoQoS");
int writes=fake.Writes;optimizer.Update([game],[bg]);Check(fake.Writes==writes,"stable session does not rewrite process settings");
optimizer.Update([],[]);Check(fake.Values[game]==normal&&fake.Values[bg]==normal&&journal.Length==0,"disable/last game exit restores everything");
optimizer.Update([game],[bg]);
var recovered=new GameOptimization(fake,journal,saved=>journal=saved);recovered.RestoreAll();
Check(fake.Values[game]==normal&&fake.Values[bg]==normal&&journal.Length==0,"restart recovery");
fake.BeforeWrite=null;optimizer=new GameOptimization(fake,[],saved=>journal=saved);
optimizer.Update([game],[]);fake.Values.Remove(game);var reused=game with{Started=9999};fake.Values[reused]=normal;
optimizer.RestoreAll();Check(fake.Values[reused]==normal&&optimizer.Pending==0,"recycled PID untouched");
fake.Values[game]=normal;optimizer.Update([game],[]);fake.Values[game]=fake.Values[game] with{Priority=0x80};optimizer.RestoreAll();
Check(fake.Values[game]==normal with{Priority=0x80},"independent priority change preserved");
fake.Values[game]=normal;fake.FailAfterPriority=true;optimizer.Update([game],[]);
Check(optimizer.Pending==1&&fake.Values[game].Priority==0x8000,"partial mutation retains recovery record");
fake.FailAfterPriority=false;optimizer.RestoreAll();Check(fake.Values[game]==normal,"partial mutation restored");
var denied=new GameOptimization(fake,[],_=>throw new IOException("disk full"));writes=fake.Writes;
denied.Update([game],[]);Check(fake.Writes==writes&&denied.Pending==0,"journal failure prevents mutation");
optimizer.Update([game],[]);fake.Deny=true;optimizer.RestoreAll();Check(optimizer.Pending==1,"access denied recovery stays pending");
fake.Deny=false;optimizer.RestoreAll();Check(optimizer.Pending==0,"recovery retries after access returns");
Check(GameOptimization.Target(new(0x80,2,2),true)==new ProcessTuning(0x80,3,2),"existing High class and unrelated power bits preserved");
Check(GameOptimization.Target(new(0x40,0,0),false).Priority==0x40,"Idle background priority is not raised");
bool invalidJournal=false;try{new GameOptimization(fake,[null!],_=>{});}catch(IOException){invalidJournal=true;}Check(invalidJournal,"malformed recovery journal fails closed");
Check(!GameLibrary.BackgroundAllowed(@"C:\Tools\PdaNetPC.exe")&&!GameLibrary.BackgroundAllowed(@"C:\Tools\Discord.exe")&&!GameLibrary.BackgroundAllowed(@"C:\Tools\vgc.exe"),"connectivity voice anti-cheat exclusions");
Check(GameLibrary.Match([game],[@"C:\Other\game.exe"]).Length==0,"same name different path does not match");
Check(GameLibrary.Match([game],[game.Path.ToUpperInvariant()]).Length==1,"full path comparison case insensitive");
foreach(double bad in new[]{double.NaN,double.PositiveInfinity,0,11}){bool rejected=false;try{new GameFocusSettings(true,[],bad).Validate();}catch(IOException){rejected=true;}Check(rejected,"invalid traffic budget rejected");}
Console.WriteLine($"PASS: {passed} isolated game optimization assertions; no Windows state changed.");

sealed class Fake:IGameTuning {
 public Dictionary<ObservedGame,ProcessTuning> Values=new();public Action? BeforeWrite;public int Writes;public bool FailAfterPriority,Deny;
 public ProcessTuning? Read(ObservedGame game){if(Deny)throw new UnauthorizedAccessException();return Values.GetValueOrDefault(game);}
 public void Write(ObservedGame game,ProcessTuning value){BeforeWrite?.Invoke();Writes++;if(FailAfterPriority){Values[game]=Values[game] with{Priority=value.Priority};throw new IOException("power write failed");}Values[game]=value;}
}
namespace TetherMax { internal static class Diagnostics {public static void Record(string operation,Exception error){} } }
