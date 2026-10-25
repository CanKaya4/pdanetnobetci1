## TetherMax Studio Edition · RC4

Windows ve Android için ortak Daylight / After Hours renkleri, özgün çevrimdışı vektör kediler, kalpler, gül, kısa atlanabilir açılış, hareket ve saydamlık tercihleri. Mevcut ağ motoru, USB, eşleştirme, rota/DNS ve hotspot altyapısı değiştirilmedi.

Windows X pencereyi tepsiye gizler; bağlantı temizliği çağırmaz. Tepsiden aynı pencere açılır. Çıkış onayı iptal edilebilir; onayda mevcut hotspot kurtarması ve bağlantı temizliği beklenir. Ana sayfa gerçek telemetri grafiği, otomatik veri birimleri ve oturum süresi gösterir. Android alt gezinme, dokunmatik grafik değerleri, haptik geri bildirim ve kalıcı görünüm tercihleri içerir.

### Doğrulama ve sınırlar

- Windows Release ve self-contained x64 MSI derlendi; APK mevcut yerel geliştirme anahtarıyla imzalandı (aynı uygulama kimliği, versionCode 15). Başka bir imzayla kurulan APK üzerine güncelleme garanti edilmez.
- Ağsız Windows UI testi: X/gizle, aynı pencereyi aç, çıkışı iptal et, gerçek çıkış, sekmeler, tema değişimi, 760/1120/1440 genişlikler ve veri birimleri geçti. Görsel PNG önizlemeleri incelendi.
- Eşleştirme testleri geçti. Mevcut bağlantı testinin ilk çalışmasında guardian-fixture zamanlama kontrolü başarısız oldu; tekrarda tüm kontroller geçti. Ağ motoruna müdahale edilmedi.
- Gerçek USB/cellular/hotspot sürekliliği, Windows kabuk tepsi çift tıklaması, farklı DPI monitörler ve Android cihaz/emülatör çalışma zamanı bu sürümde doğrulanmadı. Derleme ve ağsız UI testi bunların kanıtı değildir.
- Kurulu uygulamalar güncellenmedi: proje AGENTS.md fiziksel cihaz/Windows ağ aşaması için ayrı açık onay ister. MSI kimliği ve APK imzası korundu; tema/eşleştirme/hotspot depoları silinmez.
- Geçmiş veri, DNS engel sayacı, cihaz başına hız ve bufferbloat için ölçüm uydurulmaz. Henüz saklanmayan geçmiş grafik yoktur. Android süre, bu Activity'nin gözlemlediği etkin Windows bağlantı aralığıdır; Activity yeniden yaratılınca yeniden gözlemlenir.
- Açılışta tam sahnelenmiş gül açması/kedilerin gelişi, tüm durumlara özel pati/partikül animasyonları ve yerel Mica bu sürümde tamamlanmadı. Mevcut açılış kısa bir kedi/gül sahnesinin geçişidir.

### Dosyalar

`tethermax-android-1.0.0-rc4.apk`, `tethermax-windows-x64-1.0.0-rc4.msi` ve `SHA256SUMS-studio.txt`.

MSI per-user UpgradeCode korunur; MSI ürün sürümü 1.0.4, uygulama sürümü 1.0.0-rc.4. APK ve MSI bu sürümün önizleme paketleridir. Üretim kod imzası eklenmedi.
