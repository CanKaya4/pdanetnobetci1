# Onay bekleyen fiziksel test — otomatik çalıştırılmaz

**Henüz bu adımları çalıştırmayın.** Önce kullanıcıdan bu aşama için ayrıca açık
onay alınmalıdır. Mevcut PdaNet bağlantısını korumak için tercihen ayrı bir test
Windows bilgisayarı kullanılmalıdır. ADB sunucusunun başlatılması veya sürüm
uyuşmazlığı PdaNet'i etkileyebilir; mevcut bilgisayarda onaysız ADB bile çalıştırmayın.

## İlk onayın kapsamı

- Belirlenen test PC'sinde ve Galaxy A56'da APK kurulumuna, Android USB debugging
  yetkilendirmesine, uygulamanın hücresel bağlantı isteğine izin.
- Belirlenen tek cihaz için çakışmayan bir ADB forward; loopback SOCKS5 listener.
- Yalnızca SOCKS'u açıkça kullanan test trafiği; global proxy, DNS, adaptör,
  varsayılan rota, PdaNet veya MyPublicWiFi değişikliği yok.
- Küçük HTTP/HTTPS ve kontrollü UDP round-trip testleri; mobil veri kotası kullanır.
- İlk aşamada hız testi, uzun yük testi veya oyun trafiği yok.

## Onaydan sonra uygulanacak taslak

USB serial ve ADB sürümü test ortamında doğrulanır. APK sertifikası ve yayınlanan
SHA256 dosyaları kontrol edilir. Geliştirme imzalı APK telefon üzerinde kurulur;
TetherMax açılır, eşleştirme kodu alınır ve motor kullanıcı tarafından başlatılır.

```powershell
# YALNIZCA AYRI ONAYDAN SONRA, seçilen test bilgisayarında.
# DEVICE_SERIAL gerçek yetkilendirilmiş test cihazıyla değiştirilir.
adb -s DEVICE_SERIAL forward --no-rebind tcp:38473 localabstract:io.tethermax.usb.v1
# --no-rebind mevcut bir forward'ı değiştirmeyi reddeder.
$pairing = Read-Host 'Telefon ekranındaki eşleştirme kodu' -AsSecureString
$env:TETHERMAX_PAIRING_CODE = [System.Net.NetworkCredential]::new('', $pairing).Password
.\tethermax-windows-amd64.exe serve --approve-usb-test
```

Başka bir terminalde yalnızca o komut için SOCKS kullanılır:

```powershell
curl.exe --socks5-hostname 127.0.0.1:1088 --connect-timeout 10 --max-time 20 https://example.com/
```

Bu komut taslağı yalnızca TCP/HTTPS doğrular. UDP için kontrollü ve kullanıcıca
onaylanmış bir echo/DNS uç noktası seçilip paket boyutu, içerik, RTT ve kayıp
ölçülmelidir. Genel DNS veya üçüncü taraf sunuculara izinsiz yük testi yapılmaz.
Telefon mobil veri sayaçları ve sunucunun gördüğü çıkış adresi kontrol edilir;
başarısızlıkta Wi-Fi ya da PdaNet'e sessizce geri dönülmez. PC curl timeout ve
telefon hücresel durum hataları raporlanır; uydurma geçer sonucu verilmez.

Bitirirken TetherMax Ctrl+C ile kapatılır, yalnızca bu testin oluşturduğu forward
`adb -s DEVICE_SERIAL forward --remove tcp:38473` ile kaldırılır. `adb kill-server`
kullanılmaz. Eşleştirme ortam değişkeni kaldırılır, telefon motoru durdurulur.
Hiçbir Windows ağ yapılandırmasını geri yüklemek gerekmez: bu aşama değiştirmez.

## Sonraki ayrı onay aşamaları

1. A56 üzerinde ekran kapalıyken ve hücresel ağ geçişlerinde sürdürülebilir USB
   aktarımı, DNS, IPv4/IPv6, disconnect/reconnect, ısınma/enerji ve uzun oturum.
2. Windows sistem geneli TUN/UDP aktarımı için yalıtılmış test makinesi veya geri
   dönüş planı. Adaptör/rota/DNS değişiklikleri için yeni ve ayrı açık onay.
3. Ölçümlü, veri bütçesi belirlenmiş Türk Telekom 5G boşta/yük altında RTT, jitter,
   kayıp ve hız testi. Yer/saat/sinyal/5G NSA-SA koşulları, yön ve örnek sayısı.
4. Aynı koşullarda açık/kapalı şekillendirme A/B karşılaştırması. Yüzdelik RTT ve
   akış duraklaması gibi ölçütler; yalnızca ortalama ping yetmez.
5. Valorant, Deadlock, Genshin süreçlerinin normal Windows API'leriyle algılanması;
   oyun/anti-cheat'e enjeksiyon olmadan kontrollü profil değişimi. MyPublicWiFi
   paylaşımlı istemci trafiği ayrıca ölçülmeden destekleniyor denmez.
