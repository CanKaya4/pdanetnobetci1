# TetherMax

PdaNet'e bağımlı olmayan Android → USB → Windows internet aktarım projesi.
Uygulama içi kullanım süresi sınırı yoktur. Operatörün hotspot kotası veya tarife
kurallarının aşılması garanti edilmez; uygulama bu kuralları değiştirmez.

**0.1 motor önizlemesi:** gerçek TCP/UDP soket aktarımı, Android hücresel DNS/ağ
bağlama, ADB akış protokolü ve Windows loopback SOCKS5 istemcisi uygulanmıştır.
Bu sürüm Windows CLI uygulamasıdır; tüm bilgisayarı otomatik yönlendiren bir TUN
veya tamamlanmış oyun hızlandırıcısı değildir. APK geliştirme imzalıdır, EXE'ler
Authenticode imzalı değildir. İlk fiziksel kullanım ayrıca onay bekler.

## Güvenlik sınırı

Mevcut bilgisayar PdaNet üzerinden internete bağlıdır. PdaNet, MyPublicWiFi,
adaptörler, DNS, rota ve sistem proxy'si değiştirilmeyecek. Kod otomatik ADB,
sürücü kurulumu veya ağ yapılandırması çalıştırmaz. Windows uygulamasının varsayılanı
`status` gösterip çıkmaktır. `serve` ayrıca `--approve-usb-test` gerektirir; bu
bayrak kullanıcıdan onay almanın yerine geçmez. Tüm geliştirme Codex Cloud içindedir.

## Bulutta derleme ve test

Linux x64, Python 3.12+, curl/Git, zip/unzip ve race testi için C derleyicisi gerekir.

```sh
python scripts/bootstrap-cloud.py
scripts/test.sh
scripts/build-android.sh
scripts/build-host.sh
```

Araçlar `/workspace/toolchains` altında tutulur: Go 1.26.1, Temurin JDK 21.0.13+9,
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
Sistem geneli TUN, otomatik oyun algılama, gerçek adaptif bufferbloat şekillendirmesi,
MyPublicWiFi akış yönetimi ve A56/Türk Telekom 5G kalibrasyonu sonraki aşamalardır.
CAKE-autorate veya sqm-autorate'ın Linux kontrol betiklerini Windows'a kopyalamak
bu özellikleri sağlamaz. Motor fiziksel olarak doğrulanmadan optimizasyon sonuçları
iddia edilmeyecektir.

## Doğrulama

Güncel kanıtlar [validation.md](docs/validation.md) ve GitHub Actions sonuçlarındadır.
APK build/imza, masaüstü Java entegrasyonu, Windows runtime ve fiziksel Android/USB
farklı kontrollerdir; birinin geçmesi diğerinin geçtiği anlamına gelmez.
Onaydan sonra kullanılacak sınırlı test taslağı [burada](docs/hardware-test-plan.md).
Şimdi bu komutları kullanıcı bilgisayarında otomatik çalıştırmayın.
