# Windows ve Android önizlemesi — kullanım

Bu sürüm normal Windows internetini otomatik değiştirmez. Yalnızca SOCKS kullanan
uygulamalar aktarılır. Sistem geneli TUN, oyun/film profilleri ve adaptif SQM
henüz uygulanmadı; arayüzde etkinmiş gibi gösterilmez.

1. `tethermax-android-pairing-preview.apk` dosyasını kurun. Android 12+ gerekir.
2. Telefonda TetherMax'i açıp **Hücresel motoru başlat** seçeneğine dokunun.
3. Windows MSI önizlemesini kurun veya taşınabilir ZIP'i bir klasöre açın.
   Taşınabilir klasör içindeki tüm dosyalar birlikte kalmalı.
4. `TetherMax.exe` dosyasını açın. Windows paketi .NET çalışma zamanını içerir.
5. Yetkili USB telefonu listede görünür. **Bağlan** seçeneğini kullanın.
6. İlk seferde iki ekrandaki kısa numaraları karşılaştırıp ikisinde de onaylayın.
   Sonraki bağlantılarda uzun kod veya terminal girişi gerekmez.
7. **İnternet testi**, 3 eşzamanlı küçük HTTPS isteği ve 5 UDP DNS sorgusu çalıştırır.
   Veri kullanır; hız testi veya güvenilir uzun dönem kayıp ölçümü değildir.
8. **Bağlantıyı kes**, yalnızca bu uygulamanın motorunu ve oluşturduğu forward'ları
   kapatır. Eski ADB sunucusu, başka forward'lar ve diğer uygulamalar korunur.
9. **IP motoru testi**, bağlı telefona bellek içi gVisor IPv4 paketleri üzerinden
   1 küçük HTTPS ve 5 UDP DNS sorgusu gönderir. Wintun yüklemez, Windows adaptörü,
   rota veya DNS ayarı değiştirmez. Yeni yolun cihaz testi henüz tamamlanmadı;
   ayrıntılar [ip-engine.md](ip-engine.md). Bu düğme sistem geneli interneti açmaz.

ADB: bu önizleme `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe` konumundaki
mevcut resmî SDK'yı kullanır. ADB otomatik indirme/kurma henüz yok. Yetkisiz
cihazda USB hata ayıklama onayını telefonda vermeniz gerekir. ADB sunucu sürüm
çakışmasının mevcut yazılımları etkileyebileceği unutulmamalı; geliştirme ortamında
aynı mevcut ADB kurulumu kullanıldı. Başka cihazlara otomatik forward yapılmaz.

1088 doluysa uygulama başka boş localhost portu seçer ve tanılamada gösterir.
Sistem proxy'si, DNS, Wi-Fi, varsayılan rota, sürücü veya firewall değişmez.
Otomatik yeniden bağlantı isteğe bağlıdır; tercih ve seçilen seri sonraki
açılışta hatırlanır. Telefon servisinin hazır olması gerekir. Windows açılışında
otomatik başlatma, sistem tepsisi ve Android OS tarafından öldürülme sonrası
tam otomatik servis kurtarma bu sürümde yoktur.

Raporlar: `%LOCALAPPDATA%\TetherMax\reports`. Eşleştirme kimliği aynı uygulama
veri klasöründe DPAPI korumalıdır; loglarda kod veya özel anahtar bulunmaz.
Eşleştirmeyi sıfırlamak için iki cihazda da unut seçeneğini kullanın.

MSI kullanıcı hesabına kurulur; sürücü veya ağ ayarı yüklemez. MSI henüz gerçek
kurulum/kaldırma döngüsünden geçirilmediyse doğrulama raporunda ayrıca belirtilir.
Önizleme EXE/MSI Authenticode imzalı değildir; APK geliştirme imzalıdır.

Geliştirici: `scripts/build-local.ps1`, `dotnet run --project
windows/TetherMax.PairingTests -c Release`, `dotnet publish
windows/TetherMax.Desktop -r win-x64 --self-contained true`. MSI için yerel
`.ci-tools/wix` altına WiX 6.0.2 hazırlanır ve `scripts/package-windows.py`
çalıştırılır. Bunlar son kullanıcı işlemi değildir.
