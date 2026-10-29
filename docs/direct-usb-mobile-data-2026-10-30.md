# Ürünün birinci amacı: doğrudan USB üzerinden telefon mobil verisi

30 Ekim 2026. Kullanıcının açık önceliği: standart hotspot paylaşımına bağımlı
olmadan telefonun kendi hücresel veri yolunu USB ile Windows'a ulaştırmak.
QoS, bütçe ve premium özellikler bu temel yolun çevresindeki isteğe bağlı
araçlardır. Ayrı bir router/bonding ürününe dönüşmek birinci amaç değildir.

## 2026'da kontrol edilen birincil kaynaklar

- [Android Network API](https://developer.android.com/reference/android/net/Network):
  `getSocketFactory()` soketleri seçilen Network'e bağlar; `bindSocket()` UDP dahil
  soket trafiğini seçilen ağa sabitler; `getAllByName()` DNS'i aynı ağda çözer.
  Ağ kaybolduğunda eski soketler çalışmayı bırakır; başka ağa sessiz geçiş yapılmaz.
- [AOSP Tethering](https://source.android.com/docs/core/ota/modular-system/tethering)
  ve [offload muhasebesi](https://source.android.com/docs/core/data/tethering-offload):
  standart paylaşım ayrı forwarding/NAT/downstream ve offload sayaçları kullanır.
  Uygulama hücresel soketiyle bu forwarding yolu aynı şey değildir.
- [PdaNet USB yardım](https://pdanet.co/help/usb.php) ve
  [üreticinin kullanım rehberi](https://pdanet.co/help/devices.php): USB ile özel
  istemci yolu ve bazı operatörler için Hide Tether Usage tercihleri belgelenir.
  Sayfalardaki eski Android/operatör örnekleri 2026 Türk Telekom/Android 16
  uyumluluk kanıtı değildir; ürünün pazarlama iddiası TetherMax için kanıt sayılmaz.
- [EasyTether geliştirici Play kaydı](https://play.google.com/store/apps/details?id=com.mstream.e2tpc)
  kendi paylaşım mekanizmasını/root'suz resmi API kullanımını açıklar. Kaydın
  güncelleme tarihi 27 Ağustos 2023'tür; yeni 2026 sürümü bulunmuş gibi sunulmaz.
  [Geliştirici FAQ](https://s3.amazonaws.com/easytether/android_faq.html) telefon
  uygulamalarıyla aynı veri bağlantısının kullanıldığını söyler; Android 7 dönemine
  ait uyumluluk metinleri bugünkü cihaz testi yerine geçmez.
- [Android paylaşım yardım](https://support.google.com/android/answer/9059108?hl=tr)
  operatörlerin paylaşımı sınırlayabileceğini belirtir. Türk Telekom'un
  [2019 hotspot duyurusu](https://medya.turktelekom.com.tr/hotspot-aciklamasi)
  bugünkü belirli tarifenin sınırsız paylaşım kanıtı değildir. Kullanıcının
  gerçek tarife koşulları ve mobil/hotspot sayaçları ayrıca görülmelidir.

Bu kaynaklarda tüm operatörlerde/sınırsız mobil paketlerde kota ayrımını kesin
ortadan kaldıran doğrulanmış bir 2026 standardı bulunmadı. Üretici dokümanı,
resmi API sözleşmesi ve gerçek tarife ölçümü birbirinden ayrılmalıdır.

## TetherMax'ın kaynak kodunda doğrulanan yol

`android/src/io/tethermax/app/TunnelService.java`:

1. `NetworkRequest` yalnızca `TRANSPORT_CELLULAR` + INTERNET ister.
2. Her yeni akışta ağın hâlâ CELLULAR olduğu capabilities ile kontrol edilir.
   Hücresel ağ yoksa IOException olur; varsayılan/Wi-Fi ağına fallback yapılmaz.
3. TCP: `n.getAllByName()` + `n.getSocketFactory().createSocket()`.
4. UDP: `n.getAllByName()` + bağlanmamış DatagramSocket için `n.bindSocket()`.
5. Şifreli DNS: `selected.openConnection(HTTPS URL)`; platform sertifika ve
   hostname kontrolleri korunur. Düz DNS'e gizli geri dönüş yoktur.
6. Android relay, Android uygulamasının açtığı TCP/UDP soketlerinden veri alır;
   USB üzerinden yalnızca kimliği doğrulanmış uygulama protokolü taşınır.

Windows'ta Wintun → gVisor → relay yolu kullanılır; telefon tarafında ham Windows
IP paketleri standart Android tethering NAT'ına verilmez. Bu nedenle tasarım
uygulama soketi üzerinden telefon verisi hedefine uygundur. Bu, operatörün
sunucu tarafındaki sınıflandırmasının nasıl sonuçlanacağını tek başına kanıtlamaz.
Masaüstü uygulamanın HTTPS içeriği/User-Agent'ı değiştirilmez; TLS MITM yapılmaz.
APN/DUN/TTL registry değişikliği veya telefon rooting'i eklenmedi.

## Gerçek cihaz doğrulamasında aranacak kanıt

- USB fonksiyonunda ADB var; `rndis` / `ncm` yok.
- Android `dumpsys tethering` içindeki **güncel Tether state** bölümünde aktif
  TetheredState/downstream ve upstream yok. Geçmiş loglardaki `rndis0 TETHERED`
  satırları güncel durum gibi değerlendirilmez.
- Telefonun seçili ağ bilgisi CELLULAR / rmnet arayüzünü gösterir.
- Windows'un küçük normal HTTPS/DNS/UDP isteği sahipli TetherMax Wintun
  arayüzünden geçer; fiziksel Ethernet/Wi-Fi/RNDIS/PdaNet fallback'i kullanılmaz.
- İstek sırasında Android relay / Windows motor sayaçları artar. Bu küçük
  test yalnızca yolu doğrular; yük testi veya operatör kota karşılaştırması değildir.

İlk 30 Ekim salt okunur ön kontrolde bağlı Galaxy A56'nın USB fonksiyonu
`sec_charging,adb`, Tethering `Upstream wanted:false`, upstream `null` idi.
Windows Wi-Fi/Ethernet/PdaNet adaptörleri bağlantısız, /1 rotaları sahipli
TetherMax Wintun'daydı. Bu, eski sürümün o anda standart Android USB paylaşımına
bağımlı olmadığını gösteren teknik kanıttır; RC13 kurulum sonrası tekrar sınanmalıdır.

Operatör sınıflandırması için mevcut `controlled-usage-study.md` geçerlidir:
aynı tarife dönemi, iki ayrı sayaç, bilinen test nesnesi, diğer trafiğin durması,
ölçüm çözünürlüğü ve gecikmeli sayaç yerleşmesi gerekir. Operatör uygulamasındaki
hesaba giriş veya görünmeyen sayaç yerine tahmin yapılmaz. Yeni büyük indirme
başlatılmadı. Gerçek sonuç yoksa kota testi `inconclusive` kalır.

Kullanıcının son mesajı iki tarafa güncelleme kurma ve bu yolu sınırlı fiziksel
testle kontrol etme yetkisini verir. PdaNet/MyPublicWiFi veya yabancı ağ ayarları
değiştirilmez; yalnızca TetherMax'ın kendi oturumu kurulum için kapatılıp açılır.
