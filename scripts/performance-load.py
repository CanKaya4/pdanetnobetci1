"""Explicit, bounded load on an already configured IPv4 interface; no OS changes.

Latency is UDP DNS transaction RTT, never Valorant/ICMP ping. Bind every socket
to the supplied local address so a missing tunnel fails instead of falling back.
"""
import argparse
import concurrent.futures
import http.client
import json
import secrets
import socket
import statistics
import struct
import threading
import time
from pathlib import Path


def summary(samples):
    if not samples:
        return None
    values = sorted(samples)
    return {"samples": len(values), "medianMs": statistics.median(values),
            "p95Ms": values[int((len(values)-1)*.95)],
            "jitterMs": statistics.mean(abs(b-a) for a, b in zip(samples, samples[1:])) if len(samples)>1 else None}


def run(local, output):
    start = time.monotonic()
    stop = threading.Event()
    loading = threading.Event()
    samples = {"idle": [], "loaded": [], "recovery": []}
    errors = []
    lock = threading.Lock()
    transferred = [0, 0]

    def probe():
        # One stable UDP flow: creating an ephemeral port per sample would test
        # the relay's flow limit instead of bufferbloat.
        with socket.socket(socket.AF_INET,socket.SOCK_DGRAM) as udp:
            udp.bind((local,0));udp.connect(("1.1.1.1",53))
            while not stop.is_set():
                phase="idle" if time.monotonic()-start<5 else "loaded" if loading.is_set() else "recovery"
                ident=secrets.token_bytes(2)
                name=(secrets.token_hex(5)+".example.com").split(".")
                query=ident+b"\x01\x00\x00\x01\x00\x00\x00\x00\x00\x00"+b"".join(bytes([len(x)])+x.encode() for x in name)+b"\x00\x00\x01\x00\x01"
                tick=time.monotonic()
                try:
                    udp.send(query)
                    while True:
                        udp.settimeout(max(.001,1.5-(time.monotonic()-tick)))
                        reply=udp.recv(4096)
                        if len(reply)>=12 and reply[:2]==ident and reply[2]&128:break
                        if time.monotonic()-tick>=1.5:raise TimeoutError("DNS deadline")
                    samples[phase].append((time.monotonic()-tick)*1000)
                except OSError as error:
                    errors.append({"phase":phase,"probe":str(error)})
                    if getattr(error,"winerror",None) in (10022,10049,10050):stop.set()
                stop.wait(max(0,.2-(time.monotonic()-tick)))

    def download():
        # Four 12 MiB responses: at most 48 MiB response payload per trial.
        connection = http.client.HTTPSConnection("dl.google.com", timeout=5, source_address=(local,0))
        try:
            connection.request("GET", "/go/go1.26.3.windows-amd64.zip", headers={"Range":"bytes=0-12582911","Accept-Encoding":"identity"})
            response = connection.getresponse()
            if response.status != 206 or not response.getheader("Content-Range","").startswith("bytes 0-12582911/"): raise OSError("bounded range refused: HTTP "+str(response.status))
            count = 0
            while count<12582912 and time.monotonic()-start<25:
                data = response.read(min(65536,12582912-count))
                if not data: raise OSError("bounded range ended before the requested bytes")
                count += len(data)
                with lock: transferred[1] += len(data)
        except (OSError,http.client.HTTPException) as error:
            with lock: errors.append({"download":str(error)})
        finally: connection.close()

    thread=threading.Thread(target=probe); thread.start()
    time.sleep(5)
    load_start=time.monotonic()
    loading.set()
    with concurrent.futures.ThreadPoolExecutor(max_workers=5) as pool:
        jobs=[pool.submit(download) for _ in range(4)]
        concurrent.futures.wait(jobs)
        for job in jobs:
            try:job.result()
            except Exception as error:errors.append({"download":str(error)})
    load_seconds=time.monotonic()-load_start
    loading.clear()
    time.sleep(max(0,30-(time.monotonic()-start)))
    stop.set(); thread.join()
    result={"label":output.stem,"localAddress":local,"latencyMetric":"UDP DNS transaction RTT; not game/ICMP ping",
            "latency":{k:summary(v) for k,v in samples.items()},"uploadBytes":transferred[0],"downloadBytes":transferred[1],
            "loadSeconds":load_seconds,"uploadMbps":transferred[0]*8/1e6/load_seconds,"downloadMbps":transferred[1]*8/1e6/load_seconds,
            "payloadBudgetBytes":50331648,"workload":"4 bounded HTTPS ranges + stable-flow UDP DNS","validLoad":transferred[1]>=8388608 and load_seconds>=3 and len(samples["loaded"])>=5 and not any("download" in error for error in errors),"errors":errors,"pathAttested":False}
    output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps(result,indent=2),encoding="utf-8")
    print(json.dumps(result))


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--approve-device-test",action="store_true",required=True)
    parser.add_argument("--local-address",required=True)
    parser.add_argument("--output",type=Path,required=True)
    args=parser.parse_args()
    run(args.local_address,args.output)
