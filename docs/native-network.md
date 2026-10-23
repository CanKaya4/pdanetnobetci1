# 0.5 native Windows ağ motoru

Bu kaynak mevcut TetherMax projesine gerçek backend ekler. Kullanıcının
bilgisayarında çalıştırılmadı. Başarılı native CI kaydı ve fiziksel test kaydı
ayrı kabul kriterleridir; uygulama operatörün kota sınıflandırmasını belirlemez.

## Veri yolu

Windows uygulaması → yeni Wintun adaptörü → mevcut Go/gVisor TCP/UDP motoru →
eşleştirilmiş localhost ADB forward → Android localabstract relay →
`TRANSPORT_CELLULAR` Network üzerindeki yeni TCP/UDP soketleri.

DNS Windows NRPT/DNS istemcisinden 1.1.1.1'e yönlendirilir ve aynı IPv4 akış
motorundan geçer. TCP DNS de sıradan TCP olarak taşınır. Windows IP paketleri
Android tethering NAT'ına verilmez. PdaNet kurulumu/kodu gerekmez. TTL veya TLS
başlık dönüşümü uygulanmaz. Android'de standart paylaşımın gerçekten kapalı
olduğu uygulamanın sayaçlarından anlaşılamaz; ayrıca kullanıcı gözlemi gerekir.

## Gerçek uygulama

* `TetherMax.Network`: yükseltilmiş guardian, Windows NetTCPIP/DnsClient
  cmdlet backend'i ve native `fwpuclnt.dll` WFP API'si. Salt dry-run değildir.
* Yeni Wintun'u Go `ip-worker` açar; eski adaptör açılmaz. GUID/LUID ve taze
  `TetherMax-IP-*` adı doğrulanır. Resmî DLL SHA256 ve Authenticode kontrolü
  korunur. ADB sıfırlanmaz; seçili USB forward GUI'nin mevcut eşleştirmesidir.
* Yönetici/SYSTEM dışında erişimi olmayan ProgramData oturum klasöründe
  `intent.json`, `adapter.jsonl`, bağımsız watcher onayı ve heartbeat bulunur.
  Token yalnızca özel IPC/stdin'de ve süreç belleğinde kalır; journal/argv'ye yazılmaz.
* WFP atomik işlemi önce kurulur: non-loopback IPv4 yalnızca sahip olunan LUID
  üzerinden çıkabilir; non-loopback IPv6 engellenir. Dış adaptöre bağlanmış
  uygulamalar ve daha özel LAN/RNDIS rotaları bu sayede başarısız olmalıdır.
  WFP işlemi veya native negatif test geçmezse bu özellik doğrulanmış sayılmaz.
* Sonra yalnızca yeni adaptöre `10.254.88.2/30`, MTU1500, metrik1 atanır;
  `0.0.0.0/1` ve `128.0.0.0/1` on-link rotaları ActiveStore'a eklenir.
  Eski default rotalar silinmez, eski adaptörün DNS/IP/metriği değiştirilmez.
* Yeni adaptöre DNS1.1.1.1 ve benzersiz oturum etiketli geçici NRPT `.` kuralı
  eklenir. Önceki NRPT kuralları saklanır; GPO/politika çakışması gerçek
  Windows DNS testinde anlaşılmalıdır. DNS önbelleği temizlenir.
* Uygulama/guardian/worker ölümü veya heartbeat kesilmesinde ayrı watcher
  temizlik yapar. Watcher ölürse yaşayan guardian temizler. Sıra: oturumun
  DNS/rota/IP nesneleri → worker/adapter → WFP filtreleri → tamamlandı kaydı.
  Hata varsa WFP kaldırılmaz. CIM alt süreci de kendi OS kilidini tutar;
  geç başlayan eski işlem kurtarma tamamlandıktan sonra tekrar ayar uygulayamaz.

