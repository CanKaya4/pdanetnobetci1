# TetherMax 0.4 — trafik motoru ve cihaz doğrulaması

23 Ekim 2026. Bu sürüm sistem geneli internetin tamamlandığı anlamına gelmez.
Windows rota/DNS/IP/ICS/güvenlik duvarı yapılandırması uygulanmadı. Wintun veri
işçisi bu aşamada fiziksel bilgisayarda çalıştırılmadı.

## Gerçek veri yolundaki değişiklikler

`host/internal/traffic` iki yön için bağımsız, sınırlı kuyruklu akış zamanlayıcısı
sağlar. TCP yazıları profile göre bölünür; UDP datagramları bölünmeden izin alır.
Ağırlıklı sanal bitiş zamanı akışlar arasında hizmet sırası belirler. Soket
yazısı zamanlayıcı içinde yapılmaz; bir tıkanmış soket diğerlerinin hizmetini
tutmaz. Baytlar yalnızca başarılı aktarım sonrası sayılır. Profil/tavan değişimi
bekleyen izinlere uygulanır; iptal/bağlantı hatası beklemeyi sonlandırır.

| Profil | Gerçek davranış |
| --- | --- |
| Oyun | Küçük UDP etkileşimli sınıf; etkileşimli/paylaşılan/toplu ağırlıkları 6/2/1; TCP parçası 1200 B |
| Film / Dizi | Ağırlıklar 3/4/1; TCP parçası 4096 B |
| Günlük | Ağırlıklar 3/2/1; TCP parçası 1200 B |
| Maksimum Hız | Eşit ağırlık; TCP parçası 16384 B; adaptif azaltma atlanır, elle girilmiş tavan korunur |
| Otomatik | Bilinen oyun süreci varsa Oyun; açıkça tanımlanmış paylaşılan akış varsa Film / Dizi; diğer durumda Günlük |

Windows Toolhelp süreç listesinde Valorant, Deadlock (`project8.exe`) ve Genshin
çalıştırılabilir adları aranır. Oyun belleği açılmaz. Bu bir süreç adı sezgisidir;
paketin oyun PID'sine ait olduğunu kanıtlamaz. Manuel profil otomatik algılamayla
değiştirilmez. Gerçek oyun/anti-cheat uyumluluğu test edilmedi.

`--shared-clients` yalnızca açıkça verilen özel IPv4 kaynak CIDR'lerini sınıflar;
MyPublicWiFi/ICS ayarı yapmaz. NAT sonrasında özgün istemci adresi kaybolabilir.
Bu nedenle gerçek TV/çok cihaz veya istemci başına adil paylaşım doğrulanmadı.

## Adaptif kontrol ve ölçümün anlamı

Tavanlar varsayılan 0'dır: kapasite tahmini veya hız sınırı uydurulmaz. Adaptif
seçim açık ve yükleme tavanı girilmişse doğrulanmış USB/hücresel yoldan saniyelik
DNS işlem süresi örneklenir; mobil veri kullanır. İlk hız %90 tavan olur. En az
5 başarılı baz örnek, en az %60 gözlenen yük ve 3 yüksek gecikme örneğinde
yükleme 0,85/indirme 0,9 katsayısıyla azaltılır; kullanıcı tavanının %10 altına
inmez. 10 düşük gecikme örneğiyle tavanın %2'si kadar toparlanır; değişimler en az
2 saniye aralıklıdır. Boştaki DNS gecikmesi tek başına hız azaltmaz. Hücresel ağ
kimliği değişince ölçüm bazı sıfırlanır. Bu deneysel denetleyici CAKE/FQ-CoDel
değildir; operatör baz istasyonu kuyruğunu kontrol ettiği iddia edilmez.

Grafikler gerçek aktarım sayaç farklarından Mbps ve gerçek DNS RTT/jitter
örneklerinden çizilir; ölçülmeyen gecikme boş kalır. RTT ICMP veya oyun ping'i
değildir. Jitter, ardışık işlem süresi farkının EWMA'sıdır. En az 20 DNS işlemi
olmadan yanıtsız yüzdesi gösterilmez; bu genel IP paket kaybı değildir. Kuyruk
P50/P95/P99 son 512 TetherMax izin beklemesidir, internet kuyruk gecikmesi değil.
Windows paneli bağlı SOCKS işçisini, ayrı IP testi kendi IP işçisini ölçer.
Android sayacı etkin relay'in toplam uygulama verisidir; operatör faturası değil.

