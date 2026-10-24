#!/usr/bin/env python3
"""Install pinned tools under /workspace; preserve TLS and archive verification.
Run only in Codex Cloud. Never configures a phone or a Windows host network.
"""
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile
import urllib.parse
import urllib.request
import zipfile

ROOT = Path(os.environ.get("TETHERMAX_TOOLS", "/workspace/toolchains")).resolve()
ROOT.mkdir(parents=True, exist_ok=True)


def download(url, digest, destination):
    with urllib.request.urlopen(url, timeout=120) as response, open(destination, "wb") as out:
        shutil.copyfileobj(response, out)
    actual = hashlib.sha256(Path(destination).read_bytes()).hexdigest()
    if actual != digest:
        raise RuntimeError(f"SHA256 mismatch for {url}")


def tar_tool(url, digest, target, executable, strip=False):
    if (target / executable).exists():
        return
    with tempfile.TemporaryDirectory(dir=ROOT) as tmp:
        archive = Path(tmp) / "tool.tar.gz"
        download(url, digest, archive)
        with tarfile.open(archive) as tar:
            tar.extractall(Path(tmp) / "unpack", filter="data")
        extracted = next((Path(tmp) / "unpack").iterdir())
        if target.exists():
            # Refuse to overwrite a partially installed or user-managed tool directory.
            if any(target.iterdir()):
                raise RuntimeError(f"Nonempty incomplete tool directory: {target}")
            target.rmdir()
        shutil.move(str(extracted), target)


tar_tool("https://dl.google.com/go/go1.26.1.linux-amd64.tar.gz",
         "031f088e5d955bab8657ede27ad4e3bc5b7c1ba281f05f245bcc304f327c987a",
         ROOT / "go", Path("bin/go"))
tar_tool("https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.13%2B9/OpenJDK21U-jdk_x64_linux_hotspot_21.0.13_9.tar.gz",
         "d7f5d5cf865a2902b70ff549b97586a7d3f234e016f041ab447ea740c7d66d1c",
         ROOT / "jdk21", Path("bin/javac"))

sdk = ROOT / "android-sdk"
manager = sdk / "cmdline-tools/latest/bin/sdkmanager"
if not manager.exists():
    with tempfile.TemporaryDirectory(dir=ROOT) as tmp:
        archive = Path(tmp) / "tools.zip"
        download("https://dl.google.com/android/repository/commandlinetools-linux-13114758_latest.zip",
                 "7ec965280a073311c339e571cd5de778b9975026cfcbe79f2b1cdcb1e15317ee", archive)
        with zipfile.ZipFile(archive) as z:
            z.extractall(tmp)
            # Python zipfile does not retain executable bits.
            for entry in z.infolist():
                mode = entry.external_attr >> 16
                if mode:
                    (Path(tmp) / entry.filename).chmod(mode & 0o777)
        manager.parent.parent.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(Path(tmp) / "cmdline-tools"), manager.parent.parent)

userdir = ROOT / "android-user"
(userdir / "cache").mkdir(parents=True, exist_ok=True)
env = os.environ.copy()
env["JAVA_HOME"] = str(ROOT / "jdk21")
env["ANDROID_USER_HOME"] = str(userdir)
# Reuse the platform-supplied CA through an additional truststore, without disabling TLS.
cert = os.environ.get("CODEX_PROXY_CERT")
if cert and Path(cert).is_file():
    trust = ROOT / "cloud-java-cacerts"
    if not trust.exists():
        shutil.copy2(ROOT / "jdk21/lib/security/cacerts", trust)
        subprocess.run([str(ROOT / "jdk21/bin/keytool"), "-importcert", "-noprompt",
                        "-alias", "codex-cloud-proxy", "-file", cert, "-keystore", str(trust),
                        "-storepass", "changeit"], check=True, stdout=subprocess.DEVNULL)
    env["JAVA_OPTS"] = env.get("JAVA_OPTS", "") + " -Djavax.net.ssl.trustStore=" + str(trust)
args = [str(manager), "--sdk_root=" + str(sdk)]
proxy = urllib.parse.urlsplit(os.environ.get("HTTPS_PROXY", ""))
if proxy.hostname:
    args += ["--proxy=http", "--proxy_host=" + proxy.hostname, "--proxy_port=" + str(proxy.port or 80)]
packages = ["platforms;android-35", "build-tools;35.0.0", "platform-tools"]
subprocess.run(args + packages, input="y\n" * 200, text=True, env=env, check=True)
for file in [sdk / "platforms/android-35/android.jar", sdk / "build-tools/35.0.0/apksigner", ROOT / "jdk21/bin/javac"]:
    if not file.exists():
        raise RuntimeError(f"Missing required tool after SDK installation: {file}")
print("Cloud toolchains installed. No device or host networking was accessed.")
