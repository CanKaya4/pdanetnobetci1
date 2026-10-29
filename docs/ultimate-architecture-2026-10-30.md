# TetherMax Ultimate: mimari, araştırma ve aşamalı kabul

30 Ekim 2026 · RC13 ilk aşama. Bu belge tamamlanmış sekiz özellik ilanı değildir.
Kaynak incelemesi, uygulanmış değişiklikler ve yapılması gereken iş ayrılır.

## İncelenen çalışan temel

Bu çalışma alanında üç farklı tarihsel proje var: kökteki Nöbetçi, `tethermax/`
altındaki önceki prototip ve `fix-session/` altındaki güncel Go/Java/.NET ürün.
Geliştirme üçüncü checkout'ta yapıldı. Başlangıçta zaten değiştirilmiş RC12
tasarım, oyun odağı ve güvenilirlik dosyaları vardı; silinmedi. RC13 paketleri
bu mevcut çalışma ağacını ve yeni Smart Network değişikliklerini birlikte içerir.

Güncel veri yolu:

```
Windows uygulamaları / mevcut paylaşım
  → Wintun IPv4 → host-fair paket kuyrukları / CoDel
  → gVisor TCP/UDP → kimliği doğrulanmış USB relay
  → Android CELLULAR Network'e bağlanan soketler → internet
```

SOCKS yolu da mevcut. Windows ağ yapılandırmasını ayrı yönetici guardian işlemi
yapar; günlüğe alınan değişiklikleri watchdog geri alır. ADB forward sahipliği
seri numarası, yerel port ve uzak soket adıyla doğrulanır. Android servisi hücresel
ağ değişimlerini dinler, akış hatalarını izole eder, USB dinleyicisini geri açar.
Telefon tek başına Windows'un uçtan uca sağlıklı olduğunu varsaymaz. Mevcut
özelliklerin ayrıntıları `stability-rc10.md`, `native-network.md`, `pairing-v2.md`
ve `competitive-game-mode-2026-10-29.md` belgelerinde.

Android soketlerinin aktarım katmanı ve hücresel ağ seçimi bu aşamada değiştirilmedi.
APN/TTL, standart Android tethering, hotspot veya operatör politikası değiştirilmedi.
Hotspot kotasından muafiyet doğrulanmadı; vaat edilmez.

## Ürün karşılaştırması ve kararlar

