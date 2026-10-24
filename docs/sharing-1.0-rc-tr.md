# TetherMax 1.0 RC — Windows ICS ve MyPublicWiFi

24 Ekim 2026. RC paketidir; TV kabul sonucu ayrıca kaydedilir.

MyPublicWiFi 31.3'ün Router Mode ve ICS kaynak listeleri bu bilgisayarda Wintun'u göstermedi. Yönetici bağlamındaki Windows HNetCfg.HNetShare listesi ise etkin Wintun adaptörünü gösterdi. Bu nedenle MyPublicWiFi'nin desteklenmeyen bir API'si veya veritabanı yazımı kullanılmaz: MyPublicWiFi **No Internet Sharing** ile mevcut SSID/parolayı yayınlar, TetherMax Windows'un INetSharingConfiguration arayüzüyle Wintun'u public ve seçilen Microsoft Wi-Fi Direct adaptörünü private yapar.

Windows Wi-Fi radyosu açık olmalıdır. İlk erişim noktası başlatma hatasında `Hardware On / Software Off` gözlendi. Kullanıcı Windows Wi-Fi anahtarını açınca erişim noktası başladı. `Hosted network supported: No`, Wi-Fi Direct'in desteklenmediğini tek başına kanıtlamaz.

## İşleyiş

1. MyPublicWiFi: No Internet Sharing → Start Hotspot. Mevcut SSID/parola korunur.
2. TetherMax: Bağlan → yönetici onayı.
3. TV ve Wi-Fi: teşhisi yenile, etkin Wi-Fi Direct adaptörünü seç, Paylaşımı başlat → yönetici onayı.
4. TV'yi mevcut Wi-Fi ağına bağla; kısa gerçek video testi yap.

Wintun'un istenen adı/GUID'si sabittir; Windows'un verdiği gerçek GUID ayrıca doğrulanır. Mevcut bir adaptör açılıp sahiplenilmez. Önceki kurtarma tamamlanmadan yeni bağlantı kurulmaz.

Paylaşım mevcut korumalı TetherMax oturumuna bağlanır. Başka ICS paylaşımı varsa etkinleştirme reddedilir. Orijinal private IPv4/DNS/DHCP durumu korumalı oturumda saklanır. ICS açılmadan önce WFP işlemi içinde yerel DHCP/DNS yanıtları yalnızca seçilen interface index için serbest bırakılır. Aynı anda IPFORWARD katmanında bu private LUID'den Wintun dışına IPv4 yönlendirme ve tüm IPv6 yönlendirme engellenir. Ayrı interface-index ve LUID alanları WFP'nin aynı alandaki OR davranışından kaçınır.

Bağlantı kapanırken ICS private/public sırasıyla kapatılır, değiştirilen özel IP ayarları geri alınır; ardından ana tünel rotaları ve filtreleri kaldırılır. Başka bir işlem paylaşımı değiştirmişse körlemesine üzerine yazılmaz; hata halinde koruma tutulur. Aynı günlük/watchdog/SYSTEM görevi bu paylaşım kaydını da kurtarır. Guardian paylaşım kaynağını ve forwarding filtrelerini periyodik denetler. Hotspot yeniden oluşturulup kimlik/index değişirse paylaşım için yeniden bağlantı gerekir.

MyPublicWiFi DHCP/NAT çalıştıran Router Mode ile bu Windows ICS katmanı aynı anda kullanılmamalıdır. MyPublicWiFi SSID, parola, hesap veya veritabanına TetherMax tarafından yazılmaz. UI'deki komşu sayısı TV sayısı, toplam tünel trafiği TV verisi veya operatör hotspot sayacı olarak sunulmaz. NAT sonrasında TV/Windows akışları her durumda ayırt edilemez; ayrı TV uygulama sınıflandırması iddiası yoktur. Mevcut profil motoru korunur.

## İzole doğrulama

Windows masaüstü/guardian derlemesi, Java/Go motor ve vet testleri; eşleştirme ve bağlantı regresyonları; kurtarma süreç hataları geçti. Yeni mocked COM testi gerçek üretim PowerShell komutlarını çalıştırarak tam/yarım ICS aktivasyonundan dönüşü, private-before-public kapatmayı, orijinal IP/DNS/DHCP geri almayı ve başka paylaşıma dokunmama davranışını sınar. Bunlar gerçek NAT/video testi değildir.

Yönetici kurtarma ön kontrolü exit 0. Gerçek paylaşım, TV videosu, hotspot-off kullanımı ve yeniden bağlantı için aşağıdaki kabul kaydı esas alınır. Türk Telekom operatör muhasebesi ölçülmedi.

## Kaynaklar

