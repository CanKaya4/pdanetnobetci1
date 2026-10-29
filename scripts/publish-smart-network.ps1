param([Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-f]{40}$')][string]$Commit,[ValidateSet('status','publish')][string]$Mode='status')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
Push-Location $taskRoot
try{
 $repo='CanKaya4/pdanetnobetci1';$tag='v1.0.0-rc.13-smart-network'
 $credentialLines="protocol=https`nhost=github.com`n`n" | git credential fill
 if($LASTEXITCODE){throw 'GitHub credential helper unavailable'}
 $credential=@{}
 foreach($line in $credentialLines){if($line.Contains('=')){$pair=$line.Split('=',2);$credential[$pair[0]]=$pair[1]}}
 if(!$credential['password']){throw 'GitHub credential unavailable'}
 $headers=@{Authorization='Bearer '+$credential['password'];Accept='application/vnd.github+json';'X-GitHub-Api-Version'='2022-11-28';'User-Agent'='TetherMax-Smart-Network-Release'}
 function Request([string]$method,[string]$path,$body=$null){
  $requestArgs=@{Method=$method;Uri="https://api.github.com/repos/$repo/$path";Headers=$headers}
  if($null -ne $body){$requestArgs.ContentType='application/json; charset=utf-8';$requestArgs.Body=[Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 8))}
  try{Invoke-RestMethod @requestArgs}catch{throw "GitHub $method request failed; HTTP $([int]$_.Exception.Response.StatusCode)"}
 }
 $runs=Request 'Get' "actions/runs?head_sha=$Commit&per_page=100"
 $validation=@($runs.workflow_runs | Where-Object {$_.name -eq 'Smart Network isolated validation' -and $_.head_sha -eq $Commit})
 $validation | Select-Object id,status,conclusion,html_url | ConvertTo-Json -Depth 4
 if($Mode -eq 'status'){return}
 if(!$validation.Count -or $validation[0].conclusion -ne 'success'){throw 'Successful Smart Network CI for exact source commit required'}
 $manifest=Get-Content -Raw -Encoding utf8 build/artifacts/rc13-release-manifest.json | ConvertFrom-Json
 if($manifest.sourceCommit -ne $Commit -or !$manifest.sourceClean){throw 'Clean exact-commit package provenance required'}
 $sum=@()
 foreach($asset in $manifest.assets){
  $file=Join-Path 'build/artifacts' $asset.name
  $hash=(Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
  if($hash -ne $asset.sha256 -or (Get-Item $file).Length -ne $asset.bytes){throw 'Package differs from built manifest'}
  $sum+="$hash  $($asset.name)"
 }
 $sum | Set-Content -Encoding ascii build/artifacts/SHA256SUMS-smart-network-rc13.txt
 $validation | Select-Object id,head_sha,status,conclusion,html_url | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 build/reports/rc13-cloud-ci.json
 $notes=(Get-Content -Raw -Encoding utf8 docs/smart-network-rc13.md)+"`n`nExact source: ``$Commit`` · [Passed isolated cloud validation]($($validation[0].html_url)).`n"
 $release=Request 'Post' 'releases' @{tag_name=$tag;target_commitish=$Commit;name='TetherMax Smart Network · RC13 · İlk Aşama';body=$notes;draft=$true;prerelease=$true;make_latest='false'}
 $uploadBase=$release.upload_url.Split('{')[0]
 if(([uri]$uploadBase).Host -ne 'uploads.github.com'){throw 'Unexpected upload host'}
 $files=@($manifest.assets | ForEach-Object {Join-Path 'build/artifacts' $_.name})
 $files+=@('build/artifacts/rc13-release-manifest.json','build/artifacts/SHA256SUMS-smart-network-rc13.txt','docs/smart-network-rc13.md','docs/ultimate-architecture-2026-10-30.md')
 $files+=@(Get-ChildItem build/reports/rc13-* -File | Where-Object {$_.Extension -in '.txt','.json','.png'} | ForEach-Object FullName)
 foreach($file in $files){
  $name=Split-Path $file -Leaf
  try{$asset=Invoke-RestMethod -Method Post -Uri ($uploadBase+'?name='+[uri]::EscapeDataString($name)) -Headers $headers -ContentType 'application/octet-stream' -InFile $file}
  catch{throw "GitHub asset upload failed: $name; HTTP $([int]$_.Exception.Response.StatusCode)"}
  if($asset.size -ne (Get-Item $file).Length){throw "Uploaded asset length mismatch: $name"}
  if($asset.digest -and $asset.digest -ne ('sha256:'+(Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant())){throw "Uploaded asset digest mismatch: $name"}
  Write-Output "Verified upload: $name"
 }
 $published=Request 'Patch' "releases/$($release.id)" @{draft=$false;prerelease=$true;make_latest='false'}
 Write-Output "Published: $($published.html_url)"
 $published.assets | Select-Object name,browser_download_url,digest | ConvertTo-Json -Depth 4
}finally{$credentialLines=$null;$credential=$null;$headers=$null;Pop-Location}