WFP filtreleri dinamik oturum filtresi değildir: guardian/watchdog ikisi de
ölürse engel sessizce düşmez. Ancak otomatik iyileşme bu çift arızada garanti
edilmez. Windows SCM servisi veya boot görevi kurulmaz; reboot/BFE restart
kurtarması fiziksel olarak doğrulanmadı. Bu durum için yöneticiyle manuel
`TetherMax.Network.exe recover <korunan-oturum-klasörü> --approve-network-recovery`
vardır. Bu komut da ayrı yetkilendirme olmadan çalıştırılmaz.

## İzole test sınırı

Yerel `dotnet run --project windows/TetherMax.NetworkTests -c Release` yalnızca
mock OS backend, ayrı süreç ölümü, heartbeat, rollback arızası ve ABI kontrollerini
çalıştırır. Windows ağını değiştirmez. Testlerin native modu hem GitHub-hosted
runner ortamını hem yönetici yetkisini arar; kullanıcı PC'sinde çalıştırılmaz.

Native CI gerçek Wintun ve Windows yönlendirmesi kullanır. Java fixture yalnızca
testte benchmark adreslerini localhost'a eşler; üretim APK'sında bu fixture yoktur.
Fixture'ın CELLULAR yazısı gerçek Android kanıtı değil, açıkça mock attestasyondur.
HTTPS, UDP ve Windows DNS bunun üzerinden sınanır. İkinci sahip olunan Wintun,
yalnızca negatif kaçak testinin tuzağıdır: dış ağa paket gönderilmez. Önce tuzak
yolunun çalıştığı, guard sırasında IPv4/IPv6'nın engellendiği ve rollback sonrası
yolun geri geldiği kontrol edilir.

Test VM'sinde acil rescue gerekirse sonuç başarısızdır. Rescue yalnızca izole
test executable'ındadır; ürün paketinde yoktur ve üretim fallback'i değildir.

## Ayrı fiziksel geçiş onayı için somut kapsam

1. Önceki route/adaptör/DNS snapshot'ı alınır. İlk geçişte Ethernet 5 ve telefonun
   mevcut USB paylaşımı kapatılmaz. WFP mevcut sohbetin de çıkışını geçici kesebilir.
2. WPF düğmesi tam yukarıdaki IP, iki /1 rota, geçici DNS kuralı ve WFP engelini
   açıklar. Ayrıca Windows UAC görünür; açık kullanıcı onayı gerekir.
3. İlk deney en çok 120 saniye ve toplam 1 MiB relay payload/framing bütçelidir;
   tüm eşzamanlı akışlar ortak bütçeyi kullanır. Bunun tamamının tüketilmesi
   garanti değildir. TCP/IP ek yükleri, Android çözümleyici, yeniden iletim ve
   Windows imza doğrulama hazırlığı operatör faturalandırma baytı değildir.
   Bu yüzden 1 MiB bir fatura üst sınırı iddiası değildir.
4. Aynı anda proxy kullanan eski SOCKS worker/adaptif sorgular durdurulur.
   Normal tarayıcı HTTPS, UDP ve DNS örnekleri küçük tutulur. Süre/bütçe dolması,
   GUI kapanması veya worker arızası kurtarmayı tetikler. Kurtarma hatasında
   ağ engelli kalabilir; otomatik başarı varsayılmaz.
5. Sonra eski route/adaptör/DNS ile karşılaştırılır; bu aşama henüz hotspot-off
   kanıtı değildir. İkinci ayrı aşamada kullanıcı standart Android paylaşımını
   kapatır. Tek internet RNDIS ise bu ayrıca sohbeti kesebilir; kullanıcı geri
   açma yolunu hazır tutmalı veya bağımsız iletişim bağlantısı bulundurmalıdır.
   Bu belge Samsung/RNDIS/ADB'yi kapatmak için mevcut onay sayılmaz.

Host örnekleri `host-counters.jsonl`, Android önce/sonra kontrol sayıları
`android-before.json`/`android-after.json`, test bütçesi/zamanı `measurement.json`
olarak ayrı kaydedilir. Ani process kill'de son host örneği eksik kalabilir;
periodik örnek, operatör muhasebesiyle eşit veya kesin tam toplam değildir.
Android sayaçları tüm relay oturumlarını kapsar; paralel istemci olmamalıdır.
