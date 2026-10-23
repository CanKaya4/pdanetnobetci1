# TetherMax 0.5 native teslimatı — 24 Ekim 2026

Gerçek Windows backend'i eklendi; kullanıcı bilgisayarında canlı geçiş henüz
yapılmadı. Ethernet 5'in varsayılan çıkış olduğu salt okunur gözlemle korundu.
Bu teslimat Türk Telekom hotspot kotasının etkilenmediğini kanıtlamaz.

## Gerçek kod

`windows/TetherMax.Network` artık native WFP filtreleri ve Windows
NetTCPIP/DnsClient API'leri üzerinden yeni Wintun IP/rota/DNS yapılandırması
yapar. Eski adaptörün IP, DNS ve default rotası değiştirilmez. Yeni adaptör
GUID/LUID ile sahiplenilir; sadece onun IP/rota nesneleri ve oturumun benzersiz
DNS/WFP nesneleri geri alınır. `route-plan` inceleme komutu dry-run kalır;
gerçek bağlantı için ayrı, yükseltilmiş guardian kullanılır.

Guardian, Go trafik worker'ı ve bağımsız watcher farklı süreçlerdir. Kalıcı
intent, atomik hazır kaydı, private heartbeat, PID/başlangıç zamanı ve sahip
olunan CIM yorumlayıcısının kurtarılması uygulanmıştır. Geri alma tamamlanmazsa
kalıcı WFP engeli bırakılır; RNDIS'e sessiz başarı fallback'i yapılmaz.
WFP IPv4 çıkışını sahip olunan Wintun ile sınırlar; dış IPv6 engellenir.

WPF'de **Sistem geneli USB testi** tek akış içinde güvenli USB eşleştirmesini
ve ardından ayrı ağ onayı/UAC'yi kullanır. Test 120 saniye/1 MiB relay
payload/framing ile sınırlıdır; protokol ek yükleri bir fatura üst sınırı
iddiasına dahil değildir. Eski SOCKS/adaptif sorgular bu testte durdurulur.
Normal bitişte Windows ve Android ayrı sayaç makbuzu yerel reports klasörüne
gönderilir; ani çökmede son host örneği eksik olabilir. Token argv veya journal'a yazılmaz.

Temizleme Windows SCM servisi/boot görevi kurmaz. İki kurtarma aktörünün birlikte
ölümü veya reboot sonrasında otomatik kurtarma garantisi yoktur; kalıcı engel
ve yöneticiyle, ayrı onaylı manuel recover komutu vardır. PdaNet, ICS ve
MyPublicWiFi yönetilmez. Süresiz motor modu core API'sinde vardır; ilk fiziksel
doğrulama için GUI yalnızca sınırlı geçiş testini sunar.

## Geçen kanıtlar

