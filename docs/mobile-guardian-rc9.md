# TetherMax Mobile Guardian RC9 — 25 Ekim 2026

Android geliştirme önizlemesi: `io.tethermax.app`, versionCode 20. APK mevcut
TetherMax Development anahtarıyla imzalanır; üretim/Play sertifikası değildir.
Bu çalışma fiziksel telefona kurulmamıştır.

## Uygulanan davranış

- CELLULAR soket bağlama, eşleştirme, DNS profili, relay protokolü ve trafik
  motoru değiştirilmedi. Windows/host dosyaları bu çalışmada düzenlenmedi;
  başlangıçtaki takip edilen Windows/host diff'leri birebir korundu.
- Kullanıcı başlatmalı `connectedDevice` foreground service ve START_STICKY
  korunur. Force-stop aşılmaz; reboot/alarm/WorkManager ile yeniden başlatılmaz.
  Yeniden oluşturma açık TCP oturumlarını koruma garantisi değildir.
- Kimliği doğrulanmış Windows heartbeat'i varken 20 saniyelik CPU wake lease
  sürer; yenileme 5 yerine 10 saniyede yapılır. Bağlı oturumda kontrol 5 saniye
  kalır. İstemci yokken ekran açık/kapalı kontrol 15/30 saniyedir. Yeni kabul
  edilen USB soketi 1 saniyelik tek birleştirilmiş kontrolü tetikler ve 30 saniye
  boyunca 5 saniyelik kontrol kullanılır. Ekran kilidi aktarımı durdurmaz.
- Hatalı tek istemci diğer akışları kapatmaz. Dinleyici hatası olduğunda sadece
  dinleyici yeniden oluşturulur; mevcut relay/soketler, hücresel ağ ve eşleştirme
  korunur. Gecikmeler 1/2/4/8/16/30 saniyedir. Altı deneme tükenirse mevcut
  akışlar korunur; panel yeni bağlantı alınamadığını ve aktarım bitince elle
  yeniden başlatmayı bildirir. Kablo veya Windows ADB kanalını Android uygulaması
  yeniden kuramaz; istemcinin mevcut yeniden bağlantı davranışı gerekir.
- Pil/screen broadcast'leri ve termal/sinyal callback'leri olay tabanlıdır.
  Hücre taraması, otomatik hız testi veya yeni ping/DNS sorgusu yoktur. Veri SIM'i
  ve hücresel ağ değişiminde sinyal aboneliği yenilenir. Konum/telefon okuma
  izni istenmez; ROM erişimi reddederse panel erişilemiyor der.
- API 31+ TelephonyCallback; eski Android'de PhoneStateListener uyumluluğu.
  LTE/NR sinyal bileşenleri ve 5G gösterge türü ayrı gösterilir; NSA göstergesi
  aktif NR veri taşımasının veya kapasitenin kanıtı değildir. Sinyal olayının yaşı
  gösterilir; zayıf sinyalde konum önerisi verilir. Kapasite sinyalden türetilmez.
  Mevcut Windows adaptif zamanlayıcısı/RTT-jitter ölçümü aynen kalır; yeni bir
  radyo kapasitesi tahmincisi veya otomatik hız sınırı eklenmedi.
- Android termal durumu >= MODERATE veya pil sıcaklığı >=40°C olduğunda sadece
  isteğe bağlı görsel/analiz işi azaltılır. Bunlar TetherMax'ın ihtiyatlı yerel
  iş yükü eşikleridir; Samsung donanım güvenlik sınırı veya CPU sıcaklığı değildir.
  Pil <38°C ve termal durum <MODERATE kaldığında 60 saniye histerezisle geri
  alınır. API 29 öncesinde termal durum desteklenmiyor gösterilir.
- Kritik termal durum yüksek önem kanalında bildirilir. Android 13+ bildirim
  izni panelden kullanıcı tarafından verilir; izin kapalıysa panelde uyarı sürer.
  Sistem termal sınırlaması/kapanması engellenmez; internet otomatik kapatılmaz.
- Arka plana geçildiğinde mevcut UI/mascot duraklatma korunur; isteğe bağlı konum
  testi de iptal edilir. Isı korumasında yeni konum testi engellenir ve sürmekte
  olan test iptal edilir. Gizli grafikler örnek toplamaz; ısı korumasında analiz
  grafikleri durur. Isıda görünür UI yenilemesi 1 yerine 5 saniyedir.
