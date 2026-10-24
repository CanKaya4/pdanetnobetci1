# TetherMax 0.7 kabul kaydı

24 Ekim 2026. Bu dosya kaynak/izole doğrulama ile fiziksel cihaz sonuçlarını ayırır.

## Tamamlanan kayıtlar

- Kaynak başlangıcı: temiz `tethermax-foundation`, `1026e29`.
- Başlangıç kurulu Windows: 0.6.0; telefon APK'sı: 0.5.0-usb-preview / code 4. Telefon APK'sı çekilerek saklandı.
- Yeni APK aynı sertifikayla `install -r` üzerinden güncellendi; Keystore ve uygulama verisi temizlenmedi.
- MSI güncellemesi çıkış kodu 0; Windows 0.7.0 açıldı. Eşleştirme yeniden kod sormadan çalıştı.
- Bağımsız SYSTEM kurtarma ön kontrolü çıkış kodu 0.
- 0.6 gerçek bağlantı ve kapanış: HTTPS/DNS/UDP geçti; kapanıştan sonra seçili rota Ethernet 5'e döndü.
- 0.7 düzeltilmiş soğuk Android servis başlangıcı: üç sağlık kontrolü geçti; gerçek Wintun çıkışı doğrulandı.
- Windows ve Android açık/koyu gerçek ekranları görüntülendi ve yerel raporlara kaydedildi; siyah/boş ekranlar teslim ekranı olarak kullanılmaz.
- Canlı Film / Dizi seçimi: telefon onayı; aynı seçili Wintun rotası.
- Tüm motor testleri, Go vet, çevrimdışı kurtarma ve eşleştirme kontrolleri geçti. Dokuz bağlantı kontrolü, eşzamanlı Dispose beklemesini de kapsar.

## Son paket kabulü

Son kaynak paketinin yeniden kurulması, arayüzden profil onayı, sahip olunan USB forward kaybından toparlanma ve son kurulu dosya/hash eşleştirmesi aşağıda gerçek sonuçlarla tamamlanır. Önceki paketin başarılı sonucu son paket için otomatik başarı sayılmaz.

## Kanıt konumları

Yerel `build/reports`: baseline-060-live.json, new2-070-live.json, profile-live-070.json, connection-070-offline.txt, network-070-offline.txt, pairing-070-offline.txt, windows-070-connected.jpg, windows-070-real-light.jpg, android-070-dark.png, android-070-light.png.

## Ölçülmemiş senaryolar

Fiziksel kabloyu elle çıkar/tak, çok saatli yük testi, radyo kapsama kaybı, kalibre yük altında ping/hız A/B ve istatistiksel başarısızlık oranı ayrıca kayıt gerektirir. ADB forward kaybı testi fiziksel kablo testi diye raporlanmaz. Hücresel veri sayaçları operatör faturalandırması sayılmaz.