| Çözüm / birincil kaynak | Yararlı yöntem | TetherMax kararı |
|---|---|---|
| [Speedify çalışma biçimi](https://support.speedify.com/article/273-how-speedify-works) ve [sunucusuz mod](https://support.speedify.com/article/892-can-i-use-speedify-without-vpn-server) | Karşı uçta birleştirme, bağlantı rolleri, farklı hız/güvenilirlik tercihleri | Yerel akış dengelemesi ve tünelli bonding ayrı ürün yetenekleri olacak; yerel rota değişimi oturum devamlılığı gibi sunulmayacak. |
| [Peplink SpeedFusion](https://www.peplink.com/technology/multi-wan-cellular-bonding-technologies/) ve [teknik belge](https://download.peplink.com/resources/whitepaper-speedfusion-and-best-practices-2019.pdf) | Karşılıklı tünel, hot failover; maliyeti olan paket yedekliliği | Oturum koruma için ortak çıkış IP'si sağlayan uç gerekli; yedeklilik veri maliyetini artırır. |
| [Firewalla Smart Queue](https://help.firewalla.com/hc/en-us/articles/360056976594-Firewalla-Feature-Smart-Queue) | Akış adaleti, şekillendirme, kullanıcı kuralları | Yönetilen darboğazdaki gerçek kuyruk mekanizması korunur. Telefonun kendi uygulamalarından gelen, motordan geçmeyen trafik kontrol edilmiş sayılmaz. |
| [NetLimiter trafik kontrolü](https://netlimiter.com/docs/netlimiter-overview/features/full-traffic-control) | Uygulama sınırları, öncelikler ve kota ayrı araçlardır | CPU EcoQoS ile ağ kotası karıştırılmaz. Uygulama kimliği doğrulanamayan akışlar bilinmeyen olarak kalır. |
| [FQ-CoDel RFC 8290](https://www.rfc-editor.org/rfc/rfc8290.html) | Akış izolasyonu, kısa kuyruklar, düşük hızda MTU iletim süresine uygun hedef | Windows motoru kendi host-fair CoDel uygulamasını kullanır; Linux CAKE veya standart FQ-CoDel'in birebir uygulandığı iddia edilmez. RC13 düşük hız hedefini düzeltir. |
| [cake-autorate](https://github.com/lynxthecat/cake-autorate/blob/master/README.md) | Değişken kapasitede gerçek yük ve gecikmeye dayalı kontrol | Linux betiği Windows'a kopyalanmaz. Birden fazla bağımsız ölçüm hedefi ve hücresel saha kalibrasyonu sonraki aşamadır. |

DNS sorgu RTT'si, oyun sunucusu RTT'si değildir. DNS hizmetinin yavaşlaması,
operatör kuyruğu, USB taşıma gecikmesi ve radyo problemi farklı etkiler yaratır.
Mevcut adaptif motorun tek ölçüm türünden gerçek kapasiteyi kesin çıkardığı
iddia edilemez. Yük yönü, gerçek byte hızı ve yerel backlog kanıtı korunmalıdır.

## RC13: yeni çalışan kapsam

### DataShield ilk sürümü

- Windows relay payload sayaçları artık rastgele motor oturum kimliği ve artan
  örnek sıra numarasıyla gelir. Aynı/geri gelen örnek iki kere sayılmaz. Sayaç
  azalması reddedilir. Önceki 16 oturumdan gecikmiş kayıtlar reddedilir.
- Windows geçmişi `usage-v1.json` içinde mevcut atomik yazma ve okunabilir önceki
  kayıt yedeğiyle saklanır. Disk yazması UI dışında yapılır; aynı anda yalnızca
  bir yazma olur. Bozuk ana kayıt ve yedek tespit edilirse geçmiş sessizce sıfırlanmaz.
- Android relay verisi Activity'den bağımsız, servis bakım döngüsünde sayılır;
  SharedPreferences ile saklanır. Çalışma anındaki Android disk dayanıklılığı ve
  bildirim davranışı fiziksel telefonda henüz doğrulanmadı.
- Her iki arayüzde aylık GB bütçesi, %80 ve %100 uyarısı bulunur. Windows tepsi
  uyarısı, Android bildirim kanalı kullanılır; Android bildirim iznine bağlıdır.
  Bütçe interneti durdurmaz. Varsayılan bütçe 0, uyarı kapalı.
- Ay sınırı UTC takvim ayıdır; sınırı geçen örnek aralığının farkı yeni aya
  yazılır. Saatin geriye gitmesi mevcut ay tüketimini silmez. İki cihazın bütçe
  ayarları bağımsızdır, otomatik eşitlenmez.
- Windows/Android aynı veriyi iki uçtan gözler; toplamları toplanmaz. Bu sayaçlar
  tüm Windows/telefon uygulamalarını kapsamaz ve operatör faturası değildir.
- Kontrol noktası hedefi 30 saniyedir; OS zamanlaması/disk gecikmesi bu aralığı
  uzatabilir. Ani süreç kaybı veya telemetri boşluğunda tam faturalandırma doğruluğu
  garanti edilmez. Android `apply()` asenkron olduğundan disk flush garantisi yoktur.
- IP kuyruklarında çıkan paketler kaynak/dönüş hedef IPv4 adresiyle ayrı sayılır.
  En fazla 128 adres tutulur, taşan adresler ortak byte sayacına gider. UI ilk 16'yı
  gösterir. Kuyruğa girip düşen/silinmiş paketler sayılmaz. Sonraki katmana teslim
  başarısı garanti edilmez. IP başlıkları ve retransmission bu sayaca dahildir.
  Relay payload sayacıyla toplanmaz; fiziksel cihaz faturası olarak etiketlenmez.
- Kaynak IP tablosu telefon telemetrisinden çıkarılır; Windows'ta bellekte kalır.
  IP adresi/MAC/cihaz adı eşitliği varsayılmaz; ICS NAT kimliği gizleyebilir.

### Network Doctor ilk sürümü

Windows Analiz ekranında güncel telefon ağ bildirimi, eksik DNS ölçümü,
yeterli örnekle yanıtsız DNS oranı, yerel kuyruk beklemesi, tarihsel overflow
ve NAT görünürlüğü açıklanır. Telemetri 8 saniyeden eskiyse güncel teşhis
gösterilmez (5 saniyelik UI kontrolünde güncellenir). Kesin donanım arızası
teşhisi veya uçtan uca sağlık sertifikası verilmez. Ağ ayarı otomatik değiştirilmez.
Mevcut onaylı tanılama/geri alma işlemleri aynı kalır.

### ZeroLag düzeltmesi

RFC 8290 §5.2.2 doğrultusunda CoDel hedefi en az bir MTU/paketin mevcut hız
tavanında iletim süresini karşılar. Düşük tavanlarda normal iletim gecikmesi
bufferbloat sanılarak gereksiz düşürme yapılması önlenir. Sınırsız profilde 5 ms
varsayılanı korunur. Bu değişiklik için saf zaman modeli ve mevcut kuyruk
regresyon testleri vardır; Valorant pingi veya gerçek 5G kazancı ölçülmedi.

## Sekiz hedefin durumu ve geliştirme sırası

| Hedef | Bugün gerçekten bulunan | Bir sonraki kabul kapısı |
|---|---|---|
| ZeroLag Home | Görünür IP başına adalet, akış önceliği, CoDel, deneysel adaptif tavan; RC13 düşük hız düzeltmesi | Onaylı fiziksel yük deneyinde aynı koşullarda oyun RTT p50/p95/p99, kayıp, video stall, kapasite düşüşü ve CPU ölçümü. |
| NeverOffline | USB listener retry, akış hata izolasyonu, sahipli forward onarımı, guardian/watchdog geri alma | Farklı telefonlarda kablo çıkar/tak, hücresel gidip gelme, sleep/resume, worker/UI crash ve 24–72 saat döngüsü; mevcut TCP oturumunun kaybı ayrı raporlanmalı. |
| DataShield | Bu aşamadaki kalıcı relay sayacı, bütçe uyarısı ve sınırlı IP tablosu | Windows doğrulanmış süreç muhasebesi; Android kullanıcı izinli UID istatistikleri; NAT öncesi fiziksel cihaz görünürlüğü; ölçüm boşluğu ve billing eşleştirmesi. |
| Network Doctor Pro | Mevcut read-only ağ raporu, testler ve yeni telemetri açıklamaları | Her katman için zaman damgalı bağımsız kanıt; güven derecesi, hata enjeksiyonu ve yalnızca sahipliği doğrulanmış onarım eylemleri. |
| MultiLink | Mevcut motor tek seçili telefon/relay kullanır; bu aşamada çoklu telefon eklenmedi | Önce bağımsız LinkSession ve loopback iki relay; yeni akışların hat seçimi, metered bütçe ve histerezis; sonra onaylı iki gerçek hat. |
| RemoteHome | Yeni uzaktan yönetim servisi yok | Cihaz anahtarları, karşılıklı doğrulama, dışarı doğru TLS bağlantısı, rol/izin, iptal, replay koruması ve sınırlı komut protokolü; ilk sürüm salt okunur. |
| Business Gateway | Mevcut yerel kurtarma altyapısı | Kalıcı bağlantı olay geçmişi, gözlenen kesintiyle telemetri boşluğunu ayırma, birden fazla hat ve bildirim teslim doğrulaması. |
| Premium | Yeni lisans/abonelik sistemi yok; ücretli özellik varmış gibi kilit eklenmedi | Aşağıdaki paket sınırları, imzalı yetki belgesi ve test edilmiş ödeme backend'i. |

Öncelik: mevcut bağlantının uzun süreli fiziksel kabulü → DataShield kimlik
doğruluğu/Doctor kanıt modeli → yerel çoklu hat → güvenli salt okunur uzaktan
izleme/Business olayları → tünelli oturum koruma → ticari lisanslandırma.
Ölçüm kabulü geçmeyen bir aşama sonraki sürümde bitmiş diye sunulmaz.

## MultiLink ve RemoteHome mimarisi

Her hat kendi USB forward, pairing trust, relay, sayacı, health state ve yaşam
döngüsüne sahip olmalı. Seçici yeni akışı doğrulanmış sağlıklı hatta sabitler.
Bir hattın düşmesi diğerini kapatmamalı. Geri dönüş için çoklu başarılı örnek,
minimum kalış süresi ve sınırlı retry gerekir. Rota değişikliği TCP durumunu
başka IP'ye taşımaz; kullanıcıya hangi oturumların yeniden kurulacağı gösterilmeli.

Gerçek tek-akış bonding/oturum devamlılığı için istemci ile bölgesel çıkış sunucusu
arasında şifreli tünel ve sabit çıkış kimliği gerekir. UDP tabanlı taşıma adayı,
sıralama/yeniden birleştirme, MTU keşfi, replay koruması, bounded buffer ve anahtar
yenileme testleriyle seçilmeli. QUIC kütüphanesi eklemek tek başına IP bonding
sağlamaz. Trafiği paket çoğaltarak koruma varsayılan kapalı olmalı; mobil veri
maliyeti açıkça ölçülmeli. Sunucu konumu ek RTT getirir; önce Türkiye/Avrupa
gecikmesi ve çıkış trafik maliyeti ölçülmeli. Bulut kaynağı bu aşamada satın alınmadı.

RemoteHome internete açık yerel yönetici paneli açmamalı. Cihaz dışarı doğru
kimliği doğrulanmış kanal kurmalı; kullanıcı hesabı cihaz eşleştirmesinden ayrı
olmalı. Uzaktan komutlar şema/izin/TTL/request ID ve tekrar yürütme korumasıyla
tek tek sınırlandırılmalı. Shell, genel PowerShell veya serbest ADB komutu API
olarak sunulmamalı. Varsayılan veri yalnızca durum/özet; trafik içeriği, pairing
sırrı, SSID parolası veya ziyaret geçmişi buluta gönderilmemeli.

## DataShield uygulama kimliğinin sınırları

[Microsoft ALE](https://learn.microsoft.com/en-us/windows/win32/fwp/application-layer-enforcement--ale-)
uygulama kimliğine göre sınıflandırma sağlar; doğru byte muhasebesi ayrıca uygun
akış/packet katmanını, process start identity ve UDP wildcard davranışını ister.
Mevcut IP helper süreç eşleştirmesi best-effort'tur; NetLimiter eşdeğeri genel
Windows uygulama kontrolü olduğu iddia edilemez. Özel sürücü seçimi imzalama,
Windows uyumluluğu ve crash yüzeyiyle birlikte ayrı değerlendirilmelidir.

[Android NetworkStatsManager](https://developer.android.com/reference/android/app/usage/NetworkStatsManager)
başka uygulamalar için kullanıcıdan Usage Access yetkisi gerektirir; veriler zaman
kovalarıyla gelir ve sorgular UI thread'inde çalışmamalıdır. Root'suz uygulama
telefonun diğer uygulamalarına serbestçe hücresel ağ kotası uygulayamaz. VPNService
seçeneği mevcut VPN ile çakışma ve farklı ağ yolu etkisi içerir; bu aşamada eklenmedi.

## Premium paket taslağı — henüz ticari uygulama yok

- Free: güvenilir tek telefon USB aktarımı, güvenli durdurma/geri alma, temel
  teşhis ve kendi veri sayacı. Süre sınırı veya mevcut güvenliği ücretli kilide
  bağlama yok.
- Pro: ölçülerek kabul edilmiş adaptif QoS, süreç politikaları, daha uzun geçmiş,
  gelişmiş yerel teşhis ve yerel yedek hat politikaları.
- Family: görünürlüğü gerçekten sağlanan ev cihazları için politikalar, aile
  yöneticileri, sınırlı salt okunur uzaktan durum ve bütçeler.
- Business: çoklu gateway/hat, rol temelli yönetim, olay geçmişi, denetlenebilir
  bildirimler; oturum koruma bulut çıkış bedeliyle ayrı ve açık kapsamda.

Yetkiler istemcideki `isPro` alanına güvenmemeli. Sunucu özel anahtarı yalnızca
backend'de kalmalı; istemci gömülü açık anahtarla imzalı süreli yetki belgesini
doğrulamalı. Key rotation, offline grace, saat geri alma, cihaz kaybı, iptal ve
yeniden yükleme testleri gerekir. Ödeme webhook'u imza doğrulaması ve idempotency
ile işlenmeli. Lisans hizmeti kesilince kullanıcının çalışan USB interneti
kapatılmamalı. İmzalama/ödeme hesapları ve ürün politikaları kurulmadan güvenli
abonelik tamamlandı denilemez. Fiyat veya SLA bu aşamada vaat edilmedi.

## Fiziksel kabul matrisi

Her testte APK/MSI SHA-256, telefon/Android/OEM, Windows build, modem erişim türü,
saat, sıcaklık ve pil optimizasyonu kaydedilmeli. En az Samsung ve farklı bir OEM;
desteklenen Windows 10/11; 4G/5G; ekran açık/kapalı; USB hub/kablo varyasyonları.

1. Mevcut sürümle kontrollü baseline; aynı yer/saat/yük ve oyun sahnesi.
2. RC13 ile önce boşta, sonra TV/video + indirme + oyun benzeri UDP yükü.
3. Kapasitenin düşüp artması; upload/download ayrı darboğaz; DNS hizmeti
   yavaşlamasıyla hücresel kaybın ayrı enjekte edilmesi.
4. Kablo çıkar/tak, kilit/Doze, Windows sleep/resume, uygulama ve worker kill.
5. 24–72 saat idle/yük döngüsü; bellek, thread/socket sayısı, handles, CPU,
   pil/termal ve sayaç drift takibi. Sabit toleranslar baseline'dan sonra belirlenir.
6. Mevcut hotspot ve günlük kullanım düzenine geri dönüş; kota muafiyeti için
   ayrı kontrollü operatör hesabı karşılaştırması. Muafiyet varsayılmaz.

Bu görevde fiziksel ADB, kurulum, sürücü yükleme veya OS ağ değişikliği yapılmadı.
`AGENTS.md` bu aşama için ayrı açık onay istiyor. Build ve loopback sonuçları
gerçek telefon kabulünün yerine geçmez. Güncel sonuçlar `smart-network-rc13.md`.