- Grafik geçmişi O(1) eklemeli, 1800 örnekli primitive ring buffer kullanır.
  Üç grafiğin toplam ham örnek depolaması 86.400 bayttır (nesne başlıkları hariç).
  Başlangıçta ayrılır; örnek başına float[]/Long tahsisi veya liste kaydırma yoktur.
- Sağlık paneli mevcut pembe/lavanta kart ve kedi tasarımını kullanır. Ana Sayfa
  ve Ayarlar'dan açılır. CPU yalnızca uygulama işlem CPU zamanının geçen zamana
  oranıdır; bir çekirdek %100 kabul edilir ve çok çekirdekte aşabilir. RAM Android
  Debug.MemoryInfo PSS ölçümüdür. Örnekleme yalnızca panel görünürken 15 saniyede
  (ısıda 30 saniyede) bir yapılır; sistem toplam CPU'su uydurulmaz.
- Pil düzeyi/sıcaklığı/şarj durumu/Android sağlık sınıfı, termal durum, sinyal,
  heartbeat sağlığı, oturum süresi/yeniden bağlantı sayısı ve oturum relay baytları
  gösterilir. USB şarj kaynağı, veri kanalı sağlığından ayrıdır. Süre ve sayaçlar
  servis yaşamına aittir; geçmiş servisteki toplam veya operatör faturası değildir.
- Şarj kesme, kapasite sağlığı yüzdesi, diğer uygulamaları kapatma, ağ değiştirme,
  CPU frekansı zorlama, global güç tasarrufu veya OEM ayarı yazma yapılmaz.
  Android pil/uygulama/optimizasyon listelerine standart intent + genel ayarlar
  fallback'i vardır. Samsung Pil koruması ve Asla uyumayan uygulamalar için
  yazılı yol gösterilir; modele özel gizli component adları kullanılmaz.

## Güncel kaynaklar ve seçimler

25 Ekim 2026'da resmi kaynaklar incelendi:

- [Android 17 değişiklikleri](https://developer.android.com/about/versions/17/summary):
  yeni target'a geçiş yerel ağ izni gibi davranış değişiklikleri taşır. RC9
  Android 17 SDK (37.1) ile de derlendi; target 35/min 26 korunarak çalışan USB
  davranışına test edilmemiş bir target geçişi eklenmedi. Yeni API olması tek
  başına aktarım için yarar sağlamaz. Asıl APK uyumluluk için SDK 35 ile derlenir.
- [Foreground service türleri](https://developer.android.com/develop/background-work/services/fgs/service-types):
  connectedDevice ve mevcut CHANGE_NETWORK_STATE önkoşulu uygundur; uzun modem
  oturumu dataSync zaman sınırlı servisine dönüştürülmedi.
- [Doze/App Standby](https://developer.android.com/training/monitoring-device-state/doze-standby):
  şarjda olmak ve ekran kapatmak farklıdır; Doze wake lock'u da yok sayabilir.
  CPU lease/OEM uyku listesi kesintisizlik garantisi değildir.
- [Sinyal callback'i](https://developer.android.com/reference/android/telephony/TelephonyCallback.SignalStrengthsListener),
  [radyo gösterge callback'i](https://developer.android.com/reference/android/telephony/TelephonyCallback.DisplayInfoListener):
  sürekli hücre sorgusu yerine callback, veri SIM'ine bağlı kayıt ve kayıt temizliği.
- [Termal API](https://developer.android.com/games/optimize/adpf/thermal):
  termal callback tercih edildi. Headroom en fazla 10 saniyede sorgulanabilir ve
  NaN dönebilir; gereksiz sürekli headroom/CPU headroom polling'i eklenmedi.
- [Uygulama CPU zamanı](https://developer.android.com/reference/android/os/Process#getElapsedCpuTime()),
  [PSS ölçümü](https://developer.android.com/reference/android/os/Debug.MemoryInfo):
  uygulama ölçümü, toplam sistem ölçümü olarak sunulmaz.
- [Samsung Pil koruması](https://www.samsung.com/uk/support/galaxy-battery/battery-protection-tips/):
  One UI 7+ desteklenen cihazlarda Maksimum %80/85/90/95 seçimi; menü/sürüm
  değişebilir. TetherMax seçimi okuyup uyguladığını iddia etmez.
- [Samsung arka plan sınırları](https://www.samsung.com/uk/support/galaxy-battery/optimisation/):
  Asla otomatik uyumayan uygulamalar kullanıcı ayarıdır. Güç tasarrufu arka plan
  ağını sınırlayabileceği için otomatik etkinleştirilmez.

## Doğrulama ve önce/sonra

`scripts/test-mobile.ps1`: ServiceState (100 yeniden başlatma döngüsü), HostLease,
PositionQuality, GuardianPolicy (termal histerezis, bilinmeyen veriler, CPU hesabı,
kontrol sıklığı, sınırlı retry), SampleHistory (1.000.000 ekleme) geçti.
Java RelayDnsTest ve DnsOverHttpsTest gerçek loopback fixture'ları geçti;
DNS dışı TCP akış sürekliliği sınandı. Bunlar fiziksel USB/hücresel testi değildir.
Yeni derlenen Java fixture ile `go test -count=1 -v ./internal/bridge` geçti:
gerçek loopback TCP/UDP/protokol/eşleştirme/yarım kapatma ve güvenli iptal
senaryoları kontrol edildi. Go/Windows kaynaklarında değişiklik yapılmadı.

Başlangıç Android kaynakları `build/mobile-before/android` içine korundu; hem
önceki RC8 APK hem RC9 derlendi. APK v2/v3 imza doğrulaması geçti ve sertifika
SHA-256 aynı kaldı: `3ad9c96e1394c7f46f98d4c0971a9eb13b92e51cb93b01d3338962484c58a7ff`.
Android 17 SDK derlemesi ayrı bir derleme kontrolüdür; Android 17 runtime testi değildir.

| Ölçüt | Önce | Sonra | Kanıt türü |
|---|---|---|---|
| İstemcisiz, ekran kapalı bakım | 5 sn | 30 sn | Kod/politika testi; planlanan callback sayısı %83 azalır |
| Bağlı aktarım bakımı | 5 sn | 5 sn | Kod/politika testi |
| Wake lease yenileme | 5 sn | 10 sn | Kod incelemesi; lease 20 sn kalır |
| Ekran kapalı UI yenileme | Durmuş | Durmuş | Önceden var olan davranış korunur |
| Grafik örneği ekleme | İki nesne + O(n) kaydırma | Sabit depolama, O(1) | Bir milyon ekleme testi |
| JVM geçmiş mikro testi CPU | 375.000 ms | CPU sayacı çözünürlüğünün altında | Desktop JVM, medyan 7, 1.000.000 ekleme |
| Aynı test duvar süresi | 361.776 ms | 4.137 ms | Android olmayan sentetik test |
| Aynı test toplam tahsis | 48.041.816 bayt | 40 bayt ölçüm dizisi | Başlangıç/drawing/UI/relay hariç; retained RAM değil |
| A56 uygulama CPU/RAM | Ölçülmedi | Ölçülmedi | Fiziksel runtime testi gerekir |
| A56 pil sıcaklığı / tüketim | Ölçülmedi | Ölçülmedi | Fiziksel runtime testi gerekir |
| Günler süren USB sürekliliği | Bu çalışmada ölçülmedi | Ölçülmedi | Fiziksel soak testi gerekir |

Mikro test: `MobileHistoryBenchmark.java`, aynı eski ArrayList ekleme/kaydırma
yolunu yeni ring buffer ile karşılaştırır. Sıfır CPU sayacı değeri sıfır iş
yapıldığını göstermez. JVM süreleri telefon hızlanması yüzdesi olarak kullanılamaz.

## Kalan cihaz doğrulaması

`AGENTS.md`: “Do not run ADB against physical devices, install an APK there or
perform real phone/Windows/mobile network tests without separate explicit user
approval for that stage.” Bu nedenle fiziksel ADB/kurulum/ağ testi yapılmadı.
İzinli sonraki aşama: aynı A56/One UI/sinyal/şarj ortamında RC8 ve RC9 için aynı
yükte 30 dakikalık ekran açık/kapalı CPU/PSS/pil sıcaklığı karşılaştırması;
ardından 24–72 saat transfer, ekran kilidi, ağ kaybı/kurtarma ve kablo ayrılması.
OEM termal davranış, yeni panelin görsel düzeni, callback erişimi, Doze ve gerçek
USB dinleyici kurtarması cihazda henüz doğrulanmadı. Test sırasında mevcut
Windows ağ altyapısı değiştirilmeyecek. Hotspot/operatör kota davranışı korunur;
muafiyet garantisi yoktur.
