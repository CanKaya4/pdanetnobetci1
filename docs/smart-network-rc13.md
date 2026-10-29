# TetherMax Smart Network · RC13

30 Ekim 2026 · **ilk aşama / ön sürüm**. TetherMax Ultimate'ın sekiz hedefinin
tamamlandığı veya gerçek cihazda kararlılık kabulünün geçtiği anlamına gelmez.

## Bu sürümde eklenenler

- Windows ve Android'de gözlenen TetherMax relay verisi için kalıcı aylık sayaç,
  kullanıcı GB bütçesi ve %80 / %100 uyarıları. Aynı örneği tekrar sayma,
  yeniden bağlantı, ay değişimi, saat geriye alma ve taşma senaryoları sınanır.
- Windows Analiz ekranında IP motorunun görebildiği adreslerin oturumluk IPv4
  sayaçları. NAT arkasındaki fiziksel cihaz kimliği çıkarılmış gibi gösterilmez.
- Windows Network Doctor: telefonun hücresel durum bildirimi, eksik ölçüm,
  yanıtsız DNS denemeleri ve yerel kuyruk belirtilerine dayalı açıklamalar.
  Yeni bir ağ testi veya otomatik ağ yapılandırması başlatmaz.
- Düşük hız tavanlarında CoDel hedefinin MTU iletim süresine uyması.
- Loopback Java testleri, Windows Oracle launcher shim'i yerine tanımlı
  `JAVA_HOME` çalıştırılabilirini kullanır; test çocuk süreçlerinin sızması önlenir.
- Mevcut premium/romantik tasarım, oyun odağı ve kaynak ağacında zaten bulunan
  önceki güvenilirlik düzeltmeleri korunarak paketlenir.

Windows: **Analiz → DataShield · Network Doctor**.
Android: **Analiz → DataShield · aylık bütçe**.
Bütçe 0 ise uyarı kapalıdır. Aşım bağlantıyı kesmez. Android bildirimleri kullanıcı
iznine bağlıdır. Sayaçlar operatör faturası veya tüm uygulamaların kullanımı değildir;
Windows ve Android toplamları birbirine eklenmez. Ay UTC takvim ayıdır. Ani
kapanış/telemetri boşluğunda eksik veri olabilir; 30 saniye kayıt hedefi kesin
disk dayanıklılığı garantisi değildir. İki uçtaki bütçeler bağımsızdır.

## Tamamlanan yerel kontroller

| Kontrol | Sonuç ve gerçek kapsam |
|---|---|
| Smart Network | 19 izole C# assertion; atomik dosya/yedek, sıralama, restart, ay/taşma ve teşhis |
| Android UsageMeter | 13 saf Java assertion; ayrıca 100.000 örneklik kesin toplam kontrolü |
| Go trafik | Mevcut testler + IP sayaç toplamı, 128 adres sınırı, snapshot izolasyonu, telefon gizliliği, eşzamanlılık, düşük hız CoDel |
| Java/Go entegrasyonu | Yeni derlenen loopback Java relay ile tüm Go paket testleri ve `go vet ./...` geçti; fiziksel USB yok |
| Windows | Bağlantı/IPC, eşleştirme, hotspot portal, mock ağ/çökme kurtarma, probe ve 30 oyun assertion geçti |
| Windows UI | Yeni panelde duplicate/malformed örnek ve iki eşik uyarısı; mevcut 100 otomatik istek, hide/exit, tema ve pencere boyutu testleri geçti |
| Android mevcut politika | Servis lifecycle, 100 restart, lease, termal politika, bounded grafik; 200 relay hata enjeksiyonu geçti |

İlk yerel Go çalışmasında test assertion'ları geçtiği halde Java launcher'ın
alt süreçleri çıkmadığından inherited I/O timeout oluştu; bu çalışma **başarısızdı**.
Fixture Java seçimi düzeltildi; yalnızca bu çalışmanın sahip olduğu 9 loopback
JVM temizlendi. Tüm paketler tekrar geçti ve kalan fixture JVM sayısı sıfırdı.
Testler azaltılmadı, bekleme süresi artırılarak hata gizlenmedi.

Yeni IP muhasebesinin tek adresli mikrobenchmark'ı bu bilgisayarda 11,81 ns/op,
0 B/op ve 0 allocs/op verdi. Bu değer ağ throughput'u, oyun pingi veya uçtan uca
performans sonucu değildir. Paket/arayüz kanıtları Release eklerinde sunulur.
Android build mevcut platform API'lerine ilişkin 16 deprecation uyarısı veriyor;
uyarılar kapatılmadı. Windows build 0 hata/0 uyarı ile geçti.

## Paketler ve güvenlik sınırı

APK mevcut geliştirme kimliğiyle imzalanır; v2/v3 doğrulaması ve sertifika özeti
build raporundadır. MSI Windows x64 self-contained ön sürümdür; otomatik başlatma
ve kurulum yapılmaz. Wintun DLL ve Google platform-tools pinleri/signature
kontrolleri mevcut derleme betiğinde korunur. MSI için okuma amaçlı veritabanı
incelemesi ve SHA-256 özeti Release eklerinde bulunur. Windows ürününe ticari
Authenticode imzası eklendiği iddia edilmez.

Yeni **Smart Network isolated validation** Actions işi Linux'ta fresh Java relay
ile vet/race testlerini, disposable Windows runner'da izole sayaç/bağlantı/oyun
ve UI testlerini çalıştırır. Bu belgedeki yerel sonuçlar uzak CI başarısı sayılmaz;
uzak çalışmanın durumu GitHub Actions'tan ve eklenen CI raporundan doğrulanmalıdır.

Gerçek telefon, USB, hücresel 4G/5G ve oyun/TV yükünde RC13 doğrulanmadı.
24–72 saat dayanıklılık, çoklu OEM ve gerçek kesinti kabulü bekliyor.
Çalışan bağlantı değiştirilmedi; fiziksel ADB veya APK/MSI kurulumu yapılmadı.
Hotspot/operatör kota muafiyeti doğrulanmadı ve garanti edilmez.

## Henüz tamamlanmayan ürün hedefleri

Çoklu telefon/hat çalışması, gerçek oturum koruyan bonding, RemoteHome bulutu,
Business Gateway olay/bildirim altyapısı, tüm uygulamalar için gerçek byte
muhasebesi ve lisans/abonelik backend'i bu sürümde **uygulanmadı**.
Mevcut tek hat aktarımı ve kurtarma bunlara eşdeğer sayılmaz.

[Araştırma, sekiz hedefin durum matrisi, mimari kararlar, premium paket taslağı
ve sonraki kabul kapıları](ultimate-architecture-2026-10-30.md).