* [Tam native Windows CI, 0ea2ed0](https://github.com/CanKaya4/pdanetnobetci1/actions/runs/40024687010):
  gerçek Wintun → gVisor → loopback Java relay üzerinden her biri 256 KiB
  sertifika pinli HTTPS, 1200 bayt UDP echo ve Windows sistem DNS çözümü.
* Aynı CI'de ikinci sahip olunan Wintun üzerinden çalışan alternatif yol önce
  doğrulandı, koruma sırasında IPv4/IPv6 kaçağı engellendi, sonra geri geldi.
* GUI, guardian, worker ve watcher ayrı ayrı öldürüldü. Her senaryo sonunda
  WFP filtrelerinin yokluğu, oluşturulan adaptörün kaldırılması ve başlangıçtaki
  adaptör/IP-arayüzü/rota/DNS/NRPT snapshot'ının birebir geri gelmesi doğrulandı.
* Atomik journal/hazır yayınlama düzeltmesinin
  [dbc1f49 native CI'si de geçti](https://github.com/CanKaya4/pdanetnobetci1/actions/runs/40025260623).
  Test VM'sinin acil rescue yardımcısı gerekmedi; rescue gerektiren denemeler
  başarısız sayıldı. Bu yardımcı ürün paketinde yoktur.
* Yerel 39 Go testi ve vet, 7 offline kota analiz testi, 10 network test grubu
  geçti. Network grupları gerçek Windows PowerShell'de bellek içi CIM komutları,
  atomik dosya değişimi/yayınlama ve ayrı süreç çökme senaryolarını kapsar;
  kullanıcı bilgisayarının ağ yapılandırmasını değiştirmez.
* APK yeniden derlendi; v2/v3 imza doğrulaması geçti. WPF ve guardian x64
  self-contained yayımlandı. MSI paketleyicisi runtime ve resmî Wintun pinini
  doğruladı. Paketler kurulmadı, native helper kullanıcı PC'sinde başlatılmadı.

Native CI'deki CELLULAR bildirimi açıkça fixture mock'udur; gerçek telefon,
USB kablosu veya operatör kanıtı değildir. Önceki A56 hücresel memory-IP
doğrulamaları yeni native aktarımın fiziksel doğrulaması olarak sayılmadı.

## Operatör ölçümü

Araç A=telefon, B=RNDIS, C=PdaNet, D=TetherMax kayıtlarını ayrı işler. PdaNet
sürümü/Hide Tether Usage ayarı, onaylanan bütçe, bilinen örnek boyutu, seçili
yol, Windows/Android sayaçları, sayaç çözünürlüğü ve gecikmeli okumalar tutulur.
Görünmeyen sayaç `null` olarak kalır; sıfır kabul edilmez. Çözünürlüğün altındaki
küçük örnek, hareketli/gecikmiş sayaç, reset veya eksik kanıt belirsizdir.
Araç kendi başına indirme yapmaz ve operatör muhasebesini otomatik onaylamaz.

## Üretilen paketler

* `build/artifacts/tethermax-android-pairing-preview.apk` — geliştirme imzalı APK,
  sürüm kodu4 / `0.5.0-usb-preview`.
  SHA256 `4b1d3fdd4781468e0f4742a18ae9abc06d52d02f936b40dc6ae40d4f6029697b`.
* `build/artifacts/tethermax-windows-x64-native-050-preview.msi` — per-user,
  self-contained x64 geliştirme önizlemesi; MSI Authenticode imzasızdır.
  SHA256 `19759b93ca554336a27974a96279db2d13bb3a0de29a8ff1fcf7bc6cbccf0610`.

## Canlı kabul durumu ve bekleyen onay

| Ölçüt | Sonuç |
|---|---|
| Windows sistem geneli TetherMax bağlantısı | İzole Windows'ta ÇALIŞIYOR; kullanıcı PC'sinde henüz etkin değil |
| Standart Android hotspot kapalıyken internet | Doğrulanmış çalışan canlı bağlantı henüz yok; fiziksel test yapılmadı |
| Türk Telekom hotspot sayaç sonucu | BELİRSİZ |
| PdaNet ile kıyaslanan gerçek davranış | ÖLÇÜLMEDİ |

İlk canlı aşama için ayrı onay gerekir: mevcut Android servisini kullanarak
USB/ADB eşleştirme, resmî Wintun sürücüsü/ayrı adaptör, `10.254.88.2/30`, iki
`/1` rota, geçici DNS kuralı ve IPv4/IPv6 WFP korumasıyla 120 saniye / 1 MiB
relay payload testi. Ethernet 5, Wi-Fi, PdaNet ve standart Android paylaşımı
bu ilk aşamada kapatılmaz. Geri alma eski çıkışı geri getirir; hata durumunda
manuel kurtarma gerekebilir. Bu aşama için henüz onay alınmadı.

Sonraki hotspot-off ve A/B/C/D veri tüketim deneyleri, kendi bütçeleri ve ayrı
onaylarıyla yapılmalıdır. [Somut plan](native-network.md) ve
[sayaç ölçüm prosedürü](controlled-usage-study.md) aynı projededir.
