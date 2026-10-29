# TetherMax RC7 — 25 Ekim 2026 doğrulama kaydı

Windows ve bağlı Galaxy A56'ya `1.0.0-rc.7` kuruldu. Android versionCode: 18.
Kurulum sonucu `RC7_INSTALL_SUCCESS`; MSI çıkışı 0. Eşleştirme ve kullanıcı
ayarları kurulum öncesi yedeklendi ve kurulum sonrası hash karşılaştırmasıyla
korundukları doğrulandı. Kaynak ve paketler bu çalışma alanının `fix-session/`
klasöründedir. Bu belge tüm gün kesintisiz kullanım garantisi değildir.

## Teşhis ve uygulanan değişiklikler

- Kurulu RC5'in zamanlayıcısı başarısız bağlantı sonrası yeniden `runas`
  yardımcısı başlatabiliyordu. Teslim edilen sürümde otomatik bağlantı/elevation
  yolu engellenir; tekrar için kullanıcı Bağlan'ı seçer. Timer reentrancy koruması,
  başarısız hotspot temizliğini tekrar başlatmama ve kapanan yardımcının IPC
  bağlantısını beklemeyi erken sonlandırma düzeltmeleri pakete dahil edildi.
- Yeni bağlantının yönetici yardımcısı, kontrolcü kilidini aldıktan sonra ve yeni
  adaptörü etkinleştirmeden önce yalnızca sahipliği doğrulanmış eski TetherMax
  oturumlarını kurtarır. Aktif yabancı oturum değiştirilmez; başarısız kurtarmada
  yeni bağlantı durdurulur. Günlük kullanımda masaüstündeki onarım betiğini her
  başlangıçta ayrıca açmak amaçlanan akış değildir. Windows UAC devre dışı bırakılmadı.
- Hücresel durum telemetrisinin geçici olarak kaybolması artık çalışan USB
  oturumunu otomatik kapatmaz. Gerçek telefon/kanal ayrılması ve süreç hatası için
  mevcut güvenli kapanış korunur. Şebeke döndüğünde yeni akışlar tekrar kurulabilir;
  kesilmiş TCP/oyun oturumunun aynen devam edeceği iddia edilmez.
- Android'de son 15 saniyedeki kimliği doğrulanmış Windows telemetrisi varken,
  20 saniyelik CPU uyku kilidi yenilenir. Telemetri kesilince ve servis durunca
  bırakılır; ekran açık tutulmaz. Windows ağ yardımcısı da yalnızca bağlantı ömrü
  boyunca sistem uyku isteği tutar; ekrana açık kalma isteği koymaz. Kullanıcının
  elle uyutma/kapak kapatma davranışını engellemez.
- Mobil **Analiz → Telefon konumu testi**: hücresel ağa bağlı iki hedefe toplam
  20 kısa TCP el sıkışması, medyan/P95, hedef içi ardışık değişim ve başarısız
  örnek oranı. İptal ve ağ değişimi denetimi vardır; son tamamlanmış sonuç saklanır.
  Bu ölçüm GPS, Mbps hız testi veya oyun sunucusu pingi değildir. Konum önerisinin
  eşikleri uygulama sezgisidir; operatör sertifikasyonu değildir.

