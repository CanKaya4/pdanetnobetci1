# Hotspot bağımsız USB önceliği — 23 Ekim 2026

## Sonuç

Bu teslimat tam Windows internet bağlantısı değildir. Mevcut motor Windows
paketlerini gVisor ile akışlara dönüştürebiliyor; Android bu akışları hücresel
ağa bağlı yeni soketlerden çıkarıyor. Ancak çalışan sistem geneli rota/DNS
yönetimi, IPv6 kaçak engeli ve bağımsız ayrı süreçte native rollback henüz yok.
`routing.Run` bu güvenlik bileşenlerinin arayüzünü ve işlem sırasını uygular;
gerçek backend veya çalışan ayrı watchdog içermez. Buna hazır ürün denemez.

## Kaynak değerlendirmesi

Kaynaklar 23 Ekim 2026 tarihinde okundu. PdaNet kapalı kaynak kodu alınmadı.

* [Resmî ana sayfa](https://pdanet.co/): USB, Wi-Fi ve Bluetooth desteği
  bildiriyor; ücretsiz sürümün kesinti uyguladığını söylüyor. Pazarlama
  ifadeleri Türk Telekom tarife muhasebesi için bağımsız kanıt değildir.
* [Resmî cihaz yardım sayfası](https://pdanet.co/help/devices.php): USB modu
  Windows/Mac istemcilerini ve “Hide Tether Usage” seçeneğini belgeliyor.
  Özelliğin algoritması, paket dönüşümleri ve Türkiye operatörleri üzerindeki
  sonucu açıklanmıyor. Eski cihaz/Wi-Fi tablosu A56/Android 16 doğrulaması değildir.
* [Resmî USB yardım sayfası](https://pdanet.co/help/usb.php): masaüstü ağ
  sürücüsü, USB debugging ve ADB arayüzünden söz ediyor; yerleşik paylaşımın
  kapatılmasını da bazı bağlantı sorunları için öneriyor. USB/debug istemcisi
  modeliyle uyumlu, fakat özel USB protokolünü veya Wintun/gVisor kullandığını
  kanıtlamaz. Sayfadaki ADB değiştirme önerileri bu bilgisayarda uygulanmadı.
* [Google Play açıklaması](https://play.google.com/store/apps/details?id=com.pdanet):
  masaüstü sürümüyle birlikte güncelleme gereğini ve cihazlara göre teknik
  kısıtları açıklıyor. Wi-Fi Direct için istemci/proxy gereksinimi USB iç
  protokolünü açıklamaz.
* [wtyler2505/pdanet-linux](https://github.com/wtyler2505/pdanet-linux):
  README Linux proxy, redsocks, iptables, TTL ve DNS yönlendirme tasarımı
  anlatıyor. “Altı katman” ve operatör tespitini yendiği iddiaları yazarın
  iddialarıdır; gerçek PdaNet USB protokolü veya Türk Telekom sonucu olarak
  doğrulanmadı. HTTP CONNECT/TCP proxy açıklaması genel UDP desteğinin kanıtı
  değildir. Bu depodan kod kopyalanmadı ve betikleri çalıştırılmadı.
* [Android Network API](https://developer.android.com/reference/android/net/Network):
  `getSocketFactory` ve bağlanmamış soket üzerindeki `bindSocket`, soketi seçili
  ağa bağlar. `getAllByName` seçili ağın çözümleyicisini kullanır.
  [ConnectivityManager](https://developer.android.com/reference/android/net/ConnectivityManager)
  üzerinden CELLULAR/INTERNET ağı istenebilir. Bu API'ler faturalandırmayı
  belirlemez; Android sürümü ve cihazdaki çalışma ayrıca ölçülmelidir.

## Mimari fark ve mevcut kod

| Parça | TetherMax durumu | Kanıtın sınırı |
|---|---|---|
| USB taşıma | Seçili ADB forward → Android localabstract socket, eşleştirmeli akışlar | Android RNDIS IP bağlantısını taşıma olarak kullanmıyor; hotspot-off fiziksel deney yapılmadı |
| Windows IP motoru | Wintun PacketDevice → gVisor → TCP/UDP relay | Rota atanmadıkça normal Windows uygulamaları buraya girmez |
| TCP çıkışı | `TunnelService`: seçilen Network socket factory | Windows TCP oturumu Android üzerinde yeni TCP oturumuna çevrilir |
| UDP çıkışı | Seçilen Network'e bind edilen DatagramSocket | ADB/TCP alt taşıması nedeniyle head-of-line gecikmesi mümkün |
| DNS | IP motorunda sıradan UDP/TCP hedef akışı; Android isim çözümü seçili Network'te | Windows DNS politikası uygulanmadı; sistem geneli kaçak yok iddiası yapılamaz |
| Aktif yol raporu | WPF düğmesi ve `network-audit.ps1` | Salt okunur rota seçimi, uygulama trafiğinin paket yakalaması değildir |
| Rota yaşam döngüsü | İşlem çekirdeği ve /32 dry-run planlayıcı | Native backend/journal/watchdog uygulamaları yok; CanApply=false |
| Yeniden bağlanma | WPF güvenli eşleştirme/USB oturumunu tekrar açabilir | Mevcut TCP oturumunun devamı veya tam fiziksel unplug testi değildir |

Windows TTL ayarı, telefonun IP paketlerini NAT ile ilettiği mimaride
gözlemlenebilir olabilir. Burada TCP/UDP telefonun yeni soketlerinde yeniden
oluşturuluyor; gelen Windows IP TTL ve TCP başlığı mobil ağa aynen aktarılmıyor.
Bu nedenle Windows TTL ayarı eklenmedi. Bu çıkarım mevcut kodun veri yoluna
dayanır; PdaNet'in gizli algoritmasına ilişkin iddia değildir.

DNS'nin aynı yoldan taşınması işlevsel doğruluk ve kaçak önleme için gereklidir;
operatör sayacını değiştirme garantisi değildir. TLS ClientHello, HTTP başlıkları,
hedefler ve uygulama davranışı soket relay'inde korunabilir. UA değiştirme,
TLS araya girme veya Windows servislerini engelleme uygulanmadı.

## Sistem geneli etkinleştirmeden önce zorunlu kalan işler

1. Sahip olunan Wintun GUID/LUID üzerinde IP ve tam kimlikli rota backend'i.
2. Atomik kalıcı intent günlüğü; GUI/worker ölse de çalışan bağımsız ayrı
   süreç ve açılışta artık kayıt kurtarması. Mevcut worker heartbeat'i yeterli değil.
3. IPv4/IPv6, UDP/TCP DNS ve arabirime özel bağlanan uygulamalar için native
   fail-closed engeli. Yalnızca default metric değiştirmek yeterli değil:
   adaptör kaybolunca RNDIS yolu tekrar seçilebilir. IPv6 motoru yokken IPv6
   trafiğine sessiz alternatif yol verilmemeli.
4. Ayrı izole Windows test ortamında native ring, crash, watchdog crash,
   route replacement ve rollback arızası testleri.
5. Tam eklenecek/değişecek nesneler ve geri alma planıyla fiziksel rota onayı.
6. Kullanıcının hotspot kapalı gözlemiyle sınırlı USB hücresel deneyi ve sonra
   normal Windows uygulaması TCP/UDP/DNS kaçak deneyi.

Bu bilgisayarda default rota, DNS, adaptör, ICS, güvenlik duvarı, Wi-Fi veya
ADB değiştirilmedi. Mevcut bağlantının korunması için global etkinleştirme
komutu eklenmedi. Oyun/profil/TV geliştirmesi bu teslimatın kapsamına alınmadı.
