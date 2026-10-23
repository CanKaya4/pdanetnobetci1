# TetherMax

## Windows arayüzü ve USB eşleştirme önizlemesi

23 Ekim 2026: WPF Windows uygulaması, 6 haneli karşılıklı ilk güven onayı,
Android Keystore / Windows DPAPI kimlik saklama ve sonraki bağlantılarda kodsuz
eşleştirme eklendi. Galaxy A56 Android 16 + Windows 11 üzerinde gerçek USB
HTTPS ve UDP DNS testleri geçti. Bu, tamamlanmış 1.0 veya sistem geneli internet
bağlantısı değildir. [Kurulum](docs/windows-preview-tr.md),
[protokol ve sınırlar](docs/pairing-v2.md), [cihaz kanıtı](docs/device-validation-2026-10-23.md).

Yeni geliştirme: gVisor tabanlı deneysel IPv4 TCP/UDP paket motoru ve gerçek
Wintun ring okuma/yazma arka ucu eklendi. Sentetik IP → Java relay testleri geçti.
ADB yetkilendirildikten sonra yeni IP yolu Galaxy A56 üzerinde gerçek USB →
hücresel HTTPS ve UDP DNS testlerini geçti. Sistem rotasını değiştirmeyen özel
IPv4 testidir; Wintun üzerinden sistem geneli internet doğrulanmış değildir.
[Kapsam](docs/ip-engine.md), [0.4 trafik motoru ve cihaz kanıtı](docs/traffic-engine.md).

0.4 önizlemesi, gerçek TCP yazma/UDP datagram yoluna akış zamanlama, kullanıcı
tavanları ve deneysel adaptif hız kontrolü ekler. Beş profil Android/Windows
arasında kimliği doğrulanmış USB kanalında eşitlenir. Gerçek veri/hız grafikleri
ve isteğe bağlı hücresel DNS RTT/jitter ölçümü vardır. Oyun gecikmesinde iyileşme
veya 5G kapasitesi henüz ölçülmedi.

Normal kullanımda Windows uygulamasını açıp **Bağlan** seçilir; PowerShell veya
uzun gizli kod girişi gerekmez. Aşağıdaki CLI/cloud bölümleri geliştirme içindir.

PdaNet'e bağımlı olmayan Android → USB → Windows internet aktarım projesi.
Uygulama içi kullanım süresi sınırı yoktur. Operatörün hotspot kotası veya tarife
kurallarının aşılması garanti edilmez; uygulama bu kuralları değiştirmez.

**0.1 motor önizlemesi:** gerçek TCP/UDP soket aktarımı, Android hücresel DNS/ağ
bağlama, ADB akış protokolü ve Windows loopback SOCKS5 istemcisi uygulanmıştır.
Windows WPF ve CLI uygulamaları vardır; tüm bilgisayarı otomatik yönlendiren bir TUN
veya tamamlanmış oyun hızlandırıcısı değildir. APK geliştirme imzalıdır, EXE'ler
Authenticode imzalı değildir. Yeni fiziksel test aşamaları ayrıca onay gerektirir.

## Güvenlik sınırı

Mevcut bilgisayarın çalışan bağlantısı korunur. PdaNet, MyPublicWiFi,
adaptörler, DNS, rota ve sistem proxy'si değiştirilmeyecek. Kod otomatik ADB,
sürücü kurulumu veya ağ yapılandırması çalıştırmaz. Windows uygulamasının varsayılanı
`status` gösterip çıkmaktır. `serve` ayrıca `--approve-usb-test` gerektirir; bu
bayrak kullanıcıdan onay almanın yerine geçmez. Yeni IP işçisi varsayılan olarak
adaptör oluşturmayı reddeder. Global IP/rota/DNS yapılandırma kodu henüz yoktur.

## Bulutta derleme ve test

Linux x64, Python 3.12+, curl/Git, zip/unzip ve race testi için C derleyicisi gerekir.