Android'in foreground servisi CPU'yu kendiliğinden uyanık tutmaz:
[Android Developers açıklaması](https://android-developers.googleblog.com/2026/03/battery-technical-quality-enforcement.html).
Windows'ta ekran ve sistem uyku gereksinimleri ayrıdır:
[Microsoft güç yönetimi açıklaması](https://learn.microsoft.com/en-us/windows/win32/power/system-sleep-criteria).

## Tamamlanan testler ve kanıt dosyaları

Kanıtlar `../build/reports/` altındadır.

| Kontrol | Sonuç | Kanıt |
|---|---|---|
| Windows bağlantı regresyonları | 16 PASS; özel IPC, iptal, hazır olma, eşzamanlı kapanış, DPAPI ayarları | `rc7-connection-native.txt` |
| Windows kurtarma regresyonları | 22 PASS; sahte ağ backend'i, çökme/stall, yetim oturum ve sahiplik denetimleri; gerçek güç API'si aç/kapat | `rc7-network-native.txt` |
| WPF arayüz regresyonu | 100 otomatik istek elevation/motor açmadı; pencere/çıkış/tema/boyut testleri geçti | `rc7-ui.txt` |
| Android saf Java regresyonları | 100 servis yeniden başlatma döngüsü; CPU lease süresi ve konum sınıflandırması geçti | `rc7-android-unit.txt` |
| Java relay + Go motoru | Gerçek localhost TCP/UDP/DNS, DoH fixture/cache testleri ve 5 Go paket testi geçti; Go vet geçti | `rc7-go-test-native.txt`, `rc7-go-vet.txt` |
| APK | Derlendi; APK imzası doğrulandı; fiziksel cihaza güncellendi | `rc7-build.txt`, `rc7-install.txt` |
| MSI ve kurulu dosyalar | Kurulum başarılı; kurulu EXE/DLL/motor/Wintun hash'leri yayın klasörüyle eşleşiyor | `rc7-msi.log`, `rc7-installed-hashes.json` |
| Gerçek Windows USB yolu | Seçili rota TetherMax; normal HTTPS 200 + sistem DNS + UDP DNS geçti; adaptörde iki yönlü baytlar görüldü | `rc7-physical-health.json` |
| Gerçek ekran kapalı örnekleri | 15/15 örnekte Android Dozing; CPU aktarım kilidi açık; 15/15 DNS yanıtı | `rc7-screen-off.json` |
| Mobil konum testi | 20/20 başarı; TCP medyan 36 ms, P95 81 ms, ardışık değişim 33 ms; kullanılabilir/karşılaştır önerisi | `rc7-position-result.png` |

İlk sandbox testleri yerel socket/pipe/dosya izinlerine takıldı; normal Windows
ortamında aynı testler yeniden çalıştırılıp geçti. Bunlar ürün hatası olarak
raporlanmadı. Go race testi CGO/C derleyicisi bulunmadığı için **çalıştırılamadı**.
İlk NuGet restore'da güvenlik bildirim servisine erişilemedi; paket güvenlik açığı
taramasının geçtiği iddia edilmez. Son publish derlemeleri önbellekteki
bağımlılıklarla tamamlandı.

## Gerçek yük ölçümleri

Her test mevcut TetherMax IPv4 adresi `10.254.88.2`'ye bağlanan dört HTTPS range
aktarımıyla **48 MiB** payload indirdi. Her birinde aktarım tamamlandı, hata
listesi boştu. Toplam dört yük testi 192 MiB uygulama payload'ı kullandı; operatör
muhasebesi daha yüksek olabilir. RTT sütunları **UDP DNS işlem süresidir**;
oyun/ICMP pingi değildir. Hat ve arka plan yükü değiştiği için bu sıra kontrollü
bir laboratuvar A/B deneyi sayılmaz.

| Ayar | Ölçülen yük indirmesi | Yük medyanı | Yük P95 | Ardışık değişim |
|---|---:|---:|---:|---:|
| İlk sınırsız test | 65,24 Mbps | 125 ms | 344 ms | 106,61 ms |
| Ekran kapatma komutundan sonraki test* | 123,29 Mbps | 125 ms | 219 ms | 51,21 ms |
| Adaptif açık, sabit tavan yok | 44,82 Mbps | 109 ms | 625 ms | 126,00 ms |
| Adaptif açık, 30/5 Mbps tavan | 22,16 Mbps | 94 ms | 156 ms | 28,51 ms |

Dosyalar: `rc7-load-screen-on.json`, `rc7-load-screen-off.json`,
`rc7-load-adaptive.json`, `rc7-load-capped.json`.

*İkinci yük testi boyunca ekranın kesintisiz kapalı kaldığı doğrulanmadı;
sonrasında ayrı `rc7-screen-off.json` testi her örnekte gerçek Dozing durumunu
doğruladı. İki test birbirinin yerine kullanılmaz.

Kullanıcı son olarak adaptifi ve 30 Mbps indirme / 5 Mbps yükleme tavanını
arayüzden uyguladı. Son ölçüm daha düşük yük gecikmesine işaret ediyor; bu
ayarın her saat/operatör/konum için en iyi olduğu kanıtlanmadı. 5 Mbps yükleme
tavanının optimumluğu ayrıca upload yüküyle ölçülmedi.

## Açık kalan kabul koşulları

- Steam süreç olarak zaten açıktı. Yeniden görünür açma denemesi otomasyon
  aracında hedeflenebilir pencere üretmedi; yeni oyun oturumu/Steam arayüzü altında
  hatanın tekrar üretilemediği kanıtlanmış sayılmaz. Yük testleri sırasında aynı
  masaüstü, motor ve iki ağ yardımcısı PID'leri değişmeden çalıştı.
- Gerçek kablo çıkar/tak, bilgisayar yeniden başlatma ve saatler/günler süren
  kullanım bu çalışmada yeniden sınanmadı. Otomatik yeniden bağlanma uğruna
  kullanıcı istemeden arka arkaya UAC açılması özellikle engellenmiştir.
- TV'de gerçek film/dizi ve Wi-Fi hotspot istemci kabulü bu çalışmada
  tamamlanmadı. Vodafone kullanan ikinci telefon fiziksel olarak test edilmedi.
- Hücresel radyo kapasitesi, baz istasyonu yoğunluğu, operatör yönlendirmesi ve
  oyun sunucusu gecikmesi uygulamanın kontrolünde değildir. Sıfır jitter, kusursuz
  bütün gün bağlantı veya hotspot kotasından muafiyet kanıtlanmadı/garanti edilmez.

Bu sürüm doğrulanan uygulama kusurlarını gideren ve kısa fiziksel testlerden
geçen bir düzeltmedir; yukarıdaki açık koşullar tamamlanmış gibi sunulmamalıdır.
