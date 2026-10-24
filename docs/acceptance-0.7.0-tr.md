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

Son kaynak paketi f197610 tekrar MSI/APK ile kuruldu. MSI çıkış kodu 0. Windows uygulama ve guardian DLL hash'leri, APK ise telefondan geri çekilip karşılaştırılarak paketlerle eşleşti. Go binary `vcs.modified=false`, kaynak f197610. Windows DPAPI identity.bin başlangıç yedeğiyle aynı.

Çift Bağlan tıklaması: tek deneme kimliği. Seçili Wintun rotası 12.020,9 ms; bağımsız sağlık kontrolleri 12.548,2 ms içinde tamamlandı. HTTPS 200, sistem DNS ve UDP/DNS başarılı. GUI aşama kaydı: telefon 193 ms; servis 238 ms; eşleştirme 95 ms; worker hazır toplam 5.637 ms; ağ etkin 11.046 ms; bağlı 11.633 ms. UAC/kullanıcı beklemesi dahildir.

Önceki 0.7 GUI süreci sonlandırma testi: guardian/worker'a müdahale edilmeden seçili rota 2.267,8 ms içinde Ethernet 5'e döndü. Bu bir GUI çökmesi örneğidir; fiziksel USB testi değildir.

Gerçek son kurulu bağlı ekranlar: `build/reports/windows-070-final-connected.jpg` ve `android-070-final-connected.png`. Kaydedildikten sonra görüntü içerikleri okunarak doğrulandı. Son Windows ekranında Galaxy A56 bağlı, süre, gerçek hız/veri/akışlar; telefonda Windows bağlı, CELLULAR kaynak ve Oyun profili görünür. Ölçülmeyen RTT/jitter boş bırakılır.

Kullanıcının diğer sohbette verdiği açık koordinasyon onayı kayıt üzerinden doğrulandı. Bundan sonra bu sohbette yeni canlı ağ/kurulum veya ürün kaynak düzenlemesi başlatılmadı; yalnızca kabul dokümantasyonu tamamlandı. Bağlantı açık bırakıldı. Diğer sohbetin Wintun sabit kimliği/SharingPanel/SharingHost/sharing-audit değişiklikleri korunur, bu 0.7 teslimine eklenmez.

Arayüzden canlı profil seçimi, sahip olunan forward kaybından otomatik toparlanma, UAC reddi, fiziksel kablo, Android döndürme/ekran kapatma ve uzun süreli kabul bu son pakette tamamlanmış sayılmaz. Profil CLI telefon onayı ve aynı rota testi tamamlanmıştır; UI'nin telemetri onayı davranışı için ayrıca kabul gerekir.

## Kanıt konumları

Yerel `build/reports`: baseline-060-live.json, new2-070-live.json, profile-live-070.json, connection-070-offline.txt, network-070-offline.txt, pairing-070-offline.txt, windows-070-connected.jpg, windows-070-real-light.jpg, android-070-dark.png, android-070-light.png.

## Ölçülmemiş senaryolar

Fiziksel kabloyu elle çıkar/tak, çok saatli yük testi, radyo kapsama kaybı, kalibre yük altında ping/hız A/B ve istatistiksel başarısızlık oranı ayrıca kayıt gerektirir. ADB forward kaybı testi fiziksel kablo testi diye raporlanmaz. Hücresel veri sayaçları operatör faturalandırması sayılmaz.

## CI

Kod f197610 için iki akış da `success`: https://github.com/CanKaya4/pdanetnobetci1/actions/runs/40075902201 ve https://github.com/CanKaya4/pdanetnobetci1/actions/runs/40075898862 . İkinci akış fiziksel telefon yerine ayrı, tek kullanımlık Windows runner ve loopback fixture kullanır.
