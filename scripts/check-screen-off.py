"""Bounded read-only networking check on the existing approved USB session."""
import json, re, socket, subprocess, time
from datetime import datetime, timezone
from pathlib import Path
adb = r'C:\Users\Ali\AppData\Local\Android\Sdk\platform-tools\adb.exe'
def phone(*args):
    return subprocess.check_output([adb, '-s', 'R5GL16BFE6T', 'shell', *args], timeout=10, text=True, encoding='utf-8', errors='replace')
phone('input', 'keyevent', '223')
samples=[]
with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as udp:
    udp.bind(('10.254.88.2',0)); udp.connect(('1.1.1.1',53)); udp.settimeout(2)
    for i in range(15):
        state=phone('dumpsys','power')
        wake=re.search(r'^\s*mWakefulness=(\w+)',state,re.M)
        query=i.to_bytes(2,'big')+bytes.fromhex('01000001000000000000076578616d706c6503636f6d0000010001')
        start=time.monotonic(); error=None;elapsed=None
        try:
            udp.send(query);data=udp.recv(65535)
            if len(data)<12 or data[:2]!=query[:2] or not data[2]&128:raise OSError('Invalid DNS response')
            elapsed=round((time.monotonic()-start)*1000,2)
        except OSError as e:error=str(e)
        samples.append(dict(timeUtc=datetime.now(timezone.utc).isoformat(),wakefulness=wake[1] if wake else 'unknown',transferCpuLock=bool(re.search(r'^.*PARTIAL_WAKE_LOCK.*TetherMax:UsbTransfer',state,re.M)),rttMs=elapsed,error=error))
        time.sleep(2)
result=dict(metric='UDP DNS RTT bound to TetherMax local address; not game ping',samples=samples,screenOffSamples=sum(s['wakefulness'] in ('Asleep','Dozing') for s in samples),successfulScreenOffSamples=sum(s['wakefulness'] in ('Asleep','Dozing') and s['error'] is None for s in samples))
Path(__file__).resolve().parents[1].joinpath('build/reports/rc7-screen-off.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
print(json.dumps(result))
