# TetherMax 0.6.0 yerel kullanım kabulü — 24 Ekim 2026

Windows MSI 0.6.0 ve Android APK bu bilgisayara kuruldu. Gerçek USB/Wintun
aktarımına geçildi. Standart Android USB tethering kapalı kullanım doğrulanmadı;
A–D kabulü bütünüyle tamamlandı veya ürün tamamen hazır iddiası yoktur.

## Gerçek kabul kanıtı

- Son sınırlı oturum: native-20261023-214739-2137731.json (yerel TetherMax/reports).
  120 saniye / 1 MiB relay bütçesi; otomatik Windows HTTPS 200 (336 ms), sistem
  DNS (41 ms) ve doğrudan UDP DNS (36 ms, 61 bayt yanıt) başarılı.
- İki /1 rota, sahip olunan Wintun adaptörü ve WFP aktifken sıradan Windows
  soketleri kullanıldı. SOCKS başarı ölçütü olarak kullanılmadı. Adapter örneği
  1706 gönderilen / 6578 alınan bayt. Bu yalnızca kısa gözlem aralığıdır.
- Oturumun host/Android aktarım farkları birebir eşleşti: 730587 B yükleme,
  55727 B indirme. Android CELLULAR / rmnet0 bildirdi. Operatör sayacı değildir.
- İlk iki canlı kapanıştan birinde guardian, watcher'ın tuttuğu recovery.lock
  nedeniyle erken `recovered:false` bildirdi; watcher temizliği tamamlamıştı.
  Bu yarış düzeltildi: kurtarma kilidi sınırlı süre beklenir, ardından tamamlanmış
  işlem tekrar uygulanmaz. Son sınırlı oturum `recovered:true` ile kapandı.
- Sınırlı oturumlar ve günlük uygulama kapanışından sonra adaptör, IP arayüzü,
  rota ve DNS snapshot'ları başlangıçla birebir aynı. NRPT üçüncü test öncesi ve
  sonrası ayrıca kaydedildi. Yedek RNDIS üzerinden HTTPS 200 geri geldi.
- Günlük modda MaxSeconds=0 ve MaxPayloadBytes=0 kullanıldı. Yaklaşık üç dakikalık
  gerçek kullanımda 120 saniye/1 MiB kesilmesi olmadı. Normal HTTPS 200 döndü;
  kullanıcı yükü dışında hız/indirme yük testi başlatılmadı.
- Uygulama kapatılıp yeniden açıldı. Güvenilen telefon kod istemeden tanındı;
  kaydedilmiş yeniden bağlantı tercihi ve Windows UAC onayıyla yeni günlük
  Wintun oturumu açıldı. Yeni oturum normal HTTPS 200 verdi ve açık bırakıldı.
- Ethernet 5'e açıkça sabitlenmiş negatif TCP denemesi başarı sağlamadı fakat
  timeout verdi; bu sonuç tek başına WFP'ye atfedilmiş kesin kaçak kanıtı değildir.
  Ardından bir normal HTTPS isteğinde de tek timeout görüldü; IPv4/sabit adres,
  DNS ve yeniden normal hostname HTTPS kontrolleri başarılı oldu. IPv6 engellidir;
  IPv6 gerektiren uygulamalar destek kapsamında değildir.

## Uygulama davranışı

Bağlan artık sistem geneli günlük Wintun yolunu açar. Başarılı sınırlı
HTTPS/UDP/DNS + kurtarma raporu olmadan günlük mod açılmaz. Tanılama butonu
120 saniye/1 MiB kalır; günlük oturumda bu sınırlar yoktur. Sistem tepsisi,
isteğe bağlı HKCU başlangıç açılışı, bağlantıyı kes ve Kurtar/teşhis düğmesi vardır.
USB/forward kaybında temizlik ve yeniden deneme uygulanır; yeni yükseltilmiş
bağlantı Windows UAC onayı isteyebilir. Sessiz yönetici hizmeti kurulduğu iddia edilmez.

Trafik profili, hız tavanları ve telemetri artık gerçek IP worker'a aktarılır.
Wintun hazır kaydı profil telemetrisinden önce yayınlanır. Windows profil
seçiminin yeni değeri sonraki sistem geneli bağlantıda uygulanır; mevcut oturuma
anında uygulandığı iddia edilmez. Adaptif kontrol varsayılan kapalıdır.
Grafikler gerçek ölçümlerdir; RTT/jitter ölçümü yoksa boş kalır.

Windows yeniden başlatmasında kalmış oturumu temizleyecek korunan SYSTEM görevi
kurulur. Bu bilgisayar yeniden başlatılarak test edilmedi. Yaklaşık üç dakika
uzun süreli bellek/CPU kararlılığı kanıtı değildir. Tek pasif örnek: Go worker
~41 MB, guardian ~59 MB, watcher ~35 MB, WPF ~189 MB çalışma kümesi.

## Tam kabulü engelleyen somut koşul

Kullanıcı, telefon dışında bağımsız internetinin olmadığını doğruladı.
Ethernet 5/RNDIS mevcut tek geri dönüş yoludur. Android 16'nın normal uygulama
API'siyle USB tethering anahtarını tekrar açacak kurtarma yolu uygulanmadı;
root/gizli API kullanılmadı. Bu yüzden standart paylaşım kapatılmadı. B kabulü
bekliyor; bunu güvenli tamamlamak için telefon dışındaki bir Wi-Fi/Ethernet
bağlantısı gerekir. Kota bağımsızlığı doğrulanmadı; Türk Telekom/PdaNet sayaç
karşılaştırması yapılmadı. Önemli mobil veri tüketen deney başlatılmadı.

## Gerekli regresyonlar

Yeni eşzamanlı kurtarma kilidi testi dahil NetworkTests geçti. Go cmd/tethermax
ve internal/ipengine regresyonları geçti; Windows WPF/guardian ve Go worker
derlendi. Önceden başarılı ilgisiz testlerin tümü tekrar çalıştırılmadı.

Yerel kanıtlar build/reports altında; ham raporlar ve kimlik dosyaları Git'e
alınmadı. Başlangıç snapshot'ı acceptance-before-20261024.json, günlük kapanış
snapshot'ı daily-close-recovery-20261024.json. MSI: tethermax-windows-x64-060.msi.