Profil/telemetri kontrolü mevcut eşleştirme kimliğiyle doğrulanır. Gizli belirteç
komut satırına/loglara yazılmaz. İlk bağlantıda Windows kayıtlı tercihi gönderir;
sonraki telefon seçimi canlı motoru değiştirir. İşçi özel stdin EOF ile kapanır.

## Tamamlanan gerçek Galaxy A56 + Windows 11 kontrolleri

- ADB `device`, Android 16/API36; mevcut güvenli kimlik korunarak APK güncellendi.
- Özel IPv4 → gVisor → USB relay → Android `CELLULAR Network 212 / rmnet0` yolu:
  HTTPS 200, 190 B; 5/5 doğrulanmış UDP DNS cevabı. Yerel Windows internetine
  geri dönüş kodu yok; telefon soket açarken TRANSPORT_CELLULAR doğruluyor.
- Ön test HTTPS 114,419 ms; DNS 22,124–51,237 ms. Yalnızca testin kendi ADB veri
  forward'ı kaldırılınca otomatik yeniden eşleşme; tekrar HTTPS 200/175,4 ms ve
  DNS 5/5. Mevcut uygulamanın diğer forward'larına dokunulmadı.
- Zamanlayıcı entegre IP testi: HTTPS 200/221,969 ms; DNS 5/5
  (73,810 / 34,831 / 31,506 / 30,762 / 40,552 ms). Gerçek payload ↑1803/↓4821 B;
  2 akış, 0 hata/reddetme. Kullanılan 2/10 Mbps tavanlar test ayarıdır, ölçülmüş
  hat kapasitesi değildir. Test sonunda tavanlar 0/0, adaptif sorgular kapalı.
- Normal SOCKS üzerinden iki sınırlı HTTPS/DNS testi de geçti (her biri HTTP
  3/3=200 ve UDP DNS5/5). Bu sonuçlar IP testinden ayrı tutulur.
- Kullanıcı Android'de Film / Dizi seçti; Windows canlı motor telemetrisi
  `Etkin: Film / Dizi` gösterdi. Yalnızca yerel tercih kaydıyla başarı varsayılmadı.
- Son 0.4 APK + paketlenen self-contained Windows derlemesiyle tekrar: Film /
  Dizi, tavanlar 0/0 ve adaptif kapalı; HTTPS200/255,250 ms, UDP DNS5/5
  (60,570 / 24,893 / 36,752 / 31,832 / 37,565 ms), ↑1803/↓4819 B,
  2 akış, 0 hata/reddetme. Güncelleme sırasında servis henüz açılmadığında ilk
  bağlantı EOF verdi; servis başladıktan sonra otomatik güvenli bağlantı kuruldu.

Ham yerel kayıtlar `build/reports/ipv4-stage2/` altındadır (Git'e eklenmez).
Bu küçük örnekler 5G hız testi, yüklü bufferbloat A/B veya oyun kazancı değildir.
Fiziksel USB çıkarma yapılmadı: mevcut seçili Windows çıkışı Samsung USB RNDIS
`Ethernet 5`; uygulama güncelleme öncesi/sonrası kayıtlarında Wi-Fi Disconnected.
Adaptör ad/GUID/durumları, rotalar ve DNS adresleri karşılaştırıldığında fark yok.
Bu durumu biz değiştirmedik. Kablo çıkarma mevcut
internet bağlantısını da etkileyebileceğinden ayrı kontrollü aşamaya bırakıldı.

## Kalan geliştirme ve cihaz engelleri

- Tam Windows interneti: native ring veri testi, bağımsız kurtarma gözcüsü,
  sahipliği kayıtlı IP/rota/DNS denetleyicisi ve izole hata/geri alma testleri.
  Gerçek IP/rota/DNS değişiminden önce ayrı kullanıcı onayı gerekir.
- Canlı native Wintun profil kontrolü ve tek tık sistem geneli bağlantı akışı.
- Gerçek MyPublicWiFi/TV yönlendirmesi ve istemci kimliği/adalet doğrulaması.
- Saatler süren trafik, fiziksel USB kopma, Android süreç öldürme/kurtarma testi.
  START_STICKY kullanıcı tarafından başlatılmış servisi Android izin verdiğinde
  yeniden oluşturabilir; force-stop/üretici pil politikasını aşmaz ve henüz
  fiziksel uzun süre testi yapılmadı.
- Yüklü hücresel bağlantıda kalibrasyon, throughput ve oyun/streaming A/B.

PdaNet API'si kullanılmaz; kendi uygulama süre sınırı yoktur. Operatör kotası
veya tarife kurallarının değiştirildiği/aşılacağı iddia edilmez. APK geliştirme
imzalı, Windows paketleri Authenticode imzasız önizlemedir.
