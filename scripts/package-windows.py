"""Build a per-user MSI from the published folder; never install it automatically."""
import hashlib
import argparse
import json
import uuid
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path
root=Path(__file__).resolve().parent.parent
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--publish-dir',type=Path,default=root/'build/windows-portable')
parser.add_argument('--output',type=Path,default=root/'build/artifacts/tethermax-windows-x64-preview.msi')
parser.add_argument('--version',default='0.5.0')
args=parser.parse_args()
publish=args.publish_dir.resolve()
if any(not (publish/name).is_file() for name in ('TetherMax.exe','tethermax-engine.exe','network-audit.ps1','TetherMax.Network.exe','wintun.dll','WINTUN-LICENSE.txt')):
 raise SystemExit('Fresh desktop, engine, network guardian, pinned Wintun and notices required before packaging')
for app in ('TetherMax','TetherMax.Network'):
 config=json.loads((publish/(app+'.runtimeconfig.json')).read_text(encoding='utf-8'))
 if not config.get('runtimeOptions',{}).get('includedFrameworks'):
  raise SystemExit(app+' must be published self-contained before MSI packaging')
if hashlib.sha256((publish/'wintun.dll').read_bytes()).hexdigest()!='e5da8447dc2c320edc0fc52fa01885c103de8c118481f683643cacc3220dafce':
 raise SystemExit('Bundled official Wintun DLL checksum mismatch')
ns='http://wixtoolset.org/schemas/v4/wxs'
ET.register_namespace('',ns)
def add(parent,tag,**attrs): return ET.SubElement(parent,'{'+ns+'}'+tag,attrs)
wix=ET.Element('{'+ns+'}Wix')
pkg=add(wix,'Package',Name='TetherMax USB Preview',Manufacturer='TetherMax',Version=args.version,UpgradeCode='B1E69BC8-E38E-47FC-9C33-11E7C7FD6582',Scope='perUser',Language='1055')
add(pkg,'MajorUpgrade',DowngradeErrorMessage='A newer TetherMax preview is already installed.')
add(pkg,'MediaTemplate',EmbedCab='yes')
local=add(pkg,'StandardDirectory',Id='LocalAppDataFolder')
programs=add(local,'Directory',Id='UserPrograms',Name='Programs')
install=add(programs,'Directory',Id='INSTALLFOLDER',Name='TetherMax')
menu=add(pkg,'StandardDirectory',Id='ProgramMenuFolder')
feature=add(pkg,'Feature',Id='Main',Title='TetherMax',Level='1')
dirs={Path('.'):install}
for file in sorted(publish.rglob('*')):
 if not file.is_file(): continue
 rel=file.relative_to(publish)
 current=Path('.')
 for part in rel.parts[:-1]:
  nxt=current/part
  if nxt not in dirs: dirs[nxt]=add(dirs[current],'Directory',Id='D'+hashlib.sha256(str(nxt).encode()).hexdigest()[:20],Name=part)
  current=nxt
 suffix=hashlib.sha256(str(rel).encode()).hexdigest()[:24]
 component=add(dirs[current],'Component',Id='C'+suffix,Guid=str(uuid.uuid5(uuid.UUID('b1e69bc8-e38e-47fc-9c33-11e7c7fd6582'),str(rel))),Bitness='always64')
 add(component,'File',Id='F'+suffix,Source=str(file),Name=file.name)
 add(component,'RegistryValue',Root='HKCU',Key=r'Software\TetherMax\Installer',Name='F'+suffix,Type='integer',Value='1',KeyPath='yes')
 if rel==Path('TetherMax.exe'):
  add(component,'Shortcut',Id='StartShortcut',Directory='ProgramMenuFolder',Name='TetherMax',Target='[#F'+suffix+']',WorkingDirectory='INSTALLFOLDER',Advertise='no')
 add(feature,'ComponentRef',Id='C'+suffix)
cleanup=add(install,'Component',Id='DirectoryCleanup',Guid='225561D1-1D93-4B70-BF31-E780247CF462',Bitness='always64')
add(cleanup,'RegistryValue',Root='HKCU',Key=r'Software\TetherMax\Installer',Name='Cleanup',Type='integer',Value='1',KeyPath='yes')
for i,d in enumerate(dirs.values()): add(cleanup,'RemoveFolder',Id='RemoveDir'+str(i),Directory=d.attrib['Id'],On='uninstall')
add(feature,'ComponentRef',Id='DirectoryCleanup')
source=root/'build/tethermax-installer.wxs'
ET.ElementTree(wix).write(source,encoding='utf-8',xml_declaration=True)
subprocess.run([str(root/'.ci-tools/wix/wix.exe'),'build',str(source),'-arch','x64','-o',str(args.output.resolve())],check=True)

