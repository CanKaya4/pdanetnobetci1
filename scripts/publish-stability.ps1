param([Parameter(Mandatory=$true)][string]$Commit)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try {
 if($Commit -notmatch '^[0-9a-f]{40}$'){throw 'Expected exact tested commit SHA'}
 $repo='CanKaya4/pdanetnobetci1'
 $tag='v1.0.0-rc.10-stability'
 $credentialLines="protocol=https`nhost=github.com`n`n" | git credential fill
 if($LASTEXITCODE){throw 'GitHub credential helper unavailable'}
 $credential=@{}
 foreach($line in $credentialLines){if($line.Contains('=')){$pair=$line.Split('=',2);$credential[$pair[0]]=$pair[1]}}
 if(!$credential['password']){throw 'No GitHub credential supplied'}
 $headers=@{Authorization='Bearer '+$credential['password'];Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28';'User-Agent'='TetherMax-Mobile-Release'}
 function RequestJson($method,$path,$body){
  try {Invoke-RestMethod -Method $method -Uri "https://api.github.com/repos/$repo/$path" -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([System.Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 8))) }
  catch{throw "GitHub $method $path failed; HTTP $([int]$_.Exception.Response.StatusCode)"}
 }
 $notes=Get-Content -Raw -Encoding utf8 docs/stability-rc10.md
 $apk='build/artifacts/tethermax-android-1.0.0-rc10.apk'
 $sum=(Get-FileHash $apk -Algorithm SHA256).Hash.ToLowerInvariant()+'  tethermax-android-1.0.0-rc10.apk'
 [System.IO.File]::WriteAllText((Join-Path $taskRoot 'build/artifacts/SHA256SUMS-stability-rc10.txt'),$sum+"`n"+(Get-FileHash build/artifacts/tethermax-windows-x64-1.0.0-rc10.msi -Algorithm SHA256).Hash.ToLowerInvariant()+"  tethermax-windows-x64-1.0.0-rc10.msi`n",[System.Text.UTF8Encoding]::new($false))
 $release=RequestJson 'Post' 'releases' @{tag_name=$tag;target_commitish=$Commit;name='TetherMax Stability & Performance · RC10';body=$notes;draft=$true;prerelease=$true;make_latest='false'}
 Write-Output "Created draft: $($release.html_url)"
 $uploadBase=$release.upload_url.Split('{')[0]
 if(([uri]$uploadBase).Host -ne 'uploads.github.com'){throw 'Unexpected upload destination'}
 $files=@($apk,'build/artifacts/SHA256SUMS-stability-rc10.txt','build/artifacts/tethermax-windows-x64-1.0.0-rc10.msi','build/reports/rc10-mobile.txt','build/reports/rc10-connection.txt','build/reports/rc10-network.txt','build/reports/rc10-go-tests.txt','build/reports/rc10-ui.txt','build/reports/rc10-hotspot.txt','build/reports/rc10-pairing.txt','build/reports/rc10-probes.txt','build/reports/rc10-msi-inspection.json','build/reports/rc10-linux-race.txt')
 foreach($file in $files){
  $name=Split-Path $file -Leaf
  try{$asset=Invoke-RestMethod -Method Post -Uri ($uploadBase+'?name='+[uri]::EscapeDataString($name)) -Headers $headers -ContentType 'application/octet-stream' -InFile $file}
  catch{throw "GitHub asset upload failed: $name; HTTP $([int]$_.Exception.Response.StatusCode)"}
  if($asset.size -ne (Get-Item $file).Length){throw "Asset length mismatch: $name"}
  if($asset.digest -and $asset.digest -ne ('sha256:'+(Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant())){throw "Asset hash mismatch: $name"}
  Write-Output "Verified upload: $name"
 }
 $published=RequestJson 'Patch' "releases/$($release.id)" @{draft=$false;prerelease=$true;make_latest='false'}
 Write-Output "Published: $($published.html_url)"
 $published.assets | Select-Object name,browser_download_url,digest
} finally {
 $credentialLines=$null;$credential=$null;$headers=$null
 Pop-Location
}