- [MyPublicWiFi resmi özellikleri ve paylaşım modları](https://www.mypublicwifi.com/publicwifi/en/)
- [Wintun RequestedGUID ve NLA kimliği](https://github.com/WireGuard/wintun/blob/master/README.md)
- [Microsoft WFP katmanları](https://learn.microsoft.com/en-us/windows/win32/fwp/management-filtering-layer-identifiers-)
- [Microsoft WFP header GUID tanımları](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/fwpmu.h)
- [PdaNet USB Mode / Hide Tether Usage](https://pdanet.co/help/devices.php)

PdaNet resmi belgeleri USB ve WiFi Share davranışını açıklar; özel gizleme algoritmasını açıklamaz ve Türk Telekom tarife sınıflandırmasının kanıtı değildir. TetherMax CELLULAR TCP/UDP/DNS soketlerini kullanır; TTL/başlık değişikliği eklenmez. Aboneye özgü yazılı tarife ve doğrulanmış operatör sayaçları olmadan kota bağımsızlığı belirsiz kalır.

## 1.0 RC canlı kabul sonucu — 24 Ekim 2026

- Kaynak 89ac590; 1.0.0-rc.1 Windows MSI ve Android APK kuruldu. MSI exit 0; APK install -r Success. Kurulu Desktop/Network DLL SHA-256 değerleri paketle eşleşti. Windows DPAPI identity.bin yedekle aynı kaldı. Yeniden eşleştirme kodu gerekmedi.
- Sabit Wintun adı ve gerçek GUID: TetherMax-IP-7f568777 / 7f568777-013f-4a46-9436-544554484552. Yeni sürümde gerçek Windows HTTPS 200, sistem DNS ve doğrudan UDP/DNS başarılı. Kanıt yerel build/reports/rc1-windows-live.json. Bu kayıt uygulama başlatma süresi dağılımı değildir.
- Android gerçek ekranı incelendi: Windows bağlı, CELLULAR Network 212 / rmnet0, gerçek sayaçlar ve Oyun profili göründü; boş/siyah ekran değildi. Windows gerçek ana/paylaşım ekranları görüntülendi. Paylaşım adaptörü seçicisinin koyu temada metin kontrastı yetersiz; görsel eksik olarak bırakıldı.
- MyPublicWiFi No Internet Sharing erişim noktası Wi-Fi radyosu açıldıktan sonra başladı. TetherMax Paylaşımı başlat komutu UAC ile exit 0 döndü. Private adaptörde Windows ICS 192.168.137.1/24 ve DHCP/DNS svchost dinleyicileri gözlendi. IPv4/IPv6 IPFORWARD filtre kurulum çağrısı başarılı oldu; bu, gerçek TV yolunun kanıtı değildir.
- TV tekrar denemelerinde WLAN günlüğü WPA2/AES kimlik doğrulamasını başarılı gösterdi; kullanıcı TV'nin bağlanıyor aşamasında kalıp bağlanamadı dediğini bildirdi. Otomatik IP/bağlantı kabulü başarısız, video oynatılmadı. Elle IP tanılama seçeneği kullanıcı tarafından bulunamadı; başarılı manuel veya DHCP kabulü yoktur. Kök neden paket düzeyinde henüz doğrulanmadı; DHCP veya MyPublicWiFi uyumluluğu kesin çözülmüş sayılmaz.
- Sınırlı gözlem aralığında Wintun sayaçları arttı, Ethernet 5/RNDIS gönderme ve alma farkları sıfırdı. Bu toplam sayaç kaydı TV trafiğinin kanıtı değildir. Yerel build/reports/tv-before.json ve tv-after.json.
- Standart Android USB tethering başlangıçta rndis0 TetheredState idi; kapalı kullanım bu çalışmada sınanmadı. Wi-Fi hotspot-off ve operatör kota bağımsızlığı kabulü tamamlanmadı.
- Önceki 0.7 normal kapanışı bu güncellemeden önce kurtarıldı; RNDIS rotasına dönüş ve guardian/worker kapanışı kontrol edilmeden MSI kurulmadı. Yeni ICS etkin oturumunun kapanma/yeniden bağlanma kabul testi henüz yapılmadı. İzole paylaşım geri dönüş testleri bu canlı kabulün yerine geçmez.

Kullanıcı kullanım hakkı nedeniyle hızlı tamamlanmasını istedi. Yeni uzun test, veri yükü veya tahmine dayalı ağ değişikliği başlatılmadı. Windows bağlantısı ve mevcut kurulum bırakıldı. A-H tamamlanmadığından günlük TV kullanımına hazır final 1.0 kabulü VERİLMEDİ. Türk Telekom hotspot kotası BELİRSİZ. APK/MSI yerel build/artifacts altında; hassas ham ağ/cihaz kayıtları Git'e eklenmez.
