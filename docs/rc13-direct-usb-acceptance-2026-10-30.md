# RC13: Windows ve Galaxy A56 kurulumu / doğrudan USB kabulü

30 Ekim 2026, yaklaşık 00:32–00:37 Türkiye saati.
Ürün kaynak commit'i: `feee14a67c27ef81a353590c3f34d02f52d476e7`.
Bu belge sonradan eklenen fiziksel kanıttır; önceki RC13 derleme notlarındaki
"henüz cihaz testi yapılmadı" durumu bu sınırlı kontrol için güncellenmiştir.

## Gerçekten tamamlananlar

- Kullanıcının açık kurulum talimatıyla Windows MSI ve bağlı Galaxy A56 APK'sı
  güncellendi. Önceki uygulama dosyaları, APK ve Windows ayarları yerelde yedeklendi.
- Yalnızca eski TetherMax GUI kapatıldı; guardian/worker kendi geri alma
  akışlarıyla çıktı. PdaNet/MyPublicWiFi, APN, TTL ve Android standart paylaşım
  ayarları değiştirilmedi. Mevcut profil/bütçe ayarlarına yeni tavan dayatılmadı.
- Windows kurulu sürüm: `1.0.0-rc.13+feee14a67c27ef81a353590c3f34d02f52d476e7`.
- Android: `versionCode=24`, `1.0.0-rc.13-smart-network`.
- Windows exe/dll, network guardian exe/dll, Go engine ve Wintun dosyalarının
  SHA-256'ları publish klasörüyle birebir eşleşti. Telefona kurulan APK geri
  okundu; Release APK'sıyla SHA-256 birebir eşleşti. Veri silme/uninstall yapılmadı.
- Eşleştirme ve Windows mevcut ayar dosyaları kurulumda korundu. Yeni sürümle
  bir kez bağlantı başlatıldı; çalışan bağlantı açık bırakıldı.

## Doğrudan USB yolu

| Kontrol | Sonuç |
|---|---|
| Android USB fonksiyonları | `sec_charging,adb`; `rndis` / `ncm` yok |
| Android güncel tethering durumu | Upstream istenmiyor, upstream interface yok, aktif TetheredState yok |
| Telefon relay kaynak ağı | Kimliği doğrulanmış kontrol kanalında test öncesi/sonrası CELLULAR |
| Windows çıkış yolu | `Find-NetRoute` ile seçilen sahipli TetherMax Wintun arayüzü; iki /1 rota |
| Normal Windows HTTPS | HTTP 200, proxy yok, varsayılan TLS sertifika doğrulaması açık |
| Windows sistem DNS | IPv4 yanıtı alındı |
| Wintun'a bağlanmış UDP DNS | Geçerli 61 bayt yanıt alındı |
| Test aralığında Wintun sayacı | 1.201 B gönderildi / 7.377 B alındı |
| Android relay farkı | 2.531 B yükleme / 204.269 B indirme; eşzamanlı kullanıcı trafiği dahil |

Bu sayılar birbirine eşit olmak zorunda değildir; farklı katman ve zaman
aralıkları ile eşzamanlı trafik içerir. Hız testi veya sentetik büyük indirme
başlatılmadı. Tek gözlemin DNS/HTTPS süreleri kararlı oyun pingi sonucu sayılmaz.

Windows veri sayacı yeni motor oturumunu ve artan örnek sırasını diske yazdı.
Bütçe 0 (uyarı kapalı) kaldı. Güncelleme sonrasında yapılan gözlem anında
stability.log'a yeni uygulama hatası yazılmamıştı. Bu birkaç dakikalık gözlem
bellek/termal kararlılık veya 24–72 saat kabulü değildir.

Doğrulama yardımcısının ilk denemeleri birden fazla eski ADB forward eşlemesi,
stdin satır sonu ve gizli/eski arayüzlerin ayrımı nedeniyle durdu; bunlar başarılı
fiziksel test sayılmadı. Nihai yardımcı yalnızca aynı güvenilen telefona ait
mevcut mapping'i kullanır, LF çerçevesini gönderir ve gerçek seçilen rota
arayüzünü sınar. Yeni forward/rota oluşturmaz veya eski eşlemeleri silmez.
Sonuç kaydı `passed:true` ve üç bağlantı kontrolü `passed:true` ile üretildi.

## Operatör kotası: doğrulanmayan bölüm

Kullanıcı operatör uygulamasında ayrı hotspot sınıflandırma sayacı görmediğini
belirtti. Bu nedenle bu çalışma **operatörün normal mobil kota/hotspot kota
ayrımını ölçmez**. Standart Android paylaşımı olmadan çalışan uygulama soketi
→ USB → Windows yolu fiziksel olarak doğrulandı; kota muafiyeti vaat edilmez.

[Güncel kaynak araştırması ve teknik sınırlar](direct-usb-mobile-data-2026-10-30.md).

Henüz yapılmayanlar: 24–72 saat test, farklı OEM/Windows sürümleri, fiziksel
kablo/kapsama kaybı döngüsü, oyun+TV yükü altında kıyaslama ve operatör faturası
karşılaştırması. Bu sınırlı kabul bunların yerine geçmez.
