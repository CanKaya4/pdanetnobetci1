# Android oyun desteği — RC11, 29 Ekim 2026

Bu sürüm PC'deki rekabetçi oyunlar için telefonu **USB hücresel aktarım motoru**
olarak optimize eder. Telefonda çalışan başka oyunları/uygulamaları yakalayan
bir Android VPN'i, root aracı veya sistem geneli hızlandırıcı değildir.

## Yeni davranış

- Windows'un mevcut, kimliği doğrulanmış USB telemetrisindeki
  `gameFocusActive=true` veya etkin `gaming` profili Android'e kısa oyun
  oturumu bildirir. Windows'ta seçilen gerçek EXE'ler kullanılmaya devam eder.
  Yerel Windows PID'leri/yolları yeni mesaj olarak telefona gönderilmez.
  Önceki turda hazırlanan Rekabetçi Oyun Windows paketiyle çalışır; protokolün
  akış başlığı, portları ve kimlik doğrulaması değişmedi.
- Android **Profiller → Android oyun desteği** anahtarı varsayılan açık,
  kullanıcı tarafından kapatılabilir. Elle Oyun profili seçildiğinde daha küçük
  aktarım blokları ve azaltılmış görseller de devreye girer.
- Oyun sırasında TCP kopyalama bloğu 16 KiB yerine en fazla 4 KiB olur.
  Bu, tek yazmada biriktirilen veriyi sınırlar; fiziksel USB gecikme kazancı
  henüz ölçülmemiştir. UDP datagramları bölünmez, birleştirilmez veya kırpılmaz.
- Windows'tan güncel bir pozitif yükleme tavanı geliyorsa, **1200 bayttan büyük
  TCP yükleme yazmaları** telefonun hücresel soketine yazılmadan önce ortak
  token bütçesinden geçer: Windows tavanının %85'i, en fazla 4096 bayt başlangıç
  patlaması. Bütçe bütün uygun TCP akışları arasında ortaktır; akış başına
  ayrı %85 verilmez. Kalp atışı yenilendiğinde yeni patlama kredisi verilmez.
- UDP ve en fazla 1200 bayt TCP yazmaları bu bekleme kuyruğuna alınmaz. Bu
  bir **boyut/protokol sezgisidir**; telefonda her TCP/UDP akışının oyun sahibi
  yeniden doğrulanmaz. Büyük oyun TCP yazmaları da bütçeye girebilir; küçük
  arka plan yazmaları bütçeyi geçebilir. Dolayısıyla toplam yükleme için
  katı bir hız sınırı veya bütün oyunlar için kazanç garantisi değildir.
- Tavan 0 ise hız uydurulmaz ve yeni TCP hız kısıtlaması uygulanmaz. İsteğe
  bağlı adaptif ölçüm otomatik açılmaz; Windows'taki mevcut seçim geçerlidir.
- Büyük bir TCP akışının beklemesi diğer UDP akışını veya kontrol kanalını
  ortak bir yazma kilidinde bekletmez. Güvenilir TCP akışından bayt düşürülmez;
  geri basınç uygulanır. Normal moddaki 16 KiB kopyalama davranışı korunur.
- Otomatik oyun bildirimi 8 saniyede eskir; eski bütçe kalkar. Kullanıcı desteği
  kapatınca, hücresel `Network` değişince/kaybolunca veya relay kapanınca
  bekleyen bütçe temizlenir. Elle seçilmiş Oyun profili açık kalabilir, ancak
  eski Windows tavanı süresiz uygulanmaz.
- Oyun sırasında maskot/grafik güncellemeleri azaltılır ve isteğe bağlı konum
  testi başlatılmaz; çalışan testin iptali istenir. Termal koruma bağımsız
  çalışmaya devam eder. Durum ekranında uygulanan büyük TCP tavanı, tamamlanan
  bekletme sayısı ve toplam bekleme süresi gösterilir; bunlar oyun ping'i değildir.

## Korunan özellikler ve araştırma

Hücresel `Network` üzerinde DNS çözümü/soket açma, `Network.bindSocket`, bağımsız
UDP akışları, `TCP_NODELAY`, doğrulanmış Windows heartbeat'ine bağlı süreli
CPU wake lock, foreground service, DoH doğrulaması/cache'i ve termal izleme
zaten vardı; yeni eklenmiş özellikler olarak sunulmuyor.

