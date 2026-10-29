# Rekabetçi oyun modu — 29 Ekim 2026

## Eklenen davranış

Windows uygulamasında **Profiller → Rekabetçi oyun modu** bölümünden en fazla
16 oyunun gerçek çalışan EXE dosyası seçilir. Başlatıcı seçilmez. Algılama iki
saniyede bir yapılır; süreç kimliği PID, oluşturulma zamanı ve tam dosya yolu ile
doğrulanır. Başka klasördeki aynı isimli program eşleşmez.

Mevcut oyun trafiği odağına aşağıdaki Windows oturum yönetimi eklendi:

- **Windows süreç optimizasyonu** ayrıca etkinleştirilir; varsayılanı kapalıdır.
  Oyun süreci AboveNormal CPU önceliği alır ve execution-speed güç kısıtlaması
  kapatılır (HighQoS). Zaten High/Realtime kullanan oyunun sınıfı yükseltilmez.
  Desteklenmeyen veya erişimi engellenen süreç atlanır; sürücü/enjeksiyon yoktur.
- Kullanıcının ayrıca seçtiği arka plan EXE'leri oyun süresince BelowNormal ve
  EcoQoS alır. Bu bir CPU kaynak önceliği tercihidir; RAM boşaltmaz, disk hızına
  veya internet hızına işletim sistemi genelinde kota koymaz. Hiçbir süreç
  sonlandırılmaz veya askıya alınmaz.
- TetherMax/PdaNet/MyPublicWiFi/ADB, bilinen anti-cheat, başlatıcı ve sesli sohbet
  süreçleri arka plan seçimine alınmaz. Oyun listesine alınan yol arka plan
  listesinden çıkarılır. Güvenlik uygulamalarını arka plan listesine eklemeyin.
- Mevcut IP motorunun oyun akış önceliği ve ortak arka plan bütçesi korunur;
  bütçe artık arayüzden 0,25–10 Mbps/yön arasında seçilebilir. Varsayılan 2 Mbps.
  Motorun ilgili yöndeki hız sınırı biliniyorsa arka plan tavanı ayrıca bunun
  %15'i ile sınırlanır; mevcut motor en az 0,128 Mbps alt sınırı uygular.
  Bu nedenle çok düşük hız sınırlarında %15 mutlak bir üst sınır değildir.
  Küçük gecikme hassas paketler ve DNS için mevcut istisnalar devam eder.
- Trafik sınıflandırması **yalnızca TetherMax IP motorundan geçen ve süreç
  sahipliği doğrulanabilen akışlar** içindir. Başka adaptörden geçen trafiğe,
  bilinmeyen sürece veya hotspot istemcisine oyun kimliği yakıştırılmaz.
  SOCKS kullanımında aynı uygulama başına IP sahipliği garantisi yoktur.
- Mevcut oyun sırasında grafik/animasyonları azaltma davranışı sürdürülür.

## Geri alma ve hata davranışı

İşlem değiştirilmeden önce eski ve hedef değerler yerel, atomik yazılan
`%LOCALAPPDATA%\TetherMax\game-tuning-journal.json` dosyasına kaydedilir.
Günlük yazılamazsa değişiklik yapılmaz. Var olan günlük ve yedeği okunamıyorsa
Windows optimizasyonu başlatılmaz. Oyun bitince, özellik kapatılınca,
EXE listesi değişince veya uygulama normal kapatılınca geri alma denenir.
Kullanıcının/oyunun başka bir araçla sonradan değiştirdiği öncelik veya güç
tercihi üzerine eski değer zorla yazılmaz. Kullanıcı ayarı değiştirdikten sonra
geri alma normalde sonraki taramada başlar; sabit bir süre garantisi verilmez.

**Ani uygulama çökmesinde anlık geri alma garantisi yoktur.** Yaşayan süreçler
TetherMax yeniden açıldığında günlükten geri alınır. Geri alma erişim hatasında
günlük korunur ve sonraki taramalarda tekrar denenir. Süreç kapandıysa veya PID
yeniden kullanıldıysa yeni programa yazılmaz. Ayarlar süreç ömrü ile sınırlıdır;
Windows hizmeti, kalıcı güç planı veya genel ağ ayarı değiştirilmez.
Arayüzde doğrulanmış, bekleyen ve erişim/geri alma sorunu sayıları ayrı görünür.

## Araştırmaya dayanan seçimler

