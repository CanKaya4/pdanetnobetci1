package traffic

import("context";"encoding/binary";"encoding/json";"strings";"testing";"time")
func focusFixture(t *testing.T)(*Manager,*ownerSnapshot){
 m:=manager(t,Settings{Mode:Gaming,Focus:&FocusSettings{Enabled:true,Processes:[]FocusProcess{{42,1234}},BackgroundMbps:2}})
 o:=&ownerSnapshot{At:time.Now(),Generation:m.focus.lease.Load().Generation,Games:map[uint32]bool{42:true},Owners:map[ownerKey]uint32{}}
 m.focus.owners.Store(o);return m,o
}
func ownPacket(o *ownerSnapshot,b []byte,pid uint32){k,_:=focusPacketKey(b,false);if k.Protocol==17{k.Remote=[4]byte{};k.RemotePort=0};addOwner(o.Owners,k,pid)}
func TestFocusUsesOwnershipBothDirectionsAndPreservesUnknown(t *testing.T){
 m,o:=focusFixture(t);game:=packet(1,7000,1500);ownPacket(o,game,42);other:=packet(1,7001,1500);ownPacket(o,other,43)
 if m.focusClass(game,false,Bulk)!=Game||m.focusClass(other,false,Bulk)!=Background{t.Fatal("known process attribution failed")}
 if m.focusClass(packet(2,7002,1500),false,Bulk)!=Bulk{t.Fatal("unknown/hotspot owner was guessed")}
 if m.focusClass(other,false,Interactive)!=Interactive{t.Fatal("small latency-sensitive traffic was capped")}
 reply:=append([]byte(nil),game...);copy(reply[12:16],game[16:20]);copy(reply[16:20],game[12:16]);copy(reply[20:22],game[22:24]);copy(reply[22:24],game[20:22])
 if m.focusClass(reply,true,Bulk)!=Game{t.Fatal("download owner not matched")}
 reply[6]=0x20;if m.focusClass(reply,true,Bulk)!=Bulk{t.Fatal("fragment owner guessed")}
}
func TestFocusExpiresAndNeverResetsCapacityOnLeaseRenewal(t *testing.T){
 m,o:=focusFixture(t);game:=packet(1,7000,1500);ownPacket(o,game,42);m.upRate=7
 s:=m.Settings();if err:=m.Configure(s);err!=nil{t.Fatal(err)};if m.upRate!=7||m.focusClass(game,false,Bulk)!=Game{t.Fatal("renewal reset learned capacity")}
 lease:=*m.focus.lease.Load();lease.At=time.Now().Add(-9*time.Second);m.focus.lease.Store(&lease)
 if m.focusClass(game,false,Bulk)!=Bulk||m.backgroundRate(false)!=0{t.Fatal("stale controller retained restrictions")}
 m.Configure(s);o.At=time.Now().Add(-6*time.Second);if m.focusClass(game,false,Bulk)!=Bulk{t.Fatal("stale owner table accepted")}
 o.At=time.Now();s.Focus=&FocusSettings{Enabled:true,Processes:[]FocusProcess{{42,9999}},BackgroundMbps:2};m.Configure(s)
 if m.focusClass(game,false,Bulk)!=Bulk{t.Fatal("reused PID inherited previous identity's table")}
}
func TestFocusBackgroundCannotBlockAnotherHostGame(t *testing.T){
 m,o:=focusFixture(t);background:=packet(1,8000,1500);game:=packet(2,8001,1500);ownPacket(o,background,43);ownPacket(o,game,42)
 q:=m.PacketQueue(false);q.Enqueue(background);q.Enqueue(game);q.mu.Lock();q.bgRate=1;q.bgTokens=0;q.bgBurst=4096;q.hosts[binary.BigEndian.Uint32(background[12:16])].finish=-100
 _,chosen,_:=q.selectFlow();if chosen==nil||chosen.packets[0].class!=Game{q.mu.Unlock();t.Fatal("background token wait blocked eligible game on another host")};q.mu.Unlock()
 // Disabling focus reclassifies already queued packets; no sockets are reset.
 s:=m.Settings();s.Focus=nil;m.Configure(s);ctx,cancel:=context.WithTimeout(context.Background(),time.Second);defer cancel()
 if _,err:=q.Read(ctx);err!=nil{t.Fatal("disable did not release queued data",err)}
}
func TestFocusValidationAndPhoneRedaction(t *testing.T){
 for _,f:=range []*FocusSettings{{Enabled:true,BackgroundMbps:0},{Enabled:true,BackgroundMbps:2,Processes:[]FocusProcess{{1,1}}},{Enabled:true,BackgroundMbps:2,Processes:[]FocusProcess{{42,1},{42,2}}}}{if (Settings{Mode:Auto,Focus:f}).Validate()==nil{t.Fatal("invalid focus accepted")}}
 m,_:=focusFixture(t);encoded,err:=json.Marshal(m.Snapshot().PhoneSnapshot());if err!=nil{t.Fatal(err)};for _,private:=range []string{"\"focus\"","\"processes\"","\"started\"","\"pid\""}{if strings.Contains(string(encoded),private){t.Fatal("local Windows identity leaked to phone",private)}}
}
func TestOwnerTablesDecodeMappedIPv6AndAmbiguousUDP(t *testing.T){
 owners:=map[ownerKey]uint32{};data:=make([]byte,4+24);binary.LittleEndian.PutUint32(data,1);row:=data[4:];copy(row[4:8],[]byte{10,0,0,1});binary.BigEndian.PutUint16(row[8:10],7000);copy(row[12:16],[]byte{1,1,1,1});binary.BigEndian.PutUint16(row[16:18],443);binary.LittleEndian.PutUint32(row[20:],42)
 if err:=parseOwnerTable(data,false,true,owners);err!=nil{t.Fatal(err)};if owners[ownerKey{6,[4]byte{10,0,0,1},[4]byte{1,1,1,1},7000,443}]!=42{t.Fatal("TCP DWORD port/address layout")}
 udp:=make([]byte,4+28);binary.LittleEndian.PutUint32(udp,1);r:=udp[4:];r[10]=255;r[11]=255;copy(r[12:16],[]byte{10,0,0,1});binary.BigEndian.PutUint16(r[20:22],7001);binary.LittleEndian.PutUint32(r[24:],43)
 if err:=parseOwnerTable(udp,true,false,owners);err!=nil{t.Fatal(err)};key:=ownerKey{Protocol:17,Local:[4]byte{10,0,0,1},LocalPort:7001};if owners[key]!=43{t.Fatal("mapped IPv6 UDP layout")};addOwner(owners,key,42);if owners[key]!=0{t.Fatal("shared UDP port falsely assigned")}
 if parseOwnerTable(udp[:8],true,false,owners)==nil{t.Fatal("truncated table accepted")}
}