- [Android ConnectivityManager](https://developer.android.com/reference/android/net/ConnectivityManager)
  ağ seçimi ve callback yaşam döngüsünü tanımlar. Mevcut hücresel bağlantı
  yönetimi korunur; bağlantıyı keserek modem/ağ sıfırlama yapılmaz.
- [Android Doze rehberi](https://developer.android.com/training/monitoring-device-state/doze-standby)
  CPU wake lock'ın tek başına Doze ağ kısıtlamalarını aşmadığını açıklar.
  [2026 Android güç yönetimi açıklaması](https://android-developers.googleblog.com/2026/03/battery-technical-quality-enforcement.html)
  foreground service ile CPU wake lock'ın farklı görevlerini açıklar.
  Mevcut kısa, bağlantıya bağlı CPU lease'i korunur; sürekli/sınırsız kilit,
  otomatik pil muafiyeti veya zorla ekran açık tutma eklenmez.
- [Android Thermal API](https://developer.android.com/games/optimize/adpf/thermal)
  aşırı ısınmanın sürdürülebilir performansı sınırladığını açıklar.
  Ek görsel/test yükünü azaltmak tercih edildi; termal sınırlar kaldırılmadı.
- [Android network slicing](https://developer.android.com/develop/connectivity/5g/use-network-slicing)
  operatör destekli öncelikli ağların ayrı koşullarını açıklar. Sıradan hücresel
  soketlere bir bayrak ekleyerek garantili operatör gecikme önceliği elde edildiği
  iddia edilmez; otomatik ücretli premium capability talebi yapılmaz.

Bu bütçe telefonun **yukarı yöndeki büyük TCP gönderimini** düzenler.
Operatörden telefona zaten ulaşmış UDP indirmelerini geriye dönük kontrol
edemez. Telefonun başka uygulamalarının mobil veri tüketimini, baz istasyonu
kuyruğunu veya oyun sunucusunu yönetmez. Sahipliği bilinen arka plan trafiğinin
Windows'taki mevcut bütçesi ayrıca çalışır. Sinyal dBm değeri hız olarak alınmaz;
DNS RTT de oyun sunucusunun ping'i olarak sunulmaz.

## Tamamlanan kontroller

- 28 deterministik/izole kontrol: ortak token bütçesi, burst yenilenmemesi,
  küçük yazma istisnası, lease bitişi, tavan yokluğu, kapatma, geçersiz sayılar,
  iptal/interrupt ve gerçek bekleyenin serbest bırakılması. Ağ/cihaz erişimi yok.
- Gerçek loopback testi: kasıtlı düşük sentetik tavanda 32 KiB TCP aktarımı
  sürerken 0/32/1200 bayt ayrı UDP akışları ilerledi; TCP/UDP içerikleri korundu.
  Bu sonuç fiziksel telefon/hücresel performans ölçümü değildir.
- Relay'in 200 IO/runtime hata enjeksiyonu, DNS ve DoH/cache/fallback kontrolleri
  geçti. Saf Java motoru Java 8 hedefiyle uyarılar hata kabul edilerek derlendi.
- Android lifecycle/HostLease/konum kalitesi kontrolleri geçti.
- Yeni derlenen Java relay ile Go bridge, IP motoru ve trafik paketi testleri geçti.
- APK: Android API 26 minimum, API 35 hedef, versionCode 22,
  `1.0.0-rc.11-gaming`. Geliştirme anahtarı korunarak imza ve hizalama doğrulandı.
  Android derlemesinde mevcut eski UI/telephony API'lerine ait 16 deprecation
  uyarısı var; sıfır uyarı iddiası yok.
- APK fiziksel telefona kurulmadı. Android runtime/UI, Samsung ekran kapalı
  uzun oturum, gerçek USB/hücresel kararlılık ve oyun p95/p99 ping/jitter
  kazanımı henüz doğrulanmadı. Fiziksel cihaz komutu çalıştırılmadı.

## Kullanım ve kabul

APK manuel güncelleme için hazırlanmıştır; mevcut uygulama otomatik kaldırılmaz
veya değiştirilmez. Profiller'deki **Android oyun desteği** açıkken Windows'ta
oyun EXE'leri seçilir. Oturum sırasında Android durumunun etkin olduğu ve varsa
TCP tavanı ayrı kontrol edilir. Önce antrenman/özel sunucuda aynı sinyal/termal
koşullarda kapalı/açık karşılaştırması yapılmalıdır: oyun RTT p50/p95/p99,
jitter/paket kaybı, yükleme yükü, telefon termal durumu ve ekran kapalı oturum.
Bu fiziksel aşama için ayrıca kullanıcı onayı gerekir.

Yerel tekrar derleme: `scripts/build-android-gaming.ps1`.
Bulut/izole regresyon: `scripts/test.sh`; iki oyun testi bu akışa eklendi.