1. [Microsoft: scheduling priorities](https://learn.microsoft.com/en-us/windows/win32/procthread/scheduling-priorities)
   yüksek önceliğin diğer sistem işlerini aç bırakabileceğini açıklar.
   Bu yüzden Realtime/High zorlaması yerine AboveNormal seçildi. Bu seçimin
   her oyunda FPS artışı sağlayacağı iddia edilmez.
2. [Microsoft: SetProcessInformation](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setprocessinformation)
   ve [Quality of Service](https://learn.microsoft.com/en-us/windows/win32/procthread/quality-of-service)
   belgelerindeki execution-speed/HighQoS/EcoQoS mekanizmaları kullanılır.
   CPU affinity, E-core kapatma ve core-parking değişiklikleri uygulanmaz;
   donanım ve oyun özelinde ölçüm olmadan bunlara üstünlük atfedilemez.
3. [Riot: VAN RESTRICTION 5](https://support-leagueoflegends.riotgames.com/hc/en-us/articles/49277523219859-Error-VAN-RESTRICTION-5)
   HVCI/VBS doğrulamasını açıklıyor.
   [Vanguard On-Demand](https://www.riotgames.com/en/news/vanguard-on-demand)
   isteğe bağlı Pre-Check için Windows 11 25H2 ve güvenlik gereksinimlerini
   ayrıca listeliyor. TPM, Secure Boot, VBS/HVCI, Defender ve anti-cheat korunur.
4. [NVIDIA sistem gecikmesi rehberi](https://www.nvidia.com/en-gb/geforce/guides/system-latency-optimization-guide/)
   desteklenen oyunlarda Reflex'i önerir. Oyun içi ayar uygulamadan açılmıyor;
   kullanıcıya oyun menüsünden seçmesi hatırlatılıyor. Tüm GPU'larda destek
   bulunduğu veya tek bir FPS tavanının herkese uygun olduğu iddia edilmez.
5. [Microsoft ağ donanımı performans notları](https://learn.microsoft.com/en-us/windows-server/administration/performance-tuning/hardware/)
   interrupt moderation ile CPU yükü/gecikme takasını, offload'ın CPU tasarrufunu
   açıklar. Bu belge sunucu bağlamındadır; Windows oyunlarına evrensel ayar
   reçetesi olarak aktarılmadı. RSS/offload/interrupt ayarları değiştirilmedi.

HPET/BCDEdit, zamanlayıcı çözünürlüğü zorlama, TCP/Nagle registry paketleri,
standby RAM temizleme, tüm hizmetleri durdurma veya global DNS değiştirme
uygulanmadı. Bu uygulamaların bu bilgisayarda faydası ölçülmüş değildir.
Özellikle hücresel radyo kapasitesi, operatör kuyruğu, oyun sunucusu, termal
kısıtlama ve shader derlemesi süreç önceliğiyle ortadan kaldırılamaz.

## Kullanım ve gerçek kabul ölçütleri

1. Mevcut oturumun bağlantısını kesmeyi kabul ettiğiniz zaman TetherMax'ı tepsi
   menüsünden kapatın. ZIP'i ayrı klasöre çıkarıp `TetherMax.exe` çalıştırın.
   Paket kurulumu veya bağlantı otomatik başlatılmaz. Aynı anda eski ve yeni
   sürüm çalıştırılmaz; eski uygulama açıksa tek örnek koruması onu öne getirir.
2. Profiller'de oyun EXE'lerini ekleyin. LoL için `League of Legends.exe`,
   VALORANT için `VALORANT-Win64-Shipping.exe`, Rust için `RustClient.exe`;
   Deadlock için kurulumunuzdaki gerçek çalışan EXE'yi seçin. İsimler sadece
   örnektir; belirleyici olan seçilen tam dosya yolu ve çalışan kimliğidir.
3. Windows süreç optimizasyonunu açın, kaynak önceliğini azaltmak istediğiniz
   arka plan EXE'lerini ayrıca seçin. Trafik bütçesini ayarlayın. Bu seçim
   gereksiz uygulamaları sistem genelinde otomatik tespit edip kapatmaz.
4. Oyun açıkken Windows doğrulama sayısını ve IP motoru etkinlik onayını ayrı
   kontrol edin. Birinin çalışması diğerinin çalıştığı anlamına gelmez.
5. Donanım testine ayrıca onay verildiğinde aynı oyun/sahne/ayarlarla kapalı-açık
   en az üçer eşlenmiş oturum karşılaştırılmalıdır: ortalama FPS, 1% low,
   p99 frametime, oyun içi RTT p50/p95/p99, jitter ve paket kaybı. Mümkünse
   sıralı maç yerine antrenman/özel sunucu kullanılır. Ağ yükü ve termal durum
   kaydedilir; ölçümler olmadan yüzde kazanç veya sıfır sıçrama ilan edilmez.

## Tamamlanan doğrulama

- Windows Release build: 0 hata, 0 uyarı; x64 self-contained publish başarılı.
- 30 izole assertion: oyun/arka plan ayarı, tekrarlayan tarama, geri alma,
  yeniden açılış, yeniden kullanılan PID, bağımsız ayar değişikliği, kısmi
  yazma, disk hatası, erişim hatası/tekrar deneme, korunan süreçler ve bütçe.
  Testler sahte süreç backend'i kullanır; gerçek Windows süreç ayarlarını
  değiştirmez. CI'daki izole Windows işine eklendi; uzak CI burada çalıştırılmadı.
- Mevcut Go trafik paketi testleri ve `go vet` geçti; motor yeniden derlendi.
- Panelin açık/koyu önizlemesi yalnızca çevrimdışı render ile denetlendi.
- Gerçek oyun FPS/frametime, telefon/USB/hücresel bağlantı ve native süreç QoS
  çalışma zamanı kazanımı **test edilmedi**. Kurulu uygulama değiştirilmedi.
