TetherMax 0.1 — deneysel aktarım motoru önizlemesi / experimental engine preview.

Android APK ve Windows x64/ARM64 CLI paketleri. Windows ZIP dosyaları lisans bildirimlerini içerir. APK geliştirme anahtarıyla imzalıdır; Windows EXE'leri Authenticode imzalı değildir.

Uygulanan: Android hücresel ağ/DNS bağlama, eşleştirme doğrulaması, USB ADB akış protokolü, TCP ve UDP SOCKS5 aktarımı. PdaNet bağımlılığı veya uygulama içi oturum süresi sınırı yoktur.

Doğrulanan: Codex Cloud içinde gerçek Java–Go loopback veri aktarımı, 2 MiB ikili veri, eşzamanlı bağlantılar, TCP yarı kapatmaları, UDP paket sınırları/boş paketler, kötü protokol/kimlik reddi, shutdown ve race testleri. APK build ve v2/v3 imza doğrulaması; Windows PE x64/ARM64 build. Test raporu ektedir; bağımsız Windows ve Linux sonuçları repository Actions sayfasındadır.

Henüz doğrulanmayan: Fiziksel Android/USB/hücresel internet, Samsung A56/Türk Telekom 5G, oyun/anti-cheat ve MyPublicWiFi. Windows sistem geneli TUN/yönlendirme, otomatik oyun modu, adaptif bufferbloat şekillendirmesi ve akış bant genişliği yönetimi bu sürümde bulunmaz. UDP USB tarafında TCP üzerinden çerçevelenir; oyun gecikmesi iddiası yoktur.

GÜVENLİK: Kullanıcının mevcut PdaNet bağlantısına, MyPublicWiFi, adaptör, rota, DNS veya sistem proxy ayarlarına dokunulmaz. Varsayılan CLI yalnızca status gösterir. Fiziksel test için ayrıca açık kullanıcı onayı gerekir; --approve-usb-test bayrağı bu onayın yerine geçmez. ADB veya APK'yı mevcut bilgisayarda/telefonda henüz çalıştırmayın. Ayrı onaydan sonra docs/hardware-test-plan.md izlenir.

Operatör kota/tarife kurallarının aşılması garanti edilmez.

Kaynak ve inceleme: https://github.com/CanKaya4/pdanetnobetci1/pull/1
Araştırma: OpenTether (MIT), CAKE-autorate (GPLv2), sqm-autorate (MPL2.0); bu aşamada kaynakları kopyalanmadı. Go runtime lisansları Windows ZIP'lerinde bulunur.