```sh
python scripts/bootstrap-cloud.py
scripts/test.sh
scripts/build-android.sh
scripts/build-host.sh
```

Araçlar `/workspace/toolchains` altında tutulur: bootstrap Go 1.26.1 kurar;
IP motoru bağımlılıkları için Go 1.26.3 modül araç zinciri otomatik doğrulanıp
seçilir. CI doğrudan Go 1.26.3 kullanır. Temurin JDK 21.0.13+9,
Android platform 35 ve build-tools 35.0.0. Bootstrap indirme SHA256'larını ve TLS'yi
doğrular, SDK paketleri SDK manager doğrulamasından geçer. SDK şartları kurulumda
kabul edilir. `TETHERMAX_TOOLS`, `JAVA_HOME`, `ANDROID_SDK_ROOT` ile CI ortamındaki
araçlar kullanılabilir. Mevcut checkout kullanılır; yeni worktree oluşturulmaz.

[İndirmeler](https://github.com/CanKaya4/pdanetnobetci1/releases/tag/v0.1.0-engine-preview) geliştirme önizlemesidir. APK veya USB testini ayrıca onay vermeden çalıştırmayın.

Çıktılar `build/artifacts/`, raporlar `build/reports/` altındadır. `scripts/test.sh`
yeni derlenen Java motoruyla Go istemcisini gerçek loopback soketlerinde test eder;
Java fixture dış adreslere çıkmayı reddeder. Fiziksel telefon veya yerel Windows
bilgisayarına bağlanmaz. GitHub Actions Linux ve yerel Windows bulut runner'larında
bu testleri, APK derlemesini/imza doğrulamasını ve Windows derlemelerini tekrarlar.

## Veri yolu ve mevcut sınırlar

```text
SOCKS destekli uygulama → Windows TetherMax (127.0.0.1)
  → ADB USB forward → Android TetherMax abstract socket
  → eşleştirme doğrulaması → hücresel Network DNS + TCP/UDP → internet
```

TCP ve UDP başına bağımsız USB akışları; 128-bit eşleştirme kodu; bağlantı sınırları,
backpressure ve bağlantı kapatma bulunur. Telefon Wi-Fi'ına veya PC'nin mevcut
internetine sessiz geri dönüş yoktur. UDP, USB tarafında TCP ile çerçevelenir;
bunun gecikme etkisi fiziksel cihazda henüz ölçülmemiştir. Teknik ayrıntılar:
[protokol](docs/protocol.md), [araştırma ve lisanslar](docs/research.md),
[üçüncü taraf bildirimleri](THIRD_PARTY_NOTICES.md).

SOCKS kapsamı dışındaki Windows/oyun/MyPublicWiFi trafiği bu önizlemede aktarılmaz.
Sistem geneli TUN yönlendirme, MyPublicWiFi/ICS kurulumu ve A56/Türk Telekom 5G
kalibrasyonu sonraki aşamalardır. Oyun süreç adı algılama ve deneysel adaptif
zamanlama uygulanmıştır; gerçek oyun/TV yükü altında başarımı doğrulanmamıştır.
CAKE-autorate veya sqm-autorate'ın Linux kontrol betiklerini Windows'a kopyalamak
bu özellikleri sağlamaz. Motor fiziksel olarak doğrulanmadan optimizasyon sonuçları
iddia edilmeyecektir.

## Doğrulama

Güncel kanıtlar [validation.md](docs/validation.md) ve GitHub Actions sonuçlarındadır.
APK build/imza, masaüstü Java entegrasyonu, Windows runtime ve fiziksel Android/USB
farklı kontrollerdir; birinin geçmesi diğerinin geçtiği anlamına gelmez.
Onaydan sonra kullanılacak sınırlı test taslağı [burada](docs/hardware-test-plan.md).
Şimdi bu komutları kullanıcı bilgisayarında otomatik çalıştırmayın.
