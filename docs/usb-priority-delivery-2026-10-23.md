# USB önceliği teslim kaydı — 23 Ekim 2026

**Ana istek tamamlanmadı.** Bu commit, standart RNDIS'ten bağımsız tam Windows
internet aktarımı olarak sunulamaz. Native rota/IP/DNS backend'i, çalışan ayrı
süreçte rollback ve IPv4/IPv6/DNS fail-closed engeli hâlâ eksik. Eklenen rota
paketi bu bileşenleri zorunlu kılan işlem çekirdeğidir; bunların yerine geçmez.

## Değişen çalışan parçalar

* WPF'de **Windows ağ yolu raporu**: salt okunur adaptör/GUID, rota, metrik,
  DNS, adaptör bayt sayaçları ve hedef için seçili arabirim kaydı. Yerel JSON
  `%LOCALAPPDATA%/TetherMax/reports` altına kaydedilir. ADB ve driver çağırmaz.
  Windows PowerShell 5.1 ve PowerShell 7'de komut çıktısı incelendi; WPF pencere
  tıklaması bu oturumda denenmedi. Yürütme politikası değiştirilmez.
* `ip-check`: mobil trafik öncesi hücresel durum sorgusu, Android önce/sonra
  sayaçları ve farkları; ağ/sayaç değişiminde başarısız sonuç. Sistem geneli ve
  hotspot kapalı alanları açıkça false. Telefon bilgisi uygulamanın beyanıdır;
  bağımsız operatör muhasebesi veya platform güvenlik attestasyonu değildir.
* `route-plan --session proposed-only --adapter-guid proposed-only
  --targets 1.1.1.1,8.8.8.8`: yalnızca /32 dry-run JSON, CanApply=false.
* Rota işlem çekirdeği: intent → ayrı guard onayı → ekleme → ters sırada
  sahip olunanları kaldırma. Kaybolan ekleme yanıtı, iptal, journal/guard arızası
  ve rollback arızası simülasyonları var. Native Windows değişikliği yapamaz.
* `scripts/usage-study.py`: üç ayrı modda kullanıcı girilen sayaç farklarını
  değerlendiren çevrimdışı ölçüm yardımcısı. Gecikme/reset/eksik kanıt durumları
  inconclusive; sıfır hotspot farkından bypass sonucu çıkarmaz. Otomatik veri
  indirme, trafik sınırlandırma veya operatör hesabına erişim yapmaz.

## Bu oturumda gerçekten geçen kontroller

* Java relay yeniden derlendi; `go vet ./...` ve beş Go paketinin tüm testleri
  geçti. Üst seviye 38 test: gerçek loopback Java relay üzerinden IP TCP,
  UDP, DNS, parçalanma/yeniden iletim ve yeni rota işlem simülasyonları dahil.
* Yeni non-cellular fixture reddi: veri akışı başlamadan probe başarısız,
  telefon fixture payload sayaçları sıfır. Windows doğrudan bağlantı fallback'i yok.
* Yedi offline eşleştirme kontrolü ve üç asenkron kaldırma doğrulayıcı kontrolü geçti.
* Offline operatör ölçüm aracının dört testi geçti.
* Geliştirme anahtarlı APK yeniden derlendi; APK v2/v3 imza kontrolü geçti.
  Android kodu bu commit'te değiştirilmedi; cihaza APK kurulmadı.
* Windows WPF publish ve x64 engine build geçti. Per-user MSI WiX ile üretildi;
  otomatik kurulmadı, startup/servis/driver eklenmedi. MSI Authenticode üretim
  imzası yok; APK geliştirme imzasıdır. Paket üretimi ürün/hardware onayı değildir.
* Salt okunur snapshot'larda 1.1.1.1/8.8.8.8 için Windows seçimi Ethernet 5.
  IPv6 hedefi için kullanılabilir rota bulunamadı. Bu yalnızca yapılandırma
  gözlemidir; paket yakalama veya gelecekte kaçak olmayacağı kanıtı değildir.
* Başlangıç/bitiş snapshot'larında adapters/interfaces/routes/dns alanları
  sıralamadan bağımsız aynı. Yerel snapshot'lar özel ağ bilgileri nedeniyle Git'e eklenmedi.

## Bu oturumda yapılmayanlar

Race detector (yerel uyumlu C derleyicisi yok), fiziksel ADB/USB/Android testi,
hotspot kapatma, standart RNDIS deneyi, native Wintun ring deneyi, gerçek
rota/DNS değişikliği, global kaçak testi, fiziksel kablo çekme ve operatör
sayacı ölçümü yapılmadı. Önceki cihaz kanıtları bu commit'in fiziksel testi
olarak sayılmadı. CI tanımı veya APK/MSI üretimi bu eksikleri geçerli kılmaz.

## Üretilen yerel paketler

`build/artifacts/tethermax-android-pairing-preview.apk`

SHA256: `ff7b39eb251966b7ceb891e4633c26d34ced5a8454bb62e8318aefc647a25596`

`build/artifacts/tethermax-windows-x64-usb-audit-preview.msi`

SHA256: `49fce62e3232a50c13547454f51a41e840ad75c5095d839e17c87421495705a8`

APK/MSI build çıktıları Git'e eklenmez. İncelenen resmî ve üçüncü taraf
kaynaklar [karşılaştırmada](pdanet-comparison-2026-10-23.md), ölçüm adımları
[kontrollü deney planında](controlled-usage-study.md). Gerçek rota onayı ancak
native koruma ve bağımsız kurtarma uygulanıp ayrı ortamda sınandıktan sonra
somut nesne listesiyle istenmeli; bu teslimat rota onayı istemeye hazır değildir.
